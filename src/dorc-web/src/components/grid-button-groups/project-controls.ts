import { css, LitElement } from 'lit';
import '@vaadin/button';
import '@vaadin/icon';
import '@vaadin/icons';
import '@vaadin/vaadin-lumo-styles/icons.js';
import '../../icons/iron-icons.js';
import { Router } from '@vaadin/router';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { ProjectApiModel } from '../../apis/dorc-api';

interface ActionMenuItem {
  text: string;
  eventName: string;
  icon: string;
  detail: () => Record<string, unknown>;
  isDelete?: boolean;
}

@customElement('project-controls')
export class ProjectControls extends LitElement {
  @property({ type: Object }) project: ProjectApiModel | undefined;
  @property({ type: Boolean }) deleteHidden: boolean = true;
  @state() private _open = false;
  @state() private _dropdownTop = 0;
  @state() private _dropdownRight = 0;

  private _outsideClickHandler = (e: MouseEvent) => {
    const dropdown = this._getDropdownEl();
    if (!this.contains(e.target as Node) && !dropdown?.contains(e.target as Node)) {
      this._close();
    }
  };

  private _scrollHandler = () => {
    this._close();
  };

  // Keyboard interaction handler for the open menu. Registered on the overlay
  // when the menu opens, removed when it closes (or in disconnectedCallback as
  // a safety net). Implements the WAI-ARIA APG menu keyboard pattern:
  // ArrowDown/Up with wrap, Home/End, Enter/Space invoke, ESC + Tab/Shift+Tab
  // close-and-restore-focus.
  private _onMenuKeyDown = (e: KeyboardEvent) => {
    const overlay = this._getDropdownEl();
    if (!overlay) return;

    const items = Array.from(
      overlay.querySelectorAll<HTMLElement>('[role="menuitem"]')
    );
    if (items.length === 0) return;

    const trigger = this.shadowRoot?.getElementById(
      this._triggerId
    ) as HTMLElement | null;

    const focusItem = (idx: number) => {
      const target = items[(idx + items.length) % items.length];
      target?.focus();
    };

    const currentIdx = items.indexOf(document.activeElement as HTMLElement);

    switch (e.key) {
      case 'ArrowDown':
        e.preventDefault();
        focusItem(currentIdx < 0 ? 0 : currentIdx + 1);
        break;
      case 'ArrowUp':
        e.preventDefault();
        focusItem(currentIdx < 0 ? items.length - 1 : currentIdx - 1);
        break;
      case 'Home':
        e.preventDefault();
        focusItem(0);
        break;
      case 'End':
        e.preventDefault();
        focusItem(items.length - 1);
        break;
      case 'Enter':
      case ' ': {
        if (currentIdx < 0) return;
        const action = this.menuActions[currentIdx];
        if (!action) return;
        e.preventDefault();
        // R9b: restore focus to the trigger before invoking the action so
        // that any focus claimed by the action's destination (e.g. a dialog
        // opened by the dispatched event, or a route navigation) supersedes
        // it. If the action does not relocate focus, the trigger retains it.
        trigger?.focus();
        this._selectAction(action);
        break;
      }
      case 'Escape':
        e.preventDefault();
        this._close();
        trigger?.focus();
        break;
      case 'Tab': {
        e.preventDefault();
        this._close();
        const siblings = this._findTabbableSiblings(trigger);
        const dest = e.shiftKey ? siblings.prev : siblings.next;
        (dest ?? trigger)?.focus();
        break;
      }
      default:
        break;
    }
  };

  // Find the document tabbable elements that immediately precede and follow
  // the trigger in tab order, walking through shadow roots to locate the
  // trigger that lives inside this component's shadow DOM.
  private _findTabbableSiblings(
    trigger: HTMLElement | null
  ): { prev: HTMLElement | null; next: HTMLElement | null } {
    if (!trigger) return { prev: null, next: null };
    const all = this._collectTabbables(document);
    const idx = all.indexOf(trigger);
    if (idx < 0) return { prev: null, next: null };
    return {
      prev: idx > 0 ? all[idx - 1] : null,
      next: idx < all.length - 1 ? all[idx + 1] : null,
    };
  }

  // Collect tabbable elements across the document, descending into open
  // shadow roots. Tabbability is approximated by `tabIndex >= 0` plus a
  // visibility check via `offsetParent`. This is sufficient for the typical
  // dorc-web layouts where the trigger lives in a Lit element's shadow root
  // and the next focusable element is another similar trigger or a
  // top-level page control.
  private _collectTabbables(root: Document | ShadowRoot): HTMLElement[] {
    const result: HTMLElement[] = [];
    const walk = (n: ParentNode) => {
      n.querySelectorAll<HTMLElement>('*').forEach((el) => {
        if (
          el instanceof HTMLElement &&
          el.tabIndex >= 0 &&
          el.offsetParent !== null
        ) {
          result.push(el);
        }
        const sr = (el as HTMLElement & { shadowRoot: ShadowRoot | null })
          .shadowRoot;
        if (sr) walk(sr);
      });
    };
    walk(root);
    return result;
  }

  private _getDropdownEl(): HTMLElement | null {
    return document.getElementById(this._dropdownId);
  }

  private _uid = crypto.getRandomValues(new Uint32Array(1))[0].toString(36);

  private get _triggerId(): string {
    return `project-trigger-${this._uid}`;
  }

  private get _dropdownId(): string {
    return `project-dropdown-${this._uid}`;
  }

  static get styles() {
    return css`
      :host {
        display: inline-block;
      }

      vaadin-button {
        --lumo-button-size: 28px;
        --lumo-icon-size-m: 16px;
      }
    `;
  }

  private get menuActions(): ActionMenuItem[] {
    const actions: ActionMenuItem[] = [
      {
        text: 'Edit Metadata',
        eventName: 'open-project-metadata',
        icon: 'lumo:edit',
        detail: () => ({ Project: this.project })
      },
      {
        text: 'Project Access',
        eventName: 'open-access-control',
        icon: 'vaadin:lock',
        detail: () => ({ Name: this.project?.ProjectName })
      },
      {
        text: 'Environments',
        eventName: 'open-project-envs',
        icon: 'vaadin:records',
        detail: () => ({ Project: this.project })
      },
      {
        text: 'Components',
        eventName: 'open-project-components',
        icon: 'vaadin:package',
        detail: () => ({ Project: this.project })
      },
      {
        text: 'Reference Data',
        eventName: 'open-project-ref-data',
        icon: 'vaadin:curly-brackets',
        detail: () => ({ Project: this.project })
      },
      {
        text: 'Audit',
        eventName: 'open-project-audit-data',
        icon: 'vaadin:calendar-user',
        detail: () => ({ Project: this.project })
      }
    ];

    if (!this.deleteHidden) {
      actions.push({
        text: 'Delete Project',
        eventName: 'delete-project',
        icon: 'icons:delete',
        detail: () => ({ Project: this.project }),
        isDelete: true
      });
    }

    return actions;
  }

  connectedCallback() {
    super.connectedCallback();
    document.addEventListener('click', this._outsideClickHandler);
    document.addEventListener('scroll', this._scrollHandler, true);
  }

  disconnectedCallback() {
    super.disconnectedCallback();
    document.removeEventListener('click', this._outsideClickHandler);
    document.removeEventListener('scroll', this._scrollHandler, true);
    this._removeDropdown();
  }

  render() {
    return html`
      <vaadin-button
        id="${this._triggerId}"
        theme="icon small"
        aria-label="Project actions"
        title="Project actions"
        aria-haspopup="menu"
        aria-expanded="${this._open ? 'true' : 'false'}"
        aria-controls="${this._dropdownId}"
        @click="${this._toggle}"
      >
        <vaadin-icon
          icon="vaadin:ellipsis-dots-h"
          aria-hidden="true"
        ></vaadin-icon>
      </vaadin-button>
    `;
  }

  updated(changed: Map<string, unknown>) {
    super.updated(changed);
    if (changed.has('_open')) {
      if (this._open) {
        this._showDropdown();
      } else {
        this._removeDropdown();
      }
    }
  }

  private _toggle(e: Event) {
    e.stopPropagation();
    if (this._open) {
      this._close();
    } else {
      const btn = this.shadowRoot?.querySelector('vaadin-button');
      if (btn) {
        const rect = btn.getBoundingClientRect();
        this._dropdownTop = rect.bottom + 2;
        this._dropdownRight = window.innerWidth - rect.right;
      }
      this._open = true;
    }
  }

  private _close() {
    this._open = false;
  }

  private _showDropdown() {
    this._removeDropdown();

    const overlay = document.createElement('div');
    overlay.id = this._dropdownId;
    overlay.setAttribute('role', 'menu');
    overlay.setAttribute('aria-orientation', 'vertical');
    overlay.setAttribute('aria-labelledby', this._triggerId);
    Object.assign(overlay.style, {
      position: 'fixed',
      top: `${this._dropdownTop}px`,
      right: `${this._dropdownRight}px`,
      zIndex: '10000',
      minWidth: '180px',
      background: 'var(--lumo-base-color)',
      border: '1px solid var(--lumo-contrast-10pct)',
      borderRadius: 'var(--lumo-border-radius-m)',
      boxShadow: 'var(--lumo-box-shadow-m)',
      padding: '4px 0',
      fontFamily: 'var(--lumo-font-family)',
    });

    // Focus-visible indicator (WCAG 2.4.7) — scoped to this overlay's id so
    // each instance owns its style block. Distinct from the hover style
    // (which uses background-color only) to satisfy R7.
    const styleEl = document.createElement('style');
    styleEl.textContent = `
      #${this._dropdownId} [role="menuitem"]:focus-visible {
        outline: 2px solid var(--lumo-primary-color);
        outline-offset: -2px;
      }
    `;
    overlay.appendChild(styleEl);

    for (const action of this.menuActions) {
      const item = document.createElement('div');
      item.setAttribute('role', 'menuitem');
      // Programmatically focusable but not in the document tab order; the
      // open menu manages focus via the keydown handler (S-003b R4–R6, R8–R12).
      item.setAttribute('tabindex', '-1');
      Object.assign(item.style, {
        display: 'flex',
        alignItems: 'center',
        gap: '10px',
        padding: '8px 16px',
        cursor: 'pointer',
        color: 'var(--lumo-body-text-color)',
        fontSize: 'var(--lumo-font-size-s)',
      });

      if (action.isDelete) {
        item.style.borderTop = '1px solid var(--lumo-contrast-10pct)';
        item.style.marginTop = '4px';
        item.style.paddingTop = '12px';
      }

      item.addEventListener('mouseenter', () => {
        item.style.backgroundColor = 'var(--lumo-primary-color-10pct)';
      });
      item.addEventListener('mouseleave', () => {
        item.style.backgroundColor = '';
      });
      item.addEventListener('click', (e) => {
        e.stopPropagation();
        this._selectAction(action);
      });

      const icon = document.createElement('vaadin-icon');
      icon.setAttribute('icon', action.icon);
      icon.setAttribute('aria-hidden', 'true');
      icon.style.width = '18px';
      icon.style.height = '18px';
      icon.style.flexShrink = '0';
      if (action.isDelete) {
        icon.style.color = 'var(--dorc-error-color)';
      }
      item.appendChild(icon);

      const label = document.createElement('span');
      label.textContent = action.text;
      label.style.whiteSpace = 'nowrap';
      if (action.isDelete) {
        label.style.color = 'var(--dorc-error-color)';
      }
      item.appendChild(label);

      overlay.appendChild(item);
    }

    overlay.addEventListener('keydown', this._onMenuKeyDown);
    document.body.appendChild(overlay);

    // Move focus to the first item (R1 / R2). For mouse-initiated opens this
    // is invisible because `:focus-visible` only paints the indicator on
    // keyboard interactions; for keyboard-initiated opens (Enter/Space on
    // the trigger) the indicator is shown immediately on the first item.
    const firstItem = overlay.querySelector<HTMLElement>('[role="menuitem"]');
    firstItem?.focus();
  }

  private _removeDropdown() {
    const dropdown = this._getDropdownEl();
    dropdown?.removeEventListener('keydown', this._onMenuKeyDown);
    dropdown?.remove();
  }

  private _selectAction(action: ActionMenuItem) {
    this._open = false;
    if (action.eventName === 'open-project-audit-data') {
      const id = this.project?.ProjectId;
      if (id) Router.go(`/projects/audit?projectId=${id}`);
      return;
    }
    this.dispatchEvent(
      new CustomEvent(action.eventName, {
        detail: action.detail(),
        bubbles: true,
        composed: true
      })
    );
  }
}
