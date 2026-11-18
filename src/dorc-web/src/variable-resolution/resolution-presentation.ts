import type {
  PropertyApiModel,
  PropertyResolutionTraceApiModel,
  PropertyValueScopeOptionApiModel
} from '../apis/dorc-api';
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

export interface InsertableDollarTokenOption {
  ValueOption: string;
  SampleResolvedValue: unknown;
  CategoryLabel: string;
}

const ADMIN_SHARE_PATTERN = /^(?:[A-Za-z]|ADMIN|IPC)\$(?=[\\/]|$)/i;
const VALID_DOLLAR_TOKEN_PATTERN = /^\$([A-Za-z0-9_.-]+)\$/;

export function inspectUnescapedDollars(raw: string): UnescapedDollarInspection {
  if (!raw || !raw.includes('$')) {
    return {
      hasUnclosedDollar: false,
      hasOtherPropertyTokens: false,
      escapedSuggestion: raw
    };
  }

  let hasUnclosedDollar = false;
  let hasOtherPropertyTokens = false;
  let out = '';
  let i = 0;

  while (i < raw.length) {
    // Already-escaped $$
    if (raw[i] === '$' && raw[i + 1] === '$') {
      out += '$$';
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
        i += adminMatch[0].length;
        continue;
      }
    }

    if (raw[i] === '$') {
      const tokenMatch = VALID_DOLLAR_TOKEN_PATTERN.exec(raw.slice(i));
      if (tokenMatch) {
        // Check that the closing $ is not immediately part of an admin share like $Var\E$
        hasOtherPropertyTokens = true;
        out += tokenMatch[0];
        i += tokenMatch[0].length;
        continue;
      }

      // Lone or invalid $ (not a valid $PropertyName$ token)
      hasUnclosedDollar = true;
      out += '$$';
      i += 1;
      continue;
    }

    out += raw[i];
    i += 1;
  }

  return {
    hasUnclosedDollar,
    hasOtherPropertyTokens,
    escapedSuggestion: hasUnclosedDollar ? out : raw
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
    if (!token) continue;
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
      CategoryLabel: 'Built-in Deploy Token'
    });
  }

  for (const prop of properties ?? []) {
    const name = prop.Name?.trim();
    if (!name) continue;
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
      CategoryLabel: prop.Secure ? 'Secure Variable' : 'Scoped Variable'
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

  if (trace.Diagnostic === 'MissingInScope') {
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

  if (/\$\{[A-Za-z0-9_.-]+\}/.test(trimmed) || /\$[A-Za-z0-9_.-]+\$/.test(trimmed)) {
    return {
      kind: 'mixed-text',
      message:
        'Script parameters bind a single variable (${Name}) for the whole field. To combine a server or path with text, put the full path in a variable value.'
    };
  }

  return undefined;
}
