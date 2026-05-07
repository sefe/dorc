import { css, LitElement } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '@vaadin/button';
import '@vaadin/combo-box';
import '@vaadin/text-field';
import '@vaadin/text-area';
import '@vaadin/vertical-layout';
import {
  ApiApiModel,
  ProjectApiModel,
  RefDataApisApi,
  RefDataProjectsApi
} from '../apis/dorc-api';

const apiTypes = ['REST', 'SOAP', 'gRPC'];
const authTypes = ['None', 'Basic', 'Bearer', 'OAuth'];

@customElement('add-edit-api')
export class AddEditApi extends LitElement {
  @property({ type: Number })
  envId = 0;

  @property({ type: Array })
  existingApis: ApiApiModel[] = [];

  @property({ type: Object })
  get api(): ApiApiModel {
    return this._api;
  }

  set api(value: ApiApiModel | null | undefined) {
    if (value == null) return;
    const old = this._api;
    this._api = JSON.parse(JSON.stringify(value));

    this.Name = this._api.Name ?? '';
    this.Endpoint = this._api.Endpoint ?? '';
    this.Description = this._api.Description ?? '';
    this.Type = this._api.Type ?? 'REST';
    this.AuthType = this._api.AuthType ?? 'None';
    this.HealthCheckPath = this._api.HealthCheckPath ?? '';
    this.OwnerProjectId = this._api.OwnerProjectId ?? null;
    this.Tags = this._api.Tags ?? '';
    this.errorMessage = '';

    this.requestUpdate('api', old);
  }

  private _api: ApiApiModel = AddEditApi.emptyApi();

  @state() private Name = '';
  @state() private Endpoint = '';
  @state() private Description = '';
  @state() private Type = 'REST';
  @state() private AuthType = 'None';
  @state() private HealthCheckPath = '';
  @state() private OwnerProjectId: number | null = null;
  @state() private Tags = '';

  @state() private errorMessage = '';
  @state() private projects: ProjectApiModel[] = [];

  static get styles() {
    return css`
      .form {
        padding: 10px;
        width: 540px;
      }
      .field {
        width: 100%;
      }
      .row {
        display: flex;
        gap: 12px;
      }
      .row > * {
        flex: 1;
      }
      .error {
        color: var(--dorc-error-color);
        margin-top: 8px;
      }
      .helper {
        color: var(--lumo-secondary-text-color);
        font-size: 12px;
        margin-top: -6px;
        margin-bottom: 6px;
      }
    `;
  }

  connectedCallback() {
    super.connectedCallback();
    this.loadProjects();
  }

  render() {
    return html`
      <div class="form">
        <vaadin-vertical-layout>
          <vaadin-text-field
            class="field"
            label="Name"
            required
            .value="${this.Name}"
            @value-changed="${(e: CustomEvent) =>
              (this.Name = (e.detail.value as string) ?? '')}"
          ></vaadin-text-field>

          <vaadin-text-field
            class="field"
            label="Endpoint"
            required
            placeholder="https://$ApiHost$:$ApiPort$/v1"
            .value="${this.Endpoint}"
            @value-changed="${(e: CustomEvent) =>
              (this.Endpoint = (e.detail.value as string) ?? '')}"
          ></vaadin-text-field>
          <div class="helper">
            Use $VarName$ to reference environment-scoped variables.
          </div>

          <div class="row">
            <vaadin-combo-box
              label="Type"
              required
              ?allow-custom-value="${false}"
              .items="${apiTypes}"
              .value="${this.Type}"
              @value-changed="${(e: CustomEvent) =>
                (this.Type = (e.detail.value as string) ?? '')}"
            ></vaadin-combo-box>
            <vaadin-combo-box
              label="Auth Type"
              required
              ?allow-custom-value="${false}"
              .items="${authTypes}"
              .value="${this.AuthType}"
              @value-changed="${(e: CustomEvent) =>
                (this.AuthType = (e.detail.value as string) ?? '')}"
            ></vaadin-combo-box>
          </div>

          <vaadin-text-field
            class="field"
            label="Health Check Path"
            placeholder="/health"
            .value="${this.HealthCheckPath}"
            @value-changed="${(e: CustomEvent) =>
              (this.HealthCheckPath = (e.detail.value as string) ?? '')}"
          ></vaadin-text-field>

          <vaadin-combo-box
            class="field"
            label="Owner Project"
            item-value-path="ProjectId"
            item-label-path="ProjectName"
            .items="${this.projects}"
            .selectedItem="${this.projects.find(
              p => p.ProjectId === this.OwnerProjectId
            ) ?? null}"
            @selected-item-changed="${(e: CustomEvent) => {
              const selected = e.detail.value as ProjectApiModel | null;
              this.OwnerProjectId = selected?.ProjectId ?? null;
            }}"
            clear-button-visible
          ></vaadin-combo-box>

          <vaadin-text-field
            class="field"
            label="Tags (semicolon-separated)"
            .value="${this.Tags}"
            @value-changed="${(e: CustomEvent) =>
              (this.Tags = (e.detail.value as string) ?? '')}"
          ></vaadin-text-field>

          <vaadin-text-area
            class="field"
            label="Description"
            .value="${this.Description}"
            @value-changed="${(e: CustomEvent) =>
              (this.Description = (e.detail.value as string) ?? '')}"
          ></vaadin-text-area>
        </vaadin-vertical-layout>

        <div style="margin-top: 12px; display: flex; gap: 8px;">
          <vaadin-button
            theme="primary"
            .disabled="${!this.canSubmit()}"
            @click="${this.save}"
            >Save</vaadin-button
          >
          <vaadin-button @click="${this.reset}">Clear</vaadin-button>
        </div>

        <div class="error">${this.errorMessage}</div>
      </div>
    `;
  }

  private canSubmit(): boolean {
    if (!this.Name || this.Name.trim().length === 0) return false;
    if (!this.Endpoint || this.Endpoint.trim().length === 0) return false;
    if (!apiTypes.includes(this.Type)) return false;
    if (!authTypes.includes(this.AuthType)) return false;
    return true;
  }

  private save() {
    const trimmedName = this.Name.trim();

    const duplicate = this.existingApis.find(
      a =>
        a.Name?.toLowerCase() === trimmedName.toLowerCase() &&
        a.Id !== this._api.Id
    );
    if (duplicate) {
      this.errorMessage = `An API named '${trimmedName}' already exists for this environment.`;
      return;
    }

    const payload: ApiApiModel = {
      Id: this._api.Id ?? 0,
      EnvironmentId: this.envId,
      Name: trimmedName,
      Endpoint: this.Endpoint.trim(),
      Description: this.Description,
      Type: this.Type,
      AuthType: this.AuthType,
      HealthCheckPath: this.HealthCheckPath || null,
      OwnerProjectId: this.OwnerProjectId,
      Tags: this.Tags || null
    };

    const api = new RefDataApisApi();
    if (payload.Id && payload.Id > 0) {
      api.refDataApisPut({ apiApiModel: payload }).subscribe({
        next: data => this.fireSaved(data, 'api-updated'),
        error: err => this.handleError(err)
      });
    } else {
      api
        .refDataApisPost({ environmentId: this.envId, apiApiModel: payload })
        .subscribe({
          next: data => this.fireSaved(data, 'api-created'),
          error: err => this.handleError(err)
        });
    }
  }

  private fireSaved(data: ApiApiModel, eventName: string) {
    this.errorMessage = '';
    this.dispatchEvent(
      new CustomEvent(eventName, {
        detail: { data },
        bubbles: true,
        composed: true
      })
    );
    this.reset();
  }

  private handleError(err: unknown) {
    console.error(err);
    const e = err as { response?: unknown; message?: unknown } | null | undefined;
    if (e == null) {
      this.errorMessage = 'Save failed';
      return;
    }
    if (typeof e.response === 'string' && e.response.length > 0) {
      this.errorMessage = e.response;
      return;
    }
    if (typeof e.message === 'string' && e.message.length > 0) {
      this.errorMessage = e.message;
      return;
    }
    if (e.response != null) {
      try {
        this.errorMessage = JSON.stringify(e.response);
        return;
      } catch {
        // fall through
      }
    }
    this.errorMessage = 'Save failed';
  }

  private reset() {
    this._api = AddEditApi.emptyApi();
    this.Name = '';
    this.Endpoint = '';
    this.Description = '';
    this.Type = 'REST';
    this.AuthType = 'None';
    this.HealthCheckPath = '';
    this.OwnerProjectId = null;
    this.Tags = '';
    this.errorMessage = '';
  }

  private loadProjects() {
    const projectsApi = new RefDataProjectsApi();
    projectsApi.refDataProjectsGet().subscribe({
      next: data => {
        this.projects = (data ?? []).slice().sort((a, b) =>
          (a.ProjectName ?? '').localeCompare(b.ProjectName ?? '')
        );
      },
      error: err => console.error('Failed to load projects', err)
    });
  }

  private static emptyApi(): ApiApiModel {
    return {
      Id: 0,
      Name: '',
      Endpoint: '',
      Description: '',
      Type: 'REST',
      AuthType: 'None',
      HealthCheckPath: '',
      OwnerProjectId: null,
      Tags: ''
    };
  }
}
