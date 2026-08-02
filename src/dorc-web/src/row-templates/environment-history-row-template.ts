import { html } from 'lit';
import {
  ListRowTemplate,
  renderListDetails
} from '../components/dorc-list-row';
import type { EnvironmentHistoryApiModelExtended } from '../components/model-extensions/environment-history-api-model-extended';
import '../components/grid-button-groups/edit-comments-controls';

/**
 * Row template for environment-history entries. No status concept → chip
 * slot unused (per the §3.4 optionality rule). edit-comments-controls reads
 * only model.item, so a minimal { item } wrapper adapts it to the list row
 * (U-7 residual, resolved by inspection).
 */
export const environmentHistoryRowTemplate =
  (): ListRowTemplate<EnvironmentHistoryApiModelExtended> => ({
    primary: item => html`${item.EnvName} — ${item.UpdateType}`,
    metadata: item => html`${item.UpdateDate} · ${item.UpdatedBy}`,
    actions: item => html`
      <edit-comments-controls .model="${{ item }}"></edit-comments-controls>
    `,
    details: item =>
      renderListDetails([
        { label: 'Old version', value: item.FromValue ?? '—' },
        { label: 'New version', value: item.ToValue ?? '—' },
        { label: 'Details', value: item.Details ?? '—' },
        { label: 'Comment', value: item.Comment ?? '—' }
      ])
  });
