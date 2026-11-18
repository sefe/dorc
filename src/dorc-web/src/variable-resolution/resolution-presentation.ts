import type { PropertyResolutionTraceApiModel } from '../apis/dorc-api';
import { parsePropertyTokens } from './property-token-parser';

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
  escapedSuggestion: string;
}

export function inspectUnescapedDollars(raw: string): UnescapedDollarInspection {
  const tokens = parsePropertyTokens(raw, 'standard');
  const hasUnclosedDollar = tokens.some(t => t.kind === 'unclosed-token');
  const hasOtherPropertyTokens = tokens.some(t => t.kind === 'property');

  if (!hasUnclosedDollar) {
    return {
      hasUnclosedDollar: false,
      hasOtherPropertyTokens,
      escapedSuggestion: raw
    };
  }

  const escapedSuggestion = tokens
    .map(t =>
      t.kind === 'unclosed-token' ? t.rawText.replace(/\$/g, '$$$') : t.rawText
    )
    .join('');

  return {
    hasUnclosedDollar: true,
    hasOtherPropertyTokens,
    escapedSuggestion
  };
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

  if (
    trace.Source === 'Unresolved' ||
    trace.Source === 'RuntimeOnly' ||
    typeof trace.ResolvedValueJson !== 'string'
  ) {
    return {
      severity: 'unresolved',
      effectiveDisplayValue: '(not defined in this scope)',
      hasEffectiveValue: false,
      sourceLabel,
      diagnosticTitle:
        trace.Source === 'RuntimeOnly'
          ? 'Populated at Deploy Time'
          : 'Not Defined in Scope',
      diagnosticDetail:
        trace.Source === 'RuntimeOnly'
          ? 'This value is populated by the deployment runner at execution time.'
          : 'No value is defined for this environment or Default scope.',
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

  if (trace.Diagnostic === 'UnclosedToken') {
    const dollarInfo = inspectUnescapedDollars(
      trace.RawValue ?? effectiveDisplayValue
    );
    if (dollarInfo.hasOtherPropertyTokens) {
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

  const parsedBrace = parsePropertyTokens(trimmed, 'brace-only');
  if (parsedBrace.length === 1 && parsedBrace[0].kind === 'property') {
    return undefined;
  }

  if (/\$\{[A-Za-z0-9_.-]+\}/.test(trimmed) || /\$[A-Za-z0-9_.-]+\$/.test(trimmed)) {
    return {
      kind: 'mixed-text',
      message:
        'Script parameters bind a single variable (${Name}) for the whole field. To combine a server or path with text, put the full path in a variable value.'
    };
  }

  return undefined;
}
