/**
 * Renderer inventory and classification for the Vaadin Lit-directive migration.
 *
 * Inventory for migrating Vaadin renderers to the Lit directives.
 * Answers two questions that ad-hoc greps get wrong:
 *   1. Which renderer bindings exist, on which element, bound to which member?
 *   2. For each renderer, what does its body actually read and do?
 *
 * METHOD — stated so results are reproducible and challengeable.
 *
 * Binding sites are found with a regex, because Lit templates are strings and
 * the binding syntax genuinely is regular. EVERYTHING SEMANTIC uses the
 * TypeScript AST: member enumeration, decorator detection, body extraction and
 * `this.*` reads. The earlier audits failed because they approximated bodies
 * with a fixed character window and classified state by scanning for decorator
 * text near a name.
 *
 * Reads are resolved one level through getters and helper methods, because a
 * renderer that reads `this.filteredRows` where that getter reads
 * `this.nameFilter` depends on `nameFilter`. Deeper chains are reported as
 * such rather than silently truncated.
 *
 * Usage:  node tools/renderer-audit.mjs [--json]
 */

import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import ts from 'typescript';

const ROOT = join(fileURLToPath(new URL('.', import.meta.url)), '..');
const SRC = join(ROOT, 'src');
const SKIP_DIRS = new Set(['apis', 'node_modules']);

const RENDERER_PROPS = [
  'renderer',
  'headerRenderer',
  'footerRenderer',
  'rowDetailsRenderer'
];

// ── file discovery ──────────────────────────────────────────────────────────
function* walk(dir) {
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) {
      if (!SKIP_DIRS.has(entry)) yield* walk(full);
    } else if (entry.endsWith('.ts')) {
      yield full;
    }
  }
}

// ── AST helpers ─────────────────────────────────────────────────────────────
const decoratorsOf = (node) =>
  (ts.getDecorators?.(node) ?? []).map((d) => {
    const expr = ts.isCallExpression(d.expression) ? d.expression.expression : d.expression;
    return expr.getText();
  });

const REACTIVE_DECORATORS = new Set(['property', 'state']);

/**
 * Collects every class member in a source file: its kind, whether Lit will
 * treat it as reactive, and its body node for later analysis.
 */
function collectMembers(sourceFile) {
  const members = new Map();
  const visit = (node) => {
    if (ts.isClassDeclaration(node)) {
      for (const m of node.members) {
        const name = m.name?.getText?.();
        if (!name) continue;
        const decs = decoratorsOf(m);
        const reactive = decs.some((d) => REACTIVE_DECORATORS.has(d));
        let kind = 'unknown';
        let body = null;
        if (ts.isMethodDeclaration(m)) {
          kind = 'method';
          body = m.body ?? null;
        } else if (ts.isGetAccessor(m)) {
          kind = 'getter';
          body = m.body ?? null;
        } else if (ts.isPropertyDeclaration(m)) {
          // An arrow-function property is a renderer written as a field.
          if (m.initializer && (ts.isArrowFunction(m.initializer) || ts.isFunctionExpression(m.initializer))) {
            kind = 'function-field';
            body = m.initializer.body ?? null;
          } else {
            kind = 'field';
          }
        }
        members.set(name, {
          name,
          kind,
          reactive,
          decorators: decs,
          body,
          initializerText: ts.isPropertyDeclaration(m) ? m.initializer?.getText() ?? '' : ''
        });
      }
    }
    ts.forEachChild(node, visit);
  };
  visit(sourceFile);
  return members;
}

/** Every `this.<name>` accessed anywhere inside a node. */
function thisReads(body) {
  const found = new Set();
  if (!body) return found;
  const visit = (node) => {
    if (
      ts.isPropertyAccessExpression(node) &&
      node.expression.kind === ts.SyntaxKind.ThisKeyword
    ) {
      found.add(node.name.getText());
    }
    ts.forEachChild(node, visit);
  };
  visit(body);
  return found;
}

/**
 * Names imported from `@vaadin/*` in this file. Used to decide whether
 * `new Foo()` constructs a component or just a `Date`/`CustomEvent`/`Set` —
 * an earlier version of this script flagged all of those and reported 57
 * "element constructions" where the real figure is far lower.
 */
function vaadinImports(sourceFile) {
  const names = new Set();
  for (const stmt of sourceFile.statements) {
    if (!ts.isImportDeclaration(stmt)) continue;
    const from = stmt.moduleSpecifier.getText().replace(/['"]/g, '');
    if (!from.startsWith('@vaadin/')) continue;
    const bindings = stmt.importClause?.namedBindings;
    if (bindings && ts.isNamedImports(bindings)) {
      for (const el of bindings.elements) names.add(el.name.getText());
    }
    if (stmt.importClause?.name) names.add(stmt.importClause.name.getText());
  }
  return names;
}

/** Side effects that make a renderer more than a pure draw. */
function sideEffects(body, sourceFile, vaadinNames) {
  if (!body) return [];
  const text = body.getText(sourceFile);
  const effects = [];
  if (/\broot\s*\.\s*innerHTML\s*=/.test(text)) effects.push('innerHTML-write');
  if (/\.addEventListener\s*\(/.test(text)) effects.push('addEventListener');
  if (/\bthis\.\w*[Rr]oot\w*\s*=\s*root\b/.test(text)) effects.push('caches-root');
  if (/\bdocument\s*\.\s*createElement\s*\(/.test(text)) effects.push('createElement');
  if (/\broot\s*\.\s*appendChild\s*\(/.test(text)) effects.push('appendChild');
  // Only count construction of something actually imported from Vaadin.
  for (const m of text.matchAll(/\bnew\s+([A-Z]\w*)\s*\(/g)) {
    if (vaadinNames.has(m[1])) {
      effects.push('constructs-element');
      break;
    }
  }
  return effects;
}

// ── binding discovery ───────────────────────────────────────────────────────
/**
 * Finds `.renderer=${...}` style bindings and the element they sit on.
 * Walks backwards from the binding to the nearest opening tag, which is robust
 * against attribute values containing `>` inside `${...}`.
 */
function findBindings(text) {
  const out = [];
  const bindingRe = new RegExp(`\\.(${RENDERER_PROPS.join('|')})\\s*=\\s*("?\\$\\{|'\\$\\{)`, 'g');
  let m;
  while ((m = bindingRe.exec(text)) !== null) {
    const before = text.slice(0, m.index);
    const tagStart = before.lastIndexOf('<');
    const tagMatch = /^<([a-z][a-z0-9-]*)/.exec(text.slice(tagStart));
    const line = before.split('\n').length;

    // Extract the expression, balancing braces from the `${`.
    const exprStart = m.index + m[0].length;
    let depth = 1;
    let i = exprStart;
    while (i < text.length && depth > 0) {
      if (text[i] === '{') depth++;
      else if (text[i] === '}') depth--;
      i++;
    }
    out.push({
      prop: m[1],
      element: tagMatch ? tagMatch[1] : '(unknown)',
      expression: text.slice(exprStart, i - 1).trim(),
      line
    });
  }
  return [...out, ...findImperativeAssignments(text)];
}

/**
 * Finds plain assignments — `column.renderer = fn` — outside a template.
 *
 * The template form above is how the migrated code binds renderers, but it is
 * not the only way to set one. A direct assignment reintroduces exactly the two
 * defects the directives removed (`this` bound to the column, and a cell that
 * only repaints on requestContentUpdate()), and the gate has to see it or it
 * only enforces a style, not the property it claims to.
 */
function findImperativeAssignments(text) {
  const out = [];
  // `<identifier>.renderer = ` where the left side is not a template binding
  // (those are preceded by whitespace or a quote inside a tag, and always have
  // a `${` on the right).
  const assignRe = new RegExp(
    `\\b([A-Za-z_$][\\w$]*(?:\\.[A-Za-z_$][\\w$]*)*)\\.(${RENDERER_PROPS.join('|')})\\s*=\\s*([^=])`,
    'g'
  );
  let m;
  while ((m = assignRe.exec(text)) !== null) {
    // `.renderer=${...}` inside a template is the declarative form, already
    // collected above.
    if (m[3] === '$') continue;
    const before = text.slice(0, m.index);
    out.push({
      prop: m[2],
      element: `${m[1]} (assignment)`,
      expression: text.slice(m.index + m[0].length - 1, m.index + m[0].length + 60).split('\n')[0].trim(),
      line: before.split('\n').length
    });
  }
  return out;
}

/** Resolves a binding expression to the member it ultimately calls. */
function resolveTarget(expression) {
  const plain = /^this\.(\w+)$/.exec(expression);
  if (plain) return { member: plain[1], style: 'plain-ref' };

  const bound = /^this\.(\w+)\.bind\(this\)$/.exec(expression);
  if (bound) return { member: bound[1], style: 'bind-inline' };

  if (/^\(/.test(expression) || /=>/.test(expression.split('\n')[0])) {
    return { member: null, style: 'inline-arrow' };
  }

  const bare = /^(\w+)$/.exec(expression);
  if (bare) return { member: bare[1], style: 'module-or-local-ref' };

  return { member: null, style: 'other' };
}

/** A `_boundX = this.X.bind(this)` field forwards to X. */
function followBoundField(name, members) {
  const m = members.get(name);
  if (!m || m.kind !== 'field') return name;
  const fwd = /this\.(\w+)\.bind\(this\)/.exec(m.initializerText || '');
  return fwd ? fwd[1] : name;
}

// ── analysis ────────────────────────────────────────────────────────────────
const files = [...walk(SRC)].sort();
const bindings = [];
const renderers = new Map(); // `${file}#${member}` -> record

for (const file of files) {
  const text = readFileSync(file, 'utf8');
  if (!RENDERER_PROPS.some((p) => text.includes(`.${p}`))) continue;

  const sf = ts.createSourceFile(file, text, ts.ScriptTarget.Latest, true);
  const members = collectMembers(sf);
  const vaadinNames = vaadinImports(sf);
  const rel = relative(ROOT, file);

  for (const b of findBindings(text)) {
    const target = resolveTarget(b.expression);
    const memberName = target.member ? followBoundField(target.member, members) : null;
    bindings.push({ file: rel, ...b, ...target, resolved: memberName });

    if (!memberName) continue;
    const key = `${rel}#${memberName}`;
    if (renderers.has(key)) {
      renderers.get(key).bindingCount++;
      continue;
    }
    const member = members.get(memberName);
    if (!member) {
      renderers.set(key, { file: rel, name: memberName, missing: true, bindingCount: 1 });
      continue;
    }

    const direct = thisReads(member.body);
    // One level of transitive resolution through getters and helper methods.
    const transitive = new Set();
    let deeper = false;
    for (const r of direct) {
      const target2 = members.get(r);
      if (target2 && (target2.kind === 'getter' || target2.kind === 'method')) {
        for (const t of thisReads(target2.body)) {
          transitive.add(t);
          const t2 = members.get(t);
          if (t2 && (t2.kind === 'getter' || t2.kind === 'method')) deeper = true;
        }
      }
    }

    const all = new Set([...direct, ...transitive]);
    const reactive = [];
    const undecorated = [];
    const callables = [];
    const inherited = [];
    for (const r of all) {
      const mem = members.get(r);
      if (!mem) inherited.push(r);
      else if (mem.reactive) reactive.push(r);
      else if (mem.kind === 'method' || mem.kind === 'getter' || mem.kind === 'function-field') callables.push(r);
      else undecorated.push(r);
    }

    renderers.set(key, {
      file: rel,
      name: memberName,
      kind: member.kind,
      bindingCount: 1,
      effects: sideEffects(member.body, sf, vaadinNames),
      reactive: reactive.sort(),
      undecorated: undecorated.sort(),
      callables: callables.sort(),
      inherited: inherited.sort(),
      deeperChain: deeper
    });
  }
}

// ── report ──────────────────────────────────────────────────────────────────
const all = [...renderers.values()].filter((r) => !r.missing);
const missing = [...renderers.values()].filter((r) => r.missing);

const byElement = {};
for (const b of bindings) {
  const k = `${b.element} .${b.prop}`;
  byElement[k] = (byElement[k] ?? 0) + 1;
}

const stateful = all.filter((r) => r.reactive.length || r.undecorated.length);
const itemOnly = all.filter((r) => !r.reactive.length && !r.undecorated.length);
const withUndecorated = all.filter((r) => r.undecorated.length);
const sideEffecting = all.filter((r) => r.effects.length);

const effectCounts = {};
for (const r of sideEffecting) {
  for (const e of r.effects) effectCounts[e] = (effectCounts[e] ?? 0) + 1;
}

const styleCounts = {};
for (const b of bindings) styleCounts[b.style] = (styleCounts[b.style] ?? 0) + 1;

if (process.argv.includes('--json')) {
  console.log(JSON.stringify({ bindings, renderers: all, missing }, null, 2));
  process.exit(0);
}

// Gate mode, for the build. The migration left zero imperative `.renderer=`
// bindings; anything this finds is a new one, which reintroduces the two
// defects the directives removed — `this` bound to the grid column rather than
// the component, and a stale cell that only repaints on requestContentUpdate().
if (process.argv.includes('--check')) {
  if (bindings.length === 0) {
    console.log('Renderer audit: 0 imperative renderer bindings.');
    process.exit(0);
  }
  console.error(
    `Renderer audit: ${bindings.length} imperative renderer binding(s) found — use the @vaadin/*/lit renderer directives instead.`
  );
  for (const b of bindings) console.error(`  ${b.file}  ${b.member ?? b.style}`);
  process.exit(1);
}

const line = (s = '') => console.log(s);
line('# Renderer audit');
line();
line(`Files scanned: ${files.length}   Files with bindings: ${new Set(bindings.map((b) => b.file)).size}`);
line(`Total renderer bindings: ${bindings.length}`);
line(`Distinct renderer members: ${all.length}${missing.length ? `  (+${missing.length} unresolved)` : ''}`);
line();
line('## Bindings by host element');
for (const [k, v] of Object.entries(byElement).sort((a, b) => b[1] - a[1])) line(`  ${String(v).padStart(4)}  ${k}`);
line();
line('## Binding styles');
for (const [k, v] of Object.entries(styleCounts).sort((a, b) => b[1] - a[1])) line(`  ${String(v).padStart(4)}  ${k}`);
line();
line('## Classification (by AST body-read, not decorator scan)');
line(`  item-only (no this.<state> reads):            ${itemOnly.length}`);
line(`  stateful (reads reactive and/or plain fields): ${stateful.length}`);
line(`    ...of which read UNDECORATED fields:         ${withUndecorated.length}   <-- dependency arrays will NOT fire`);
line(`  renderers whose reads chain deeper than 1 hop: ${all.filter((r) => r.deeperChain).length}`);
line();
line('## Side effects');
line(`  renderers with any side effect: ${sideEffecting.length}`);
for (const [k, v] of Object.entries(effectCounts).sort((a, b) => b[1] - a[1])) line(`  ${String(v).padStart(4)}  ${k}`);
line();
if (withUndecorated.length) {
  line('## Renderers reading undecorated fields (must be promoted to @state first)');
  for (const r of withUndecorated.sort((a, b) => a.file.localeCompare(b.file))) {
    line(`  ${r.file}`);
    line(`      ${r.name}()  undecorated: [${r.undecorated.join(', ')}]${r.reactive.length ? `  reactive: [${r.reactive.join(', ')}]` : ''}`);
  }
  line();
}
if (sideEffecting.length) {
  line('## Side-effecting renderers');
  for (const r of sideEffecting.sort((a, b) => a.file.localeCompare(b.file))) {
    line(`  ${r.file}`);
    line(`      ${r.name}()  ${r.effects.join(', ')}`);
  }
  line();
}
if (missing.length) {
  line('## Unresolved binding targets (not class members — module-level or inherited)');
  for (const r of missing) line(`  ${r.file}  ${r.name}`);
}
