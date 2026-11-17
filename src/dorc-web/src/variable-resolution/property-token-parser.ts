export type ParsedPropertyToken =
  | { kind: 'static'; value: string; rawText: string }
  | {
      kind: 'property';
      propertyName: string;
      rawText: string;
      syntax: 'dollar' | 'brace';
    }
  | { kind: 'escaped-dollar'; value: '$'; rawText: '$$' }
  | { kind: 'expression-prefix'; rawText: 'fn:'; expressionBody: string }
  | { kind: 'unclosed-token'; rawText: string };

const BRACE_TOKEN_PATTERN = /^\$\{([A-Za-z0-9_.-]+)\}$/;

export function parsePropertyTokens(input: string): ParsedPropertyToken[] {
  if (input.length === 0) {
    return [];
  }

  const braceMatch = BRACE_TOKEN_PATTERN.exec(input);
  if (braceMatch) {
    return [
      {
        kind: 'property',
        propertyName: braceMatch[1],
        rawText: input,
        syntax: 'brace'
      }
    ];
  }

  const tokens: ParsedPropertyToken[] = [];
  let cursor = 0;

  if (input.startsWith('fn:')) {
    const body = input.slice(3);
    tokens.push({
      kind: 'expression-prefix',
      rawText: 'fn:',
      expressionBody: body
    });
    cursor = 3;
  }

  let staticStart = cursor;

  while (cursor < input.length) {
    if (input[cursor] !== '$') {
      cursor++;
      continue;
    }

    if (cursor > staticStart) {
      const slice = input.slice(staticStart, cursor);
      tokens.push({ kind: 'static', value: slice, rawText: slice });
    }

    const nextIdx = cursor + 1;
    if (nextIdx >= input.length) {
      tokens.push({ kind: 'unclosed-token', rawText: '$' });
      return tokens;
    }

    if (input[nextIdx] === '$') {
      tokens.push({ kind: 'escaped-dollar', value: '$', rawText: '$$' });
      cursor += 2;
      staticStart = cursor;
      continue;
    }

    const closingIdx = input.indexOf('$', nextIdx);
    if (closingIdx === -1) {
      const unclosedSlice = input.slice(cursor);
      tokens.push({ kind: 'unclosed-token', rawText: unclosedSlice });
      return tokens;
    }

    const propName = input.slice(nextIdx, closingIdx);
    const rawToken = input.slice(cursor, closingIdx + 1);
    tokens.push({
      kind: 'property',
      propertyName: propName,
      rawText: rawToken,
      syntax: 'dollar'
    });
    cursor = closingIdx + 1;
    staticStart = cursor;
  }

  if (cursor > staticStart) {
    const tail = input.slice(staticStart, cursor);
    tokens.push({ kind: 'static', value: tail, rawText: tail });
  }

  return tokens;
}

export function extractReferencedPropertyNames(data: unknown): string[] {
  const seen = new Set<string>();
  const ordered: string[] = [];

  const addName = (name: string) => {
    const trimmed = name.trim();
    if (!trimmed) {
      return;
    }
    const lower = trimmed.toLowerCase();
    if (!seen.has(lower)) {
      seen.add(lower);
      ordered.push(trimmed);
    }
  };

  const visit = (node: unknown) => {
    if (typeof node === 'string') {
      for (const token of parsePropertyTokens(node)) {
        if (token.kind === 'property') {
          addName(token.propertyName);
        }
      }
      return;
    }

    if (Array.isArray(node)) {
      for (const item of node) {
        visit(item);
      }
      return;
    }

    if (node !== null && typeof node === 'object') {
      if (typeof (node as Node).nodeType === 'number') {
        return;
      }
      for (const [key, value] of Object.entries(
        node as Record<string, unknown>
      )) {
        for (const token of parsePropertyTokens(key)) {
          if (token.kind === 'property') {
            addName(token.propertyName);
          }
        }
        visit(value);
      }
    }
  };

  visit(data);
  return ordered;
}
