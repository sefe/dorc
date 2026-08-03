/**
 * Checks renderer directive dependency arrays against what the renderer reads.
 *
 * A Lit renderer directive re-runs when a value in its dependency array
 * changes. A renderer that reads component state but declares no dependency on
 * it paints once and then goes stale — the defect this migration was for. This
 * reports every such case so the arrays can be filled in deliberately.
 *
 * Only reactive fields (`@state` / `@property`) are reported. An undecorated
 * field cannot drive an update either way: it does not trigger a host re-render,
 * so putting it in the array achieves nothing.
 *
 *   node tools/renderer-deps.mjs
 */

import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';
import ts from 'typescript';

const ROOT = new URL('..', import.meta.url).pathname.replace(/\/$/, '');
const SRC = join(ROOT, 'src');

const DIRECTIVES = [
  'columnBodyRenderer',
  'columnHeaderRenderer',
  'columnFooterRenderer',
  'gridRowDetailsRenderer',
  'comboBoxRenderer',
  'selectRenderer',
  'notificationRenderer',
  'dialogRenderer',
  'dialogFooterRenderer',
  'dialogHeaderRenderer'
];

const walk = dir =>
  readdirSync(dir).flatMap(entry => {
    const full = join(dir, entry);
    return statSync(full).isDirectory()
      ? walk(full)
      : full.endsWith('.ts')
        ? [full]
        : [];
  });

let stale = 0;
let checked = 0;

for (const file of walk(SRC)) {
  const text = readFileSync(file, 'utf8');
  if (!DIRECTIVES.some(d => text.includes(`${d}(`))) continue;

  const source = ts.createSourceFile(file, text, ts.ScriptTarget.Latest, true);
  const { reactive, members } = collectClass(source);
  // A component's reactive fields are not all declared in its own file:
  // page-env-base declares `environmentId` and every environment tab inherits
  // it. Without walking up, a renderer reading an inherited property passes
  // the gate with an empty dependency array.
  for (const name of inheritedReactiveFields(source, file)) reactive.add(name);

  for (const call of directiveCalls(source)) {
    const member = members.get(call.member);
    if (!member) continue;
    checked += 1;

    const reads = stateReads(member, reactive, members);
    const missing = [...reads].filter(name => !call.deps.includes(name));
    if (!missing.length) continue;

    stale += 1;
    const line =
      source.getLineAndCharacterOfPosition(call.node.getStart(source)).line + 1;
    console.log(
      `${relative(ROOT, file)}:${line}  ${call.directive}(this.${call.member}) ` +
        `reads ${missing.join(', ')} but does not depend on ${missing.length > 1 ? 'them' : 'it'}`
    );
  }
}

console.log(
  `\n${checked} directive binding(s) checked, ${stale} with a missing dependency.`
);
process.exitCode = stale ? 1 : 0;

/** Reactive field names and every class member, for the enclosing class. */
function collectClass(source) {
  const reactive = new Set();
  const members = new Map();

  const visit = node => {
    // A reactive accessor may carry its decorator on either half of the
    // get/set pair — Lit only needs one — so both have to be inspected, or a
    // setter-decorated property reads as non-reactive and its renderers slip
    // through the check entirely.
    if (
      (ts.isPropertyDeclaration(node) ||
        ts.isGetAccessor(node) ||
        ts.isSetAccessor(node)) &&
      ts.isIdentifier(node.name)
    ) {
      const decorators = ts.getDecorators?.(node) ?? [];
      const isReactive = decorators.some(d => {
        const expression = ts.isCallExpression(d.expression)
          ? d.expression.expression
          : d.expression;
        return (
          ts.isIdentifier(expression) &&
          (expression.text === 'state' || expression.text === 'property')
        );
      });
      if (isReactive) reactive.add(node.name.text);
      // Keep the first body seen; a setter body is not where reads live.
      if (!members.has(node.name.text) || !ts.isSetAccessor(node)) {
        members.set(node.name.text, node);
      }
    } else if (ts.isMethodDeclaration(node) && ts.isIdentifier(node.name)) {
      members.set(node.name.text, node);
    }
    ts.forEachChild(node, visit);
  };
  visit(source);
  return { reactive, members };
}

/**
 * Reactive field names declared by base classes, followed through relative
 * imports. Handles `extends Base` and `extends SomeMixin(Base)`; a base that
 * cannot be resolved to a file in `src` is simply skipped, so the gate stays
 * conservative rather than wrong.
 */
function inheritedReactiveFields(source, file, seen = new Set()) {
  const names = new Set();

  const baseNames = [];
  const findHeritage = node => {
    if (ts.isClassDeclaration(node) && node.heritageClauses) {
      for (const clause of node.heritageClauses) {
        for (const type of clause.types) {
          const expr = type.expression;
          if (ts.isIdentifier(expr)) baseNames.push(expr.text);
          // `ResponsiveMixin(PageElement)` — both halves declare fields. The
          // argument is the base class; the callee is a function returning a
          // class of its own, and that class's reactive properties are just as
          // inherited. collectClass walks nested classes, so resolving the
          // mixin's module is enough to pick them up.
          else if (ts.isCallExpression(expr)) {
            if (ts.isIdentifier(expr.expression)) {
              baseNames.push(expr.expression.text);
            }
            for (const arg of expr.arguments) {
              if (ts.isIdentifier(arg)) baseNames.push(arg.text);
            }
          }
        }
      }
    }
    ts.forEachChild(node, findHeritage);
  };
  findHeritage(source);
  if (!baseNames.length) return names;

  // Map each imported name to the module it came from.
  const importedFrom = new Map();
  const findImports = node => {
    if (
      ts.isImportDeclaration(node) &&
      node.importClause?.namedBindings &&
      ts.isNamedImports(node.importClause.namedBindings) &&
      ts.isStringLiteral(node.moduleSpecifier)
    ) {
      for (const element of node.importClause.namedBindings.elements) {
        importedFrom.set(element.name.text, node.moduleSpecifier.text);
      }
    }
    ts.forEachChild(node, findImports);
  };
  findImports(source);

  for (const base of baseNames) {
    const specifier = importedFrom.get(base);
    if (!specifier || !specifier.startsWith('.')) continue;

    const resolved = resolveModule(file, specifier);
    if (!resolved || seen.has(resolved)) continue;
    seen.add(resolved);

    const baseText = readFileSync(resolved, 'utf8');
    const baseSource = ts.createSourceFile(
      resolved,
      baseText,
      ts.ScriptTarget.Latest,
      true
    );
    for (const name of collectClass(baseSource).reactive) names.add(name);
    for (const name of inheritedReactiveFields(baseSource, resolved, seen)) {
      names.add(name);
    }
  }

  return names;
}

/** Resolves a relative import to a `.ts` file on disk, or null. */
function resolveModule(fromFile, specifier) {
  const base = join(fromFile, '..', specifier.replace(/\.js$/, ''));
  for (const candidate of [`${base}.ts`, join(base, 'index.ts'), base]) {
    try {
      if (statSync(candidate).isFile()) return candidate;
    } catch {
      // Not this one.
    }
  }
  return null;
}

/** `${fooRenderer(this.bar, [this.baz])}` occurrences. */
function directiveCalls(source) {
  const out = [];
  const visit = node => {
    if (
      ts.isCallExpression(node) &&
      ts.isIdentifier(node.expression) &&
      DIRECTIVES.includes(node.expression.text) &&
      node.arguments[0] &&
      ts.isPropertyAccessExpression(node.arguments[0]) &&
      node.arguments[0].expression.kind === ts.SyntaxKind.ThisKeyword
    ) {
      const depsArg = node.arguments[1];
      const deps =
        depsArg && ts.isArrayLiteralExpression(depsArg)
          ? depsArg.elements
              .filter(
                e =>
                  ts.isPropertyAccessExpression(e) &&
                  e.expression.kind === ts.SyntaxKind.ThisKeyword
              )
              .map(e => e.name.text)
          : [];
      out.push({
        node,
        directive: node.expression.text,
        member: node.arguments[0].name.text,
        deps
      });
    }
    ts.forEachChild(node, visit);
  };
  visit(source);
  return out;
}

/** The body of a member declared as a method or as an arrow-function field. */
function functionBodyOf(node) {
  if (ts.isMethodDeclaration(node) || ts.isGetAccessor(node)) return node.body;
  if (
    ts.isPropertyDeclaration(node) &&
    node.initializer &&
    (ts.isArrowFunction(node.initializer) ||
      ts.isFunctionExpression(node.initializer))
  ) {
    return node.initializer.body;
  }
  return null;
}

/**
 * Reactive fields the renderer reads, following one hop through helper methods
 * so that a renderer calling `this.canEditScripts()` still reports the roles it
 * ultimately depends on.
 */
function stateReads(member, reactive, members) {
  const reads = new Set();
  const seen = new Set();

  const scan = (node, depth) => {
    // Start from the function body, so the member's own arrow initialiser is
    // not mistaken for a nested deferred callback by the check below.
    const body = functionBodyOf(node) ?? node;
    const visit = n => {
      if (
        ts.isPropertyAccessExpression(n) &&
        n.expression.kind === ts.SyntaxKind.ThisKeyword
      ) {
        const name = n.name.text;
        // A call is a hop, not a read: `this.foo()` depends on what foo reads.
        const isCallee =
          ts.isCallExpression(n.parent) && n.parent.expression === n;
        if (isCallee) {
          if (depth > 0 && members.has(name) && !seen.has(name)) {
            seen.add(name);
            scan(members.get(name), depth - 1);
          }
        } else if (reactive.has(name)) {
          reads.add(name);
          if (depth > 0 && members.has(name) && !seen.has(name)) {
            // A reactive getter's own reads count too.
            seen.add(name);
            scan(members.get(name), depth - 1);
          }
        } else if (depth > 0 && members.has(name) && !seen.has(name)) {
          seen.add(name);
          scan(members.get(name), depth - 1);
        }
        // Keep descending: `this.a.b` and nested calls still matter.
      }
      // A write is not a read — `this.filter = x` in a handler is not a
      // dependency, and treating it as one would reset the field being typed in.
      if (
        ts.isBinaryExpression(n) &&
        n.operatorToken.kind === ts.SyntaxKind.EqualsToken &&
        ts.isPropertyAccessExpression(n.left) &&
        n.left.expression.kind === ts.SyntaxKind.ThisKeyword
      ) {
        ts.forEachChild(n.right, visit);
        return;
      }
      // An event handler runs long after the render that installed it, so what
      // it reads is not a dependency of the render. Handlers appear as bare
      // template substitutions (`@click="${() => ...}"`); callbacks that do run
      // during the render are call arguments (`items.map(x => ...)`), so those
      // are still followed.
      if (
        (ts.isArrowFunction(n) || ts.isFunctionExpression(n)) &&
        !(ts.isCallExpression(n.parent) && n.parent.arguments.includes(n))
      ) {
        return;
      }
      ts.forEachChild(n, visit);
    };
    ts.forEachChild(body, visit);
  };

  scan(member, 1);
  return reads;
}
