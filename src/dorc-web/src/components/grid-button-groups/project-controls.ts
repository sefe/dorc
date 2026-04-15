import { css, LitElement } from 'lit';
import '@vaadin/button';
import '@vaadin/icon';
import '@vaadin/icons';
import '@vaadin/vaadin-lumo-styles/icons.js';
import '../../icons/iron-icons.js';
import { customElement, property, state } from 'lit/decorators.js';
import { html, nothing } from 'lit/html.js';
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

  private _outsideClickHandler = (e: MouseEvent) => {
    if (!this.contains(e.target as Node)) {
      this._open = false;
    }
  };

  static get styles() {
    return css`
      :host {
        display: inline-block;
        position: relative;
      }

      vaadin-button {
        --lumo-button-size: 28px;
        --lumo-icon-size-m: 16px;
      }

      .dropdown {
        position: absolute;
        right: 0;
        top: 100%;
        z-index: 100;
        min-width: 180px;
        background: var(--lumo-base-color);
        border: 1px solid var(--lumo-contrast-10pct);
        border-radius: var(--lumo-border-radius-m);
        box-shadow: var(--lumo-box-shadow-m);
        padding: 4px 0;
      }

      .menu-item {
        display: flex;
        align-items: center;
        gap: 10px;
        padding: 8px 16px;
        cursor: pointer;
        color: var(--lumo-body-text-color);
        font-size: var(--lumo-font-size-s);
      }

      .menu-item:hover {
        background-color: var(--lumo-primary-color-10pct);
      }

      .menu-item vaadin-icon {
        width: 18px;
        height: 18px;
        flex-shrink: 0;
      }

      .menu-item span {
        white-space: nowrap;
      }

      .menu-item.delete {
        border-top: 1px solid var(--lumo-contrast-10pct);
        margin-top: 4px;
        padding-top: 12px;
      }

      .menu-item.delete vaadin-icon,
      .menu-item.delete span {
        color: var(--dorc-error-color);
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
  }

  disconnectedCallback() {
    super.disconnectedCallback();
    document.removeEventListener('click', this._outsideClickHandler);
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
      ${this._open ? html`
        <div class="dropdown">
          ${this.menuActions.map(action => html`
            <div
              class="menu-item ${action.isDelete ? 'delete' : ''}"
              @click="${() => this._selectAction(action)}"
            >
              <vaadin-icon icon="${action.icon}"></vaadin-icon>
              <span>${action.text}</span>
            </div>
          `)}
        </div>
      ` : nothing}
    `;
  }

  private _toggle(e: Event) {
    e.stopPropagation();
    this._open = !this._open;
  }

  private _selectAction(action: ActionMenuItem) {
    this._open = false;
    this.dispatchEvent(
      new CustomEvent(action.eventName, {
        detail: action.detail(),
        bubbles: true,
        composed: true
      })
    );
  }
}
