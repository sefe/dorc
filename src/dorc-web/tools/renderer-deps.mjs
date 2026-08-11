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
import { fileURLToPath } from 'node:url';
import { join, relative, resolve, sep } from 'node:path';
import ts from 'typescript';

// fileURLToPath, not `.pathname`: on Windows the latter yields `/D:/a/...`,
// which join() turns into `D:\D:\a\...`. This runs in CI on windows-latest.
const ROOT = join(fileURLToPath(new URL('.', import.meta.url)), '..');
// The tree to scan. Defaults to this package's `src`; `--root <dir>` points it
// at a fixture tree so the gate itself can be tested against known-good and
// known-bad inputs. Without that, the only thing exercising these rules is the
// repo they were written against, which cannot show a rule that never fires.
const SRC = (() => {
  const flag = process.argv.indexOf('--root');
  return flag !== -1 && process.argv[flag + 1]
    ? resolve(process.argv[flag + 1])
    : join(ROOT, 'src');
})();

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

  // Scoped to the class the binding is written in, not to the file. A file with
  // two classes would otherwise share one member map, so `this.rowRenderer` in
  // one could resolve to the other's — reporting the wrong reads, or none.
  // No file in `src` has two renderer-bearing classes today, which is exactly
  // why this had to be reasoned about rather than observed.
  const scopes = new Map();
  const scopeFor = node => {
    const owner = enclosingClass(node) ?? source;
    if (!scopes.has(owner)) {
      const scope = collectClass(owner);
      // A component's reactive fields are not all declared in its own file:
      // page-env-base declares `environmentId` and every environment tab
      // inherits it. Without walking up, a renderer reading an inherited
      // property passes the gate with an empty dependency array.
      for (const name of inheritedReactiveFields(owner, file, source)) {
        scope.reactive.add(name);
      }
      scopes.set(owner, scope);
    }
    return scopes.get(owner);
  };

  for (const call of directiveCalls(source)) {
    const { reactive, members } = scopeFor(call.node);
    if (call.member === null) {
      // Not `this.<member>`. A module-level function — declared here or
      // imported — cannot read component state at all, so an empty dependency
      // array is correct for it and nothing further needs checking. Anything
      // else (a closure declared inside render(), say) is a renderer whose
      // reads this tool cannot see, and it fails rather than passing silently:
      // the directive dirty-checks the dependency array, never the renderer
      // identity, so a fresh closure with `[]` is pinned at its first render.
      const line =
        source.getLineAndCharacterOfPosition(call.node.getStart(source)).line + 1;
      if (isModuleLevelFunction(source, file, call.source)) {
        checked += 1;
        continue;
      }
      stale += 1;
      console.log(
        `${relative(ROOT, file)}:${line}  ${call.directive}(${call.source}) ` +
          `is not a class member or module-level function — its reads cannot be checked`
      );
      continue;
    }

    const member = members.get(call.member);
    if (!member) {
      // Not silently skipped: a binding this tool cannot resolve is a binding
      // outside the "0 with a missing dependency" claim while looking covered
      // by it. Same policy as the non-`this.<member>` branch above. Reachable
      // when the renderer is inherited from a base class in another file.
      stale += 1;
      const line =
        source.getLineAndCharacterOfPosition(call.node.getStart(source)).line + 1;
      console.log(
        `${relative(ROOT, file)}:${line}  ${call.directive}(${call.display}) ` +
          `is not declared in this file — its reads cannot be checked`
      );
      continue;
    }
    checked += 1;

    const reads = stateReads(member, reactive, members);
    const missing = [...reads].filter(name => !call.deps.includes(name));
    if (!missing.length) continue;

    stale += 1;
    const line =
      source.getLineAndCharacterOfPosition(call.node.getStart(source)).line + 1;
    console.log(
      `${relative(ROOT, file)}:${line}  ${call.directive}(${call.display}) ` +
        `reads ${missing.join(', ')} but does not depend on ${missing.length > 1 ? 'them' : 'it'}`
    );
  }
}

console.log(
  `\n${checked} directive binding(s) checked, ${stale} with a missing dependency.`
);
process.exitCode = stale ? 1 : 0;

/** The class a node is written inside, or null at module scope. */
function enclosingClass(node) {
  let current = node.parent;
  while (
    current &&
    !ts.isClassDeclaration(current) &&
    !ts.isClassExpression(current)
  ) {
    current = current.parent;
  }
  return current ?? null;
}

/** Reactive field names and every class member, for the given class or file. */
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
function inheritedReactiveFields(node, file, moduleSource, seen = new Set()) {
  const names = new Set();

  const baseNames = [];
  const findHeritage = node => {
    if (
      (ts.isClassDeclaration(node) || ts.isClassExpression(node)) &&
      node.heritageClauses
    ) {
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
  findHeritage(node);
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
  findImports(moduleSource);

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
    // Whole-file for a base module: a mixin declares its class inside a
    // function, and there is no single class node to point at. The superset is
    // the conservative direction here — it can only add dependencies the gate
    // demands, never hide one.
    for (const name of collectClass(baseSource).reactive) names.add(name);
    for (const name of inheritedReactiveFields(
      baseSource,
      resolved,
      baseSource,
      seen
    )) {
      names.add(name);
    }
  }

  return names;
}

/**
 * Resolves a relative import to a `.ts` file on disk, or null.
 *
 * The result is constrained to `src/`. Nothing here takes external input — the
 * specifier comes from an import statement in the repo's own source — but a
 * `../..` chain would otherwise let the walk read outside the tree it is meant
 * to analyse, and there is no reason for it ever to do that.
 */
function resolveModule(fromFile, specifier) {
  const base = join(fromFile, '..', specifier.replace(/\.js$/, ''));
  // `sep`, not '/': join() emits backslashes on Windows, so a '/' test would
  // never match there and every base class would silently fail to resolve —
  // the gate would keep passing while checking less.
  if (!base.startsWith(SRC + sep)) return null;
  for (const candidate of [`${base}.ts`, join(base, 'index.ts'), base]) {
    try {
      if (statSync(candidate).isFile()) return candidate;
    } catch {
      // Not this one.
    }
  }
  return null;
}

/**
 * True when `name` resolves to a function declared at module scope — here or in
 * an imported module. Such a function has no component to read state from, so
 * an empty dependency array is right for it by construction.
 */
function isModuleLevelFunction(source, file, name, seen = new Set()) {
  if (!name || !/^[A-Za-z_$][\w$]*$/.test(name)) return false;
  if (seen.has(`${file}#${name}`)) return false;
  seen.add(`${file}#${name}`);

  for (const statement of source.statements) {
    if (
      ts.isFunctionDeclaration(statement) &&
      statement.name?.text === name
    ) {
      return true;
    }
    if (ts.isVariableStatement(statement)) {
      for (const decl of statement.declarationList.declarations) {
        if (
          ts.isIdentifier(decl.name) &&
          decl.name.text === name &&
          decl.initializer &&
          (ts.isArrowFunction(decl.initializer) ||
            ts.isFunctionExpression(decl.initializer))
        ) {
          return true;
        }
      }
    }
    if (
      ts.isImportDeclaration(statement) &&
      ts.isStringLiteral(statement.moduleSpecifier) &&
      statement.importClause?.namedBindings &&
      ts.isNamedImports(statement.importClause.namedBindings)
    ) {
      const imported = statement.importClause.namedBindings.elements.find(
        e => e.name.text === name
      );
      if (!imported) continue;
      const target = resolveModule(file, statement.moduleSpecifier.text);
      if (!target) return false;
      const original = (imported.propertyName ?? imported.name).text;
      const targetSource = ts.createSourceFile(
        target,
        readFileSync(target, 'utf8'),
        ts.ScriptTarget.Latest,
        true
      );
      return isModuleLevelFunction(targetSource, target, original, seen);
    }
  }
  return false;
}

/** `${fooRenderer(this.bar, [this.baz])}` occurrences. */
function directiveCalls(source) {
  const out = [];
  const visit = node => {
    if (
      ts.isCallExpression(node) &&
      ts.isIdentifier(node.expression) &&
      DIRECTIVES.includes(node.expression.text) &&
      node.arguments[0]
    ) {
      const target = node.arguments[0];
      // Anything that is not `this.<member>` cannot be resolved to a class
      // member, so its reads cannot be checked. Report it rather than dropping
      // it: silently skipping is how a binding ends up outside the "0 with a
      // missing dependency" claim while looking covered by it.
      // `this.valueRenderer('FromValue')` — a factory method. The renderer is
      // whatever it returns, and that closure's reads are the ones that matter,
      // so resolve the factory and check its body.
      const factory =
        ts.isCallExpression(target) &&
        ts.isPropertyAccessExpression(target.expression) &&
        target.expression.expression.kind === ts.SyntaxKind.ThisKeyword
          ? target.expression.name.text
          : null;

      if (
        factory === null &&
        (!ts.isPropertyAccessExpression(target) ||
          target.expression.kind !== ts.SyntaxKind.ThisKeyword)
      ) {
        out.push({
          node,
          directive: node.expression.text,
          member: null,
          source: ts.isIdentifier(target) ? target.text : target.getText(source),
          deps: []
        });
        ts.forEachChild(node, visit);
        return;
      }
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
        member: factory ?? target.name.text,
        display: factory ? `this.${factory}(…)` : `this.${target.name.text}`,
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
        } else if (
          depth > 0 &&
          members.has(name) &&
          !seen.has(name) &&
          (ts.isGetAccessor(members.get(name)) ||
            (ts.isCallExpression(n.parent) && n.parent.arguments.includes(n)))
        ) {
          // Referencing a member without calling it yields a *value*, and
          // whether its reads are render-time reads depends on when that value
          // is invoked:
          //   - a getter runs on access, so now;
          //   - a member passed as a call argument — `tags.map(this.rowTemplate)`
          //     — is invoked by that call, so also now;
          //   - anything else is a function that runs later.
          //     `@click="${this._confirmPlan}"` is a handler, and following it
          //     would report what the click reads as a dependency of the render.
          // Narrowing this to getters alone (which is how it was first written)
          // stopped following the map/filter idiom, which is used at 14 sites in
          // this codebase — a renderer adopting it would go stale silently.
          seen.add(name);
          scan(members.get(name), depth - 1);
        }
        // Keep descending: `this.a.b` and nested calls still matter.
      }
      // `const { selectedId } = this` is a read of every name it binds. Both
      // gates matched PropertyAccessExpression only, so destructuring from
      // `this` was invisible — and it is already house style here
      // (`const { outlet, resolver } = this;` in router.ts).
      if (
        ts.isVariableDeclaration(n) &&
        n.initializer &&
        n.initializer.kind === ts.SyntaxKind.ThisKeyword &&
        ts.isObjectBindingPattern(n.name)
      ) {
        for (const element of n.name.elements) {
          const key = element.propertyName ?? element.name;
          if (ts.isIdentifier(key) && reactive.has(key.text)) reads.add(key.text);
        }
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
      // A returned function is the exception: `valueRenderer(field)` is a
      // factory whose returned arrow *is* the renderer, so its reads are
      // render-time reads.
      if (
        (ts.isArrowFunction(n) || ts.isFunctionExpression(n)) &&
        !(ts.isCallExpression(n.parent) && n.parent.arguments.includes(n)) &&
        !ts.isReturnStatement(n.parent)
      ) {
        return;
      }
      ts.forEachChild(n, visit);
    };
    ts.forEachChild(body, visit);
  };

  // Depth 2, not 1: a renderer reaching reactive state through two helper
  // methods slipped through at depth 1.
  scan(member, 2);
  return reads;
}
