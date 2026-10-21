import '@vaadin/icon';
import '@vaadin/icons/vaadin-icons';
import { css, html, LitElement, nothing, PropertyValues } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { drawerShortcuts } from './drawer-shortcuts';

export type ProjectWorkspaceTab =
  'environments' | 'definitions' | 'components' | 'bundles';

@customElement('project-workspace-nav')
export class ProjectWorkspaceNav extends LitElement {
  static styles = css`
    :host {
      display: block;
      background: var(--dorc-bg-secondary);
      border-bottom: 1px solid var(--dorc-border-color);
      font-family: var(--lumo-font-family);
    }

    nav {
      display: flex;
      align-items: center;
      gap: var(--lumo-space-xs);
      padding: 0 var(--lumo-space-m);
      min-height: 36px;
      overflow-x: auto;
    }

    .project-context {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 4px var(--lumo-space-s) 4px 0;
      margin-right: var(--lumo-space-xs);
      border-right: 1px solid var(--dorc-border-color);
      font-size: var(--lumo-font-size-s);
      font-weight: 600;
      color: var(--dorc-text-primary);
      white-space: nowrap;
      flex-shrink: 0;
    }

    .project-context vaadin-icon {
      width: var(--lumo-icon-size-s);
      height: var(--lumo-icon-size-s);
      color: var(--dorc-text-secondary);
    }

    a.tab-link,
    span.tab-link {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 6px var(--lumo-space-s);
      border-bottom: 2px solid transparent;
      color: var(--dorc-text-secondary);
      font-size: var(--lumo-font-size-s);
      font-weight: 500;
      text-decoration: none;
      white-space: nowrap;
      flex-shrink: 0;
      transition:
        color 0.15s ease,
        border-color 0.15s ease;
    }

    a.tab-link:hover {
      color: var(--dorc-link-color);
    }

    a.tab-link[aria-current='page'],
    span.tab-link[aria-current='page'] {
      color: var(--dorc-link-color);
      border-bottom-color: var(--dorc-link-color);
      font-weight: 600;
    }

    span.tab-link[aria-disabled='true'] {
      opacity: 0.5;
      cursor: default;
    }

    .tab-link vaadin-icon {
      width: 14px;
      height: 14px;
    }

    @media (max-width: 768px) {
      nav {
        padding: 0 var(--lumo-space-s);
      }
    }
  `;

  @property({ type: Number }) projectId?: number;
  @property() projectName = '';
  @property() activeTab: ProjectWorkspaceTab = 'environments';

  protected updated(changedProperties: PropertyValues) {
    super.updated(changedProperties);
    if (
      (changedProperties.has('projectId') ||
        changedProperties.has('projectName')) &&
      this.projectName &&
      this.projectId != null &&
      Number.isFinite(this.projectId)
    ) {
      drawerShortcuts.enrichProject({
        ProjectId: this.projectId,
        ProjectName: this.projectName
      });
    }
  }

  private pinProject() {
    if (!this.projectName) return;
    drawerShortcuts.add('projects', {
      ProjectId:
        this.projectId != null && Number.isFinite(this.projectId)
          ? this.projectId
          : undefined,
      ProjectName: this.projectName
    });
  }

  private renderTab(
    id: ProjectWorkspaceTab,
    label: string,
    icon: string,
    href: string | undefined
  ) {
    const isCurrent = this.activeTab === id;
    if (!href) {
      return html`<span
        class="tab-link"
        aria-current=${isCurrent ? 'page' : nothing}
        aria-disabled="true"
      >
        <vaadin-icon icon=${icon} theme="small"></vaadin-icon>
        ${label}
      </span>`;
    }
    return html`<a
      class="tab-link"
      href=${href}
      aria-current=${isCurrent ? 'page' : nothing}
      @click=${() => this.pinProject()}
    >
      <vaadin-icon icon=${icon} theme="small"></vaadin-icon>
      ${label}
    </a>`;
  }

  render() {
    const encodedName = this.projectName
      ? encodeURIComponent(this.projectName)
      : '';
    const hasId =
      this.projectId != null &&
      Number.isFinite(this.projectId) &&
      this.projectId > 0;
    const projectLabel =
      this.projectName ||
      (hasId ? `Project #${this.projectId}` : 'Project Workspace');

    return html`
      <nav aria-label="Project workspace navigation">
        <span class="project-context">
          <vaadin-icon icon="vaadin:archives" theme="small"></vaadin-icon>
          <span>${projectLabel}</span>
        </span>
        ${this.renderTab(
          'environments',
          'Environments',
          'vaadin:cubes',
          encodedName ? `/project-envs/${encodedName}` : undefined
        )}
        ${this.renderTab(
          'definitions',
          'Deployment Definitions',
          'vaadin:file-tree',
          hasId ? `/project-ref-data/${this.projectId}` : undefined
        )}
        ${this.renderTab(
          'components',
          'Component Deployments',
          'vaadin:records',
          hasId ? `/project-components/${this.projectId}` : undefined
        )}
        ${this.renderTab(
          'bundles',
          'Bundles',
          'vaadin:package',
          encodedName ? `/project-envs/${encodedName}/bundles` : undefined
        )}
      </nav>
    `;
  }
}
