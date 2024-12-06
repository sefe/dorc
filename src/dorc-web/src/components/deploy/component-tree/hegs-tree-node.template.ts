import { html } from 'lit/html.js';
import { classMap } from 'lit/directives/class-map.js';

import { HegsTreeNode } from './hegs-tree-node';

import { TreeNode } from './TreeNode';

export function addHegsTreeNodeTemplate(this: HegsTreeNode) {
  return html`
    <div
      id="root"
      class="node-container ${classMap(this._computeSelectedClass())}"
    >
      <div class="node-row">
        <span
          class="node-preicon ${classMap(this._computePreIconClass())}"
          @click="${this.toggleChildren}"
          @keydown="${this.toggleChildren}"
        ></span>

        <input
          .checked="${this.checked}"
          .indeterminate="${this.indeterminate}"
          type="checkbox"
          id="deployment"
          data-componentId="${this.data?.id}"
          @click="${this.selectComponentForDeployment}"
        />

        <label for="checkbox">${this.data?.name}</label>

        ${this.actions && this.actions.length > 0
          ? html`
              <paper-menu-button id="actions">
                <paper-icon-button
                  style="color: #747f8d;"
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
          : html``}
      </div>
      ${this.open
        ? html`
            <ul>
              ${this.data.children.map(
                (child: TreeNode) =>
                  html` <li>
                    <hegs-tree-node
                      id="hegs-tree-node"
                      .data="${child}"
                      .actions="${this.actions}"
                      .checked="${child.checked}"
                      .indeterminate="${child.indeterminate}"
                    ></hegs-tree-node>
                  </li>`
              )}
            </ul>
          `
        : html``}
    </div>
  `;
}
