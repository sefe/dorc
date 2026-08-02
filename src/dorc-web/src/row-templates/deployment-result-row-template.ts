import { html } from 'lit';
import type { DeploymentResultApiModel } from '../apis/dorc-api';
import {
  ListRowTemplate,
  renderListDetails
} from '../components/dorc-list-row';

/**
 * Row template for component deployment results (the failure-triage panel on
 * page-monitor-result). Priority (U-3): component identity + status, then
 * timings; the log excerpt and terraform action via disclosure/actions.
 */

const fmtTime = (iso?: string | null): string =>
  iso ? new Date(iso).toLocaleTimeString('en-GB') : '—';

const chipKind = (
  status?: string | null
): 'success' | 'failure' | 'neutral' => {
  if (status === 'Complete') return 'success';
  if (status === 'Failed' || status === 'Cancelled') return 'failure';
  return 'neutral';
};

export const deploymentResultRowTemplate = (host: {
  dispatchEvent: (e: Event) => boolean;
  openTerraformPlan: (id: number) => void;
}): ListRowTemplate<DeploymentResultApiModel> => ({
  primary: item =>
    html`<a
      href="scripts?search-name=${item.ComponentName}"
      target="_blank"
      @click="${(e: Event) => e.stopPropagation()}"
      >${item.ComponentName}</a
    >`,
  metadata: item =>
    html`${fmtTime(item.StartedTime)} → ${fmtTime(item.CompletedTime)}`,
  chip: item =>
    item.Status ? { label: item.Status, kind: chipKind(item.Status) } : null,
  actions: item => html`
    <vaadin-button
      theme="small"
      title="View log"
      style="min-width: 36px; padding: 0"
      @click="${(e: Event) => {
        e.stopPropagation();
        host.dispatchEvent(
          new CustomEvent('open-result-log', {
            detail: { result: item },
            bubbles: true,
            composed: true
          })
        );
      }}"
    >
      <vaadin-icon
        icon="vaadin:file-text-o"
        style="color: var(--dorc-link-color)"
      ></vaadin-icon>
    </vaadin-button>
    ${item.Status === 'WaitingConfirmation' || item.Status === 'Confirmed'
      ? html`<vaadin-button
          theme="small"
          title="View Terraform Plan"
          style="min-width: 36px; padding: 0"
          @click="${(e: Event) => {
            e.stopPropagation();
            host.openTerraformPlan(item.Id!);
          }}"
        >
          <vaadin-icon icon="vaadin:file-text"></vaadin-icon>
        </vaadin-button>`
      : html``}
  `,
  details: item =>
    renderListDetails([
      { label: 'Started', value: fmtTime(item.StartedTime) },
      { label: 'Completed', value: fmtTime(item.CompletedTime) },
      {
        label: 'Log',
        value: html`<span style="font-family: monospace"
          >${item.Log?.substring(0, 200) ?? '—'}</span
        >`
      }
    ])
});
