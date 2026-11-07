import { css } from 'lit';

export const addHegsTreeNodeStyles = css`
  :host(.selected) > .node-container > .node-row {
    background-color: var(
      --paper-tree-selected-background-color,
      rgba(200, 200, 200, 0.5)
    );
    color: var(--paper-tree-selected-color, inherit);
  }
  :host(.selected) > .node-container > .node-row > #actions {
    display: inline-block;
  }
  .node-container {
    white-space: nowrap;
    display: block;
  }
  .node-row {
    padding-left: 4px;
    padding-right: 4px;
  }
  .node-preicon.collapsed,
  .node-preicon.expanded {
    padding-left: 0px;
  }
  .node-preicon {
    padding-left: 18px;
  }
  .node-preicon:before {
    margin-right: 0px;
  }
  .node-preicon.collapsed:before {
    content: '\\23F5';
  }
  .node-preicon.expanded:before {
    content: '\\23F7';
  }
  .node-preicon,
  .node-name {
    cursor: pointer;
  }
  .node-icon {
    cursor: pointer;
    width: 24px;
    height: 24px;
  }
  #actions {
    float: right;
    padding: 0;
  }
  paper-icon-button {
    padding: 0px;
  }
  paper-icon-button.giant {
    width: 20px;
    height: 20px;
  }
  vaadin-icon {
    color: var(--dorc-text-secondary);
  }
  span {
    color: var(--dorc-text-secondary);
    font-family: var(--lumo-font-family);
    font-size: var(--lumo-font-size-m);
  }
  label {
    color: var(--dorc-text-secondary);
    font-family: var(--lumo-font-family);
    font-size: var(--lumo-font-size-m);
  }
  input[type='checkbox']:hover {
    box-shadow: 0px 0px 10px var(--dorc-text-secondary);
  }
  ul {
    margin: 0;
    padding-left: 20px;
  }
  li {
    list-style-type: none;
  }
  .node-container.selectable-container {
    white-space: normal;
  }
  .node-row.selectable-row {
    display: flex;
    align-items: flex-start;
    gap: 2px;
    padding: 1px 2px;
  }
  .node-row.selectable-row .node-preicon {
    flex-shrink: 0;
    width: 18px;
    height: 24px;
    padding-left: 0;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    margin-top: 2px;
    border-radius: var(--lumo-border-radius-s);
    color: var(--dorc-text-secondary-strong, var(--dorc-text-secondary));
  }
  .node-row.selectable-row .node-preicon.collapsed:hover,
  .node-row.selectable-row .node-preicon.expanded:hover {
    background: var(--dorc-bg-secondary);
    color: var(--dorc-text-primary);
  }
  .node-select-btn {
    font: inherit;
    color: var(--dorc-text-primary);
    background: transparent;
    border: 0;
    border-radius: var(--lumo-border-radius-s);
    padding: var(--lumo-space-xs) var(--lumo-space-s);
    flex: 1;
    min-width: 0;
    text-align: start;
    overflow-wrap: anywhere;
    cursor: pointer;
    display: flex;
    flex-direction: column;
    gap: 1px;
  }
  .node-select-btn:hover {
    background: var(--dorc-bg-secondary);
  }
  .node-select-btn[aria-pressed='true'] {
    background: var(--dorc-chip-bg);
    color: var(--dorc-chip-text);
  }
  .node-select-btn:focus-visible {
    outline: 2px solid var(--dorc-icon-interactive);
    outline-offset: -2px;
  }
  .node-title-row {
    display: flex;
    align-items: center;
    flex-wrap: wrap;
    gap: 4px;
    color: inherit;
    font-size: var(--lumo-font-size-s);
    font-weight: 500;
  }
  .node-title {
    color: inherit;
    font-size: inherit;
  }
  .node-badge {
    display: inline-block;
    padding: 0 5px;
    border-radius: 999px;
    font-size: 11px;
    font-weight: 600;
    background: var(--dorc-bg-secondary);
    color: var(--dorc-text-secondary-strong, var(--dorc-text-secondary));
  }
  .node-subtitle {
    display: block;
    font-size: var(--lumo-font-size-xs);
    color: var(--dorc-text-secondary-strong, var(--dorc-text-secondary));
  }
  ul.selectable-children {
    margin-inline-start: 9px;
    padding-left: 14px;
  }
  li.selectable-child {
    position: relative;
  }
  li.selectable-child::before {
    content: '';
    position: absolute;
    inset-inline-start: -14px;
    top: 0;
    bottom: 0;
    border-inline-start: 1.5px solid var(--dorc-text-secondary);
    opacity: 0.55;
  }
  li.selectable-child:last-child::before {
    bottom: auto;
    height: 16px;
  }
  li.selectable-child::after {
    content: '';
    position: absolute;
    inset-inline-start: -14px;
    top: 16px;
    width: 12px;
    border-top: 1.5px solid var(--dorc-text-secondary);
    opacity: 0.55;
  }
  [hidden] {
    display: none;
  }
`;
