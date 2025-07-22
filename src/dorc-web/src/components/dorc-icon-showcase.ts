import { LitElement, html, css } from 'lit';
import { customElement } from 'lit/decorators.js';
import './dorc-icon.js';

/**
 * DOrc Icon Showcase
 * Demonstrates the new consistent icon system
 */
@customElement('dorc-icon-showcase')
export class DorcIconShowcase extends LitElement {
  static get styles() {
    return css`
      :host {
        display: block;
        padding: 20px;
        font-family: var(--lumo-font-family);
      }
      
      h1, h2 {
        color: var(--dorc-color-primary, #3366CC);
      }
      
      .icon-grid {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(150px, 1fr));
        gap: 16px;
        margin: 20px 0;
      }
      
      .icon-item {
        display: flex;
        flex-direction: column;
        align-items: center;
        padding: 12px;
        border: 1px solid #ddd;
        border-radius: 8px;
        background: #f9f9f9;
      }
      
      .icon-item dorc-icon {
        margin-bottom: 8px;
      }
      
      .icon-name {
        font-size: 12px;
        text-align: center;
        color: #666;
      }
      
      .color-demo {
        display: flex;
        gap: 8px;
        margin: 8px 0;
        align-items: center;
      }
      
      .size-demo {
        display: flex;
        gap: 16px;
        margin: 16px 0;
        align-items: center;
      }
    `;
  }

  render() {
    return html`
      <h1>DOrc Icon System Showcase</h1>
      
      <h2>Color Themes</h2>
      <div class="color-demo">
        <dorc-icon icon="edit" color="primary"></dorc-icon>
        <span>Primary</span>
        <dorc-icon icon="delete" color="danger"></dorc-icon>
        <span>Danger</span>
        <dorc-icon icon="save" color="success"></dorc-icon>
        <span>Success</span>
        <dorc-icon icon="settings" color="warning"></dorc-icon>
        <span>Warning</span>
        <dorc-icon icon="info" color="neutral"></dorc-icon>
        <span>Neutral</span>
      </div>
      
      <h2>Size Variations</h2>
      <div class="size-demo">
        <dorc-icon icon="server" size="16" color="primary"></dorc-icon>
        <dorc-icon icon="server" size="24" color="primary"></dorc-icon>
        <dorc-icon icon="server" size="32" color="primary"></dorc-icon>
        <dorc-icon icon="server" size="48" color="primary"></dorc-icon>
      </div>
      
      <h2>Action Icons</h2>
      <div class="icon-grid">
        ${this.renderIconGroup([
          'edit', 'delete', 'save', 'refresh', 'copy', 'clear', 'close'
        ])}
      </div>
      
      <h2>Media Controls</h2>
      <div class="icon-grid">
        ${this.renderIconGroup([
          'play', 'stop', 'repeat'
        ])}
      </div>
      
      <h2>Security</h2>
      <div class="icon-grid">
        ${this.renderIconGroup([
          'lock', 'unlock', 'key', 'safe'
        ])}
      </div>
      
      <h2>Infrastructure</h2>
      <div class="icon-grid">
        ${this.renderIconGroup([
          'server', 'database', 'environment', 'desktop', 'container'
        ])}
      </div>
      
      <h2>User Management</h2>
      <div class="icon-grid">
        ${this.renderIconGroup([
          'user', 'users', 'group', 'group-add'
        ])}
      </div>
      
      <h2>Custom DOrc Icons</h2>
      <div class="icon-grid">
        ${this.renderIconGroup([
          'powershell', 'variables', 'admin'
        ])}
      </div>
    `;
  }
  
  private renderIconGroup(icons: string[]) {
    return icons.map(icon => html`
      <div class="icon-item">
        <dorc-icon icon="${icon}" color="primary"></dorc-icon>
        <div class="icon-name">${icon}</div>
      </div>
    `);
  }
}