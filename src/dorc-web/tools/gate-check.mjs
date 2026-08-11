/**
 * Tests the three build gates against fixture trees.
 *
 * The gates in this directory are the only thing standing between the
 * migration's correctness rules and a silent regression, and until now the
 * only input exercising them was the repository they were written against.
 * That cannot distinguish a rule that holds from a rule that never fires: an
 * `if` with a typo'd property name reports nothing on clean code and nothing
 * on broken code, and both look like success.
 *
 * So each gate is run twice. Once over a `clean/` tree written to satisfy
 * every rule — anything reported there is a false positive, and a gate that
 * cries wolf gets suppressed, which is how gates die. Once per `stale/<case>/`
 * tree, each of which violates exactly one rule and must be reported with the
 * expected wording. A case that stops firing is a rule that has quietly
 * stopped applying.
 *
 * The fixtures are deliberately not app code and are excluded from tsconfig:
 * they contain the defects on purpose.
 *
 *   node tools/gate-check.mjs
 */

import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { join } from 'node:path';

const HERE = fileURLToPath(new URL('.', import.meta.url));
const fixture = (...parts) => join(HERE, 'gate-fixtures', ...parts);

/**
 * Each case names the gate, the tree to point it at, and what must be true of
 * the result. `expect` is matched as a substring of stdout+stderr — enough to
 * pin the rule that fired without freezing the exact phrasing of the report.
 */
const CASES = [
  // ── renderer-audit ────────────────────────────────────────────────────────
  {
    gate: 'renderer-audit.mjs',
    args: ['--check'],
    root: fixture('renderer-audit', 'clean'),
    exit: 0,
    expect: '0 imperative renderer bindings',
    why: 'directive bindings, and the word "renderer" inside a comment and a string'
  },
  ...[
    ['property-assignment', 'column.renderer = ...'],
    ['non-null-assertion', 'col!.renderer = ... — the form the old regex missed'],
    ['element-access', "col['headerRenderer'] = ..."],
    ['broken-chain', 'a chain prettier split across lines']
  ].map(([name, why]) => ({
    gate: 'renderer-audit.mjs',
    args: ['--check'],
    root: fixture('renderer-audit', 'stale', name),
    exit: 1,
    expect: '1 imperative renderer binding(s) found',
    why
  })),

  // ── renderer-deps ─────────────────────────────────────────────────────────
  {
    gate: 'renderer-deps.mjs',
    args: [],
    root: fixture('renderer-deps', 'clean'),
    exit: 0,
    expect: '9 directive binding(s) checked, 0 with a missing dependency',
    why: 'every rule in its passing form — and all nine bindings actually checked'
  },
  ...[
    ['missing-dep', 'reads readonly but does not depend on it', 'the base case'],
    [
      'inline-closure',
      'is not a class member or module-level function',
      'a fresh closure with [] is pinned at first paint'
    ],
    [
      'foreign-member',
      'is not declared in this file',
      'an inherited renderer, whose reads are not visible here'
    ],
    [
      'inherited-field',
      'reads environmentId but does not depend on it',
      'a reactive field declared by a base class in another file'
    ],
    [
      'destructuring',
      'reads selectedId but does not depend on it',
      '`const { selectedId } = this` is a read'
    ],
    [
      'getter-hop',
      'reads nameFilter but does not depend on it',
      'a getter runs on access, so its reads are render-time reads'
    ],
    [
      'call-argument',
      'reads readonlyFlag but does not depend on it',
      '`tags.map(this.tagTemplate)` invokes the member during the render'
    ],
    [
      'factory',
      'reads highlight but does not depend on it',
      '`this.valueRenderer(field)` — the returned closure is the renderer'
    ],
    [
      'setter-decorated',
      'reads rows but does not depend on it',
      'the decorator sits on the setter half of the pair'
    ],
    [
      'two-hop',
      'reads deep but does not depend on it',
      'two helper methods away, which depth 1 let through'
    ]
  ].map(([name, expect, why]) => ({
    gate: 'renderer-deps.mjs',
    args: [],
    root: fixture('renderer-deps', 'stale', name),
    exit: 1,
    expect,
    why
  })),

  // ── confirm-prompt-snapshot ───────────────────────────────────────────────
  {
    gate: 'confirm-prompt-snapshot.mjs',
    args: [],
    root: fixture('confirm-prompt', 'clean'),
    exit: 0,
    expect: 'No confirmPrompt site reads component state after the await',
    why: 'snapshot-first, shadowRoot, writes, calls, and the inline .then form'
  },
  ...[
    [
      'read-after-await',
      'reads this.serverId after the await',
      'the base case: the row can be recycled while the dialog is open'
    ],
    [
      'read-in-subscribe',
      'reads this.serverId after the await',
      'the network round trip is a second window'
    ],
    [
      'then-inline',
      'reads this.serverId after the await',
      '.then(cb) is the same hazard as await'
    ],
    [
      'then-delegate',
      'hands the answer to a callback declared elsewhere',
      'the body is not here; reported as uncheckable rather than passed'
    ]
  ].map(([name, expect, why]) => ({
    gate: 'confirm-prompt-snapshot.mjs',
    args: [],
    root: fixture('confirm-prompt', 'stale', name),
    exit: 1,
    expect,
    why
  }))
];

let failures = 0;

for (const testCase of CASES) {
  const result = spawnSync(
    process.execPath,
    [join(HERE, testCase.gate), ...testCase.args, '--root', testCase.root],
    { encoding: 'utf8' }
  );
  const output = `${result.stdout ?? ''}${result.stderr ?? ''}`;
  const label = `${testCase.gate} ${testCase.root.slice(HERE.length)}`;

  const problems = [];
  if (result.status !== testCase.exit) {
    problems.push(`exit ${result.status}, expected ${testCase.exit}`);
  }
  if (!output.includes(testCase.expect)) {
    problems.push(`output does not contain "${testCase.expect}"`);
  }

  if (problems.length) {
    failures += 1;
    console.log(`FAIL  ${label}`);
    console.log(`      ${testCase.why}`);
    for (const problem of problems) console.log(`      ${problem}`);
    console.log(
      output
        .trim()
        .split('\n')
        .map(l => `      | ${l}`)
        .join('\n')
    );
  } else {
    console.log(`ok    ${label}`);
  }
}

console.log(
  failures
    ? `\n${failures} of ${CASES.length} gate fixture(s) failed.`
    : `\nAll ${CASES.length} gate fixtures behaved as specified.`
);
process.exitCode = failures ? 1 : 0;
