/**
 * Checks that nothing read after `await confirmPrompt(...)` is component state.
 *
 * `window.confirm()` blocked the event loop, so the code after it ran in the
 * same turn as the code before. `await confirmPrompt(...)` does not: the grid
 * refreshes, cells are recycled, SignalR events land, and properties the host
 * set are reassigned — all while the dialog is open. A handler that reads
 * `this.<field>` afterwards can therefore act on a different row than the one
 * the prompt named. That defect was found in seven grid-cell controls, and then
 * again in `map-daemons`, which the first sweep missed because it was scoped to
 * a directory rather than to the hazard.
 *
 * The fix is always the same: snapshot into a local before the await, and use
 * the local from then on.
 *
 *   node tools/confirm-prompt-snapshot.mjs
 */

import { readFileSync, readdirSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { join, relative } from 'node:path';
import ts from 'typescript';

// fileURLToPath, not `.pathname`: on Windows the latter yields `/D:/a/...`,
// which join() turns into `D:\D:\a\...`. This runs in CI on windows-latest.
const ROOT = join(fileURLToPath(new URL('.', import.meta.url)), '..');
const SRC = join(ROOT, 'src');

/**
 * Fields that cannot go stale, because they are the component itself rather
 * than anything it was handed. `shadowRoot` is where the success and error
 * notifications get appended; the element is the same element either way.
 */
const ALLOWED = new Set(['shadowRoot', 'renderRoot', 'dispatchEvent']);

const walk = dir =>
  readdirSync(dir).flatMap(entry => {
    const full = join(dir, entry);
    return statSync(full).isDirectory()
      ? entry === 'apis'
        ? []
        : walk(full)
      : full.endsWith('.ts')
        ? [full]
        : [];
  });

let offenders = 0;

for (const file of walk(SRC)) {
  const text = readFileSync(file, 'utf8');
  if (!text.includes('confirmPrompt(')) continue;

  const source = ts.createSourceFile(file, text, ts.ScriptTarget.Latest, true);

  const visit = node => {
    if (
      ts.isAwaitExpression(node) &&
      ts.isCallExpression(node.expression) &&
      node.expression.expression.getText(source).includes('confirmPrompt')
    ) {
      const fn = enclosingFunction(node);
      if (fn?.body) {
        const reads = stateReadsAfter(fn.body, node.getEnd(), source);
        if (reads.length) {
          offenders += 1;
          const line =
            source.getLineAndCharacterOfPosition(node.getStart(source)).line + 1;
          console.log(
            `${relative(ROOT, file)}:${line}  ${fn.name?.getText(source) ?? '(anonymous)'}() ` +
              `reads this.${reads.join(', this.')} after the await — snapshot before it instead`
          );
        }
      }
    }
    ts.forEachChild(node, visit);
  };
  visit(source);
}

console.log(
  offenders
    ? `\n${offenders} confirmPrompt site(s) reading component state after the await.`
    : '\nNo confirmPrompt site reads component state after the await.'
);
process.exitCode = offenders ? 1 : 0;

function enclosingFunction(node) {
  let current = node.parent;
  while (
    current &&
    !ts.isMethodDeclaration(current) &&
    !ts.isFunctionDeclaration(current) &&
    !ts.isArrowFunction(current) &&
    !ts.isFunctionExpression(current)
  ) {
    current = current.parent;
  }
  return current;
}

/**
 * `this.<field>` reads positioned after `offset`, including those inside
 * subscribe callbacks — the network round trip is a second window in which the
 * row can change, so a fresh read there is the same defect.
 */
function stateReadsAfter(body, offset, source) {
  const reads = new Set();
  const scan = node => {
    if (
      ts.isPropertyAccessExpression(node) &&
      node.expression.kind === ts.SyntaxKind.ThisKeyword &&
      node.getStart(source) > offset &&
      !ALLOWED.has(node.name.text)
    ) {
      // Assigning to a field is not reading stale state, and calling a method
      // is behaviour rather than row identity.
      const isWrite =
        ts.isBinaryExpression(node.parent) &&
        node.parent.left === node &&
        node.parent.operatorToken.kind === ts.SyntaxKind.EqualsToken;
      const isCall =
        ts.isCallExpression(node.parent) && node.parent.expression === node;
      if (!isWrite && !isCall) reads.add(node.name.text);
    }
    ts.forEachChild(node, scan);
  };
  scan(body);
  return [...reads];
}
