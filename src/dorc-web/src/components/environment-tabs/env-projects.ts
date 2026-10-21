import '@vaadin/details';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/icon';
import '@vaadin/icons/vaadin-icons';
import { css } from 'lit';
import { customElement } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../application-daemons';
import { PageEnvBase } from './page-env-base';
import '../project-card';

@customElement('env-projects')
export class EnvProjects extends PageEnvBase {
  static get styles() {
    return css`
      :host {
        display: flex;
        flex-direction: column;
        width: 100%;
        height: 100%;
      }
      /* Matches the sibling environment tabs (env-databases, env-servers):
         calc(100% - 4px) compensates for the inline padding-left:4px below, which
         this panel was previously overflowing by, and --divider-color themes the
         summary rule that otherwise fell back to Vaadin's default (D-29). */
      vaadin-details {
        overflow: auto;
        width: calc(100% - 4px);
        flex: 1;
        min-height: 0;
        --divider-color: var(--dorc-border-color);
      }

      /* Cards were mapped straight into the panel, so eight projects rendered as
         a single 300px column with no gap — adjacent box-shadows merging into a
         grey seam — while the rest of a wide panel sat empty (D-30). Same grid
         convention as page-project-envs. */
      .projects {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(300px, 300px));
        gap: var(--lumo-space-xs);
        padding: var(--lumo-space-xs) 0 0 var(--lumo-space-xs);
        justify-content: flex-start;
        align-items: stretch;
        align-content: flex-start;
      }

      .projects > project-card {
        display: block;
        height: 100%;
      }
      .summary-bar {
        display: flex;
        align-items: center;
        gap: var(--lumo-space-s);
        font-weight: 400;
      }
      .summary-bar vaadin-icon {
        color: var(--dorc-link-color);
      }
      .count-badge {
        background-color: var(--dorc-link-color);
        color: var(--dorc-bg-primary);
        border-radius: 1em;
        font-size: var(--lumo-font-size-xs);
        font-weight: 600;
        padding: 0 var(--lumo-space-s);
        line-height: 1.6;
      }
      @media (max-width: 768px) {
        .projects {
          grid-template-columns: 1fr;
        }
      }
    `;
  }

  render() {
    return html`
      <vaadin-details
        opened
        style="border-top: 6px solid var(--dorc-link-color); background-color: var(--dorc-bg-secondary); padding-left: 4px; padding-right: 4px; margin: 0px; box-sizing: border-box;"
      >
        <vaadin-details-summary slot="summary">
          <div class="summary-bar">
            <vaadin-icon icon="vaadin:archives" theme="small"></vaadin-icon>
            Mapped Projects
            <span class="count-badge"
              >${this.envContent?.MappedProjects?.length ?? 0}</span
            >
          </div>
        </vaadin-details-summary>
        <div class="projects">
          ${this.envContent?.MappedProjects?.map(
            proj =>
              html`<project-card
                .project="${proj}"
                .catalogHref=${`/module-catalog?project=${encodeURIComponent(proj.ProjectName ?? '')}&environment=${encodeURIComponent(this.environment?.EnvironmentName ?? this.environmentName)}`}
              ></project-card>`
          )}
        </div>
      </vaadin-details>
    `;
  }

  constructor() {
    super();

    super.loadEnvironmentInfo();
  }
}
