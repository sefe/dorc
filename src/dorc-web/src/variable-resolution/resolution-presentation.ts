import type {
  PropertyApiModel,
  PropertyResolutionTraceApiModel,
  PropertyValueScopeOptionApiModel
} from '../apis/dorc-api';
import {
  isAdminShareDollarAt,
  ParsedPropertyToken,
  parsePropertyTokens
} from './property-token-parser';

export type ResolutionSeverity =
  | 'resolved'
  | 'warning'
  | 'masked'
  | 'unresolved'
  | 'pending';

export interface PresentedTrace {
  severity: ResolutionSeverity;
  effectiveDisplayValue: string;
  hasEffectiveValue: boolean;
  sourceLabel: string;
  diagnosticTitle: string;
  diagnosticDetail: string;
  hasBlockedTokensByUnescapedDollar: boolean;
}

export interface UnescapedDollarInspection {
  hasUnclosedDollar: boolean;
  hasOtherPropertyTokens: boolean;
  hasBraceSyntax: boolean;
  isTypingOpenToken: boolean;
  escapedSuggestion: string;
  braceConvertedSuggestion: string;
}

export interface InsertableDollarTokenOption {
  ValueOption: string;
  SampleResolvedValue: unknown;
  CategoryLabel: string;
}

const ADMIN_SHARE_PATTERN = /^(?:[A-Za-z]|ADMIN|IPC)\$(?=[\\/]|$)/i;
const VALID_DOLLAR_TOKEN_PATTERN = /^\$([^$\\/\s]+)\$/;
const BRACE_TOKEN_AT_START_PATTERN = /^\$\{\s*([A-Za-z0-9_.-]+)\s*\}/;
const OPEN_TOKEN_AT_END_PATTERN = /^\$[A-Za-z0-9_.-]*$/;
const EXPRESSION_PREFIX = 'fn:';

export function lookupTrace(
  traces: ReadonlyMap<string, PropertyResolutionTraceApiModel>,
  propertyName: string
): PropertyResolutionTraceApiModel | undefined {
  return (
    traces.get(propertyName) ?? traces.get(propertyName.toLowerCase())
  );
}

export function inspectUnescapedDollars(raw: string): UnescapedDollarInspection {
  if (!raw || !raw.includes('$')) {
    return {
      hasUnclosedDollar: false,
      hasOtherPropertyTokens: false,
      hasBraceSyntax: false,
      isTypingOpenToken: false,
      escapedSuggestion: raw,
      braceConvertedSuggestion: raw
    };
  }

  const trimmedStart = raw.trimStart();
  const leadingWhitespaceLen = raw.length - trimmedStart.length;
  const hasFnPrefix = trimmedStart.startsWith(EXPRESSION_PREFIX);
  let out = hasFnPrefix
    ? raw.slice(0, leadingWhitespaceLen + EXPRESSION_PREFIX.length)
    : '';
  let braceOut = out;
  let i = out.length;

  let hasUnclosedDollar = false;
  let hasOtherPropertyTokens = false;
  let hasBraceSyntax = false;
  let isTypingOpenToken = false;

  while (i < raw.length) {
    // Already-escaped $$
    if (raw[i] === '$' && raw[i + 1] === '$') {
      out += '$$';
      braceOut += '$$';
      i += 2;
      continue;
    }

    // Windows admin/hidden share like E$\, C$/, ADMIN$\, IPC$\ at start or after \ or /
    const prevChar = i > 0 ? raw[i - 1] : '';
    if (i === 0 || prevChar === '\\' || prevChar === '/') {
      const adminMatch = ADMIN_SHARE_PATTERN.exec(raw.slice(i));
      if (adminMatch) {
        const sharePrefix = adminMatch[0].slice(0, -1); // e.g. 'E' or 'ADMIN'
        hasUnclosedDollar = true;
        out += `${sharePrefix}$$`;
        braceOut += adminMatch[0];
        i += adminMatch[0].length;
        continue;
      }
    }

    if (raw[i] === '$') {
      const sliceFromHere = raw.slice(i);
      const braceMatch = BRACE_TOKEN_AT_START_PATTERN.exec(sliceFromHere);
      if (braceMatch) {
        hasBraceSyntax = true;
        hasOtherPropertyTokens = true;
        out += `$${braceMatch[1]}$`;
        braceOut += `$${braceMatch[1]}$`;
        i += braceMatch[0].length;
        continue;
      }

      const tokenMatch = VALID_DOLLAR_TOKEN_PATTERN.exec(sliceFromHere);
      if (tokenMatch) {
        hasOtherPropertyTokens = true;
        out += tokenMatch[0];
        braceOut += tokenMatch[0];
        i += tokenMatch[0].length;
        continue;
      }

      if (OPEN_TOKEN_AT_END_PATTERN.test(sliceFromHere)) {
        isTypingOpenToken = true;
        out += sliceFromHere;
        braceOut += sliceFromHere;
        break;
      }

      // Lone or invalid $ (not a valid $PropertyName$ token)
      hasUnclosedDollar = true;
      out += '$$';
      braceOut += '$';
      i += 1;
      continue;
    }

    out += raw[i];
    braceOut += raw[i];
    i += 1;
  }

  return {
    hasUnclosedDollar,
    hasOtherPropertyTokens,
    hasBraceSyntax,
    isTypingOpenToken,
    escapedSuggestion: hasUnclosedDollar || hasBraceSyntax ? out : raw,
    braceConvertedSuggestion: hasBraceSyntax ? braceOut : raw
  };
}

const RUNTIME_DOLLAR_TOKENS: Array<{ name: string; description: string }> = [
  { name: 'EnvironmentName', description: 'Deploy target environment name' },
  {
    name: 'EnvironmentShortName',
    description: 'Short environment identifier'
  },
  { name: 'DropFolder', description: 'Artifact drop folder at deploy time' },
  { name: 'ScriptRoot', description: 'Scripts root directory at deploy time' },
  {
    name: 'DeploymentLogDir',
    description: 'Deployment log directory at deploy time'
  },
  { name: 'BuildNumber', description: 'Build / release number at deploy time' },
  {
    name: 'IsProd',
    description: 'True when deploying to a production environment'
  }
];

const FN_EXPRESSION_SNIPPETS: Array<{
  snippet: string;
  description: string;
}> = [
  {
    snippet: 'fn:System.IO.Path.Combine("$DropFolder$", "app")',
    description: 'C# Path.Combine with $Variable$ expansion'
  },
  {
    snippet: 'fn:"$EnvironmentShortName$".ToLowerInvariant()',
    description: 'C# string transformation on a $Variable$ token'
  },
  {
    snippet: 'fn:System.DateTime.UtcNow.ToString("yyyyMMdd")',
    description: 'Dynamic UTC date string evaluated at deploy time'
  }
];

export function buildInsertableDollarTokens(
  scopeOptions: PropertyValueScopeOptionApiModel[] | undefined,
  properties: PropertyApiModel[] | undefined,
  excludePropertyName?: string
): InsertableDollarTokenOption[] {
  const seen = new Set<string>();
  const result: InsertableDollarTokenOption[] = [];
  const excludedLower = excludePropertyName?.trim().toLowerCase() ?? '';

  for (const opt of scopeOptions ?? []) {
    const token = opt.ValueOption?.trim();
    if (!token || token === '$$' || token.includes(';')) continue;
    const key = token.toLowerCase();
    if (seen.has(key)) continue;
    seen.add(key);
    result.push({
      ValueOption: token,
      SampleResolvedValue: opt.SampleResolvedValue ?? 'Built-in scope token',
      CategoryLabel: 'Built-in Scope Token'
    });
  }

  for (const rt of RUNTIME_DOLLAR_TOKENS) {
    const token = `$${rt.name}$`;
    const key = token.toLowerCase();
    if (seen.has(key)) continue;
    seen.add(key);
    result.push({
      ValueOption: token,
      SampleResolvedValue: rt.description,
      CategoryLabel: 'Deploy-Time (Runtime)'
    });
  }

  for (const prop of properties ?? []) {
    const name = prop.Name?.trim();
    if (!name || /[\\/\s$]/.test(name)) continue;
    if (excludedLower && name.toLowerCase() === excludedLower) continue;
    const token = `$${name}$`;
    const key = token.toLowerCase();
    if (seen.has(key)) continue;
    seen.add(key);
    result.push({
      ValueOption: token,
      SampleResolvedValue: prop.Secure
        ? 'Encrypted variable ($' + name + '$)'
        : 'Scoped DOrc variable ($' + name + '$)',
      CategoryLabel: prop.Secure ? 'Secure Variable' : 'Variable'
    });
  }

  for (const expr of FN_EXPRESSION_SNIPPETS) {
    const key = expr.snippet.toLowerCase();
    if (seen.has(key)) continue;
    seen.add(key);
    result.push({
      ValueOption: expr.snippet,
      SampleResolvedValue: expr.description,
      CategoryLabel: 'fn: C# Expression'
    });
  }

  return result;
}

export function formatResolutionSource(
  trace: PropertyResolutionTraceApiModel | undefined
): string {
  if (!trace) return 'Resolving…';
  const scope = trace.ScopeName || 'Current';
  switch (trace.Source) {
    case 'RequestOverride':
      return 'Request Override';
    case 'EnvironmentScope':
      return `Set on ${scope}`;
    case 'AncestorEnvironmentScope':
      return `Inherited from ${trace.ScopeName || 'Parent'}`;
    case 'GlobalDefault':
      return 'Default (Global)';
    case 'BuiltInScopeOption':
      return `Built-in (${trace.ValueType || 'Scope'})`;
    case 'ConfigValue':
      return `Config (${trace.ScopeName || 'Fallback'})`;
    case 'RuntimeOnly':
      return 'Deploy-Time Runtime';
    default:
      return 'Not Set in Scope';
  }
}

export function presentTrace(
  trace: PropertyResolutionTraceApiModel | undefined
): PresentedTrace {
  if (!trace) {
    return {
      severity: 'pending',
      effectiveDisplayValue: 'Resolving…',
      hasEffectiveValue: false,
      sourceLabel: 'Resolving…',
      diagnosticTitle: 'Pending',
      diagnosticDetail: 'Fetching variable resolution preview…',
      hasBlockedTokensByUnescapedDollar: false
    };
  }

  const sourceLabel = formatResolutionSource(trace);

  if (trace.IsSecure || trace.Diagnostic === 'Masked') {
    return {
      severity: 'masked',
      effectiveDisplayValue: '•••••••• (Secure)',
      hasEffectiveValue: true,
      sourceLabel,
      diagnosticTitle: 'Encrypted Secret',
      diagnosticDetail:
        'This variable is marked Secure and its value is masked.',
      hasBlockedTokensByUnescapedDollar: false
    };
  }

  if (
    trace.Diagnostic === 'ExpressionDeferred' ||
    trace.Diagnostic === 'ExpressionBlockedByOverride'
  ) {
    const raw = trace.RawValue ?? '(deferred expression)';
    return {
      severity: 'unresolved',
      effectiveDisplayValue: raw,
      hasEffectiveValue: false,
      sourceLabel,
      diagnosticTitle:
        trace.Diagnostic === 'ExpressionBlockedByOverride'
          ? 'fn: Expression Blocked by Override'
          : 'fn: Evaluated at Deploy Time',
      diagnosticDetail:
        trace.Diagnostic === 'ExpressionBlockedByOverride'
          ? 'Request override blocked dynamic C# expression evaluation.'
          : 'Dynamic fn: C# expressions are evaluated only during an actual deployment.',
      hasBlockedTokensByUnescapedDollar: false
    };
  }

  if (trace.Diagnostic === 'CircularReference') {
    return {
      severity: 'unresolved',
      effectiveDisplayValue: trace.RawValue ?? '(circular reference)',
      hasEffectiveValue: false,
      sourceLabel,
      diagnosticTitle: 'Circular Reference',
      diagnosticDetail:
        'This variable references itself directly or indirectly.',
      hasBlockedTokensByUnescapedDollar: false
    };
  }

  if (trace.Source === 'RuntimeOnly') {
    return {
      severity: 'warning',
      effectiveDisplayValue: '(populated at deploy time)',
      hasEffectiveValue: false,
      sourceLabel,
      diagnosticTitle: 'Populated at Deploy Time',
      diagnosticDetail:
        'This value is populated by the deployment runner at execution time.',
      hasBlockedTokensByUnescapedDollar: false
    };
  }

  if (
    trace.Source === 'Unresolved' ||
    typeof trace.ResolvedValueJson !== 'string'
  ) {
    return {
      severity: 'unresolved',
      effectiveDisplayValue: '(not defined in this scope)',
      hasEffectiveValue: false,
      sourceLabel,
      diagnosticTitle: 'Not Defined in Scope',
      diagnosticDetail:
        'No value is defined for this environment or Default scope.',
      hasBlockedTokensByUnescapedDollar: false
    };
  }

  let parsedVal: unknown = trace.ResolvedValueJson;
  try {
    parsedVal = JSON.parse(trace.ResolvedValueJson);
  } catch {
    /* keep raw string */
  }
  const effectiveDisplayValue =
    typeof parsedVal === 'string' ? parsedVal : JSON.stringify(parsedVal);

  if (trace.Diagnostic === 'MissingInScope') {
    const rawTokens = parsePropertyTokens(
      trace.RawValue ?? effectiveDisplayValue,
      'standard'
    );
    const hasUnclosedParserToken = rawTokens.some(
      t => t.kind === 'unclosed-token'
    );
    if (hasUnclosedParserToken) {
      return {
        severity: 'warning',
        effectiveDisplayValue,
        hasEffectiveValue: true,
        sourceLabel,
        diagnosticTitle: 'Unescaped $ Paired Into Bogus Token',
        diagnosticDetail:
          'Multiple literal $ were paired as a $Variable$ token. Escape literal $ as $$.',
        hasBlockedTokensByUnescapedDollar: true
      };
    }
    return {
      severity: 'unresolved',
      effectiveDisplayValue,
      hasEffectiveValue: true,
      sourceLabel,
      diagnosticTitle: 'Nested $Variable$ Missing in Scope',
      diagnosticDetail:
        'This variable references another $Variable$ token that has no value defined in this environment or Default scope.',
      hasBlockedTokensByUnescapedDollar: false
    };
  }

  if (trace.Diagnostic === 'RuntimeOnly') {
    return {
      severity: 'warning',
      effectiveDisplayValue,
      hasEffectiveValue: true,
      sourceLabel,
      diagnosticTitle: 'Contains Deploy-Time Token',
      diagnosticDetail:
        'References a token (such as $DropFolder$ or $BuildNumber$) that is populated at deployment execution time.',
      hasBlockedTokensByUnescapedDollar: false
    };
  }

  if (trace.Diagnostic === 'NonStringInConcatenation') {
    return {
      severity: 'warning',
      effectiveDisplayValue,
      hasEffectiveValue: true,
      sourceLabel,
      diagnosticTitle: 'Array/Object Token in String Path',
      diagnosticDetail:
        'Combines a multi-value array or object token (such as $ServerNames_<Tag>$ with multiple servers) inside a string.',
      hasBlockedTokensByUnescapedDollar: false
    };
  }

  if (trace.Diagnostic === 'ValueTruncated') {
    return {
      severity: 'warning',
      effectiveDisplayValue,
      hasEffectiveValue: true,
      sourceLabel,
      diagnosticTitle: 'Preview Value Truncated',
      diagnosticDetail:
        'The expanded variable value exceeded the maximum preview size and was truncated.',
      hasBlockedTokensByUnescapedDollar: false
    };
  }

  if (trace.Diagnostic === 'UnclosedToken') {
    const dollarInfo = inspectUnescapedDollars(
      trace.RawValue ?? effectiveDisplayValue
    );
    if (
      dollarInfo.hasUnclosedDollar &&
      dollarInfo.hasOtherPropertyTokens
    ) {
      return {
        severity: 'warning',
        effectiveDisplayValue,
        hasEffectiveValue: true,
        sourceLabel,
        diagnosticTitle: 'Unescaped $ Prevents Variable Expansion',
        diagnosticDetail:
          'A single $ (such as E$) prevented other $Variable$ tokens in this value from expanding. Escape literal $ as $$ (e.g. E$$).',
        hasBlockedTokensByUnescapedDollar: true
      };
    }
    return {
      severity: 'resolved',
      effectiveDisplayValue,
      hasEffectiveValue: true,
      sourceLabel,
      diagnosticTitle: 'Literal $ Kept (e.g. E$)',
      diagnosticDetail:
        'Contains a literal $ (such as a Windows admin share E$). DOrc passes this path as-is. Escape as $$ (E$$) only if combining with $Variable$ tokens.',
      hasBlockedTokensByUnescapedDollar: false
    };
  }

  if (trace.Diagnostic === 'FallbackToDefault') {
    return {
      severity: 'resolved',
      effectiveDisplayValue,
      hasEffectiveValue: true,
      sourceLabel,
      diagnosticTitle: 'Using Default (Global) Value',
      diagnosticDetail:
        'No override is set on this environment; using the Default value.',
      hasBlockedTokensByUnescapedDollar: false
    };
  }

  return {
    severity: 'resolved',
    effectiveDisplayValue,
    hasEffectiveValue: true,
    sourceLabel,
    diagnosticTitle: 'Resolved',
    diagnosticDetail: `Resolved from ${sourceLabel}.`,
    hasBlockedTokensByUnescapedDollar: false
  };
}

export interface ParameterBindingWarning {
  kind: 'dollar-syntax' | 'mixed-text';
  suggestedVariableName?: string;
  message: string;
}

const LEGACY_DOLLAR_WHOLE_PATTERN = /^\$([A-Za-z0-9_.-]+)\$$/;
const SPACED_BRACE_WHOLE_PATTERN = /^\$\{\s*([A-Za-z0-9_.-]+)\s*\}$/;
const BARE_BRACE_WHOLE_PATTERN = /^\{([A-Za-z0-9_.-]+)\}$/;

export function inspectParameterBindingSyntax(
  rawText: string
): ParameterBindingWarning | undefined {
  const trimmed = rawText.trim();
  if (!trimmed) return undefined;

  const dollarMatch = LEGACY_DOLLAR_WHOLE_PATTERN.exec(trimmed);
  if (dollarMatch) {
    return {
      kind: 'dollar-syntax',
      suggestedVariableName: dollarMatch[1],
      message: `Script parameters use \${${dollarMatch[1]}} rather than $${dollarMatch[1]}$.`
    };
  }

  const spacedBraceMatch = SPACED_BRACE_WHOLE_PATTERN.exec(trimmed);
  if (spacedBraceMatch && trimmed !== `\${${spacedBraceMatch[1]}}`) {
    return {
      kind: 'dollar-syntax',
      suggestedVariableName: spacedBraceMatch[1],
      message: `Remove spaces inside \${${spacedBraceMatch[1]}} so DOrc binds the variable directly.`
    };
  }

  const bareBraceMatch = BARE_BRACE_WHOLE_PATTERN.exec(trimmed);
  if (bareBraceMatch) {
    return {
      kind: 'dollar-syntax',
      suggestedVariableName: bareBraceMatch[1],
      message: `Script parameters require a leading $: use \${${bareBraceMatch[1]}}. `
    };
  }

  const parsedBrace = parsePropertyTokens(trimmed, 'brace-only');
  if (parsedBrace.length === 1 && parsedBrace[0].kind === 'property') {
    if (rawText !== trimmed) {
      return {
        kind: 'dollar-syntax',
        suggestedVariableName: parsedBrace[0].propertyName,
        message: `Remove surrounding spaces around \${${parsedBrace[0].propertyName}} so DOrc binds the variable directly.`
      };
    }
    return undefined;
  }

  if (trimmed.includes('$$')) {
    return {
      kind: 'mixed-text',
      message:
        'Script parameters do not unescape $$. Use a single $ for literal paths (e.g. E$), or bind a ${VariableName} variable.'
    };
  }

  if (/\$\{[^}]*\}/.test(trimmed) || /\$[A-Za-z0-9_.-]+\$/.test(trimmed)) {
    return {
      kind: 'mixed-text',
      message:
        'Script parameters bind a single variable (${Name}) for the whole field. To combine a server or path with text, put the full path in a variable value.'
    };
  }

  return undefined;
}

export interface DollarValuePreview {
  status: 'resolved' | 'warning' | 'unresolved' | 'masked';
  resolvedText: string;
  scopeSummary: string;
}

export type DollarTokenSegmentStatus =
  | 'resolved'
  | 'fallback'
  | 'warning'
  | 'unresolved'
  | 'masked'
  | 'runtime';

export type DollarValueSegment =
  | {
      kind: 'literal';
      rawText: string;
      displayText: string;
    }
  | {
      kind: 'expression-prefix';
      rawText: 'fn:';
      displayText: 'fn:';
      tooltip: string;
    }
  | {
      kind: 'escaped-dollar';
      rawText: '$$';
      displayText: '$';
      tooltip: string;
    }
  | {
      kind: 'property';
      rawToken: string;
      rawText: string;
      propertyName: string;
      status: DollarTokenSegmentStatus;
      resolvedPreview: string;
      badgeLabel: string;
      tooltip: string;
    }
  | {
      kind: 'unclosed-dollar';
      rawText: string;
      displayText: string;
      tooltip: string;
    };

export interface ActiveDollarCompletionTrigger {
  dollarStartIndex: number;
  replaceEndIndex: number;
  queryText: string;
  triggerKind?: 'dollar' | 'fn';
}

export function formatSampleResolvedPreview(sampleValue: unknown): string {
  if (sampleValue === null || sampleValue === undefined) {
    return '';
  }
  const raw =
    typeof sampleValue === 'string'
      ? sampleValue
      : JSON.stringify(sampleValue);
  return raw.replace(/\s+/g, ' ').trim();
}

export function detectActiveDollarCompletion(
  rawValue: string,
  caretIndex: number
): ActiveDollarCompletionTrigger | undefined {
  if (!rawValue || caretIndex <= 0) {
    return undefined;
  }

  const clampedCaret = Math.min(caretIndex, rawValue.length);
  const prefix = rawValue.slice(0, clampedCaret);
  const trimmedPrefix = prefix.trimStart();
  const leadingWs = prefix.length - trimmedPrefix.length;

  // Detect typing fn: at the start of the value before any $ token is opened
  if (
    trimmedPrefix.toLowerCase().startsWith(EXPRESSION_PREFIX) &&
    !trimmedPrefix.includes('$') &&
    /^[A-Za-z0-9_.:"]*$/.test(trimmedPrefix)
  ) {
    return {
      dollarStartIndex: leadingWs,
      replaceEndIndex: clampedCaret,
      queryText: trimmedPrefix,
      triggerKind: 'fn'
    };
  }

  let idx = trimmedPrefix.startsWith(EXPRESSION_PREFIX)
    ? leadingWs + EXPRESSION_PREFIX.length
    : 0;

  while (idx < prefix.length) {
    if (prefix[idx] !== '$') {
      idx++;
      continue;
    }

    if (idx + 1 < prefix.length && prefix[idx + 1] === '$') {
      idx += 2;
      continue;
    }

    if (isAdminShareDollarAt(rawValue, idx)) {
      idx++;
      continue;
    }

    let closingIdx = -1;
    for (let scan = idx + 1; scan < prefix.length; scan++) {
      const ch = prefix[scan];
      if (ch === '$') {
        closingIdx = scan;
        break;
      }
      if (
        ch === '\\' ||
        ch === '/' ||
        ch === '\r' ||
        ch === '\n' ||
        ch === '{' ||
        ch === '}'
      ) {
        break;
      }
    }

    if (closingIdx > idx + 1) {
      idx = closingIdx + 1;
      continue;
    }

    const candidateQuery = prefix.slice(idx + 1);
    if (/^[A-Za-z0-9_.\- ]*$/.test(candidateQuery)) {
      const replaceEnd =
        clampedCaret < rawValue.length && rawValue[clampedCaret] === '$'
          ? clampedCaret + 1
          : clampedCaret;
      return {
        dollarStartIndex: idx,
        replaceEndIndex: replaceEnd,
        queryText: candidateQuery,
        triggerKind: 'dollar'
      };
    }

    idx++;
  }

  return undefined;
}

export function filterInsertableDollarTokens(
  tokens: readonly InsertableDollarTokenOption[],
  queryText: string
): InsertableDollarTokenOption[] {
  const trimmedQuery = queryText.trim().toLowerCase();
  if (!trimmedQuery) {
    return [...tokens];
  }
  if (trimmedQuery.startsWith(EXPRESSION_PREFIX)) {
    const fnSubQuery = trimmedQuery.slice(EXPRESSION_PREFIX.length).trim();
    const fnMatches = tokens.filter(opt => {
      const tokenName = opt.ValueOption.toLowerCase();
      const sample = formatSampleResolvedPreview(
        opt.SampleResolvedValue
      ).toLowerCase();
      if (!tokenName.startsWith(EXPRESSION_PREFIX)) {
        return false;
      }
      return (
        !fnSubQuery ||
        tokenName.includes(fnSubQuery) ||
        sample.includes(fnSubQuery)
      );
    });
    if (fnMatches.length > 0) {
      return fnMatches;
    }
  }
  const normalized = trimmedQuery.replace(/^\$+|\$+$/g, '').trim();
  if (!normalized) {
    return tokens.filter(
      opt => !opt.ValueOption.toLowerCase().startsWith(EXPRESSION_PREFIX)
    );
  }
  return tokens.filter(opt => {
    const tokenName = opt.ValueOption.toLowerCase();
    const sample = formatSampleResolvedPreview(
      opt.SampleResolvedValue
    ).toLowerCase();
    const category = opt.CategoryLabel.toLowerCase();
    return (
      tokenName.includes(normalized) ||
      sample.includes(normalized) ||
      category.includes(normalized)
    );
  });
}

export function applyDollarTokenCompletion(
  rawValue: string,
  token: string,
  trigger: ActiveDollarCompletionTrigger | undefined,
  caretStart: number,
  caretEnd: number
): { nextValue: string; nextCaretIndex: number } {
  if (trigger) {
    const nextValue =
      rawValue.slice(0, trigger.dollarStartIndex) +
      token +
      rawValue.slice(trigger.replaceEndIndex);
    return {
      nextValue,
      nextCaretIndex: trigger.dollarStartIndex + token.length
    };
  }
  const safeStart = Math.max(0, Math.min(caretStart, rawValue.length));
  const safeEnd = Math.max(safeStart, Math.min(caretEnd, rawValue.length));
  const nextValue =
    rawValue.slice(0, safeStart) + token + rawValue.slice(safeEnd);
  return {
    nextValue,
    nextCaretIndex: safeStart + token.length
  };
}

export function synthesizeScopePreviewTraces(
  traces: ReadonlyMap<string, PropertyResolutionTraceApiModel>,
  scopeOptions: readonly PropertyValueScopeOptionApiModel[] | undefined,
  properties: readonly PropertyApiModel[] | undefined,
  scopeName: string
): Map<string, PropertyResolutionTraceApiModel> {
  const merged = new Map<string, PropertyResolutionTraceApiModel>();

  for (const rt of RUNTIME_DOLLAR_TOKENS) {
    merged.set(rt.name, {
      PropertyName: rt.name,
      Source: 'RuntimeOnly',
      ScopeName: scopeName,
      Diagnostic: 'RuntimeOnly'
    });
  }

  for (const prop of properties ?? []) {
    const name = prop.Name?.trim();
    if (!name || !prop.Secure) continue;
    merged.set(name, {
      PropertyName: name,
      Source: 'EnvironmentScope',
      ScopeName: scopeName,
      IsSecure: true,
      Diagnostic: 'Masked'
    });
  }

  for (const opt of scopeOptions ?? []) {
    const rawToken = opt.ValueOption?.trim();
    if (!rawToken || !rawToken.startsWith('$') || !rawToken.endsWith('$')) {
      continue;
    }
    const propName = rawToken.slice(1, -1);
    if (!propName || propName.includes(';')) continue;
    merged.set(propName, {
      PropertyName: propName,
      Source: 'EnvironmentScope',
      ScopeName: scopeName,
      ResolvedValueJson: JSON.stringify(opt.SampleResolvedValue ?? ''),
      Diagnostic: 'None'
    });
  }

  for (const [k, v] of traces.entries()) {
    merged.set(k, v);
  }

  return merged;
}

export function segmentizeDollarValueWithTraces(
  rawValue: string,
  traces: ReadonlyMap<string, PropertyResolutionTraceApiModel>
): DollarValueSegment[] {
  if (!rawValue) {
    return [];
  }

  const trimmedStart = rawValue.trimStart();
  const leadingWsLen = rawValue.length - trimmedStart.length;
  const normalizedInput = trimmedStart.startsWith(EXPRESSION_PREFIX)
    ? trimmedStart
    : rawValue;

  const tokens = parsePropertyTokens(normalizedInput, 'standard');
  const segments: DollarValueSegment[] = [];

  if (leadingWsLen > 0 && trimmedStart.startsWith(EXPRESSION_PREFIX)) {
    const ws = rawValue.slice(0, leadingWsLen);
    segments.push({
      kind: 'literal',
      rawText: ws,
      displayText: ws
    });
  }

  for (const token of tokens) {
    if (token.kind === 'expression-prefix') {
      segments.push({
        kind: 'expression-prefix',
        rawText: 'fn:',
        displayText: 'fn:',
        tooltip:
          'Dynamic C# expression prefix (fn:) — $Variable$ tokens expand before C# evaluation'
      });
      continue;
    }

    if (token.kind === 'static') {
      segments.push({
        kind: 'literal',
        rawText: token.rawText,
        displayText: token.value
      });
      continue;
    }

    if (token.kind === 'escaped-dollar') {
      segments.push({
        kind: 'escaped-dollar',
        rawText: '$$',
        displayText: '$',
        tooltip: 'Escaped literal $ ($$)'
      });
      continue;
    }

    if (token.kind === 'unclosed-token') {
      segments.push({
        kind: 'unclosed-dollar',
        rawText: token.rawText,
        displayText: token.rawText,
        tooltip:
          'Unescaped $ prevents $Variable$ tokens from expanding — escape literal $ as $$'
      });
      continue;
    }

    const trace = lookupTrace(traces, token.propertyName);
    if (!trace) {
      const runtimeMatch = RUNTIME_DOLLAR_TOKENS.find(
        rt => rt.name.toLowerCase() === token.propertyName.toLowerCase()
      );
      if (runtimeMatch) {
        segments.push({
          kind: 'property',
          rawToken: token.rawText,
          rawText: token.rawText,
          propertyName: token.propertyName,
          status: 'runtime',
          resolvedPreview: '(populated at deploy time)',
          badgeLabel: 'Deploy-Time (Runtime)',
          tooltip: `Deploy-Time (Runtime) -> ${runtimeMatch.description}`
        });
      } else {
        segments.push({
          kind: 'property',
          rawToken: token.rawText,
          rawText: token.rawText,
          propertyName: token.propertyName,
          status: 'unresolved',
          resolvedPreview: token.rawText,
          badgeLabel: 'Missing in scope',
          tooltip: 'Missing in scope'
        });
      }
      continue;
    }

    const presented = presentTrace(trace);

    if (trace.IsSecure || trace.Diagnostic === 'Masked') {
      const scopeProvenance = trace.ScopeName
        ? `Env (${trace.ScopeName})`
        : formatResolutionSource(trace);
      segments.push({
        kind: 'property',
        rawToken: token.rawText,
        rawText: token.rawText,
        propertyName: token.propertyName,
        status: 'masked',
        resolvedPreview: presented.effectiveDisplayValue,
        badgeLabel: 'Secret',
        tooltip: `Secret (${scopeProvenance}) -> ${presented.effectiveDisplayValue}`
      });
      continue;
    }

    if (trace.Source === 'RuntimeOnly' || trace.Diagnostic === 'RuntimeOnly') {
      const previewVal = presented.hasEffectiveValue
        ? presented.effectiveDisplayValue
        : '(populated at deploy time)';
      segments.push({
        kind: 'property',
        rawToken: token.rawText,
        rawText: token.rawText,
        propertyName: token.propertyName,
        status: 'runtime',
        resolvedPreview: previewVal,
        badgeLabel: 'Deploy-Time (Runtime)',
        tooltip: `Deploy-Time (Runtime) -> ${previewVal}`
      });
      continue;
    }

    if (
      trace.Source === 'Unresolved' ||
      !presented.hasEffectiveValue ||
      trace.Diagnostic === 'MissingInScope'
    ) {
      const previewVal = presented.hasEffectiveValue
        ? presented.effectiveDisplayValue
        : '(not defined in this scope)';
      const tooltip =
        trace.Diagnostic === 'MissingInScope' && presented.hasEffectiveValue
          ? `Missing in scope -> ${previewVal}`
          : 'Missing in scope';
      segments.push({
        kind: 'property',
        rawToken: token.rawText,
        rawText: token.rawText,
        propertyName: token.propertyName,
        status: 'unresolved',
        resolvedPreview: previewVal,
        badgeLabel: 'Missing in scope',
        tooltip
      });
      continue;
    }

    if (
      trace.Diagnostic === 'FallbackToDefault' ||
      trace.Source === 'GlobalDefault' ||
      trace.Source === 'ConfigValue'
    ) {
      const badgeLabel =
        trace.Source === 'ConfigValue'
          ? `Config (${trace.ScopeName || 'Fallback'})`
          : 'Default fallback';
      segments.push({
        kind: 'property',
        rawToken: token.rawText,
        rawText: token.rawText,
        propertyName: token.propertyName,
        status: 'fallback',
        resolvedPreview: presented.effectiveDisplayValue,
        badgeLabel,
        tooltip: `${badgeLabel} -> ${presented.effectiveDisplayValue}`
      });
      continue;
    }

    const envBadge =
      trace.Source === 'AncestorEnvironmentScope'
        ? `Inherited (${trace.ScopeName || 'Parent'})`
        : trace.Source === 'RequestOverride'
          ? 'Request Override'
          : trace.ScopeName
            ? `Env (${trace.ScopeName})`
            : `Built-in (${trace.ValueType || 'Scope'})`;
    const segStatus: DollarTokenSegmentStatus =
      presented.severity === 'warning' ? 'warning' : 'resolved';

    segments.push({
      kind: 'property',
      rawToken: token.rawText,
      rawText: token.rawText,
      propertyName: token.propertyName,
      status: segStatus,
      resolvedPreview: presented.effectiveDisplayValue,
      badgeLabel: envBadge,
      tooltip: `${envBadge} -> ${presented.effectiveDisplayValue}`
    });
  }

  return segments;
}

export function evaluateDollarValuePreview(
  rawText: string,
  tracesByProperty: ReadonlyMap<string, PropertyResolutionTraceApiModel>,
  scopeLabel: string
): DollarValuePreview | undefined {
  if (!rawText) {
    return undefined;
  }

  const trimmedStart = rawText.trimStart();
  const hasFnPrefix = trimmedStart.startsWith(EXPRESSION_PREFIX);
  if (!rawText.includes('$') && !hasFnPrefix) {
    return undefined;
  }

  const leadingWs = hasFnPrefix
    ? rawText.slice(0, rawText.length - trimmedStart.length)
    : '';
  const tokens = parsePropertyTokens(
    hasFnPrefix ? trimmedStart : rawText,
    'standard'
  );
  const propTokens = tokens.filter(
    (t): t is Extract<ParsedPropertyToken, { kind: 'property' }> =>
      t.kind === 'property'
  );
  const hasUnclosedParserToken = tokens.some(t => t.kind === 'unclosed-token');

  if (hasUnclosedParserToken) {
    return {
      status: 'warning',
      resolvedText: rawText,
      scopeSummary:
        'Unescaped $ prevents $Variable$ tokens from expanding — escape literal $ as $$'
    };
  }

  if (
    propTokens.length === 0 &&
    !tokens.some(t => t.kind === 'escaped-dollar') &&
    !hasFnPrefix
  ) {
    return undefined;
  }

  let out = leadingWs;
  let status: 'resolved' | 'warning' | 'unresolved' | 'masked' = hasFnPrefix
    ? 'warning'
    : 'resolved';
  let anyMissingEffectiveValue = false;
  const details: string[] = [];

  if (hasFnPrefix) {
    details.push(
      'fn: C# expression evaluated at deploy time after $Variable$ expansion'
    );
  }

  for (const token of tokens) {
    if (token.kind === 'escaped-dollar') {
      out += '$';
      continue;
    }
    if (token.kind !== 'property') {
      out += token.rawText;
      continue;
    }

    const trace = lookupTrace(tracesByProperty, token.propertyName);
    if (!trace) {
      status = 'unresolved';
      anyMissingEffectiveValue = true;
      out += token.rawText;
      details.push(`$${token.propertyName}$: resolving in ${scopeLabel}…`);
      continue;
    }

    const presented = presentTrace(trace);
    details.push(`$${token.propertyName}$: ${presented.diagnosticTitle} (${presented.sourceLabel})`);

    if (!presented.hasEffectiveValue) {
      anyMissingEffectiveValue = true;
    }

    if (presented.severity === 'masked') {
      if (status === 'resolved') status = 'masked';
      out += presented.effectiveDisplayValue;
    } else if (presented.severity === 'warning') {
      if (status !== 'unresolved') status = 'warning';
      out += presented.hasEffectiveValue
        ? presented.effectiveDisplayValue
        : token.rawText;
    } else if (presented.severity === 'resolved' && presented.hasEffectiveValue) {
      out += presented.effectiveDisplayValue;
    } else {
      status = 'unresolved';
      out += presented.hasEffectiveValue
        ? presented.effectiveDisplayValue
        : token.rawText;
    }
  }

  return {
    status,
    resolvedText: anyMissingEffectiveValue ? rawText : out,
    scopeSummary: details.join(' · ')
  };
}
