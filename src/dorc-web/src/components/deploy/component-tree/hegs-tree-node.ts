import '@vaadin/vaadin-lumo-styles/icons.js';
import { LitElement, PropertyValues } from 'lit';
import { customElement, property, query, state } from 'lit/decorators.js';

import { addHegsTreeNodeTemplate } from './hegs-tree-node.template';
import { addHegsTreeNodeStyles } from './hegs-tree-node.styles';

import { TreeNode } from './TreeNode';
import { TreeAction } from './TreeAction';

@customElement('hegs-tree-node')
export class HegsTreeNode extends LitElement {
  @property({ type: Array }) actions: TreeAction[] = [];

  @property({ type: Object }) data: TreeNode;

  @property({ type: Boolean }) open = false;

  @query('#deployment')
  private deploymentInput: HTMLInputElement | undefined;

  @state()
  protected _checked: boolean = false;

  @property({ type: Boolean })
  get checked(): boolean {
    return this._checked;
  }

  set checked(value: boolean) {
    const oldValue = this._checked;
    this._checked = value;
    this.requestUpdate('checked', oldValue);
  }

  private _indeterminate = false;

  @property({ type: Boolean })
  get indeterminate(): boolean {
    return this._indeterminate;
  }

  set indeterminate(value: boolean) {
    const oldValue = this._indeterminate;
    this._indeterminate = value;
    this.requestUpdate('indeterminate', oldValue);
  }

  constructor() {
    super();
    this.data = {
      id: -1,
      icon: '',
      name: '',
      open: false,
      children: [],
      numOfChildren: 0,
      hasParent: true,
      parentId: 0,
      checked: false,
      indeterminate: false
    };
  }

  static styles = addHegsTreeNodeStyles;

  render = addHegsTreeNodeTemplate;

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    this.addEventListener(
      'tree-node-changed',
      this.updateParent as EventListener
    );

    this.open = this.data.open;
  }

  ResetCheckedStates() {
    const elems = this.shadowRoot?.querySelectorAll('[id=hegs-tree-node]');
    elems?.forEach(elem => {
      const node = elem as HegsTreeNode;
      node.ResetCheckedStates();
    });
    if (this.indeterminate) {
      console.log(`Setting indeterminate to false for ${this.data.name}`);
      this.setIndeterminateState(false);
    }
    if (this.checked) {
      console.log(`Setting checked to false for ${this.data.name}`);
      this.setCheckedState(false);
    }
  }

  public getCheckedComponents(): HegsTreeNode[] {
    const output: HegsTreeNode[] = [];
    const elems = this.shadowRoot?.querySelectorAll(
      '[id=hegs-tree-node]'
    ) as NodeListOf<HegsTreeNode>;
    elems?.forEach(elem => {
      if (elem.checked) {
        output.push(elem);
      } else if (elem.indeterminate) {
        const value = elem.getCheckedComponents();
        value.forEach(checked => {
          if (checked.checked) {
            output.push(checked);
          }
        });
      }
    });
    return output;
  }

  /**
   * Returns the necessary classes.
   *
   * @param {object} change An object containing the property that changed and its value.
   * @return {string} The class name indicating whether the node is open or closed
   */
  _computePreIconClass() {
    const open: boolean = (this.data && this.open) ?? false;
    const children: boolean =
      (this.data && this.data.children && this.data.children.length > 0) ??
      false;
    const expanded: boolean = open && children;

    return { expanded, collapsed: children };
  }

  /**
   * Compute the necessary node icon.
   *
   * @param {string=folder} an icon name.
   * @return {string} the computed icon name.
   */
  _computeIcon(icon: string | undefined) {
    return icon || 'folder';
  }

  _actionClicked(event: any) {
    this.dispatchEvent(
      new CustomEvent(event.model.item.event, {
        bubbles: true,
        composed: true
      })
    );
  }

  selectComponentForDeployment() {
    const label = this.deploymentInput?.nextElementSibling as HTMLLabelElement;

    const myEvent = new CustomEvent('tree-node-changed', {
      detail: {
        message: 'tree-node-changed fired',
        changed: label.textContent,
        to: this.deploymentInput?.checked
      },
      bubbles: false,
      composed: true
    });
    this.dispatchEvent(myEvent);
  }

  /**
   * Highlights node as the selected node.
   */
  select() {
    if (!this.className.includes('selected')) {
      this.dispatchEvent(
        new CustomEvent('select', { bubbles: true, composed: true })
      );
    }
  }

  _computeSelectedClass() {
    let selected = false;

    if (!this.className.includes('selected')) {
      selected = true;
    }
    return { selected };
  }

  /**
   * Display/Hide the children nodes.
   */
  toggleChildren() {
    this.open =
      (!this.open && this.data?.children && this.data.children.length > 0) ??
      false;
  }

  public getChecked(): boolean {
    return this.deploymentInput?.checked ?? false;
  }

  public getIndeterminate(): boolean {
    return this.deploymentInput?.indeterminate ?? false;
  }

  public setCheckedState(value: boolean) {
    if (this.deploymentInput) {
      this.deploymentInput.checked = value;
      this._checked = value;
      this.data.checked = value;
    }
  }

  public setIndeterminateState(value: boolean) {
    if (this.deploymentInput) {
      this.deploymentInput.indeterminate = value;
      this._indeterminate = value;
      this.data.indeterminate = value;
    }
  }

  private updateParent(event: CustomEvent) {
    let allChildrenChecked = false;
    let someChildrenChecked = false;
    let noChildrenChecked = false;
    let allChildrenIndeterminate = false;
    let someChildrenIndeterminate = false;
    let noChildrenIndeterminate = false;
    const elems = this.shadowRoot?.querySelectorAll('[id=hegs-tree-node]');
    if (elems !== undefined && elems?.length > 0) {
      allChildrenChecked = true;
      allChildrenIndeterminate = true;
      elems?.forEach(elem => {
        const node = elem as HegsTreeNode;
        const checked = node.getChecked();
        const indeterminate = node.getIndeterminate();

        allChildrenChecked = allChildrenChecked && checked;
        someChildrenChecked = someChildrenChecked || checked;

        allChildrenIndeterminate = allChildrenIndeterminate && indeterminate;
        someChildrenIndeterminate = someChildrenIndeterminate || indeterminate;
      });
      noChildrenChecked = !allChildrenChecked;
      noChildrenIndeterminate = !allChildrenIndeterminate;
    }

    if (this.deploymentInput) {
      this.data.checked = event.detail.to;
      if (event.detail.changed !== this.data.name) {
        // inside a parent
        // Need to all of these properties need to be set manually as the events bubble up
        // and if we use the bound properties then we get downward propagation also
        if (allChildrenChecked) {
          this.deploymentInput.indeterminate = false;
          this.data.indeterminate = false;
          this._indeterminate = false;

          this.deploymentInput.checked = true;
          this._checked = true;
        } else if (someChildrenChecked || someChildrenIndeterminate) {
          this.deploymentInput.indeterminate = true;
          this.data.indeterminate = true;
          this._indeterminate = true;

          this.deploymentInput.checked = false;
          this._checked = false;
        } else if (noChildrenChecked || noChildrenIndeterminate) {
          this.deploymentInput.indeterminate = false;
          this.data.indeterminate = false;
          this._indeterminate = false;

          this.deploymentInput.checked = false;
          this._checked = false;
        }
      } else {
        this.updateChildren(event.detail.to);
        this.setIndeterminateState(false);
        this.setCheckedState(event.detail.to);
      }
    }
  }

  private updateChildren(newState: boolean) {
    this.data.children.forEach(child =>
      this.updateNodeChildrenDataChecked(child, newState)
    );
    const elems = this.shadowRoot?.querySelectorAll('[id=hegs-tree-node]');
    if (elems !== undefined && elems?.length > 0) {
      elems?.forEach(elem => {
        const node = elem as HegsTreeNode;
        node.updateChildren(newState);
        node.setIndeterminateState(false);
        node.setCheckedState(newState);
      });
    }
  }

  private updateNodeChildrenDataChecked(node: TreeNode, newState: boolean) {
    node.checked = newState;
    node.children.forEach(child =>
      this.updateNodeChildrenDataChecked(child, newState)
    );
  }
}
