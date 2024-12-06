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
    color: #747f8d;
  }
  span {
    color: #747f8d;
    font-family: var(--lumo-font-family);
    font-size: var(--lumo-font-size-m);
  }
  label {
    color: #747f8d;
    font-family: var(--lumo-font-family);
    font-size: var(--lumo-font-size-m);
  }
  input[type='checkbox']:hover {
    box-shadow: 0px 0px 10px #747f8d;
  }
  ul {
    margin: 0;
    padding-left: 20px;
  }
  li {
    list-style-type: none;
  }
  [hidden] {
    display: none;
  }
`;
