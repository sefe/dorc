import {
  ComplexAttributeConverter,
  css,
  html,
  LitElement,
  nothing,
  PropertyValues,
  TemplateResult
} from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { classMap } from 'lit/directives/class-map.js';
import { ifDefined } from 'lit/directives/if-defined.js';
import type { PropertyResolutionTraceApiModel } from '../apis/dorc-api';
import {
  defaultPropertyResolutionClientCache,
  PropertyResolutionClientCache,
  PropertyResolutionContext
} from '../variable-resolution/property-resolution-client-cache';
import { presentTrace } from '../variable-resolution/resolution-presentation';
import {
  extractReferencedPropertyNames,
  ParsedPropertyToken,
  parsePropertyTokens,
  PropertyTokenSyntaxProfile,
  resolveTokenSyntaxProfile
} from '../variable-resolution/property-token-parser';

export type JsonViewerDisplayMode = 'raw' | 'inline' | 'resolved';

function escapeJsonStringSegment(segment: string): string {
  return JSON.stringify(segment).slice(1, -1);
}

interface JsonViewerState {
  expanded: { [path: string]: boolean };
  filtered: { [path: string]: boolean };
  highlight: string | null;
}

const enum SupportedTypes {
  String = 'string',
  Number = 'number',
  Boolean = 'boolean',
  Object = 'object',
  Null = 'null',
  Array = 'array'
}

function isRegex(obj: unknown): obj is RegExp {
  return obj instanceof RegExp;
}

export function getType(obj: unknown): SupportedTypes {
  if (obj === null) {
    return SupportedTypes.Null;
  }
  if (Array.isArray(obj)) {
    return SupportedTypes.Array;
  }
  return (obj as object).constructor.name.toLowerCase() as SupportedTypes;
}

function isPrimitive(obj: unknown): boolean {
  return obj !== Object(obj);
}

function isNode(obj: unknown): obj is Node {
  return Boolean(obj && (obj as Node).nodeType);
}

function isPrimitiveOrNode(obj: unknown): boolean {
  return isPrimitive(obj) || isNode(obj);
}

export function evaluateStringWithTraces(
  raw: string,
  traces: Map<string, PropertyResolutionTraceApiModel>,
  profile: PropertyTokenSyntaxProfile = 'standard'
): {
  status: 'resolved' | 'unresolved' | 'literal';
  resolvedValue: unknown;
  resolvedDisplayString: string;
} {
  const tokens = parsePropertyTokens(raw, profile);
  if (
    tokens.some(
      t => t.kind === 'unclosed-token' || t.kind === 'expression-prefix'
    )
  ) {
    return {
      status: 'unresolved',
      resolvedValue: raw,
      resolvedDisplayString: raw
    };
  }

  const propTokens = tokens.filter(
    (t): t is Extract<ParsedPropertyToken, { kind: 'property' }> =>
      t.kind === 'property'
  );

  if (propTokens.length === 0) {
    const unescaped = tokens
      .map(t => (t.kind === 'escaped-dollar' ? '$' : t.rawText))
      .join('');
    return {
      status: 'literal',
      resolvedValue: unescaped,
      resolvedDisplayString: unescaped
    };
  }

  for (const pt of propTokens) {
    const trace = traces.get(pt.propertyName);
    if (
      !trace ||
      trace.Source === 'Unresolved' ||
      trace.Source === 'RuntimeOnly' ||
      (trace.Diagnostic != null &&
        trace.Diagnostic !== 'Ok' &&
        trace.Diagnostic !== 'FallbackToDefault' &&
        trace.Diagnostic !== 'UnclosedToken') ||
      trace.ResolvedValueJson == null
    ) {
      return {
        status: 'unresolved',
        resolvedValue: raw,
        resolvedDisplayString: raw
      };
    }
  }

  if (tokens.length === 1 && tokens[0].kind === 'property') {
    const singleTrace = traces.get(tokens[0].propertyName)!;
    const parsedVal = JSON.parse(singleTrace.ResolvedValueJson!);
    const displayStr =
      typeof parsedVal === 'string' ? parsedVal : JSON.stringify(parsedVal);
    return {
      status: 'resolved',
      resolvedValue: parsedVal,
      resolvedDisplayString: displayStr
    };
  }

  let out = '';
  for (const token of tokens) {
    if (token.kind === 'property') {
      const trace = traces.get(token.propertyName)!;
      const parsedVal = JSON.parse(trace.ResolvedValueJson!);
      if (
        typeof parsedVal !== 'string' &&
        typeof parsedVal !== 'number' &&
        typeof parsedVal !== 'boolean'
      ) {
        return {
          status: 'unresolved',
          resolvedValue: raw,
          resolvedDisplayString: raw
        };
      }
      out += String(parsedVal);
    } else if (token.kind === 'escaped-dollar') {
      out += '$';
    } else {
      out += token.rawText;
    }
  }

  return {
    status: 'resolved',
    resolvedValue: out,
    resolvedDisplayString: out
  };
}

export function generateNodePreview(
  node: Record<string, unknown> | unknown[],
  {
    nodeCount = 3,
    maxLength = 15
  }: { nodeCount?: number; maxLength?: number } = {}
): string {
  const isArray = Array.isArray(node);
  const objectNodes = Object.keys(node);
  const keys = objectNodes.slice(0, nodeCount);
  const record = node as Record<string, unknown>;

  const getNodePreview = (nodeValue: unknown): string => {
    const nodeType = getType(nodeValue);

    switch (nodeType) {
      case SupportedTypes.Object:
        return Object.keys(nodeValue as object).length === 0
          ? '{ }'
          : '{ ... }';
      case SupportedTypes.Array:
        return (nodeValue as unknown[]).length === 0 ? '[ ]' : '[ ... ]';
      case SupportedTypes.String: {
        const strVal = nodeValue as string;
        return `"${strVal.substring(0, maxLength)}${
          strVal.length > maxLength ? '...' : ''
        }"`;
      }
      default:
        return String(nodeValue);
    }
  };

  const childPreviews: string[] = [];
  for (const key of keys) {
    const nodeValue = record[key];
    const prefix = isArray ? '' : `${key}: `;
    childPreviews.push(`${prefix}${getNodePreview(nodeValue)}`);
  }

  if (objectNodes.length > nodeCount) {
    childPreviews.push('...');
  }

  const previewText = childPreviews.join(', ');
  return isArray ? `[ ${previewText} ]` : `{ ${previewText} }`;
}

export function* deepTraverse(
  obj: unknown
): Generator<[unknown, string, string[]]> {
  if (obj === undefined || obj === null) {
    return;
  }

  const stack: Array<[unknown, string, string[]]> = [[obj, '', []]];

  while (stack.length) {
    const [node, path, parents] = stack.shift()!;

    if (path) {
      yield [node, path, parents];
    }

    if (!isPrimitiveOrNode(node)) {
      for (const [key, value] of Object.entries(
        node as Record<string, unknown>
      )) {
        stack.push([
          value,
          `${path}${path ? '.' : ''}${key}`,
          [...parents, path]
        ]);
      }
    }
  }
}

export function checkGlob(str: string, glob: string): boolean {
  const strParts = str.split('.');
  const globParts = glob.split('.');

  let strIndex = 0;
  let globIndex = 0;

  while (strIndex < strParts.length) {
    const globPart = globParts[globIndex];
    const strPart = strParts[strIndex];

    if (globPart === strPart || globPart === '*') {
      globIndex++;
      strIndex++;
    } else if (globPart === '**') {
      globIndex++;
      strIndex = strParts.length - (globParts.length - globIndex);
    } else {
      return false;
    }
  }

  return globIndex === globParts.length;
}

export const JSONConverter: ComplexAttributeConverter = {
  fromAttribute: (value: string | null): unknown => {
    return value && value.trim() ? JSON.parse(value) : undefined;
  },
  toAttribute: (value: unknown): string => {
    return JSON.stringify(value);
  }
};

const isDefined = (value: unknown): boolean => value !== void 0;

export const isMatchingPath = (
  path: string,
  criteria: string | RegExp
): boolean =>
  isRegex(criteria) ? Boolean(path.match(criteria)) : checkGlob(path, criteria);

const toggleNode =
  (path: string) =>
  (state: JsonViewerState): Partial<JsonViewerState> => ({
    expanded: {
      ...state.expanded,
      [path]: !state.expanded[path]
    }
  });

const expand =
  (regexOrGlob: string | RegExp, isExpanded: boolean) =>
  (_state: JsonViewerState, el: HegsJsonViewer): Partial<JsonViewerState> => {
    const expanded: Record<string, boolean> = {};

    for (const [, path, parents] of deepTraverse(el.data)) {
      if (isMatchingPath(path, regexOrGlob)) {
        expanded[path] = isExpanded;
        for (const p of parents) {
          expanded[p] = isExpanded;
        }
      }
    }

    return { expanded };
  };

const filter =
  (regexOrGlob: string | RegExp) =>
  (_state: JsonViewerState, el: HegsJsonViewer): Partial<JsonViewerState> => {
    const filtered: Record<string, boolean> = {};

    for (const [, path, parents] of deepTraverse(el.data)) {
      if (isMatchingPath(path, regexOrGlob)) {
        filtered[path] = false;
        for (const p of parents) {
          filtered[p] = false;
        }
      } else {
        filtered[path] = true;
      }
    }

    return { filtered };
  };

const resetFilter = () => (): Partial<JsonViewerState> => ({ filtered: {} });

const highlight = (path: string | null) => (): Partial<JsonViewerState> => ({
  highlight: path
});

function getBadgeFallback(
  label: string | null | undefined,
  fallback: string
): string {
  return label ? label : fallback;
}

export function getTokenBadgeAppearance(
  trace: PropertyResolutionTraceApiModel | undefined
): { cssClass: string; badgeLabel: string } {
  if (!trace) {
    return { cssClass: 'token-pending', badgeLabel: 'Resolving...' };
  }

  if (
    trace.Diagnostic === 'ExpressionDeferred' ||
    trace.Diagnostic === 'ExpressionBlockedByOverride'
  ) {
    return {
      cssClass: 'token-expression',
      badgeLabel:
        trace.Diagnostic === 'ExpressionBlockedByOverride'
          ? 'fn: Blocked by Override'
          : 'fn: Deferred to Deploy'
    };
  }

  if (trace.Diagnostic === 'CircularReference') {
    return { cssClass: 'token-error', badgeLabel: 'Circular Reference' };
  }

  if (trace.IsSecure) {
    return {
      cssClass: 'token-secure',
      badgeLabel: `Secret (${getBadgeFallback(trace.ScopeName, 'Redacted')})`
    };
  }

  switch (trace.Source) {
    case 'RequestOverride':
      return { cssClass: 'token-override', badgeLabel: 'Request Override' };
    case 'EnvironmentScope':
      return {
        cssClass: 'token-env',
        badgeLabel: `Env (${getBadgeFallback(trace.ScopeName, 'Current')})`
      };
    case 'AncestorEnvironmentScope':
      return {
        cssClass: 'token-ancestor',
        badgeLabel: `Inherited (${getBadgeFallback(trace.ScopeName, 'Parent')})`
      };
    case 'GlobalDefault':
      return { cssClass: 'token-default', badgeLabel: 'Default (Global)' };
    case 'BuiltInScopeOption':
      return {
        cssClass: 'token-builtin',
        badgeLabel: `Built-in (${getBadgeFallback(trace.ValueType, 'Scope')})`
      };
    case 'ConfigValue':
      return {
        cssClass: 'token-default',
        badgeLabel: `Config (${getBadgeFallback(trace.ScopeName, 'Fallback')})`
      };
    case 'RuntimeOnly':
      return { cssClass: 'token-runtime', badgeLabel: 'Monitor Runtime' };
    default:
      return { cssClass: 'token-error', badgeLabel: 'Missing in Scope' };
  }
}

@customElement('hegs-json-viewer')
export class HegsJsonViewer extends LitElement {
  static get styles() {
    return css`
      :host {
        --background-color: #181c24;
        --surface-elevated: #202632;
        --border-subtle: rgba(255, 255, 255, 0.1);
        --color: #e6edf3;
        --string-color: #a5d6ff;
        --number-color: #f2cc60;
        --boolean-color: #79c0ff;
        --null-color: #d2a8ff;
        --property-color: #7ee787;
        --preview-color: #8b949e;
        --highlight-color: rgba(248, 81, 73, 0.35);

        --font-family:
          ui-monospace, SFMono-Regular, 'SF Mono', Menlo, Consolas,
          'Liberation Mono', monospace;
        --font-size: 0.875rem;

        --indent-size: 1.35em;
        --indentguide-size: 1px;
        --indentguide-style: solid;
        --indentguide-color: rgba(255, 255, 255, 0.08);
        --indentguide-color-active: rgba(121, 192, 255, 0.45);
        --indentguide: var(--indentguide-size) var(--indentguide-style)
          var(--indentguide-color);
        --indentguide-active: var(--indentguide-size) var(--indentguide-style)
          var(--indentguide-color-active);

        display: block;
        background-color: var(--background-color);
        color: var(--color);
        font-family: var(--font-family);
        font-size: var(--font-size);
        line-height: 1.6;
        overflow: auto;
        max-width: 100%;
        padding: 5px;
        border-radius: 8px;
        border: 1px solid var(--border-subtle);
        box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.04);
        box-sizing: border-box;
        position: relative;
      }

      .context-toolbar {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        justify-content: space-between;
        gap: 8px;
        padding: 6px 10px;
        margin-bottom: 8px;
        background: var(--surface-elevated);
        border: 1px solid var(--border-subtle);
        border-radius: 6px;
        font-size: 0.78rem;
        box-shadow: 0 2px 6px rgba(0, 0, 0, 0.2);
      }

      .context-summary {
        display: flex;
        align-items: center;
        gap: 8px;
        flex-wrap: wrap;
      }

      .context-env-pill {
        display: inline-flex;
        align-items: center;
        background: rgba(56, 139, 253, 0.16);
        color: #79c0ff;
        border: 1px solid rgba(56, 139, 253, 0.4);
        border-radius: 999px;
        padding: 2px 10px;
        font-weight: 600;
        letter-spacing: 0.01em;
      }

      .context-counts-text {
        color: #9ca3af;
      }

      .mode-switcher {
        display: inline-flex;
        padding: 2px;
        background: rgba(0, 0, 0, 0.28);
        border: 1px solid var(--border-subtle);
        border-radius: 6px;
        gap: 2px;
      }

      .mode-btn {
        background: transparent;
        color: #9ca3af;
        border: none;
        border-radius: 4px;
        padding: 2px 10px;
        font-family: inherit;
        font-size: 0.78rem;
        font-weight: 500;
        cursor: pointer;
        transition:
          background-color 140ms ease,
          color 140ms ease;
      }

      .mode-btn:hover {
        color: #e6edf3;
        background: rgba(255, 255, 255, 0.06);
      }

      .mode-btn:focus-visible {
        outline: 2px solid #58a6ff;
        outline-offset: 1px;
      }

      .mode-btn.active {
        background: #2f81f7;
        color: #ffffff;
        font-weight: 600;
        box-shadow: 0 1px 3px rgba(0, 0, 0, 0.3);
      }

      .token-chip {
        display: inline-flex;
        align-items: center;
        padding: 0 6px;
        margin: 0 1px;
        border-radius: 4px;
        font-family: inherit;
        font-size: 0.92em;
        line-height: 1.4;
        font-weight: 600;
        cursor: pointer;
        border: 1px solid transparent;
        transition:
          transform 100ms ease,
          filter 120ms ease,
          box-shadow 120ms ease;
      }

      .token-chip:hover {
        filter: brightness(1.14);
        box-shadow: 0 1px 4px rgba(0, 0, 0, 0.35);
      }

      .token-chip:focus-visible {
        outline: 2px solid #58a6ff;
        outline-offset: 1px;
      }

      .token-env {
        background: rgba(46, 160, 67, 0.2);
        color: #7ee787;
        border-color: rgba(46, 160, 67, 0.5);
      }

      .token-ancestor {
        background: rgba(57, 197, 207, 0.18);
        color: #56d4dd;
        border-color: rgba(57, 197, 207, 0.5);
      }

      .token-default {
        background: rgba(210, 153, 34, 0.2);
        color: #e3b341;
        border-color: rgba(210, 153, 34, 0.5);
      }

      .token-override {
        background: rgba(163, 113, 247, 0.22);
        color: #d2a8ff;
        border-color: rgba(163, 113, 247, 0.55);
      }

      .token-builtin {
        background: rgba(56, 139, 253, 0.2);
        color: #79c0ff;
        border-color: rgba(56, 139, 253, 0.5);
      }

      .token-runtime,
      .token-pending {
        background: rgba(139, 148, 158, 0.2);
        color: #c9d1d9;
        border-color: rgba(139, 148, 158, 0.45);
      }

      .token-expression {
        background: rgba(137, 87, 229, 0.24);
        color: #d2a8ff;
        border-color: rgba(137, 87, 229, 0.55);
      }

      .token-secure {
        background: rgba(219, 109, 40, 0.22);
        color: #ffa657;
        border-color: rgba(219, 109, 40, 0.55);
      }

      .token-error {
        background: rgba(248, 81, 73, 0.24);
        color: #ff7b72;
        border-color: rgba(248, 81, 73, 0.65);
      }

      .inline-resolved-arrow {
        color: #6e7681;
        margin: 0 5px;
        user-select: none;
      }

      .inline-resolved-val {
        display: inline-block;
        padding: 0 6px;
        border-radius: 4px;
        background: rgba(227, 179, 65, 0.12);
        border: 1px dashed rgba(227, 179, 65, 0.35);
        color: #f2cc60;
        font-style: normal;
        font-size: 0.92em;
      }

      .inspector-card {
        margin-top: 8px;
        padding: 10px 12px;
        background: var(--surface-elevated);
        border: 1px solid rgba(56, 139, 253, 0.45);
        border-left: 3px solid #2f81f7;
        border-radius: 6px;
        font-size: 0.8rem;
        color: var(--color);
        box-shadow: 0 4px 12px rgba(0, 0, 0, 0.35);
      }

      .inspector-header {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 8px;
        padding-bottom: 6px;
        margin-bottom: 8px;
        border-bottom: 1px solid var(--border-subtle);
      }

      .inspector-title {
        display: inline-flex;
        align-items: center;
        gap: 6px;
      }

      .inspector-grid {
        display: grid;
        grid-template-columns: auto 1fr;
        gap: 4px 10px;
        align-items: baseline;
      }

      .inspector-label {
        color: #8b949e;
        font-weight: 600;
        font-size: 0.75rem;
        text-transform: uppercase;
        letter-spacing: 0.03em;
      }

      .inspector-code {
        background: rgba(0, 0, 0, 0.25);
        padding: 1px 6px;
        border-radius: 4px;
        color: #e6edf3;
        word-break: break-all;
      }

      .inspector-chain {
        margin: 8px 0 4px;
        padding-left: 18px;
        color: #9ca3af;
      }

      .inspector-actions {
        display: flex;
        gap: 8px;
        margin-top: 8px;
        padding-top: 6px;
        border-top: 1px solid var(--border-subtle);
      }

      .inspector-link {
        color: #79c0ff;
        text-decoration: none;
        font-weight: 500;
        cursor: pointer;
        background: rgba(56, 139, 253, 0.12);
        border: 1px solid rgba(56, 139, 253, 0.3);
        border-radius: 4px;
        padding: 2px 8px;
        font-family: inherit;
        font-size: 0.76rem;
        transition: background-color 120ms ease;
      }

      .inspector-link:hover {
        background: rgba(56, 139, 253, 0.24);
      }

      .preview {
        color: var(--preview-color);
        font-style: italic;
        font-size: 0.92em;
      }

      .null {
        color: var(--null-color);
      }

      .key {
        color: var(--property-color);
        display: inline-block;
        padding: 1px 3px;
        border-radius: 3px;
      }

      .collapsable:before {
        display: inline-block;
        color: var(--property-color);
        font-size: 0.72em;
        content: '▶';
        line-height: 1em;
        width: 1.1em;
        height: 1em;
        text-align: center;
        margin-right: 2px;
        transition: transform 160ms cubic-bezier(0.2, 0, 0, 1);
        transform: rotate(90deg);
      }

      .collapsable.collapsableCollapsed:before {
        transform: rotate(0);
      }

      .collapsable {
        cursor: pointer;
        user-select: none;
      }

      .collapsable:hover {
        background: rgba(255, 255, 255, 0.06);
      }

      .collapsable:focus-visible {
        outline: 2px solid #58a6ff;
        outline-offset: 1px;
      }

      .string {
        color: var(--string-color);
      }

      .number {
        color: var(--number-color);
      }

      .boolean {
        color: var(--boolean-color);
      }

      ul {
        padding: 0;
        margin: 0;
        clear: both;
      }

      ul,
      li {
        list-style: none;
        position: relative;
      }

      li {
        padding: 1px 0;
      }

      li ul > li {
        position: relative;
        margin-left: var(--indent-size);
        padding-left: 0px;
      }

      ul ul:before {
        content: '';
        border-left: var(--indentguide);
        position: absolute;
        left: calc(0.45em - var(--indentguide-size));
        top: 0.25em;
        bottom: 0.25em;
        transition: border-color 140ms ease;
      }

      ul ul:hover:before {
        border-left: var(--indentguide-active);
      }

      mark {
        background-color: var(--highlight-color);
        color: inherit;
        border-radius: 3px;
        padding: 0 2px;
      }
    `;
  }

  @property({ converter: JSONConverter, type: Object })
  data?: unknown;

  @property({ type: Object })
  resolutionContext?: PropertyResolutionContext;

  @property({ type: String, attribute: 'display-mode' })
  displayMode: JsonViewerDisplayMode = 'inline';

  @property({ attribute: false })
  resolutionCache: PropertyResolutionClientCache =
    defaultPropertyResolutionClientCache;

  @state() private state: JsonViewerState = {
    expanded: {},
    filtered: {},
    highlight: null
  };

  @state() private traces: Map<string, PropertyResolutionTraceApiModel> =
    new Map();

  @state() private referencedNames: string[] = [];

  @state() private activeInspectedProperty: string | null = null;

  private fetchGeneration = 0;

  private async setState(
    stateFn: (
      state: JsonViewerState,
      el: HegsJsonViewer
    ) => Partial<JsonViewerState>
  ) {
    const currentState = this.state;

    this.state = {
      ...currentState,
      ...stateFn(currentState, this)
    };
  }

  connectedCallback() {
    if (!this.hasAttribute('data') && !isDefined(this.data)) {
      this.setAttribute('data', this.innerText);
    }

    super.connectedCallback();
  }

  protected willUpdate(changedProps: PropertyValues<this>) {
    if (
      changedProps.has('data') ||
      changedProps.has('resolutionContext') ||
      changedProps.has('resolutionCache')
    ) {
      this.refreshPropertyTraces();
    }
  }

  private async refreshPropertyTraces(): Promise<void> {
    const names = extractReferencedPropertyNames(
      this.data,
      this.resolutionContext?.locationKind
    );
    this.referencedNames = names;
    this.traces = new Map();
    this.activeInspectedProperty = null;
    const gen = ++this.fetchGeneration;

    if (names.length === 0 || !this.resolutionContext) {
      return;
    }

    try {
      const resolvedMap = await this.resolutionCache.resolveProperties(
        this.resolutionContext,
        names
      );
      if (gen === this.fetchGeneration) {
        this.traces = resolvedMap;
      }
    } catch {
      if (gen === this.fetchGeneration) {
        this.traces = new Map();
      }
    }
  }

  handlePropertyClick = (path: string) => (e: Event) => {
    e.preventDefault();

    this.setState(toggleNode(path));
  };

  /**
   * Keyboard equivalent of {@link handlePropertyClick}. Collapsable keys are
   * exposed as buttons, so Enter and Space must toggle them too.
   */
  handlePropertyKeydown = (path: string) => (e: KeyboardEvent) => {
    if (e.key !== 'Enter' && e.key !== ' ') {
      return;
    }
    e.preventDefault();

    this.setState(toggleNode(path));
  };

  private handleDisplayModeSelect = (mode: JsonViewerDisplayMode) => () => {
    this.displayMode = mode;
  };

  private handleInspectToken = (propertyName: string) => (e: Event) => {
    e.stopPropagation();
    this.activeInspectedProperty =
      this.activeInspectedProperty === propertyName ? null : propertyName;
  };

  expand(glob: string | RegExp) {
    this.setState(expand(glob, true));
  }

  expandAll() {
    this.setState(expand('**', true));
  }

  collapseAll() {
    this.setState(expand('**', false));
  }

  collapse(glob: string | RegExp) {
    this.setState(expand(glob, false));
  }

  *search(criteria: string) {
    for (const [node, path] of deepTraverse(this.data)) {
      if (isPrimitiveOrNode(node) && String(node).includes(criteria)) {
        this.expand(path);
        this.updateComplete.then(() => {
          const elem = this.shadowRoot!.querySelector(
            `[data-path="${path}"]`
          ) as HTMLElement;
          elem.scrollIntoView({
            behavior: 'smooth',
            inline: 'center',
            block: 'center'
          });
          elem.focus();
        });

        this.setState(highlight(path));

        yield {
          value: node,
          path
        };
      }
    }

    this.setState(highlight(null));
  }

  filter(criteria: string | RegExp) {
    this.setState(filter(criteria));
  }

  resetFilter() {
    this.setState(resetFilter());
  }

  renderObject(node: Record<string, unknown>, path: string): TemplateResult {
    return html`
      <ul part="object">
        ${Object.keys(node).map(key => {
          const nodeData = node[key];
          const nodePath = path ? `${path}.${key}` : key;
          const isPrimitive = isPrimitiveOrNode(nodeData);

          return html`
            <li
              part="property"
              data-path="${nodePath}"
              .hidden="${this.state.filtered[nodePath]}"
            >
              <span
                part="key"
                class="${classMap({
                  key: Boolean(key),
                  collapsable: !isPrimitive,
                  collapsableCollapsed: !this.state.expanded[nodePath]
                })}"
                role="${ifDefined(!isPrimitive ? 'button' : undefined)}"
                tabindex="${ifDefined(!isPrimitive ? 0 : undefined)}"
                aria-expanded="${ifDefined(
                  !isPrimitive
                    ? this.state.expanded[nodePath]
                      ? 'true'
                      : 'false'
                    : undefined
                )}"
                @click="${
                  !isPrimitive ? this.handlePropertyClick(nodePath) : null
                }"
                @keydown="${
                  !isPrimitive ? this.handlePropertyKeydown(nodePath) : null
                }"
              >
                ${key}:
              </span>
              ${this.renderNode(nodeData, nodePath)}
            </li>
          `;
        })}
      </ul>
    `;
  }

  renderNode(node: unknown, path = '') {
    const isPrimitive = isPrimitiveOrNode(node);
    const isExpanded = !path || this.state.expanded[path] || isPrimitive;

    if (isExpanded) {
      return isPrimitive
        ? this.renderPrimitive(node, path)
        : this.renderObject(node as Record<string, unknown>, path);
    } else {
      return this.renderNodePreview(
        node as Record<string, unknown> | unknown[]
      );
    }
  }

  renderNodePreview(node: Record<string, unknown> | unknown[]) {
    return html`
      <span part="preview" class="preview"> ${generateNodePreview(node)} </span>
    `;
  }

  private renderContextualString(raw: string, path: string): TemplateResult {
    const profile = resolveTokenSyntaxProfile(
      this.resolutionContext?.locationKind,
      path
    );
    const tokens = parsePropertyTokens(raw, profile);
    const hasSpecial = tokens.some(
      t =>
        t.kind === 'property' ||
        t.kind === 'escaped-dollar' ||
        t.kind === 'expression-prefix' ||
        t.kind === 'unclosed-token'
    );

    if (!hasSpecial) {
      return html`<span
        part="primitive primitive-string"
        tabindex="0"
        class="string"
        >${JSON.stringify(raw)}</span
      >`;
    }

    if (this.displayMode === 'raw') {
      return html`<span
        part="primitive primitive-string"
        tabindex="0"
        class="string"
        >"${tokens.map(token => this.renderTokenSegment(token))}"</span
      >`;
    }

    const evaluated = evaluateStringWithTraces(raw, this.traces, profile);

    if (this.displayMode === 'resolved') {
      if (evaluated.status === 'resolved' || evaluated.status === 'literal') {
        return html`<span
          part="primitive primitive-string"
          tabindex="0"
          class="string"
          title="Raw: ${raw}"
          >${JSON.stringify(evaluated.resolvedValue)}</span
        >`;
      }
      return html`<span
        part="primitive primitive-string"
        tabindex="0"
        class="string"
        >"${tokens.map(token => this.renderTokenSegment(token))}"</span
      >`;
    }

    // 'inline' mode: show tokenized raw value + resolved preview when resolved
    return html`<span
      part="primitive primitive-string"
      tabindex="0"
      class="string"
      >"${tokens.map(token => this.renderTokenSegment(token))}"${
        evaluated.status === 'resolved'
          ? html`<span class="inline-resolved-arrow">→</span
              ><span class="inline-resolved-val" part="resolved-preview"
                >${JSON.stringify(evaluated.resolvedValue)}</span
              >`
          : nothing
      }</span
    >`;
  }

  private renderTokenSegment(token: ParsedPropertyToken): TemplateResult {
    if (token.kind === 'property') {
      const trace = this.traces.get(token.propertyName);
      const badge = getTokenBadgeAppearance(trace);
      return html`<button
        type="button"
        part="property-token"
        class="token-chip ${badge.cssClass}"
        data-property-name="${token.propertyName}"
        title="${badge.badgeLabel}"
        @click="${this.handleInspectToken(token.propertyName)}"
      >
        ${escapeJsonStringSegment(token.rawText)}
      </button>`;
    }

    if (token.kind === 'expression-prefix') {
      return html`<span
        part="expression-token"
        class="token-chip token-expression"
        title="C# Expression (Evaluated at Deploy Time)"
        >${escapeJsonStringSegment(token.rawText)}</span
      >`;
    }

    if (token.kind === 'escaped-dollar') {
      return html`<span
        part="escaped-dollar"
        class="token-chip token-builtin"
        title="Escaped literal $"
        >${escapeJsonStringSegment(token.rawText)}</span
      >`;
    }

    if (token.kind === 'unclosed-token') {
      return html`<span
        part="unclosed-token"
        class="token-chip token-error"
        title="Unclosed $ token"
        >${escapeJsonStringSegment(token.rawText)}</span
      >`;
    }

    return html`${escapeJsonStringSegment(token.value)}`;
  }

  private renderToolbar(): TemplateResult | typeof nothing {
    if (this.referencedNames.length === 0) {
      return nothing;
    }

    let resolvedCount = 0;
    let missingCount = 0;
    let secretCount = 0;

    for (const name of this.referencedNames) {
      const trace = this.traces.get(name);
      if (!trace) {
        continue;
      }
      if (trace.IsSecure) {
        secretCount++;
      }
      if (trace.Source === 'Unresolved') {
        missingCount++;
      } else {
        resolvedCount++;
      }
    }

    const envLabel = this.resolutionContext?.environmentName?.trim();

    return html`
      <div class="context-toolbar" part="context-toolbar">
        <div class="context-summary">
          ${
            envLabel
              ? html`<span class="context-env-pill" part="context-env"
                  >Env: ${envLabel}</span
                >`
              : html`<span class="context-env-pill" part="context-env"
                  >Scope: Global Default</span
                >`
          }
          <span class="context-counts-text" part="context-counts">
            ${this.referencedNames.length}
            ${this.referencedNames.length === 1 ? 'property' : 'properties'}
            (${resolvedCount} resolved${
              missingCount > 0 ? `, ${missingCount} missing` : ''
            }${secretCount > 0 ? `, ${secretCount} secret` : ''})
          </span>
        </div>
        <div
          class="mode-switcher"
          role="group"
          aria-label="Property display mode"
        >
          ${(['raw', 'inline', 'resolved'] as JsonViewerDisplayMode[]).map(
            mode => html`
              <button
                type="button"
                class="mode-btn ${this.displayMode === mode ? 'active' : ''}"
                data-mode="${mode}"
                aria-pressed="${this.displayMode === mode ? 'true' : 'false'}"
                @click="${this.handleDisplayModeSelect(mode)}"
              >
                ${mode.charAt(0).toUpperCase() + mode.slice(1)}
              </button>
            `
          )}
        </div>
      </div>
    `;
  }

  private renderInspectorCard(): TemplateResult | typeof nothing {
    if (!this.activeInspectedProperty) {
      return nothing;
    }

    const propName = this.activeInspectedProperty;
    const trace = this.traces.get(propName);
    const badge = getTokenBadgeAppearance(trace);
    const presented = presentTrace(trace);
    const usesBraceSyntax =
      this.resolutionContext?.locationKind === 'deployment-definition' ||
      this.resolutionContext?.locationKind === 'script-arguments';
    const tokenDisplayTitle = usesBraceSyntax
      ? `\${${propName}}`
      : `$${propName}$`;
    const rawScope =
      trace?.Source === 'GlobalDefault'
        ? ''
        : (trace?.ScopeName ?? this.resolutionContext?.environmentName ?? '');
    const lookupHref = `/variables/value-lookup?name=${encodeURIComponent(
      propName
    )}${rawScope ? `&scope=${encodeURIComponent(rawScope)}` : ''}`;

    return html`
      <div class="inspector-card" part="inspector-card">
        <div class="inspector-header">
          <div class="inspector-title">
            <strong>${tokenDisplayTitle}</strong>
            <span class="token-chip ${badge.cssClass}"
              >${badge.badgeLabel}</span
            >
          </div>
          <button
            type="button"
            class="inspector-link"
            data-action="close-inspector"
            @click="${this.handleInspectToken(propName)}"
          >
            Close
          </button>
        </div>
        <div class="inspector-grid">
          <span class="inspector-label">Raw</span>
          <code class="inspector-code"
            >${trace?.RawValue ?? '(not defined)'}</code
          >
          <span class="inspector-label">Resolved</span>
          <code class="inspector-code"
            >${
              presented.hasEffectiveValue
                ? presented.effectiveDisplayValue
                : (trace?.ResolvedValueJson ?? '(unresolved)')
            }</code
          >
          <span class="inspector-label">Diagnostic</span>
          <code class="inspector-code"
            >${
              trace?.Diagnostic
                ? `${trace.Diagnostic} — ${presented.diagnosticTitle}`
                : 'Pending'
            }</code
          >
        </div>
        ${
          trace?.ResolutionChain && trace.ResolutionChain.length > 0
            ? html`
                <ol class="inspector-chain">
                  ${trace.ResolutionChain.map(hop => html`<li>${hop}</li>`)}
                </ol>
              `
            : nothing
        }
        <div class="inspector-actions">
          <a class="inspector-link" href="${lookupHref}">
            Open in Value Lookup →
          </a>
        </div>
      </div>
    `;
  }

  renderPrimitive(node: unknown, path: string) {
    const highlight = this.state.highlight;
    const nodeType = getType(node);
    const value = isNode(node)
      ? node
      : typeof node === 'string'
        ? this.renderContextualString(node, path)
        : html`
            <span
              part="primitive primitive-${nodeType}"
              tabindex="0"
              class="${nodeType}"
              >${JSON.stringify(node)}</span
            >
          `;

    return path === highlight
      ? html`<mark part="highlight">${value}</mark>`
      : value;
  }

  render() {
    const data = this.data;

    return isDefined(data)
      ? html`${this.renderToolbar()}${this.renderNode(
          data
        )}${this.renderInspectorCard()}`
      : null;
  }
}
