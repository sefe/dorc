import '@vaadin/button';
import '@vaadin/combo-box';
import '@vaadin/text-field';
import '@vaadin/dialog';
import { dialogRenderer, dialogFooterRenderer } from '@vaadin/dialog/lit';
import { Notification } from '@vaadin/notification';
import { css, html, LitElement } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { Router } from '@vaadin/router';
import {
  ComponentApiModel,
  ProjectApiModel,
  RefDataProjectsApi,
  TerraformApi,
  TerraformTemplateManifest,
} from '../apis/dorc-api';
import { retrieveErrorMessage } from '../helpers/errorMessage-retriever';

// Option B: "Deploy from stock template" wizard. Creates a Catalog-mode
// component in the chosen project so the engineer can deploy via the
// existing DOrc deploy flow. Manifest parameter schema is shown read-only
// so the engineer knows which property values they will need to set on
// the deploy page.
@customElement('deploy-from-template-dialog')
export class DeployFromTemplateDialog extends LitElement {
  static get styles() {
    return css`
      :host {
        display: contents;
      }
      .form {
        display: flex;
        flex-direction: column;
        gap: 12px;
        min-width: 480px;
        max-width: 640px;
      }
      .params-table {
        width: 100%;
        border-collapse: collapse;
        font-size: var(--lumo-font-size-s);
      }
      .params-table th, .params-table td {
        text-align: left;
        padding: 4px 8px;
        border-bottom: 1px solid var(--lumo-contrast-10pct);
      }
      .hint {
        color: var(--lumo-secondary-text-color);
        font-size: var(--lumo-font-size-s);
      }
      .error-line {
        color: var(--lumo-error-text-color);
      }
    `;
  }

  @property({ type: Boolean })
  opened = false;

  @property({ attribute: false })
  template: TerraformTemplateManifest | null = null;

  @state()
  private projects: ProjectApiModel[] = [];

  @state()
  private selectedProject: ProjectApiModel | null = null;

  @state()
  private componentName = '';

  @state()
  private submitting = false;

  @state()
  private error: string | null = null;

  private projectsApi = new RefDataProjectsApi();
  private terraformApi = new TerraformApi();

  open(template: TerraformTemplateManifest) {
    this.template = template;
    this.componentName = template.Name;
    this.selectedProject = null;
    this.error = null;
    this.opened = true;
    this.loadProjects();
  }

  private loadProjects() {
    this.projectsApi.refDataProjectsGet().subscribe({
      next: (data) => {
        this.projects = data ?? [];
      },
      error: (err) => {
        this.error = retrieveErrorMessage(err) ?? 'Failed to load projects.';
      },
    });
  }

  render() {
    return html`
      <vaadin-dialog
        .opened="${this.opened}"
        @opened-changed="${(e: CustomEvent) => (this.opened = e.detail.value)}"
        header-title="Deploy from template${this.template ? `: ${this.template.Name}@${this.template.Version}` : ''}"
        modeless
        ${dialogRenderer(this.bodyRenderer, [
          this.template,
          this.projects,
          this.selectedProject,
          this.componentName,
          this.submitting,
          this.error,
        ])}
        ${dialogFooterRenderer(this.footerRenderer, [this.submitting, this.selectedProject, this.componentName])}
      ></vaadin-dialog>
    `;
  }

  private bodyRenderer = () => {
    if (!this.template) return html``;
    const t = this.template;
    return html`
      <div class="form">
        <div class="hint">
          A new Catalog-mode component will be created in the chosen project. After
          submission you will be taken to the project where you can configure
          environment-specific property values for this component and deploy it
          through the standard DOrc deploy flow.
        </div>

        ${this.error ? html`<div class="error-line">${this.error}</div>` : ''}

        <vaadin-combo-box
          label="Destination project"
          item-label-path="ProjectName"
          item-value-path="ProjectId"
          .items="${this.projects}"
          .selectedItem="${this.selectedProject ?? undefined}"
          @selected-item-changed="${(e: CustomEvent) =>
            (this.selectedProject = e.detail.value as ProjectApiModel | null)}"
          required
        ></vaadin-combo-box>

        <vaadin-text-field
          label="Component name"
          .value="${this.componentName}"
          @value-changed="${(e: CustomEvent) => (this.componentName = (e.detail.value as string) ?? '')}"
          helper-text="Defaults to the template name. Must be unique within the project."
          required
        ></vaadin-text-field>

        <div>
          <strong>Parameter schema (informational)</strong>
          <div class="hint">
            These are the inputs the module exposes. After the component is
            created, set environment-specific values via the existing DOrc
            property mechanism on the deploy page.
          </div>
          <table class="params-table">
            <thead>
              <tr><th>Name</th><th>Type</th><th>Required</th><th>Default</th><th>Description</th></tr>
            </thead>
            <tbody>
              ${(t.Parameters ?? []).map(
                (p) => html`
                  <tr>
                    <td><code>${p.Name}</code></td>
                    <td>${p.Type}</td>
                    <td>${p.Required ? 'yes' : 'no'}</td>
                    <td>${p.Default ?? ''}</td>
                    <td>${p.Description ?? ''}</td>
                  </tr>
                `,
              )}
            </tbody>
          </table>
        </div>
      </div>
    `;
  };

  private footerRenderer = () => html`
    <vaadin-button
      theme="tertiary"
      @click="${() => (this.opened = false)}"
      .disabled="${this.submitting}"
    >
      Cancel
    </vaadin-button>
    <vaadin-button
      theme="primary"
      @click="${() => this.submit()}"
      .disabled="${this.submitting || !this.selectedProject || !this.componentName}"
    >
      Create component &amp; go to project
    </vaadin-button>
  `;

  private submit() {
    if (!this.template || !this.selectedProject || !this.componentName) return;

    this.submitting = true;
    this.error = null;

    const projectId = this.selectedProject.ProjectId as number;

    this.terraformApi
      .terraformTemplateInstantiatePost({
        name: this.template.Name,
        version: this.template.Version,
        body: {
          ProjectId: projectId,
          ComponentName: this.componentName,
          ParentComponentId: null,
        },
      })
      .subscribe({
        next: (component: ComponentApiModel) => {
          this.submitting = false;
          this.opened = false;
          const n = Notification.show(
            `Created component '${component.ComponentName}' from template ${this.template?.Name}@${this.template?.Version}`,
            { duration: 4000, position: 'bottom-end' },
          );
          n.setAttribute('theme', 'success');
          // Route to the project's components view so the engineer can
          // configure environment property values + deploy.
          Router.go(`/project/${projectId}/components`);
        },
        error: (err) => {
          this.submitting = false;
          this.error = retrieveErrorMessage(err) ?? 'Failed to create component from template.';
        },
      });
  }
}
