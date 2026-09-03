import { afterEach, describe, expect, it, vi } from 'vitest';

import type { HegsTreeNode } from './hegs-tree-node';
import { TreeNode } from './TreeNode';

vi.mock('@vaadin/vaadin-lumo-styles/icons.js', () => ({}));
import './hegs-tree-node';

function createTreeNode(id: number, name: string): TreeNode {
  return {
    id,
    name,
    icon: '',
    open: false,
    children: [],
    numOfChildren: 0,
    hasParent: false,
    parentId: undefined,
    checked: false,
    indeterminate: false
  };
}

async function createElement(data: TreeNode): Promise<HegsTreeNode> {
  const element = document.createElement('hegs-tree-node') as HegsTreeNode;
  element.data = data;
  document.body.appendChild(element);
  await element.updateComplete;
  return element;
}

describe('HegsTreeNode', () => {
  afterEach(() => {
    document.body.innerHTML = '';
  });

  it('uses matching input id and label for attributes', async () => {
    const element = await createElement(createTreeNode(123, 'ELK BigBoy'));
    const input = element.shadowRoot?.querySelector(
      'input[type="checkbox"]'
    ) as HTMLInputElement;
    const label = element.shadowRoot?.querySelector('label') as HTMLLabelElement;

    expect(input.id).toBe('deployment-123');
    expect(label.getAttribute('for')).toBe('deployment-123');
  });

  it('toggles checkbox when clicking the label text', async () => {
    const element = await createElement(
      createTreeNode(456, 'BigBoy Windows 11')
    );
    const input = element.shadowRoot?.querySelector(
      'input[type="checkbox"]'
    ) as HTMLInputElement;
    const label = element.shadowRoot?.querySelector('label') as HTMLLabelElement;

    expect(input.checked).toBe(false);
    label.click();
    await element.updateComplete;

    expect(input.checked).toBe(true);
    expect(element.checked).toBe(true);
  });
});
