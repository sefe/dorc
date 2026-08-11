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
 * Known limit: this checks the handler's own body. It does not follow a call
 * made after the await into the method it names. Following it was tried and
 * backed out — a delegate that refreshes the view (`loadMappedDaemons()`,
 * `performDelete()`) legitimately reads current state, so the hop reported
 * seven false positives on correct code, and a gate that cries wolf gets
 * suppressed. Handlers that delegate must thread the snapshot in as a
 * parameter, as `server-controls.performDeleteServer` does.
 *
 * Where that limit would hide a site rather than merely under-check one —
 * `.then(this.handler)`, whose entire post-confirmation body is elsewhere —
 * the site is reported as uncheckable instead of passing silently.
 *
 *   node tools/confirm-prompt-snapshot.mjs
 */

import { readFileSync, readdirSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { join, relative, resolve } from 'node:path';
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
    // `await confirmPrompt(...)` and `confirmPrompt(...).then(...)` are the same
    // hazard; only the first was visible while this matched AwaitExpression
    // alone.
    const isAwaited =
      ts.isAwaitExpression(node) &&
      ts.isCallExpression(node.expression) &&
      node.expression.expression.getText(source).includes('confirmPrompt');
    const isThened =
      ts.isCallExpression(node) &&
      ts.isPropertyAccessExpression(node.expression) &&
      node.expression.name.text === 'then' &&
      node.expression.expression.getText(source).includes('confirmPrompt(');

    if (isAwaited || isThened) {
      // `.then(this.handleAnswer)` — the post-confirmation code is somewhere
      // else entirely, and following it is the delegate hop this tool
      // deliberately does not take (see the header). Falling back to "the rest
      // of the enclosing function" would scan the wrong body and report
      // nothing, so the site would sit inside the clean bill of health while
      // never having been looked at. Report it as uncheckable instead — the
      // same policy renderer-deps applies to a binding it cannot resolve.
      const thenCallback = isThened ? node.arguments[0] : undefined;
      const isInlineCallback =
        thenCallback &&
        (ts.isArrowFunction(thenCallback) ||
          ts.isFunctionExpression(thenCallback));

      if (isThened && !isInlineCallback) {
        offenders += 1;
        const line =
          source.getLineAndCharacterOfPosition(node.getStart(source)).line + 1;
        console.log(
          `${relative(ROOT, file)}:${line}  confirmPrompt().then(` +
            `${thenCallback ? thenCallback.getText(source) : ''}) hands the answer ` +
            `to a callback declared elsewhere — its reads cannot be checked; ` +
            `inline it, or await and pass the snapshot in as a parameter`
        );
        ts.forEachChild(node, visit);
        return;
      }

      // For `.then(cb)` the post-confirmation code is the callback body, and all
      // of it runs after the answer — so scan that with no offset. For `await`
      // it is the rest of the enclosing function, after the await expression.
      const scanTarget = isInlineCallback
        ? { body: thenCallback.body, offset: 0, node: thenCallback }
        : (() => {
            const fn = enclosingFunction(node);
            return fn?.body
              ? { body: fn.body, offset: node.getEnd(), node: fn }
              : null;
          })();

      if (scanTarget?.body) {
        const fn = enclosingFunction(node);
        const reads = stateReadsAfter(
          scanTarget.body,
          scanTarget.offset,
          source
        );
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
