export type DiffOpType = 'keep' | 'insert' | 'delete';

export interface LineDiffOp {
  type: DiffOpType;
  line: string;
}

export interface CharDiffOp {
  type: DiffOpType;
  value: string;
}

export type SideBySideKind = 'keep' | 'change' | 'delete' | 'insert';

export interface SideBySideRow {
  left: string | null;
  right: string | null;
  kind: SideBySideKind;
}

function lcsTable<T>(
  oldItems: ArrayLike<T>,
  newItems: ArrayLike<T>
): number[][] {
  const m = oldItems.length;
  const n = newItems.length;
  const dp: number[][] = Array.from({ length: m + 1 }, () =>
    new Array(n + 1).fill(0)
  );
  for (let i = 1; i <= m; i++) {
    for (let j = 1; j <= n; j++) {
      if (oldItems[i - 1] === newItems[j - 1]) {
        dp[i][j] = dp[i - 1][j - 1] + 1;
      } else {
        dp[i][j] = Math.max(dp[i - 1][j], dp[i][j - 1]);
      }
    }
  }
  return dp;
}

function backtrack<T>(
  oldItems: ArrayLike<T>,
  newItems: ArrayLike<T>,
  dp: number[][]
): { type: DiffOpType; item: T }[] {
  const ops: { type: DiffOpType; item: T }[] = [];
  let i = oldItems.length;
  let j = newItems.length;
  while (i > 0 || j > 0) {
    if (i > 0 && j > 0 && oldItems[i - 1] === newItems[j - 1]) {
      ops.push({ type: 'keep', item: oldItems[i - 1] });
      i--;
      j--;
    } else if (j > 0 && (i === 0 || dp[i][j - 1] >= dp[i - 1][j])) {
      ops.push({ type: 'insert', item: newItems[j - 1] });
      j--;
    } else {
      ops.push({ type: 'delete', item: oldItems[i - 1] });
      i--;
    }
  }
  ops.reverse();
  return ops;
}

export function computeLineDiff(oldStr: string, newStr: string): LineDiffOp[] {
  const oldLines = oldStr.split('\n');
  const newLines = newStr.split('\n');
  return backtrack(oldLines, newLines, lcsTable(oldLines, newLines)).map(
    op => ({ type: op.type, line: op.item })
  );
}

// Character-level LCS diff used inside paired change lines. Returns a
// run-length-merged op list so adjacent same-type characters render as a
// single highlighted span rather than per-character.
export function computeCharDiff(oldStr: string, newStr: string): CharDiffOp[] {
  const ops = backtrack(oldStr, newStr, lcsTable(oldStr, newStr));
  const merged: CharDiffOp[] = [];
  for (const op of ops) {
    const last = merged[merged.length - 1];
    if (last && last.type === op.type) {
      last.value += op.item;
    } else {
      merged.push({ type: op.type, value: op.item });
    }
  }
  return merged;
}

// Convert line-LCS ops into a side-by-side row stream. Within each run of
// consecutive non-keep ops, deletes pair positionally with inserts:
//   - Pair k of (deletes, inserts) -> one 'change' row (left=del, right=ins)
//   - Surplus deletes -> 'delete' rows (left only)
//   - Surplus inserts -> 'insert' rows (right only)
// Keep ops emit a single row with the same line on both sides.
export function buildSideBySide(ops: LineDiffOp[]): SideBySideRow[] {
  const out: SideBySideRow[] = [];
  let i = 0;
  while (i < ops.length) {
    if (ops[i].type === 'keep') {
      out.push({ left: ops[i].line, right: ops[i].line, kind: 'keep' });
      i++;
      continue;
    }
    const dels: string[] = [];
    const ins: string[] = [];
    while (i < ops.length && ops[i].type !== 'keep') {
      if (ops[i].type === 'delete') dels.push(ops[i].line);
      else ins.push(ops[i].line);
      i++;
    }
    const max = Math.max(dels.length, ins.length);
    for (let k = 0; k < max; k++) {
      const l = dels[k];
      const r = ins[k];
      let kind: SideBySideKind;
      if (l !== undefined && r !== undefined) kind = 'change';
      else if (l !== undefined) kind = 'delete';
      else kind = 'insert';
      out.push({ left: l ?? null, right: r ?? null, kind });
    }
  }
  return out;
}

export function prettyJson(raw: string): string {
  try {
    return JSON.stringify(JSON.parse(raw), null, 2);
  } catch {
    return raw;
  }
}
