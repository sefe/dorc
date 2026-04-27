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

  private _getDropdownEl(): HTMLElement | null {
    return document.getElementById(`project-dropdown-${this._uid}`);
  }

  private _uid = crypto.getRandomValues(new Uint32Array(1))[0].toString(36);

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
        theme="icon small"
        aria-label="Project actions"
        title="Project actions"
        @click="${this._toggle}"
      >
        <vaadin-icon icon="vaadin:ellipsis-dots-h"></vaadin-icon>
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
    overlay.id = `project-dropdown-${this._uid}`;
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

    for (const action of this.menuActions) {
      const item = document.createElement('div');
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

    document.body.appendChild(overlay);
  }

  private _removeDropdown() {
    this._getDropdownEl()?.remove();
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
