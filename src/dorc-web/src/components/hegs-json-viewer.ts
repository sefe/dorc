import {
  ComplexAttributeConverter,
  css,
  html,
  LitElement,
  TemplateResult
} from 'lit';
import { property, state } from 'lit/decorators.js';
import { classMap } from 'lit/directives/class-map.js';

import { customElement } from 'lit/decorators.js';

/**
 * @since 1.0
 *
 * @csspart object - The object wrapper element.
 * @csspart property - The wrapper element of a property.
 * @csspart key - The key element of a property.
 * @csspart primitive - The primitive value.
 * @csspart primitive--string - Applied when the primitive is a string.
 * @csspart primitive--number - Applied when the primitive is a number.
 * @csspart primitive--boolean - Applied when the primitive is a boolean.
 * @csspart primitive--null - Applied when the primitive is a null.
 * @csspart preview - The value preview of a property.
 * @csspart highlight - The highlighted value.
 *
 * @cssproperty [--background-color] - The component background color.
 * @cssproperty [--color] - The text color.
 * @cssproperty [--font-family] - The font family.
 * @cssproperty [--font-size] - The font size.
 * @cssproperty [--indent-size] - The size of the indentation of nested properties.
 * @cssproperty [--indentguide-size] - The width of the indentation line.
 * @cssproperty [--indentguide-style] - The style of the indentation line.
 * @cssproperty [--indentguide-color] - The color of the indentation line.
 * @cssproperty [--indentguide-color-active] - The color of the indentation line when is active.
 * @cssproperty [--indentguide]
 * @cssproperty [--indentguide-active]
 * @cssproperty [--string-color] - The color of a string type value
 * @cssproperty [--number-color] - The color of a number type value
 * @cssproperty [--boolean-color] - The color of a boolean type value
 * @cssproperty [--null-color] - The color of a null type value
 * @cssproperty [--property-color] - The color of the property key.
 * @cssproperty [--preview-color] - The color of the collapsed property preview.
 * @cssproperty [--highlight-color] - The color of the highlighted value.
 */

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

  @state() private state: JsonViewerState = {
    expanded: {},
    filtered: {},
    highlight: null
  };

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

  handlePropertyClick = (path: string) => (e: Event) => {
    e.preventDefault();

    this.setState(toggleNode(path));
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
                @click="${!isPrimitive
                  ? this.handlePropertyClick(nodePath)
                  : null}"
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

  renderPrimitive(node: Primitive | null, path: string) {
    const highlight = this.state.highlight;
    const nodeType = getType(node);
    const value = isNode(node)
      ? node
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

    return isDefined(data) ? this.renderNode(data) : null;
  }
}
