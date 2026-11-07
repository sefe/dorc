import { html, nothing } from 'lit/html.js';
import { classMap } from 'lit/directives/class-map.js';

import { HegsTreeNode } from './hegs-tree-node';

import { TreeNode } from './TreeNode';

export function addHegsTreeNodeTemplate(this: HegsTreeNode) {
  const hasChildren = Boolean(
    this.data?.children && this.data.children.length > 0
  );
  const isSelected = Boolean(
    this.selectable &&
    this.data?.path !== undefined &&
    this.data.path === this.selectedPath
  );
  return html`
    <div
      id="root"
      class="node-container ${classMap({
        ...this._computeSelectedClass(),
        'selectable-container': this.selectable,
        'is-selected': isSelected,
        'is-child': Boolean(this.data?.hasParent)
      })}"
    >
      <div class="node-row ${this.selectable ? 'selectable-row' : ''}">
        <span
          class="node-preicon ${classMap(this._computePreIconClass())}"
          role=${hasChildren ? 'button' : nothing}
          aria-expanded=${hasChildren ? (this.open ? 'true' : 'false') : nothing}
          aria-label=${
            hasChildren
              ? `${this.open ? 'Collapse' : 'Expand'} ${this.data?.name ?? ''}`
              : nothing
          }
          @click="${this.toggleChildren}"
          @keydown="${this.toggleChildren}"
        ></span>

        ${
          this.selectable
            ? html`
                <button
                  type="button"
                  class="node-select-btn"
                  aria-pressed=${isSelected ? 'true' : 'false'}
                  @click="${this.selectNode}"
                >
                  <span class="node-title-row">
                    <span class="node-title">${this.data?.name}</span>
                    ${
                      hasChildren
                        ? html`<span
                            class="node-badge"
                            title="${this.data.children.length} child steps"
                            >${this.data.children.length}</span
                          >`
                        : nothing
                    }
                    ${
                      this.data?.disabled
                        ? html`<span class="node-badge">Off</span>`
                        : nothing
                    }
                  </span>
                  ${
                    this.data?.subtitle
                      ? html`<span class="node-subtitle"
                          >${this.data.subtitle}</span
                        >`
                      : nothing
                  }
                </button>
              `
            : html`
                <input
                  .checked="${this.checked}"
                  .indeterminate="${this.indeterminate}"
                  type="checkbox"
                  id="deployment"
                  data-componentId="${this.data?.id}"
                  @click="${this.selectComponentForDeployment}"
                />

                <label for="checkbox">${this.data?.name}</label>
              `
        }
        ${
          this.actions && this.actions.length > 0
            ? html`
                <paper-menu-button id="actions">
                  <paper-icon-button
                    style="color: var(--dorc-text-secondary);"
                    class="giant"
                    icon="lumo:edit"
                    noink
                    slot="dropdown-trigger"
                  ></paper-icon-button>

                  <paper-listbox slot="dropdown-content">
                    ${this.actions.map(
                      (action: any) => html`
                        <paper-item
                          @click="${this._actionClicked}"
                          @keydown=${this._actionClicked}
                          >${action.label}
                        </paper-item>
                      `
                    )}
                  </paper-listbox>
                </paper-menu-button>
              `
            : html``
        }
      </div>
      ${
        this.open && hasChildren
          ? html`
              <ul
                class="tree-children ${this.selectable ? 'selectable-children' : ''}"
              >
                ${this.data.children.map(
                  (child: TreeNode) =>
                    html` <li
                      class="${this.selectable ? 'selectable-child' : ''}"
                    >
                      <hegs-tree-node
                        id="hegs-tree-node"
                        .data="${child}"
                        .actions="${this.actions}"
                        .checked="${child.checked}"
                        .indeterminate="${child.indeterminate}"
                        .selectable="${this.selectable}"
                        .selectedPath="${this.selectedPath}"
                      ></hegs-tree-node>
                    </li>`
                )}
              </ul>
            `
          : html``
      }
    </div>
  `;
}
