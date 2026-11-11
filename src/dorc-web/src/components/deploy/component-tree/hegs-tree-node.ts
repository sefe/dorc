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

  @property({ type: Boolean }) selectable = false;

  @property({ type: String }) selectedPath = '';

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

  protected updated(changed: PropertyValues) {
    super.updated(changed);
    if (changed.has('data')) {
      this.open = this.data.open;
      this.checked = Boolean(this.data.checked && !this.isCheckboxDisabled());
      this.indeterminate = Boolean(
        this.data.indeterminate && !this.isCheckboxDisabled()
      );
    }
  }

  public hasDisabledDescendants(
    node: TreeNode | undefined = this.data
  ): boolean {
    if (!node?.children || node.children.length === 0) {
      return false;
    }
    return node.children.some(
      child => Boolean(child.disabled) || this.hasDisabledDescendants(child)
    );
  }

  public hasEnabledDescendants(
    node: TreeNode | undefined = this.data
  ): boolean {
    if (!node?.children || node.children.length === 0) {
      return false;
    }
    return node.children.some(child => {
      if (child.disabled) {
        return false;
      }
      if (child.children && child.children.length > 0) {
        return this.hasEnabledDescendants(child);
      }
      return true;
    });
  }

  public isCheckboxDisabled(): boolean {
    if (this.data?.disabled) {
      return true;
    }
    if (this.data?.children && this.data.children.length > 0) {
      return !this.hasEnabledDescendants(this.data);
    }
    return false;
  }

  private areAllEnabledDescendantsChecked(
    node: TreeNode | undefined = this.data
  ): boolean {
    if (!node?.children || node.children.length === 0) {
      return Boolean(node?.checked && !node?.disabled);
    }
    let sawEnabledLeaf = false;
    const check = (current: TreeNode): boolean => {
      if (current.disabled) {
        return true;
      }
      if (current.children && current.children.length > 0) {
        return current.children.every(child => check(child));
      }
      sawEnabledLeaf = true;
      return Boolean(current.checked);
    };
    return check(node) && sawEnabledLeaf;
  }

  ResetCheckedStates() {
    this.updateNodeChildrenDataChecked(this.data, false);
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
    if (elems && elems.length > 0) {
      elems.forEach(elem => {
        if (elem.data?.disabled) {
          return;
        }
        if (elem.checked && !elem.hasDisabledDescendants()) {
          output.push(elem);
        } else if (elem.checked || elem.indeterminate) {
          const value = elem.getCheckedComponents();
          value.forEach(checked => {
            if (checked.checked && !checked.data?.disabled) {
              output.push(checked);
            }
          });
        }
      });
      return output;
    }

    this.collectCheckedFromData(this.data?.children ?? [], output);
    return output;
  }

  private collectCheckedFromData(nodes: TreeNode[], output: HegsTreeNode[]) {
    for (const child of nodes) {
      if (child.disabled) {
        continue;
      }
      if (child.checked && !this.hasDisabledDescendants(child)) {
        const node = new HegsTreeNode();
        node.data = child;
        node.checked = true;
        output.push(node);
      } else if (child.checked || child.indeterminate) {
        this.collectCheckedFromData(child.children ?? [], output);
      }
    }
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

  selectComponentForDeployment(e?: Event) {
    if (this.isCheckboxDisabled()) {
      e?.preventDefault();
      this.setIndeterminateState(false);
      this.setCheckedState(false);
      return;
    }

    const label = this.deploymentInput?.nextElementSibling as HTMLLabelElement;
    const hasChildren = Boolean(
      this.data?.children && this.data.children.length > 0
    );
    const targetChecked = hasChildren
      ? !this.areAllEnabledDescendantsChecked(this.data)
      : (this.deploymentInput?.checked ?? !this.checked);

    if (this.deploymentInput) {
      this.deploymentInput.checked = targetChecked;
    }

    const myEvent = new CustomEvent('tree-node-changed', {
      detail: {
        message: 'tree-node-changed fired',
        changed: this.data?.name ?? label?.textContent,
        changedId: this.data?.id,
        to: targetChecked
      },
      bubbles: true,
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
  toggleChildren(event?: Event) {
    event?.stopPropagation();
    const hasChildren = Boolean(
      this.data?.children && this.data.children.length > 0
    );
    if (!hasChildren) {
      this.open = false;
      return;
    }
    this.open = !this.open;
    if (this.data) {
      this.data.open = this.open;
    }
    this.dispatchEvent(
      new CustomEvent('hegs-tree-node-toggled', {
        detail: {
          node: this.data,
          path: this.data?.path ?? '',
          open: this.open
        },
        bubbles: true,
        composed: true
      })
    );
  }

  selectNode() {
    this.dispatchEvent(
      new CustomEvent('hegs-tree-node-selected', {
        detail: {
          node: this.data,
          path: this.data?.path ?? ''
        },
        bubbles: true,
        composed: true
      })
    );
  }

  public getChecked(): boolean {
    if (this.isCheckboxDisabled()) {
      return false;
    }
    return this.deploymentInput?.checked ?? this._checked;
  }

  public getIndeterminate(): boolean {
    if (this.isCheckboxDisabled()) {
      return false;
    }
    return this.deploymentInput?.indeterminate ?? this._indeterminate;
  }

  public setCheckedState(value: boolean) {
    const nextValue = this.isCheckboxDisabled() ? false : value;
    if (this.deploymentInput) {
      this.deploymentInput.checked = nextValue;
    }
    this._checked = nextValue;
    if (this.data) {
      this.data.checked = nextValue;
    }
    if (this.isCheckboxDisabled()) {
      this.setIndeterminateState(false);
    }
  }

  public setIndeterminateState(value: boolean) {
    const nextValue = this.isCheckboxDisabled() ? false : value;
    if (this.deploymentInput) {
      this.deploymentInput.indeterminate = nextValue;
    }
    this._indeterminate = nextValue;
    if (this.data) {
      this.data.indeterminate = nextValue;
    }
  }

  public syncStateFromData() {
    if (this.isCheckboxDisabled()) {
      this.setIndeterminateState(false);
      this.setCheckedState(false);
      return;
    }
    if (this.data?.children && this.data.children.length > 0) {
      const allChildrenChecked = this.data.children.every(
        child => !child.disabled && child.checked && !child.indeterminate
      );
      const someChildrenSelected = this.data.children.some(
        child => !child.disabled && (child.checked || child.indeterminate)
      );
      this.setIndeterminateState(!allChildrenChecked && someChildrenSelected);
      this.setCheckedState(allChildrenChecked);
    } else {
      this.setIndeterminateState(Boolean(this.data?.indeterminate));
      this.setCheckedState(Boolean(this.data?.checked));
    }
  }

  private updateParent(event: CustomEvent) {
    const isSelf =
      event.detail?.changedId !== undefined &&
      this.data?.id !== undefined &&
      this.data.id !== -1
        ? event.detail.changedId === this.data.id
        : event.detail?.changed === this.data?.name;

    if (isSelf) {
      this.updateChildren(Boolean(event.detail?.to));
      if (this.data?.children && this.data.children.length > 0) {
        this.syncStateFromData();
      } else {
        this.setIndeterminateState(false);
        this.setCheckedState(Boolean(event.detail?.to));
      }
      return;
    }

    const elems = this.shadowRoot?.querySelectorAll('[id=hegs-tree-node]');
    if (elems !== undefined && elems.length > 0) {
      let allChildrenChecked = true;
      let someChildrenSelected = false;
      elems.forEach(elem => {
        const node = elem as HegsTreeNode;
        const checked = !node.isCheckboxDisabled() && node.getChecked();
        const indeterminate =
          !node.isCheckboxDisabled() && node.getIndeterminate();

        allChildrenChecked = allChildrenChecked && checked && !indeterminate;
        someChildrenSelected = someChildrenSelected || checked || indeterminate;
      });
      this.setIndeterminateState(!allChildrenChecked && someChildrenSelected);
      this.setCheckedState(allChildrenChecked);
    } else {
      this.syncStateFromData();
    }
  }

  private updateChildren(newState: boolean) {
    const targetState = this.isCheckboxDisabled() ? false : newState;
    this.data.children.forEach(child =>
      this.updateNodeChildrenDataChecked(child, targetState)
    );
    const elems = this.shadowRoot?.querySelectorAll('[id=hegs-tree-node]');
    if (elems !== undefined && elems?.length > 0) {
      elems?.forEach(elem => {
        const node = elem as HegsTreeNode;
        if (node.isCheckboxDisabled()) {
          node.updateChildren(false);
          node.setIndeterminateState(false);
          node.setCheckedState(false);
        } else {
          node.updateChildren(targetState);
          node.syncStateFromData();
        }
      });
    }
  }

  private updateNodeChildrenDataChecked(node: TreeNode, newState: boolean) {
    if (node.disabled) {
      node.checked = false;
      node.indeterminate = false;
      node.children?.forEach(child =>
        this.updateNodeChildrenDataChecked(child, false)
      );
      return;
    }

    if (node.children && node.children.length > 0) {
      node.children.forEach(child =>
        this.updateNodeChildrenDataChecked(child, newState)
      );
      const allChildrenChecked = node.children.every(
        child => !child.disabled && child.checked && !child.indeterminate
      );
      const someChildrenSelected = node.children.some(
        child => !child.disabled && (child.checked || child.indeterminate)
      );
      node.checked = allChildrenChecked;
      node.indeterminate = !allChildrenChecked && someChildrenSelected;
    } else {
      node.checked = newState;
      node.indeterminate = false;
    }
  }
}
