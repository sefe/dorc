import { css, LitElement } from 'lit';
import './hegs-tree-node';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { TreeNode } from './TreeNode';
import { HegsTreeNode } from './hegs-tree-node';
import { TreeAction } from './TreeAction';

@customElement('hegs-tree')
export class HegsTree extends LitElement {
  private selected: TreeNode | undefined = undefined;

  @property({ type: Array }) private data: TreeNode[] | undefined;

  @property({ type: Array }) private actions: TreeAction[] = [];

  @property({ type: Boolean }) componentsLoading = false;

  constructor() {
    super();
    this.addEventListener('select', this._selectNode as EventListener);
  }

  static get styles() {
    return css`
      .small-loader {
        border: 2px solid #f3f3f3; /* Light grey */
        border-top: 2px solid #3498db; /* Blue */
        border-radius: 50%;
        width: 12px;
        height: 12px;
        animation: spin 2s linear infinite;
      }

      @keyframes spin {
        0% {
          transform: rotate(0deg);
        }
        100% {
          transform: rotate(360deg);
        }
      }
    `;
  }

  render() {
    return html`
      ${this.componentsLoading
        ? html` <div class="small-loader"></div> `
        : html`
            <div style="width: 700px">
              <div style="padding: 3px"></div>
              ${this.data?.map(
                (child: TreeNode) => html`
                  <hegs-tree-node
                    id="hegs-tree-node"
                    .data="${child}"
                    .actions="${this.actions}"
                  ></hegs-tree-node>
                `
              )}
              <div style="padding: 3px"></div>
            </div>
          `}
    `;
  }

  public getCheckedComponents(): HegsTreeNode[] {
    const output: HegsTreeNode[] = [];
    const treeRoot = this.shadowRoot?.querySelectorAll(
      '[id=hegs-tree-node]'
    ) as NodeListOf<HegsTreeNode>;
    treeRoot?.forEach(rootElem => {
      if (rootElem.checked) {
        output.push(rootElem);
        console.log(`Adding component to deploy ${rootElem.data.name}`);
      } else if (rootElem.indeterminate) {
        const descendants = rootElem.getCheckedComponents(); // this will be a flat list
        descendants.forEach(descendant => {
          if (descendant.checked) {
            output.push(descendant);
            console.log(`Adding component to deploy ${descendant.data.name}`);
          }
        });
      }
    });
    return output;
  }

  /**
   * Called when the `select` event is fired from an internal node.
   *
   * @param {object} e An event object.
   */
  _selectNode(e: CustomEvent) {
    if (this.selected) {
      // this.toggleClass("selected", false, this.selected);
    }

    // Only selects `<paper-tree-node>`.
    if (e.detail && e.detail.tagName === 'PAPER-TREE-NODE') {
      this.selected = e.detail;
    } else {
      this.selected = undefined;
    }
  }

  ResetCheckedStates() {
    const elems = this.shadowRoot?.querySelectorAll('[id=hegs-tree-node]');
    elems?.forEach(elem => {
      const node = elem as HegsTreeNode;
      node.ResetCheckedStates();
    });
  }
}
