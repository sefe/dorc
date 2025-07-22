import { LitElement, html, css } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import '@vaadin/icon';
import '../icons/dorc-icons.js';
import { mapIcon } from '../utils/icon-mapping.js';

/**
 * DOrc Icon Component
 * 
 * A wrapper component for consistent icon usage across the DOrc application.
 * Automatically maps old icon names to new DOrc icons for easy migration.
 * 
 * Usage:
 * <dorc-icon icon="server"></dorc-icon>
 * <dorc-icon icon="vaadin:server"></dorc-icon> // Auto-mapped to dorc:server
 * <dorc-icon icon="edit" color="primary"></dorc-icon>
 */
@customElement('dorc-icon')
export class DorcIcon extends LitElement {
  /**
   * The icon name. Can be:
   * - New DOrc icon: 'server', 'edit', 'delete'
   * - Full DOrc icon: 'dorc:server'
   * - Legacy icon: 'vaadin:server' (auto-mapped)
   */
  @property({ type: String })
  icon = '';

  /**
   * Icon color. Can be CSS color value or predefined theme colors:
   * - 'primary' (cornflowerblue)
   * - 'danger' (red)
   * - 'success' (green)
   * - 'warning' (orange)
   * - 'neutral' (grey)
   */
  @property({ type: String })
  color = '';

  /**
   * Icon size in pixels. Defaults to 24px.
   */
  @property({ type: Number })
  size = 24;

  /**
   * Accessibility label for screen readers
   */
  @property({ type: String })
  'aria-label' = '';

  static get styles() {
    return css`
      :host {
        display: inline-flex;
        align-items: center;
        justify-content: center;
      }
      
      vaadin-icon {
        --vaadin-icon-width: var(--dorc-icon-size, 24px);
        --vaadin-icon-height: var(--dorc-icon-size, 24px);
        color: var(--dorc-icon-color, currentColor);
      }
      
      /* Theme color presets */
      :host([color="primary"]) vaadin-icon {
        color: var(--dorc-color-primary, #3366CC);
      }
      
      :host([color="danger"]) vaadin-icon {
        color: var(--dorc-color-danger, #FF3131);
      }
      
      :host([color="success"]) vaadin-icon {
        color: var(--dorc-color-success, #2ECC40);
      }
      
      :host([color="warning"]) vaadin-icon {
        color: var(--dorc-color-warning, #FF851B);
      }
      
      :host([color="neutral"]) vaadin-icon {
        color: var(--dorc-color-neutral, #666666);
      }
    `;
  }

  render() {
    const iconName = this.getIconName();
    const iconColor = this.getIconColor();
    const iconSize = `${this.size}px`;

    return html`
      <vaadin-icon
        icon="${iconName}"
        style="
          --dorc-icon-size: ${iconSize};
          ${iconColor ? `--dorc-icon-color: ${iconColor};` : ''}
        "
        aria-label="${this['aria-label'] || this.icon}"
      ></vaadin-icon>
    `;
  }

  private getIconName(): string {
    if (!this.icon) {
      console.warn('DorcIcon: No icon specified');
      return 'dorc:info';
    }

    // If it's already a dorc icon, use it directly
    if (this.icon.startsWith('dorc:')) {
      return this.icon;
    }

    // If it's just the icon name without prefix, add dorc prefix
    if (!this.icon.includes(':')) {
      return `dorc:${this.icon}`;
    }

    // Otherwise, try to map legacy icon
    return mapIcon(this.icon);
  }

  private getIconColor(): string {
    // If color is a theme name, it will be handled by CSS
    if (['primary', 'danger', 'success', 'warning', 'neutral'].includes(this.color)) {
      return '';
    }
    
    // Return custom color value
    return this.color;
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'dorc-icon': DorcIcon;
  }
}