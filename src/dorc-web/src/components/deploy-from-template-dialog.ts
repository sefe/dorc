import '@vaadin/button';
import '@vaadin/combo-box';
import '@vaadin/text-field';
import '@vaadin/number-field';
import '@vaadin/password-field';
import '@vaadin/checkbox';
import '@vaadin/dialog';
import { dialogRenderer, dialogFooterRenderer } from '@vaadin/dialog/lit';
import { Notification } from '@vaadin/notification';
import { css, html, LitElement } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { Router } from '@vaadin/router';
import {
  ProjectApiModel,
  RefDataProjectsApi,
  RefDataProjectEnvironmentMappingsApi,
  TerraformApi,
  TerraformTemplateManifest,
  TerraformTemplateParameter,
  EnvironmentApiModel,
  EnvironmentApiModelTemplateApiModel,
} from '../apis/dorc-api';
import { retrieveErrorMessage } from '../helpers/errorMessage-retriever';

// 'Deploy from stock template' wizard. Single-page form with three
// implicit sections (Target / Inputs / Review) all fields visible at once
// for a small parameter count, masking sensitive ones. On submit, calls the
// extended instantiate endpoint with EnvironmentName + Parameters set,
// switching the API to create-and-deploy mode. On success, the user
// is navigated to the monitor-requests page where they can confirm the plan
// via the existing terraform-plan-dialog hosted by component-deployment-results.
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
        min-width: 520px;
        max-width: 720px;
      }
      .params {
        display: flex;
        flex-direction: column;
        gap: 8px;
        border-top: 1px solid var(--lumo-contrast-10pct);
        padding-top: 12px;
      }
      .param-row {
        display: grid;
        grid-template-columns: 200px 1fr;
        align-items: center;
        gap: 8px;
      }
      .param-label {
        font-family: var(--lumo-font-family);
        font-size: var(--lumo-font-size-s);
      }
      .param-label code {
        background: var(--lumo-contrast-5pct);
        padding: 1px 4px;
        border-radius: var(--lumo-border-radius-s);
      }
      .param-help {
        font-size: var(--lumo-font-size-xs);
        color: var(--lumo-secondary-text-color);
      }
      .hint {
        color: var(--lumo-secondary-text-color);
        font-size: var(--lumo-font-size-s);
      }
      .error-line {
        color: var(--lumo-error-text-color);
      }
      .section-title {
        font-weight: 600;
        font-size: var(--lumo-font-size-s);
        text-transform: uppercase;
        color: var(--lumo-secondary-text-color);
        margin-top: 8px;
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
  private environments: EnvironmentApiModel[] = [];

  @state()
  private selectedProject: ProjectApiModel | null = null;

  @state()
  private selectedEnvironmentName: string = '';

  @state()
  private componentName = '';

  @state()
  private paramValues: Record<string, string> = {};

  @state()
  private submitting = false;

  @state()
  private error: string | null = null;

  private projectsApi = new RefDataProjectsApi();
  private envMappingsApi = new RefDataProjectEnvironmentMappingsApi();
  private terraformApi = new TerraformApi();

  open(template: TerraformTemplateManifest) {
    this.template = template;
    this.componentName = template.Name;
    this.selectedProject = null;
    this.selectedEnvironmentName = '';
    this.environments = [];
    this.paramValues = {};
    // Pre-fill defaults from the manifest.
    for (const p of template.Parameters ?? []) {
      if (p.Default != null && p.Default !== '') {
        this.paramValues = { ...this.paramValues, [p.Name]: p.Default };
      }
    }
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

  private loadEnvironmentsForProject(projectName: string) {
    this.envMappingsApi
      .refDataProjectEnvironmentMappingsGet({ project: projectName, includeRead: false })
      .subscribe({
        next: (wrapper: EnvironmentApiModelTemplateApiModel) => {
          this.environments = wrapper.Items ?? [];
        },
        error: (err) => {
          this.error = retrieveErrorMessage(err) ?? 'Failed to load environments for the chosen project.';
          this.environments = [];
        },
      });
  }

  private onProjectChange(project: ProjectApiModel | null) {
    this.selectedProject = project;
    this.selectedEnvironmentName = '';
    this.environments = [];
    if (project?.ProjectName) {
      this.loadEnvironmentsForProject(project.ProjectName);
    }
  }

  private setParamValue(name: string, value: string) {
    this.paramValues = { ...this.paramValues, [name]: value };
  }

  private clientValidate(): string | null {
    if (!this.selectedProject) return 'Select a destination project.';
    if (!this.selectedEnvironmentName) return 'Select a destination environment.';
    if (!this.componentName) return 'Component name is required.';
    if (!this.template) return 'Template is missing.';
    for (const p of this.template.Parameters ?? []) {
      const v = this.paramValues[p.Name];
      if (p.Required && (v == null || v === '')) {
        return `Parameter '${p.Name}' is required.`;
      }
      if (v != null && v !== '' && p.Pattern) {
        try {
          if (!new RegExp(p.Pattern).test(v)) {
            return `Parameter '${p.Name}' does not match the manifest's pattern: ${p.Pattern}`;
          }
        } catch {
          // Ignore invalid regex from manifest; server-side validator is canonical.
        }
      }
      if (v != null && v !== '' && p.Type === 'Number') {
        const n = Number(v);
        if (Number.isNaN(n)) return `Parameter '${p.Name}' must be a number.`;
        if (p.Min != null && n < p.Min) return `Parameter '${p.Name}' must be >= ${p.Min}.`;
        if (p.Max != null && n > p.Max) return `Parameter '${p.Name}' must be <= ${p.Max}.`;
      }
    }
    return null;
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
          this.environments,
          this.selectedProject,
          this.selectedEnvironmentName,
          this.componentName,
          this.paramValues,
          this.submitting,
          this.error,
        ])}
        ${dialogFooterRenderer(this.footerRenderer, [this.submitting, this.selectedProject, this.selectedEnvironmentName, this.componentName])}
      ></vaadin-dialog>
    `;
  }

  private bodyRenderer = () => {
    if (!this.template) return html``;
    const t = this.template;
    return html`
      <div class="form">
        <div class="hint">
          The wizard creates a Catalog-mode component in the chosen project and
          submits a deployment request against the chosen environment with the
          parameter values you supply below. After submission you'll be taken
          to the monitor-requests page where you can confirm the Terraform plan.
        </div>

        ${this.error ? html`<div class="error-line">${this.error}</div>` : ''}

        <div class="section-title">Target</div>

        <vaadin-combo-box
          label="Destination project"
          item-label-path="ProjectName"
          item-value-path="ProjectId"
          .items="${this.projects}"
          .selectedItem="${this.selectedProject ?? undefined}"
          @selected-item-changed="${(e: CustomEvent) =>
            this.onProjectChange(e.detail.value as ProjectApiModel | null)}"
          required
        ></vaadin-combo-box>

        <vaadin-combo-box
          label="Destination environment"
          item-label-path="EnvironmentName"
          item-value-path="EnvironmentName"
          .items="${this.environments}"
          .value="${this.selectedEnvironmentName}"
          @value-changed="${(e: CustomEvent) =>
            (this.selectedEnvironmentName = (e.detail.value as string) ?? '')}"
          .disabled="${!this.selectedProject}"
          required
        ></vaadin-combo-box>

        <vaadin-text-field
          label="Component name"
          .value="${this.componentName}"
          @value-changed="${(e: CustomEvent) => (this.componentName = (e.detail.value as string) ?? '')}"
          helper-text="Defaults to the template name. Must be unique within the project."
          required
        ></vaadin-text-field>

        <div class="section-title">Inputs</div>
        <div class="params">
          ${(t.Parameters ?? []).map((p) => this.paramRenderer(p))}
        </div>
      </div>
    `;
  };

  private paramRenderer = (p: TerraformTemplateParameter) => {
    const value = this.paramValues[p.Name] ?? '';
    const helper = `${p.Description ?? ''}${p.Required ? ' (required)' : ''}${p.Default ? ` default: ${p.Default}` : ''}`;
    if (p.Sensitive) {
      return html`
        <div class="param-row">
          <div class="param-label">
            <code>${p.Name}</code>
            <div class="param-help">sensitive · ${p.Type}</div>
          </div>
          <vaadin-password-field
            .value="${value}"
            @value-changed="${(e: CustomEvent) => this.setParamValue(p.Name, (e.detail.value as string) ?? '')}"
            helper-text="${helper}"
            ?required="${p.Required}"
          ></vaadin-password-field>
        </div>
      `;
    }
    if (p.Type === 'Bool') {
      return html`
        <div class="param-row">
          <div class="param-label">
            <code>${p.Name}</code>
            <div class="param-help">${p.Type}</div>
          </div>
          <div>
            <vaadin-checkbox
              .checked="${value === 'true'}"
              @checked-changed="${(e: CustomEvent) =>
                this.setParamValue(p.Name, (e.detail.value as boolean) ? 'true' : 'false')}"
              label="${helper}"
            ></vaadin-checkbox>
          </div>
        </div>
      `;
    }
    if (p.AllowedValues && p.AllowedValues.length > 0) {
      return html`
        <div class="param-row">
          <div class="param-label">
            <code>${p.Name}</code>
            <div class="param-help">${p.Type} · allow-list</div>
          </div>
          <vaadin-combo-box
            .items="${p.AllowedValues}"
            .value="${value}"
            @value-changed="${(e: CustomEvent) => this.setParamValue(p.Name, (e.detail.value as string) ?? '')}"
            helper-text="${helper}"
            ?required="${p.Required}"
          ></vaadin-combo-box>
        </div>
      `;
    }
    if (p.Type === 'Number') {
      return html`
        <div class="param-row">
          <div class="param-label">
            <code>${p.Name}</code>
            <div class="param-help">${p.Type}${p.Min != null ? ` · min ${p.Min}` : ''}${p.Max != null ? ` · max ${p.Max}` : ''}</div>
          </div>
          <vaadin-number-field
            .value="${value}"
            @value-changed="${(e: CustomEvent) => this.setParamValue(p.Name, (e.detail.value as string) ?? '')}"
            helper-text="${helper}"
            ?required="${p.Required}"
            .min="${p.Min ?? undefined}"
            .max="${p.Max ?? undefined}"
          ></vaadin-number-field>
        </div>
      `;
    }
    // String (default)
    return html`
      <div class="param-row">
        <div class="param-label">
          <code>${p.Name}</code>
          <div class="param-help">${p.Type}${p.Pattern ? ' · pattern' : ''}</div>
        </div>
        <vaadin-text-field
          .value="${value}"
          @value-changed="${(e: CustomEvent) => this.setParamValue(p.Name, (e.detail.value as string) ?? '')}"
          helper-text="${helper}"
          ?required="${p.Required}"
          .pattern="${p.Pattern ?? ''}"
        ></vaadin-text-field>
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
      .disabled="${this.submitting || !this.selectedProject || !this.selectedEnvironmentName || !this.componentName}"
    >
      Deploy
    </vaadin-button>
  `;

  private submit() {
    if (!this.template || !this.selectedProject || !this.selectedEnvironmentName || !this.componentName) return;

    const validation = this.clientValidate();
    if (validation) {
      this.error = validation;
      return;
    }

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
          EnvironmentName: this.selectedEnvironmentName,
          Parameters: this.paramValues,
        } as any,
      })
      .subscribe({
        next: (response: any) => {
          this.submitting = false;
          this.opened = false;
          const requestId = response?.requestId;
          const message = requestId
            ? `Created component '${this.componentName}' and submitted deploy request #${requestId} to ${this.selectedEnvironmentName}.`
            : `Created component '${this.componentName}'.`;
          const n = Notification.show(message, { duration: 5000, position: 'bottom-end' });
          n.setAttribute('theme', 'success');
          Router.go('/monitor-requests');
        },
        error: (err: any) => {
          this.submitting = false;
          this.error = retrieveErrorMessage(err) ?? 'Failed to create the component / deploy request.';
        },
      });
  }
}
