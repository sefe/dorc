import { css, LitElement } from 'lit';
import '@vaadin/menu-bar';
import type { MenuBarItemSelectedEvent } from '@vaadin/menu-bar';
import '@vaadin/icons';
import '@vaadin/vaadin-lumo-styles/icons.js';
import '../../icons/iron-icons.js';
import { customElement, property } from 'lit/decorators.js';
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

  static get styles() {
    return css`
      :host {
        display: inline-block;
      }

      vaadin-menu-bar {
        --lumo-space-xs: 0px;
        --lumo-space-s: 0px;
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

  private get menuItems() {
    return [
      {
        component: this.createTriggerButton(),
        children: this.menuActions.map(action => ({
          text: action.eventName,
          component: this.createMenuItem(action)
        }))
      }
    ];
  }

  render() {
    return html`
      <vaadin-menu-bar
        theme="icon tertiary small"
        .items="${this.menuItems}"
        @item-selected="${this.onItemSelected}"
      ></vaadin-menu-bar>
    `;
  }

  private createTriggerButton(): HTMLElement {
    const item = document.createElement('vaadin-menu-bar-item');
    item.setAttribute('aria-label', 'Project actions');
    item.setAttribute('title', 'Project actions');
    const icon = document.createElement('vaadin-icon');
    icon.setAttribute('icon', 'vaadin:ellipsis-dots-h');
    icon.style.color = 'var(--dorc-link-color)';
    item.appendChild(icon);
    return item;
  }

  private createMenuItem(action: ActionMenuItem): HTMLElement {
    const item = document.createElement('vaadin-menu-bar-item');
    item.style.display = 'flex';
    item.style.alignItems = 'center';
    item.style.gap = '8px';

    const icon = document.createElement('vaadin-icon');
    icon.setAttribute('icon', action.icon);
    icon.style.width = '18px';
    icon.style.height = '18px';
    icon.style.color = action.isDelete
      ? 'var(--dorc-error-color)'
      : 'var(--dorc-link-color)';
    item.appendChild(icon);

    const label = document.createElement('span');
    label.textContent = action.text;
    if (action.isDelete) {
      label.style.color = 'var(--dorc-error-color)';
    }
    item.appendChild(label);

    return item;
  }

  private onItemSelected(e: MenuBarItemSelectedEvent) {
    const selectedItem = e.detail.value as { text?: string };
    const eventName = selectedItem?.text;
    if (!eventName) return;

    const action = this.menuActions.find(a => a.eventName === eventName);
    if (!action) return;

    this.dispatchEvent(
      new CustomEvent(action.eventName, {
        detail: action.detail(),
        bubbles: true,
        composed: true
      })
    );
  }
}
