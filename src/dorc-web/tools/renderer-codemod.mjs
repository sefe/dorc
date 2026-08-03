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

function convertFile(text, rel) {
  const source = ts.createSourceFile(rel, text, ts.ScriptTarget.Latest, true);

  const bindings = findBindings(text);
  if (!bindings.length) return { text };

  const members = collectRenderers(source);

  /** Text edits as {start, end, replacement}, applied back-to-front. */
  const edits = [];
  const neededImports = new Map();
  let usedRender = false;

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
    usedRender = true;
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
  if (usedRender) out = dropUnusedRenderImport(out);

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
      return; // don't descend: the root reference here is expected
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
  let body = text.slice(offset, node.getEnd());

  // `render(X, root);` -> `return X;`, innermost-last so offsets stay valid.
  const statementEdits = [];
  for (const call of renderCalls) {
    const statement = call.parent;
    const isBareStatement =
      ts.isExpressionStatement(statement) && statement.expression === call;
    if (!isBareStatement) {
      return { ok: false, reason: 'render() result is used as a value' };
    }
    statementEdits.push({
      start: statement.getStart(source) - offset,
      end: statement.getEnd() - offset,
      replacement: `return ${text.slice(call.arguments[0].getStart(source), call.arguments[0].getEnd())};`
    });
  }
  for (const edit of statementEdits.sort((a, b) => b.start - a.start)) {
    body = body.slice(0, edit.start) + edit.replacement + body.slice(edit.end);
  }

  // Rewrite the parameter list. Directive body renderers get
  // `(item, model, column)`; header and footer renderers get `(column)`.
  const params = arrow.parameters;
  const modelParam = params[2] && ts.isIdentifier(params[2].name) ? params[2].name.text : null;
  const columnParam = params[1] && ts.isIdentifier(params[1].name) ? params[1].name.text : null;

  const paramList = [];
  if (takesItem) {
    paramList.push('item');
    if (modelParam && !modelParam.startsWith('_')) paramList.push(modelParam);
    else if (columnParam && !columnParam.startsWith('_')) paramList.push('_model', columnParam);
  } else if (columnParam && !columnParam.startsWith('_')) {
    paramList.push(columnParam);
  }

  const paramsStart = params.length
    ? params[0].getStart(source) - offset
    : body.indexOf('(') + 1;
  const paramsEnd = params.length
    ? params[params.length - 1].getEnd() - offset
    : paramsStart;
  body = body.slice(0, paramsStart) + paramList.join(', ') + body.slice(paramsEnd);

  return { ok: true, text: body, readsThis };
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

/** Drops `render` from the lit import when nothing calls it any more. */
function dropUnusedRenderImport(text) {
  if (/\brender\s*\(/.test(text.replace(/^\s*render\(\)\s*\{/gm, ''))) return text;
  return text.replace(
    /import \{([^}]*)\} from 'lit';/,
    (whole, names) => {
      const kept = names
        .split(',')
        .map(s => s.trim())
        .filter(s => s && s !== 'render');
      return kept.length ? `import { ${kept.join(', ')} } from 'lit';` : '';
    }
  );
}
