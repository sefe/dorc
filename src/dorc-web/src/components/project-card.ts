import { css, LitElement } from 'lit';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import { customElement, property } from 'lit/decorators.js';
import './dorc-icon.js';
import { html } from 'lit/html.js';
import { ProjectApiModel } from '../apis/dorc-api';

@customElement('project-card')
export class ProjectCard extends LitElement {
  @property({ type: Object }) project: ProjectApiModel | undefined;

  static get styles() {
    return css`
      .card-element {
        padding: 10px;
        box-shadow: 1px 2px 3px rgba(0, 0, 0, 0.2);
        min-width: 300px;
        height: 50px;
        color: white;
      }
      .card-element__heading {
        color: gray;
      }
      .card-element__text {
        color: gray;
      }

      .statistics-cards {
        max-width: 500px;
        display: flex;
        flex-wrap: wrap;
      }
      .statistics-cards__item {
        margin: 5px;
        flex-shrink: 0;
        background-color: white;
      }
    `;
  }

  render() {
    return html`
      <div class="statistics-cards__item card-element">
        <div style="float: left">
          <h3 class="card-element__heading" style="margin: 0px">
            ${this.project?.ProjectName}
          </h3>
          ${
            this.project?.ProjectDescription === '' ||
            this.project?.ProjectDescription === null ||
            this.project?.ProjectDescription === undefined
              ? html`<span class="card-element__text" style="font-style: italic"
                  >No Description</span
                >`
              : html`<span class="card-element__text"
                  >${this.project?.ProjectDescription}</span
                >`
          }
        </div>
          <div style="float: right; width: 50px">
            <vaadin-button
              title="Project Environments for ${this.project?.ProjectName}"
              theme="icon"
              @click="${this.openProjectEnvironments}"
            >
              <dorc-icon icon="list" color="primary"></dorc-icon>
            </vaadin-button>
          </div>
        </div>
      </div>
    `;
  }

  openProjectEnvironments() {
    const event = new CustomEvent('open-project-envs', {
      detail: {
        Project: this.project
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
  }
}
