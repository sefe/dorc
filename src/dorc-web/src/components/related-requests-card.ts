import '@vaadin/button';
import '@vaadin/details';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/grid/vaadin-grid-column';
import '@vaadin/horizontal-layout';
import '@vaadin/vertical-layout';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';

import { css, LitElement } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { GridItemModel } from '@vaadin/grid';
import { render } from 'lit';
import './component-deployment-results';
import './grid-button-groups/request-controls';
import type { DeploymentRequestApiModel, DeploymentResultApiModel } from '../apis/dorc-api';

@customElement('related-requests-card')
export class RelatedRequestsCard extends LitElement {
  @property({ type: Array })
  relatedRequests: DeploymentRequestApiModel[] = [];

  @property({ type: Map })
  relatedRequestsResults: Map<number, DeploymentResultApiModel[]> = new Map();

  @property({ type: Boolean })
  loading = false;

  @property({ type: Number })
  currentRequestId = 0;

  static get styles() {
    return css`
      .small-loader {
        border: 2px solid #f3f3f3;
        border-top: 2px solid #3498db;
        border-radius: 50%;
        width: 12px;
        height: 12px;
        animation: spin 2s linear infinite;
      }

      @keyframes spin {
        0% {
          transform: rotate(0deg);
        }
        100% {
          transform: rotate(360deg);
        }
      }

      vaadin-grid {
        margin-bottom: 12px;
      }

      .results-section {
        border-top: 2px solid #e0e0e0;
        margin-top: 12px;
      }
    `;
  }

  private formatDateTime(dateVal: string | undefined | null): { date: string; time: string } {
    if (dateVal === undefined || dateVal === null) {
      return { date: '', time: '' };
    }
    const date = new Date(dateVal);
    const time = date.toLocaleTimeString('en-GB');
    const dateStr = date.toLocaleDateString('en-GB', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric'
    });
    return { date: dateStr, time };
  }

  private getAttemptNumber(requestId: number): number {
    const allRequestIds = [
      ...this.relatedRequests.map(r => r.Id ?? 0),
      this.currentRequestId
    ].filter(id => id > 0);

    allRequestIds.sort((a, b) => a - b);

    return allRequestIds.indexOf(requestId) + 1;
  }

  private idRenderer = (
    root: HTMLElement,
    _: HTMLElement,
    model: GridItemModel<DeploymentRequestApiModel>
  ) => {
    const request = model.item;
    const attemptNumber = this.getAttemptNumber(request.Id ?? 0);
    render(
      html`
        <vaadin-horizontal-layout style="align-items: center;" theme="spacing">
          <span style="font-size: var(--lumo-font-size-s); color: var(--lumo-secondary-text-color);">
            Attempt ${attemptNumber} - ${request.Id}
          </span>
          <vaadin-button
            title="View Detailed Results for Attempt ${attemptNumber}"
            theme="icon small"
            @click="${() => {
              window.location.href = `/monitor-result/${request.Id}`;
            }}"
          >
            <vaadin-icon
              icon="vaadin:ellipsis-dots-h"
              style="color: cornflowerblue"
            ></vaadin-icon>
          </vaadin-button>
        </vaadin-horizontal-layout>
      `,
      root
    );
  };

  private detailsRenderer = (
    root: HTMLElement,
    _: HTMLElement,
    model: GridItemModel<DeploymentRequestApiModel>
  ) => {
    const request = model.item;
    render(
      html`
        <vaadin-vertical-layout>
          <div style="font-size: var(--lumo-font-size-m);">${request.Project} - ${request.EnvironmentName}</div>
          <div style="font-size: var(--lumo-font-size-s); color: var(--lumo-secondary-text-color);">
            ${request.BuildNumber}
          </div>
        </vaadin-vertical-layout>
      `,
      root
    );
  };

  private timingsRenderer = (
    root: HTMLElement,
    _: HTMLElement,
    model: GridItemModel<DeploymentRequestApiModel>
  ) => {
    const request = model.item;
    const startedDateTime = this.formatDateTime(request.StartedTime);
    const completedDateTime = this.formatDateTime(request.CompletedTime);

    render(
      html`
        <vaadin-vertical-layout style="line-height: var(--lumo-line-height-s);">
          <div style="font-size: var(--lumo-font-size-s); color: var(--lumo-secondary-text-color);">
            ${startedDateTime.date} ${startedDateTime.time}
          </div>
          <div style="font-size: var(--lumo-font-size-s); color: var(--lumo-secondary-text-color);">
            ${completedDateTime.date} ${completedDateTime.time}
          </div>
        </vaadin-vertical-layout>
      `,
      root
    );
  };

  private usernameRenderer = (
    root: HTMLElement,
    _: HTMLElement,
    model: GridItemModel<DeploymentRequestApiModel>
  ) => {
    const request = model.item;
    render(
      html`
        <div style="font-size: var(--lumo-font-size-s); color: var(--lumo-secondary-text-color);">
          ${request.UserName}
        </div>
      `,
      root
    );
  };

  private statusRenderer = (
    root: HTMLElement,
    _: HTMLElement,
    model: GridItemModel<DeploymentRequestApiModel>
  ) => {
    const request = model.item;
    render(
      html`
        <div style="font-size: var(--lumo-font-size-s); color: var(--lumo-secondary-text-color);">
          ${request.Status}
        </div>
      `,
      root
    );
  };

  private controlsRenderer = (
    root: HTMLElement,
    _: HTMLElement,
    model: GridItemModel<DeploymentRequestApiModel>
  ) => {
    const request = model.item;
    render(
      html`
        <request-controls
          .requestId="${request.Id ?? 0}"
          .cancelable="${!!request.UserEditable &&
          (request.Status === 'Running' ||
            request.Status === 'Requesting' ||
            request.Status === 'Pending' ||
            request.Status === 'Restarting')}"
          .canRedeploy="${!!request.UserEditable && request.Status !== 'Pending'}"
        ></request-controls>
      `,
      root
    );
  };

  private componentsRenderer = (
    root: HTMLElement,
    _: HTMLElement,
    model: GridItemModel<DeploymentRequestApiModel>
  ) => {
    const request = model.item;
    const elements = request.Components?.split('|');

    render(
      html`
        <vaadin-vertical-layout>
          ${elements?.map(
            element =>
              html`<div style="font-size: var(--lumo-font-size-s); color: var(--lumo-secondary-text-color);">
                ${element}
              </div>`
          )}
        </vaadin-vertical-layout>
      `,
      root
    );
  };

  render() {
    if (this.relatedRequests.length === 0) {
      return html``;
    }

    const currentAttemptNumber = this.getAttemptNumber(this.currentRequestId);
    const totalAttempts = this.relatedRequests.length + 1;

    return html`
      <vaadin-details
        summary="Viewing Request - ${this.currentRequestId} (Attempt ${currentAttemptNumber} of ${totalAttempts})"
        style="border-top: 6px solid #ff9800; background-color: ghostwhite; padding: 8px; margin-top: 8px"
      >
        ${this.loading
          ? html`<div class="small-loader"></div>`
          : html`
            <vaadin-grid
              .items="${this.relatedRequests}"
              theme="compact row-stripes no-row-borders no-border"
              all-rows-visible
            >
              <vaadin-grid-column
                header="ID"
                .renderer="${this.idRenderer}"
                auto-width
                flex-grow="0"
              ></vaadin-grid-column>
              <vaadin-grid-column
                header="Details"
                .renderer="${this.detailsRenderer}"
                auto-width
              ></vaadin-grid-column>
              <vaadin-grid-column
                header="Timings"
                .renderer="${this.timingsRenderer}"
                auto-width
              ></vaadin-grid-column>
              <vaadin-grid-column
                header="User"
                .renderer="${this.usernameRenderer}"
                auto-width
              ></vaadin-grid-column>
              <vaadin-grid-column
                header="Status"
                .renderer="${this.statusRenderer}"
                auto-width
              ></vaadin-grid-column>
              <vaadin-grid-column
                .renderer="${this.controlsRenderer}"
                width="100px"
              ></vaadin-grid-column>
              <vaadin-grid-column
                header="Components"
                .renderer="${this.componentsRenderer}"
                auto-width
              ></vaadin-grid-column>
            </vaadin-grid>

            ${this.relatedRequests.map(request => {
  const results = this.relatedRequestsResults.get(request.Id!) || [];
  const attemptNumber = this.getAttemptNumber(request.Id ?? 0);
  
  const isLoading = !this.relatedRequestsResults.has(request.Id!) && 
                   ['Cancelled', 'Failed', 'Complete', 'Warning'].includes(request.Status || '');
  
  const hasResults = results.length > 0;
  const isTerminalState = ['Cancelled', 'Failed', 'Complete', 'Warning'].includes(request.Status || '');
  const shouldShowSection = hasResults || isTerminalState;

  return shouldShowSection
    ? html`
        <div class="results-section">
          <vaadin-details
            opened
            summary="Deployment Component Results - Attempt ${attemptNumber} - ${request.Id}"
            style="padding: 8px; background-color: #fafafa;"
          >
            ${hasResults
              ? html`
                  <component-deployment-results
                    .resultItems="${results}"
                    .compact="${true}"
                  ></component-deployment-results>
                `
              : isLoading
              ? html`
                  <div style="display: flex; align-items: center; justify-content: center; padding: 16px; gap: 8px;">
                    <div class="small-loader"></div>
                    <span style="color: var(--lumo-secondary-text-color); font-style: italic;">
                      Loading component results...
                    </span>
                  </div>
                `
              : html`
                  <div style="padding: 16px; color: var(--lumo-secondary-text-color); font-style: italic; text-align: center;">
                    ${request.Status === 'Cancelled' 
                      ? 'Request was cancelled before component deployments started.'
                      : request.Status === 'Failed'
                      ? 'Request failed before component results were created.'
                      : 'No deployment results available.'}
                  </div>
                `}
          </vaadin-details>
        </div>
      `
    : html``;
})}
          `}
      </vaadin-details>
    `;
  }
}