import { html } from 'lit';
import type { TemplateResult } from 'lit';
import type { DeploymentRequestApiModel } from '../apis/dorc-api';
import {
  ListRowTemplate,
  renderListDetails
} from '../components/dorc-list-row';
import '../components/grid-button-groups/request-controls';

/**
 * Row template for the deployment-request entity (HLPS §3.4; U-3 worked
 * example). Used by page-monitor-requests and env-monitor — one entity, one
 * declaration; each view imports it by name so SC3 resolves per view.
 *
 * Priority ranking (U-3, blanket approval 2026-08-02): an on-call engineer
 * triaging at 375px needs — identity (id + project + environment), status,
 * then build/user/time; everything else via disclosure.
 */

const fmtDateTime = (iso?: string | null): string => {
  if (!iso) return '—';
  const d = new Date(iso);
  return `${d.toLocaleDateString('en-GB', {
    day: '2-digit',
    month: '2-digit'
  })} ${d.toLocaleTimeString('en-GB')}`;
};

const chipKind = (
  status?: string | null
): 'success' | 'failure' | 'neutral' => {
  if (status === 'Complete') return 'success';
  if (status === 'Failed' || status === 'Errored') return 'failure';
  return 'neutral';
};

const actionsFor = (item: DeploymentRequestApiModel): TemplateResult => html`
  <request-controls
    .requestId=${item.Id ?? 0}
    .cancelable=${!!item.UserEditable &&
    (item.Status === 'Running' ||
      item.Status === 'Requesting' ||
      item.Status === 'Pending' ||
      item.Status === 'Restarting' ||
      item.Status === 'Paused')}
    .canRestart=${!!item.UserEditable &&
    item.Status !== 'Pending' &&
    item.Status !== 'Paused'}
    .canPause=${!!item.UserEditable && item.Status === 'Pending'}
    .canResume=${!!item.UserEditable && item.Status === 'Paused'}
  ></request-controls>
`;

export const deploymentRequestRowTemplate = (
  host: EventTarget
): ListRowTemplate<DeploymentRequestApiModel> => ({
  primary: item => html`
    <button
      type="button"
      class="id-btn"
      @click="${(e: Event) => {
        e.stopPropagation();
        host.dispatchEvent(
          new CustomEvent('open-monitor-result', {
            detail: { request: item, message: 'Show results for Request' },
            bubbles: true,
            composed: true
          })
        );
      }}"
    >
      ${item.Id}
    </button>
    ${item.Project} - ${item.EnvironmentName}
  `,
  metadata: item =>
    html`${item.BuildNumber} · ${item.UserName} ·
    ${fmtDateTime(item.StartedTime)}`,
  chip: item =>
    item.Status ? { label: item.Status, kind: chipKind(item.Status) } : null,
  actions: actionsFor,
  details: item =>
    renderListDetails([
      { label: 'Build', value: item.BuildNumber ?? '—' },
      { label: 'Requested', value: fmtDateTime(item.RequestedTime) },
      { label: 'Started', value: fmtDateTime(item.StartedTime) },
      { label: 'Completed', value: fmtDateTime(item.CompletedTime) },
      { label: 'User', value: item.UserName ?? '—' },
      { label: 'Components', value: item.Components ?? '—' }
    ])
});
