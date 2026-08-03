/**
 * Rewrites imperative Vaadin renderers into Lit renderer directives.
 *
 * The imperative form Vaadin has deprecated:
 *
 *     .renderer="${this.fooRenderer}"
 *     fooRenderer = (root, column, model) => { render(html`...`, root); };
 *
 * The directive form:
 *
 *     ${columnBodyRenderer(this.fooRenderer, [])}
 *     fooRenderer = (item, model, column) => html`...`;
 *
 * Only the shapes this can prove safe are rewritten. Anything that touches
 * `root` for something other than being `render`'s second argument — writing
 * `innerHTML`, attaching listeners, caching the node — is reported and left
 * alone, because those need a human decision about what the template should be.
 *
 * Dependency arrays are always emitted empty. A renderer that genuinely
 * depends on component state needs that array filled in deliberately; guessing
 * it here would either miss a dependency or add one that resets an
 * uncontrolled field on every keystroke.
 *
 *   node tools/renderer-codemod.mjs           # report only
 *   node tools/renderer-codemod.mjs --write   # apply
 */

import { readFileSync, writeFileSync } from 'node:fs';
import { join, relative } from 'node:path';
import ts from 'typescript';
import { readdirSync, statSync } from 'node:fs';

const ROOT = new URL('..', import.meta.url).pathname.replace(/\/$/, '');
const SRC = join(ROOT, 'src');
const WRITE = process.argv.includes('--write');

const walk = dir =>
  readdirSync(dir).flatMap(entry => {
    const full = join(dir, entry);
    return statSync(full).isDirectory()
      ? walk(full)
      : full.endsWith('.ts')
        ? [full]
        : [];
  });

/** Host element + bound property -> the directive that replaces it. */
const DIRECTIVES = {
  renderer: {
    'vaadin-grid-column': ['columnBodyRenderer', '@vaadin/grid/lit'],
    'vaadin-grid-sort-column': ['columnBodyRenderer', '@vaadin/grid/lit'],
    'vaadin-grid-filter-column': ['columnBodyRenderer', '@vaadin/grid/lit'],
    'vaadin-combo-box': ['comboBoxRenderer', '@vaadin/combo-box/lit'],
    'vaadin-select': ['selectRenderer', '@vaadin/select/lit']
  },
  headerRenderer: {
    'vaadin-grid-column': ['columnHeaderRenderer', '@vaadin/grid/lit'],
    'vaadin-grid-sort-column': ['columnHeaderRenderer', '@vaadin/grid/lit'],
    'vaadin-grid-filter-column': ['columnHeaderRenderer', '@vaadin/grid/lit']
  },
  footerRenderer: {
    'vaadin-grid-column': ['columnFooterRenderer', '@vaadin/grid/lit'],
    'vaadin-grid-sort-column': ['columnFooterRenderer', '@vaadin/grid/lit']
  },
  rowDetailsRenderer: {
    'vaadin-grid': ['gridRowDetailsRenderer', '@vaadin/grid/lit']
  }
};

/** Header and footer renderers receive only the column; body renderers get the item. */
const TAKES_ITEM = new Set([
  'columnBodyRenderer',
  'comboBoxRenderer',
  'gridRowDetailsRenderer',
  'selectRenderer'
]);

function convertFile(text, rel) {
  const source = ts.createSourceFile(rel, text, ts.ScriptTarget.Latest, true);

  const bindings = findBindings(text);
  if (!bindings.length) return { text };

  const members = collectRenderers(source);

  /** Text edits as {start, end, replacement}, applied back-to-front. */
  const edits = [];
  const neededImports = new Map();
  let needsNothing = false;

  for (const binding of bindings) {
    const table = DIRECTIVES[binding.prop];
    const entry = table && table[binding.element];
    if (!entry) {
      report.skipped.push(`${rel}: ${binding.element} .${binding.prop} — no directive mapping`);
      continue;
    }
    const [directive, module] = entry;
    const member = members.get(binding.member);
    if (!member) {
      report.skipped.push(`${rel}: ${binding.expression} — renderer is not a class field arrow function`);
      continue;
    }
    if (member.converted) {
      // Same renderer bound twice; the member rewrite already happened.
      edits.push(bindingEdit(binding, directive));
      neededImports.set(module, (neededImports.get(module) ?? new Set()).add(directive));
      continue;
    }

    const rewrite = rewriteRenderer(member, text, TAKES_ITEM.has(directive));
    if (!rewrite.ok) {
      report.skipped.push(`${rel}: ${binding.member}() — ${rewrite.reason}`);
      continue;
    }

    if (member.isMethod && rewrite.readsThis) {
      report.rebound.push(
        `${rel}: ${binding.member}() reads this.${rewrite.readsThis} — was the column, now the component`
      );
    }

    member.converted = true;
    if (rewrite.needsNothing) needsNothing = true;
    edits.push({ start: member.node.getStart(source), end: member.node.getEnd(), replacement: rewrite.text });
    edits.push(bindingEdit(binding, directive));
    neededImports.set(module, (neededImports.get(module) ?? new Set()).add(directive));
    report.converted.push(`${rel}: ${binding.member}() -> ${directive}`);
  }

  if (!edits.length) return { text };

  let out = text;
  for (const edit of edits.sort((a, b) => b.start - a.start)) {
    out = out.slice(0, edit.start) + edit.replacement + out.slice(edit.end);
  }

  for (const [module, names] of neededImports) {
    out = addImport(out, module, [...names]);
  }
  if (needsNothing) out = ensureLitImport(out, 'nothing');
  out = pruneImports(out);

  return { text: out };
}

function bindingEdit(binding, directive) {
  return {
    start: binding.start,
    end: binding.end,
    replacement: `\${${directive}(${binding.expression}, [])}`
  };
}

/** Finds `.renderer="${this.x}"` style bindings and the element they sit on. */
function findBindings(text) {
  const out = [];
  const re = /\.(renderer|headerRenderer|footerRenderer|rowDetailsRenderer)="\$\{(this\.[A-Za-z_$][\w$]*)\}"/g;
  let match;
  while ((match = re.exec(text))) {
    const element = elementFor(text, match.index);
    if (!element) continue;
    out.push({
      prop: match[1],
      expression: match[2],
      member: match[2].slice('this.'.length),
      element,
      start: match.index,
      end: match.index + match[0].length
    });
  }
  return out;
}

/** Scans backwards for the opening tag the binding belongs to. */
function elementFor(text, index) {
  const open = text.lastIndexOf('<', index);
  if (open === -1) return null;
  const name = /^<([a-z][\w-]*)/.exec(text.slice(open, index));
  return name ? name[1] : null;
}

/**
 * Renderer definitions, keyed by member name — both `x = (root) => {}` fields
 * and `x(root) {}` methods.
 *
 * The two differ in how `this` behaves. Vaadin invokes a bare `.renderer`
 * with `this` set to the column, so a prototype method reading `this.foo` gets
 * the column's property, not the component's — silently undefined. The
 * directive calls `renderer.call(host, ...)`, so converting a method changes
 * what `this` means inside it. `isMethod` marks those for the report.
 */
function collectRenderers(source) {
  const out = new Map();
  const visit = node => {
    if (
      ts.isPropertyDeclaration(node) &&
      node.initializer &&
      ts.isArrowFunction(node.initializer) &&
      ts.isIdentifier(node.name)
    ) {
      out.set(node.name.text, { node, arrow: node.initializer });
    } else if (ts.isMethodDeclaration(node) && ts.isIdentifier(node.name)) {
      out.set(node.name.text, { node, arrow: node, isMethod: true });
    }
    ts.forEachChild(node, visit);
  };
  visit(source);
  return out;
}

/**
 * Rewrites one renderer field. Returns `{ok: false, reason}` when the body
 * does anything with `root` that a returned template cannot express.
 */
function rewriteRenderer(member, text, takesItem) {
  const { node, arrow } = member;
  const rootParam = arrow.parameters[0];
  if (!rootParam || !ts.isIdentifier(rootParam.name)) {
    return { ok: false, reason: 'first parameter is not a plain identifier' };
  }
  const rootName = rootParam.name.text;

  const renderCalls = [];
  let rootMisuse = null;
  const visit = n => {
    if (
      ts.isCallExpression(n) &&
      ts.isIdentifier(n.expression) &&
      n.expression.text === 'render' &&
      n.arguments.length === 2 &&
      ts.isIdentifier(n.arguments[1]) &&
      n.arguments[1].text === rootName
    ) {
      renderCalls.push(n);
      // Descend into the template only. The second argument is the expected
      // `root` reference; the template may still smuggle one into an event
      // handler, and that has to disqualify the renderer.
      ts.forEachChild(n.arguments[0], visit);
      return;
    }
    if (ts.isIdentifier(n) && n.text === rootName && n.parent !== rootParam) {
      rootMisuse ??= n;
    }
    ts.forEachChild(n, visit);
  };
  ts.forEachChild(arrow.body, visit);

  let readsThis = null;
  const findThis = n => {
    if (
      ts.isPropertyAccessExpression(n) &&
      n.expression.kind === ts.SyntaxKind.ThisKeyword
    ) {
      readsThis ??= n.name.text;
    }
    ts.forEachChild(n, findThis);
  };
  ts.forEachChild(arrow.body, findThis);

  if (rootMisuse) return { ok: false, reason: `uses ${rootName} directly` };
  if (!renderCalls.length) return { ok: false, reason: 'no render() into root' };

  const source = node.getSourceFile();
  const offset = node.getStart(source);
  const params = arrow.parameters;
  const modelParam =
    params[2] && ts.isIdentifier(params[2].name) ? params[2].name.text : null;
  const columnParam =
    params[1] && ts.isIdentifier(params[1].name) ? params[1].name.text : null;

  // The item type comes from the old `GridItemModel<T>` / `ComboBoxItemModel<T>`
  // annotation. Without it the rewritten parameter would be implicitly `any`.
  const itemType = params[2] && modelTypeArgument(params[2].type);
  if (takesItem && !itemType) {
    return { ok: false, reason: 'cannot infer the item type from the model parameter' };
  }

  let body = text.slice(offset, node.getEnd());

  /** Text edits within `body`, applied back-to-front. */
  const edits = [];

  // `model.item` -> `item`, so `model` itself usually falls out of use.
  let usesModel = false;
  let needsNothing = false;
  const rewriteRefs = n => {
    if (
      takesItem &&
      ts.isPropertyAccessExpression(n) &&
      ts.isIdentifier(n.expression) &&
      n.expression.text === modelParam &&
      n.name.text === 'item'
    ) {
      edits.push({
        start: n.getStart(source) - offset,
        end: n.getEnd() - offset,
        replacement: 'item'
      });
      return;
    }
    if (ts.isIdentifier(n) && n.text === modelParam) usesModel = true;
    // A bare `return;` used to mean "rendered nothing"; as a renderer result
    // `undefined` is not a valid template, so it has to become `nothing`.
    if (ts.isReturnStatement(n) && !n.expression) {
      needsNothing = true;
      edits.push({
        start: n.getStart(source) - offset,
        end: n.getEnd() - offset,
        replacement: 'return nothing;'
      });
    }
    ts.forEachChild(n, rewriteRefs);
  };
  ts.forEachChild(arrow.body, rewriteRefs);

  // `render(X, root);` -> `return X;`. This range encloses the `model.item`
  // edits above, so those are folded into the replacement text and removed
  // from the outer list — overlapping edits would corrupt each other's offsets.
  for (const call of renderCalls) {
    const statement = call.parent;
    if (!ts.isExpressionStatement(statement) || statement.expression !== call) {
      return { ok: false, reason: 'render() result is used as a value' };
    }
    const from = call.arguments[0].getStart(source) - offset;
    const to = call.arguments[0].getEnd() - offset;

    let template = body.slice(from, to);
    const inner = edits.filter(e => e.start >= from && e.end <= to);
    for (const edit of inner.sort((a, b) => b.start - a.start)) {
      template =
        template.slice(0, edit.start - from) +
        edit.replacement +
        template.slice(edit.end - from);
      edits.splice(edits.indexOf(edit), 1);
    }

    edits.push({
      start: statement.getStart(source) - offset,
      end: statement.getEnd() - offset,
      replacement: `return ${template};`
    });
  }

  // Rewrite the parameter list. Body renderers get `(item, model, column)`,
  // header and footer renderers get `(column)`. Anything the body no longer
  // mentions is dropped rather than underscore-prefixed.
  const paramList = [];
  if (takesItem) {
    paramList.push(`item: ${itemType}`);
    if (usesModel) {
      paramList.push(text.slice(params[2].getStart(source), params[2].getEnd()));
    }
  } else if (columnParam && !columnParam.startsWith('_')) {
    paramList.push(text.slice(params[1].getStart(source), params[1].getEnd()));
  }

  edits.push({
    start: params[0].getStart(source) - offset,
    end: params[params.length - 1].getEnd() - offset,
    replacement: paramList.join(', ')
  });

  for (const edit of edits.sort((a, b) => b.start - a.start)) {
    body = body.slice(0, edit.start) + edit.replacement + body.slice(edit.end);
  }

  // Nothing the new signature drops may survive in the rewritten body. Several
  // renderers read `_column.someControl` to recover the host component — a
  // workaround for the `this` a bare renderer loses, which the directive makes
  // unnecessary but which has to be unwound by hand rather than left dangling.
  const dropped = params
    .map(p => (ts.isIdentifier(p.name) ? p.name.text : null))
    .filter(name => name && !paramList.some(p => p === name || p.startsWith(`${name}:`)));
  const dangling = dropped.find(name =>
    new RegExp(`\\b${name}\\b`).test(body)
  );
  if (dangling) {
    return { ok: false, reason: `still references the dropped \`${dangling}\` parameter` };
  }

  return { ok: true, text: body, readsThis, needsNothing };
}

/** `GridItemModel<Foo>` / `ComboBoxItemModel<Foo>` -> `Foo`. */
function modelTypeArgument(typeNode) {
  if (!typeNode || !ts.isTypeReferenceNode(typeNode)) return null;
  const arg = typeNode.typeArguments?.[0];
  return arg ? arg.getText() : null;
}

/** Adds a named export to the existing `from 'lit'` import if it is missing. */
function ensureLitImport(text, name) {
  const existing = /import \{([^}]*)\} from 'lit';/.exec(text);
  if (!existing) return `import { ${name} } from 'lit';\n` + text;
  const have = existing[1].split(',').map(s => s.trim()).filter(Boolean);
  if (have.includes(name)) return text;
  return text.replace(
    existing[0],
    `import { ${[...have, name].sort().join(', ')} } from 'lit';`
  );
}

function addImport(text, module, names) {
  const existing = new RegExp(
    `import \\{([^}]*)\\} from '${module.replace(/[/-]/g, '\\$&')}';`
  ).exec(text);
  if (existing) {
    const have = existing[1].split(',').map(s => s.trim()).filter(Boolean);
    const merged = [...new Set([...have, ...names])].sort();
    return text.replace(existing[0], `import { ${merged.join(', ')} } from '${module}';`);
  }
  return `import { ${names.sort().join(', ')} } from '${module}';\n` + text;
}

/**
 * Drops the imports the rewrite orphans — the imperative renderer signature's
 * types and lit's `render` itself. Only these names are considered, so an
 * unrelated unused import is left for the linter to report as before.
 */
const ORPHANABLE = ['GridItemModel', 'GridColumn', 'ComboBoxItemModel', 'ComboBox', 'render'];

function pruneImports(text) {
  return text.replace(
    /import (type )?\{([^}]*)\} from '([^']+)';\n/g,
    (whole, typeOnly, names, module) => {
      const kept = names
        .split(',')
        .map(s => s.trim())
        .filter(name => {
          if (!name) return false;
          const bare = name.replace(/^type /, '');
          if (!ORPHANABLE.includes(bare)) return true;
          // `render() {` is Lit's own lifecycle method, not a call.
          const uses = new RegExp(`\\b${bare}\\b`, 'g');
          const body = text.slice(text.indexOf(whole) + whole.length);
          return uses.test(body.replace(/^\s*render\(\)\s*\{/gm, ''));
        });
      if (!kept.length) return '';
      return `import ${typeOnly ?? ''}{ ${kept.join(', ')} } from '${module}';\n`;
    }
  );
}

const report = { converted: [], rebound: [], skipped: [] };

for (const file of walk(SRC)) {
  const original = readFileSync(file, 'utf8');
  if (!/\.(header|footer|rowDetails)?[rR]enderer=/.test(original)) continue;

  const rel = relative(ROOT, file);
  const result = convertFile(original, rel);
  if (result.text !== original && WRITE) writeFileSync(file, result.text);
}

console.log(`Converted ${report.converted.length} renderer(s).`);
for (const line of report.converted) console.log('  ' + line);
if (report.rebound.length) {
  console.log(
    `\n\`this\` rebound to the host in ${report.rebound.length} method renderer(s) — review each:`
  );
  for (const line of report.rebound) console.log('  ' + line);
}
if (report.skipped.length) {
  console.log(`\nLeft alone (${report.skipped.length}):`);
  for (const line of report.skipped) console.log('  ' + line);
}
if (!WRITE) console.log('\nDry run — pass --write to apply.');
