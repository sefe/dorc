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
import type { PropertyResolutionTraceApiModel } from '../apis/dorc-api';
import {
  defaultPropertyResolutionClientCache,
  PropertyResolutionClientCache,
  PropertyResolutionContext
} from '../variable-resolution/property-resolution-client-cache';
import {
  extractReferencedPropertyNames,
  ParsedPropertyToken,
  parsePropertyTokens
} from '../variable-resolution/property-token-parser';

export type JsonViewerDisplayMode = 'raw' | 'inline' | 'resolved';

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

type Primitive = string | number | boolean;

function isRegex(obj: RegExp | any): boolean {
  return obj instanceof RegExp;
}

function getType(obj: any): SupportedTypes {
  return obj === null
    ? SupportedTypes.Null
    : Array.isArray(obj)
      ? SupportedTypes.Array
      : (obj!.constructor.name.toLowerCase() as SupportedTypes);
}

function isPrimitive(obj: any): boolean {
  return obj !== Object(obj);
}

function isNode(obj: any): boolean {
  return !!obj && !!(obj as Node).nodeType;
}

function isPrimitiveOrNode(obj: any): boolean {
  return isPrimitive(obj) || isNode(obj);
}

export function evaluateStringWithTraces(
  raw: string,
  traces: Map<string, PropertyResolutionTraceApiModel>
): {
  status: 'resolved' | 'unresolved' | 'literal';
  resolvedValue: unknown;
  resolvedDisplayString: string;
} {
  const tokens = parsePropertyTokens(raw);
  const propTokens = tokens.filter(
    (
      t
    ): t is Extract<
      ParsedPropertyToken,
      { kind: 'property' }
    > => t.kind === 'property'
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
    const trace = traces.get(pt.propertyName.toLowerCase());
    if (
      !trace ||
      trace.Source === 'Unresolved' ||
      trace.Source === 'RuntimeOnly' ||
      trace.Diagnostic === 'CircularReference' ||
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
    const singleTrace = traces.get(tokens[0].propertyName.toLowerCase())!;
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
      const trace = traces.get(token.propertyName.toLowerCase())!;
      const parsedVal = JSON.parse(trace.ResolvedValueJson!);
      out +=
        typeof parsedVal === 'string' ? parsedVal : JSON.stringify(parsedVal);
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

function generateNodePreview(
  node: any,
  {
    nodeCount = 3,
    maxLength = 15
  }: { nodeCount?: number; maxLength?: number } = {}
): string {
  const isArray = Array.isArray(node);
  const objectNodes = Object.keys(node);
  const keys = objectNodes.slice(0, nodeCount);
  const preview = [];

  const getNodePreview = (nodeValue: any) => {
    const nodeType = getType(nodeValue);

    switch (nodeType) {
      case SupportedTypes.Object:
        return Object.keys(nodeValue).length === 0 ? '{ }' : '{ ... }';
      case SupportedTypes.Array:
        return nodeValue.length === 0 ? '[ ]' : '[ ... ]';
      case SupportedTypes.String:
        return `"${nodeValue.substring(0, maxLength)}${nodeValue.length > maxLength ? '...' : ''}"`;
      default:
        return String(nodeValue);
    }
  };

  const childPreviews = [];
  for (const key of keys) {
    const nodePreview = [];
    const nodeValue = node[key];

    if (!isArray) nodePreview.push(`${key}: `);

    nodePreview.push(getNodePreview(nodeValue));
    childPreviews.push(nodePreview.join(''));
  }

  if (objectNodes.length > nodeCount) {
    childPreviews.push('...');
  }
  preview.push(childPreviews.join(', '));

  const previewText = preview.join('');

  return isArray ? `[ ${previewText} ]` : `{ ${previewText} }`;
}

function* deepTraverse(obj: any): Generator<[any, string, string[]]> {
  const stack: Array<[any, string, string[]]> = [[obj, '', []]];

  while (stack.length) {
    const [node, path, parents] = stack.shift()!;

    if (path) {
      yield [node, path, parents];
    }

    if (!isPrimitive(node)) {
      for (const [key, value] of Object.entries(node)) {
        stack.push([
          value,
          `${path}${path ? '.' : ''}${key}`,
          [...parents, path]
        ]);
      }
    }
  }
}

/**
 * Matches a string using a glob-like syntax)
 */
function checkGlob(str: string, glob: string): boolean {
  const strParts = str.split('.');
  const globaParts = glob.split('.');

  const isStar = (s: string) => s === '*';
  const isGlobStar = (s: string) => s === '**';

  let strIndex = 0;
  let globIndex = 0;

  while (strIndex < strParts.length) {
    const globPart = globaParts[globIndex];
    const strPart = strParts[strIndex];

    if (globPart === strPart || isStar(globPart)) {
      globIndex++;
      strIndex++;
    } else if (isGlobStar(globPart)) {
      globIndex++;
      strIndex = strParts.length - (globaParts.length - globIndex);
    } else {
      return false;
    }
  }

  return globIndex === globaParts.length;
}

const JSONConverter: ComplexAttributeConverter = {
  fromAttribute: (value: string): any => {
    return value && value.trim() ? JSON.parse(value) : undefined;
  },
  toAttribute: (value: any): string => {
    return JSON.stringify(value);
  }
};

const isDefined = (value: any): boolean => value !== void 0;

const isMatchingPath = (path: string, criteria: string | RegExp) =>
  isRegex(criteria)
    ? !!path.match(criteria as RegExp)
    : checkGlob(path, criteria as string);

const toggleNode =
  (path: string, expanded?: boolean) =>
  (state: JsonViewerState): Partial<JsonViewerState> => ({
    expanded: {
      ...state.expanded,
      [path]: isDefined(expanded) ? !!expanded : !state.expanded[path]
    }
  });

const expand =
  (regexOrGlob: string | RegExp, isExpanded: boolean) =>
  (_state: JsonViewerState, el: any): Partial<JsonViewerState> => {
    const expanded: Record<string, boolean> = {};

    if (regexOrGlob) {
      for (const [, path, parents] of deepTraverse(el.data)) {
        if (isMatchingPath(path, regexOrGlob)) {
          expanded[path] = isExpanded;
          parents.forEach((p: string) => (expanded[p] = isExpanded));
        }
      }
    }

    return { expanded };
  };

const filter =
  (regexOrGlob: string | RegExp) =>
  (_state: JsonViewerState, el: any): Partial<JsonViewerState> => {
    const filtered: Record<string, boolean> = {};

    if (regexOrGlob) {
      for (const [, path, parents] of deepTraverse(el.data)) {
        if (isMatchingPath(path, regexOrGlob)) {
          filtered[path] = false;
          parents.forEach((p: string) => (filtered[p] = false));
        } else {
          filtered[path] = true;
        }
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
        --background-color: #2a2f3a;
        --color: #f8f8f2;
        --string-color: #a3eea0;
        --number-color: #d19a66;
        --boolean-color: #4ba7ef;
        --null-color: #df9cf3;
        --property-color: #6fb3d2;
        --preview-color: rgba(222, 175, 143, 0.9);
        --highlight-color: #7b0000;

        --font-family: monaco, Consolas, 'Lucida Console', monospace;
        --font-size: 1rem;

        --indent-size: 1.5em;
        --indentguide-size: 1px;
        --indentguide-style: solid;
        --indentguide-color: #333;
        --indentguide-color-active: #666;
        --indentguide: var(--indentguide-size) var(--indentguide-style)
          var(--indentguide-color);
        --indentguide-active: var(--indentguide-size) var(--indentguide-style)
          var(--indentguide-color-active);

        display: block;
        background-color: var(--background-color);
        color: var(--color);
        font-family: var(--font-family);
        font-size: var(--font-size);
        overflow: auto;
        max-width: 100%;
        padding: 5px;
        box-sizing: border-box;
        position: relative;
      }

      .context-toolbar {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        justify-content: space-between;
        gap: 6px;
        padding: 4px 8px;
        margin-bottom: 6px;
        background: rgba(255, 255, 255, 0.06);
        border: 1px solid rgba(255, 255, 255, 0.12);
        border-radius: 4px;
        font-size: 0.8em;
      }

      .context-summary {
        display: flex;
        align-items: center;
        gap: 8px;
        flex-wrap: wrap;
      }

      .context-env-pill {
        background: rgba(111, 179, 210, 0.2);
        color: #9ad5f0;
        border: 1px solid rgba(111, 179, 210, 0.45);
        border-radius: 10px;
        padding: 1px 8px;
        font-weight: 600;
      }

      .mode-switcher {
        display: inline-flex;
        border: 1px solid rgba(255, 255, 255, 0.2);
        border-radius: 4px;
        overflow: hidden;
      }

      .mode-btn {
        background: transparent;
        color: #ccc;
        border: none;
        padding: 2px 8px;
        font-family: inherit;
        font-size: 0.95em;
        cursor: pointer;
      }

      .mode-btn.active {
        background: #4ba7ef;
        color: #111;
        font-weight: 600;
      }

      .token-chip {
        display: inline-block;
        padding: 0 4px;
        margin: 0 1px;
        border-radius: 3px;
        font-family: inherit;
        font-size: inherit;
        line-height: inherit;
        font-weight: 600;
        cursor: pointer;
        border: 1px solid transparent;
      }

      .token-env {
        background: rgba(76, 175, 80, 0.22);
        color: #b9f6ca;
        border-color: rgba(76, 175, 80, 0.55);
      }

      .token-ancestor {
        background: rgba(0, 188, 212, 0.22);
        color: #84ffff;
        border-color: rgba(0, 188, 212, 0.55);
      }

      .token-default {
        background: rgba(255, 193, 7, 0.2);
        color: #ffe57f;
        border-color: rgba(255, 193, 7, 0.55);
      }

      .token-override {
        background: rgba(156, 39, 176, 0.25);
        color: #ea80fc;
        border-color: rgba(156, 39, 176, 0.6);
      }

      .token-builtin {
        background: rgba(33, 150, 243, 0.22);
        color: #82b1ff;
        border-color: rgba(33, 150, 243, 0.55);
      }

      .token-runtime,
      .token-pending {
        background: rgba(158, 158, 158, 0.22);
        color: #e0e0e0;
        border-color: rgba(158, 158, 158, 0.5);
      }

      .token-expression {
        background: rgba(124, 77, 255, 0.25);
        color: #b388ff;
        border-color: rgba(124, 77, 255, 0.6);
      }

      .token-secure {
        background: rgba(255, 152, 0, 0.24);
        color: #ffd180;
        border-color: rgba(255, 152, 0, 0.6);
      }

      .token-error {
        background: rgba(244, 67, 54, 0.28);
        color: #ff8a80;
        border-color: rgba(244, 67, 54, 0.75);
      }

      .inline-resolved-arrow {
        color: #9e9e9e;
        margin: 0 4px;
      }

      .inline-resolved-val {
        color: #ffd54f;
        font-style: italic;
      }

      .inspector-card {
        margin-top: 6px;
        padding: 8px 10px;
        background: #1e222a;
        border: 1px solid #4ba7ef;
        border-radius: 4px;
        font-size: 0.85em;
        color: #f8f8f2;
      }

      .inspector-header {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 8px;
        margin-bottom: 4px;
      }

      .inspector-chain {
        margin: 4px 0;
        padding-left: 16px;
        color: #cfd8dc;
      }

      .inspector-actions {
        display: flex;
        gap: 8px;
        margin-top: 6px;
      }

      .inspector-link {
        color: #82b1ff;
        text-decoration: underline;
        cursor: pointer;
        background: none;
        border: none;
        padding: 0;
        font: inherit;
      }

      .preview {
        color: var(--preview-color);
      }

      .null {
        color: var(--null-color);
      }

      .key {
        color: var(--property-color);
        display: inline-block;
      }

      .collapsable:before {
        display: inline-block;
        color: var(--color);
        font-size: 0.8em;
        content: '▶';
        line-height: 1em;
        width: 1em;
        height: 1em;
        text-align: center;

        transition: transform 195ms ease-out;
        transform: rotate(90deg);
        color: var(--property-color);
      }

      .collapsable.collapsableCollapsed:before {
        transform: rotate(0);
      }

      .collapsable {
        cursor: pointer;
        user-select: none;
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
        clear: both;
      }

      ul,
      li {
        list-style: none;
        position: relative;
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
        left: calc(0.5em - var(--indentguide-size));
        top: 0.3em;
        bottom: 0.3em;
      }

      ul ul:hover:before {
        border-left: var(--indentguide-active);
      }

      mark {
        background-color: var(--highlight-color);
      }
    `;
  }

  @property({ converter: JSONConverter, type: Object })
  data?: any;

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
    const names = extractReferencedPropertyNames(this.data);
    this.referencedNames = names;
    const gen = ++this.fetchGeneration;

    if (names.length === 0 || !this.resolutionContext) {
      this.traces = new Map();
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
          const node = this.shadowRoot!.querySelector(
            `[data-path="${path}"]`
          ) as HTMLElement;
          node.scrollIntoView({
            behavior: 'smooth',
            inline: 'center',
            block: 'center'
          });

          node.focus();
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
                  key: key,
                  collapsable: !isPrimitive,
                  collapsableCollapsed: !this.state.expanded[nodePath]
                })}"
                role="${!isPrimitive ? 'button' : nothing}"
                tabindex="${!isPrimitive ? '0' : nothing}"
                aria-expanded="${
                  !isPrimitive
                    ? String(Boolean(this.state.expanded[nodePath]))
                    : nothing
                }"
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

  renderNode(node: any, path = '') {
    const isPrimitive = isPrimitiveOrNode(node);
    const isExpanded = !path || this.state.expanded[path] || isPrimitive;

    if (isExpanded) {
      return isPrimitive
        ? this.renderPrimitive(node, path)
        : this.renderObject(node, path);
    } else {
      return this.renderNodePreview(node);
    }
  }

  renderNodePreview(node: any) {
    return html`
      <span part="preview" class="preview"> ${generateNodePreview(node)} </span>
    `;
  }

  private renderContextualString(raw: string): TemplateResult {
    const tokens = parsePropertyTokens(raw);
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

    const evaluated = evaluateStringWithTraces(raw, this.traces);

    if (this.displayMode === 'resolved') {
      if (evaluated.status === 'resolved') {
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
              ><span
                class="inline-resolved-val"
                part="resolved-preview"
                >${JSON.stringify(evaluated.resolvedValue)}</span
              >`
          : nothing
      }</span
    >`;
  }

  private renderTokenSegment(token: ParsedPropertyToken): TemplateResult {
    if (token.kind === 'property') {
      const trace = this.traces.get(token.propertyName.toLowerCase());
      const badge = getTokenBadgeAppearance(trace);
      return html`<button
        type="button"
        part="property-token"
        class="token-chip ${badge.cssClass}"
        data-property-name="${token.propertyName}"
        title="${badge.badgeLabel}"
        @click="${this.handleInspectToken(token.propertyName)}"
        >${token.rawText}</button
      >`;
    }

    if (token.kind === 'expression-prefix') {
      return html`<span
        part="expression-token"
        class="token-chip token-expression"
        title="C# Expression (Evaluated at Deploy Time)"
        >${token.rawText}</span
      >`;
    }

    if (token.kind === 'escaped-dollar') {
      return html`<span
        part="escaped-dollar"
        class="token-chip token-builtin"
        title="Escaped literal $"
        >${token.rawText}</span
      >`;
    }

    if (token.kind === 'unclosed-token') {
      return html`<span
        part="unclosed-token"
        class="token-chip token-error"
        title="Unclosed $ token"
        >${token.rawText}</span
      >`;
    }

    return html`${token.value}`;
  }

  private renderToolbar(): TemplateResult | typeof nothing {
    if (this.referencedNames.length === 0) {
      return nothing;
    }

    let resolvedCount = 0;
    let missingCount = 0;
    let secretCount = 0;

    for (const name of this.referencedNames) {
      const trace = this.traces.get(name.toLowerCase());
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
          <span part="context-counts">
            ${this.referencedNames.length}
            ${this.referencedNames.length === 1 ? 'property' : 'properties'}
            (${resolvedCount} resolved${
              missingCount > 0 ? `, ${missingCount} missing` : ''
            }${secretCount > 0 ? `, ${secretCount} secret` : ''})
          </span>
        </div>
        <div class="mode-switcher" role="group" aria-label="Property display mode">
          ${(['raw', 'inline', 'resolved'] as JsonViewerDisplayMode[]).map(
            mode => html`
              <button
                type="button"
                class="mode-btn ${this.displayMode === mode ? 'active' : ''}"
                data-mode="${mode}"
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
    const trace = this.traces.get(propName.toLowerCase());
    const badge = getTokenBadgeAppearance(trace);
    const scopeParam =
      trace?.ScopeName ?? this.resolutionContext?.environmentName ?? '';
    const lookupHref = `/variables/value-lookup?name=${encodeURIComponent(
      propName
    )}${scopeParam ? `&scope=${encodeURIComponent(scopeParam)}` : ''}`;

    return html`
      <div class="inspector-card" part="inspector-card">
        <div class="inspector-header">
          <div>
            <strong>$${propName}$</strong>
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
        <div>
          <div>
            <strong>Raw:</strong>
            <code>${trace?.RawValue ?? '(not defined)'}</code>
          </div>
          <div>
            <strong>Resolved:</strong>
            <code>${trace?.ResolvedValueJson ?? '(unresolved)'}</code>
          </div>
          <div>
            <strong>Diagnostic:</strong>
            <code>${trace?.Diagnostic ?? 'Pending'}</code>
          </div>
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
            Open in Value Lookup
          </a>
        </div>
      </div>
    `;
  }

  renderPrimitive(node: Primitive | null, path: string) {
    const highlight = this.state.highlight;
    const nodeType = getType(node);
    const value = isNode(node)
      ? node
      : typeof node === 'string'
        ? this.renderContextualString(node)
        : html`
            <span
              part="primitive primitive-${nodeType}"
              tabindex="0"
              class="${getType(node)}"
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

