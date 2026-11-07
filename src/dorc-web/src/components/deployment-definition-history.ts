import '@vaadin/button';
import { css, html, LitElement, nothing, type PropertyValues } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import {
  DeploymentDefinitionsApi,
  type DeploymentDefinitionHistoryApiModel
} from '../apis/dorc-api';
import { retrieveErrorMessage } from '../helpers/errorMessage-retriever';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';

@customElement('deployment-definition-history')
export class DeploymentDefinitionHistory extends LitElement {
  @property({ type: Number }) projectId = 0;
  @property({ type: Number }) componentId: number | undefined;
  @property({ type: String }) revision = '';
  @state() private items: DeploymentDefinitionHistoryApiModel[] = [];
  @state() private loading = false;
  @state() private error = '';
  @state() private page = 1;
  @state() private total = 0;
  private loadGeneration = 0;
  private readonly pageSize = 25;

  static styles = css`
    :host {
      display: block;
      color: var(--dorc-text-primary);
    }
    header,
    footer {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--lumo-space-m);
      flex-wrap: wrap;
    }
    h2 {
      margin: 0;
      font-size: var(--lumo-font-size-l);
    }
    p {
      max-width: 75ch;
      color: var(--dorc-text-secondary-strong);
    }
    ol {
      list-style: none;
      padding: 0;
      margin: var(--lumo-space-m) 0;
    }
    li {
      border-inline-start: 2px solid var(--dorc-border-color);
      padding: 0 0 var(--lumo-space-l) var(--lumo-space-m);
      overflow-wrap: anywhere;
    }
    .heading {
      display: flex;
      gap: var(--lumo-space-s);
      align-items: baseline;
      flex-wrap: wrap;
    }
    time,
    .metadata {
      color: var(--dorc-text-secondary-strong);
      font-size: var(--lumo-font-size-s);
    }
    .legacy {
      color: var(--dorc-warning-text);
    }
    a {
      color: var(--dorc-link-color);
    }
    nav {
      display: flex;
      flex-wrap: wrap;
      gap: var(--lumo-space-m);
    }
    .changes {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--lumo-space-m);
    }
    pre {
      margin: var(--lumo-space-s) 0;
      padding: var(--lumo-space-s);
      background: var(--dorc-bg-secondary);
      white-space: pre-wrap;
      overflow-wrap: anywhere;
      max-height: 25rem;
      overflow: auto;
    }
    .error {
      color: var(--lumo-error-text-color);
    }
    @media (max-width: 768px) {
      .changes {
        grid-template-columns: 1fr;
      }
    }
  `;

  protected updated(changed: PropertyValues) {
    super.updated(changed);
    if (
      changed.has('projectId') ||
      changed.has('componentId') ||
      changed.has('revision')
    ) {
      this.page = 1;
      this.refresh();
    }
  }

  disconnectedCallback() {
    super.disconnectedCallback();
    this.loadGeneration++;
  }

  public refresh() {
    const generation = ++this.loadGeneration;
    this.items = [];
    this.error = '';
    this.total = 0;
    if (this.projectId <= 0) {
      this.loading = false;
      return;
    }
    this.loading = true;
    new DeploymentDefinitionsApi(dorcApiConfiguration)
      .deploymentDefinitionsHistoryGet({
        projectId: this.projectId,
        componentId: this.componentId,
        page: this.page,
        limit: this.pageSize
      })
      .subscribe({
        next: data => {
          if (generation !== this.loadGeneration) return;
          this.items = data.Items ?? [];
          this.total = data.TotalItems ?? 0;
          this.loading = false;
        },
        error: error => {
          if (generation !== this.loadGeneration) return;
          this.error =
            retrieveErrorMessage(error) ||
            'Could not load definition history. Try again.';
          this.loading = false;
        }
      });
  }

  private changePage(delta: number) {
    this.page += delta;
    this.refresh();
  }

  render() {
    return html`
      <header>
        <h2>${this.componentId ? 'Component history' : 'Project history'}</h2>
        <vaadin-button @click=${() => this.refresh()} .disabled=${this.loading}
          >Refresh history</vaadin-button
        >
      </header>
      <p>
        Configuration changes, submitted revisions and execution evidence are
        separate events. Legacy records may not establish what actually ran.
      </p>
      ${this.loading ? html`<p role="status">Loading history...</p>` : nothing}
      ${this.error ? html`<p class="error" role="alert">${this.error}</p>` : nothing}
      ${!this.loading && !this.error && !this.items.length ? html`<p>No recorded history for this selection.</p>` : nothing}
      <ol aria-label="Definition and deployment history">
        ${this.items.map(
          item =>
            html` <li>
              <div class="heading">
                <strong>${item.EventType}</strong>
                <time
                  >${item.OccurredAt ? new Date(item.OccurredAt).toLocaleString() : 'Time not recorded'}</time
                >
              </div>
              <div class="metadata">
                ${item.ComponentName || 'Project configuration'}${item.Actor ? ` by ${item.Actor}` : ''}
              </div>
              <p>${item.Summary}</p>
              ${item.IsLegacy ? html`<p class="legacy">Legacy evidence; not proof of a successful configuration save or exact executed source.</p>` : nothing}
              ${item.DefinitionRevision ? html`<div class="metadata">Definition revision: ${item.DefinitionRevision}</div>` : nothing}
              ${item.SourceSummary ? html`<p>${item.SourceSummary}</p>` : nothing}
              <nav>
                ${item.RequestId ? html`<a href="/monitor-result/${item.RequestId}">Deployment request #${item.RequestId}</a>` : nothing}
                ${item.ComponentId ? html`<a href="/project-ref-data/${this.projectId}?componentId=${item.ComponentId}">Current definition</a>` : nothing}
              </nav>
              ${
                item.BeforeJson || item.AfterJson
                  ? html` <details>
                      <summary>Configuration change</summary>
                      <div class="changes">
                        <section>
                          <h3>Before</h3>
                          <pre>${item.BeforeJson || 'Not recorded'}</pre>
                        </section>
                        <section>
                          <h3>After</h3>
                          <pre>${item.AfterJson || 'Not recorded'}</pre>
                        </section>
                      </div>
                    </details>`
                  : nothing
              }
            </li>`
        )}
      </ol>
      <footer>
        <span
          >${this.total}
          events${this.total ? `; page ${this.page} of ${Math.ceil(this.total / this.pageSize)}` : ''}</span
        >
        <div>
          <vaadin-button
            @click=${() => this.changePage(-1)}
            .disabled=${this.loading || this.page <= 1}
            >Previous</vaadin-button
          >
          <vaadin-button
            @click=${() => this.changePage(1)}
            .disabled=${this.loading || this.page * this.pageSize >= this.total}
            >Next</vaadin-button
          >
        </div>
      </footer>
    `;
  }
}
