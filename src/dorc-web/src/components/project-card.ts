import { css, LitElement } from 'lit';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../icons/hardware-icons.js';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '../icons/iron-icons.js';
import { ProjectApiModel } from '../apis/dorc-api';

@customElement('project-card')
export class ProjectCard extends LitElement {
  @property({ type: Object }) project: ProjectApiModel | undefined;

  static get styles() {
    return css`
      .card-element {
        padding: 10px;
        box-shadow: 1px 2px 3px rgba(0, 0, 0, 0.2);
        width: 300px;
        min-height: 50px;
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: var(--lumo-space-s);
        box-sizing: border-box;
      }
      @media (max-width: 768px) {
        .card-element {
          width: 100%;
          min-width: 0;
        }
      }
      .card-element__heading {
        color: gray;
      }
      .card-element__text {
        color: gray;
      }
      .card-content {
        flex: 1;
        min-width: 0;
      }

      .statistics-cards {
        max-width: 100%;
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
        <div class="card-content">
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
        <div>
          <vaadin-button
            title="Project Environments for ${this.project?.ProjectName}"
            theme="icon"
            @click="${this.openProjectEnvironments}"
          >
            <vaadin-icon
              icon="vaadin:records"
              style="color: cornflowerblue"
            ></vaadin-icon>
          </vaadin-button>
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
