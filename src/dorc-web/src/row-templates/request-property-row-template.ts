import { html } from 'lit';
import type { RequestProperty } from '../apis/dorc-api';
import {
  ListRowTemplate,
  renderListDetails
} from '../components/dorc-list-row';
import '../components/deploy/property-override-controls';

/**
 * Row template for property-override entries (make-like-production dialog).
 * Priority (U-3): the property name identifies the row; the value is the
 * payload; remove is the only action.
 */
export const requestPropertyRowTemplate = (host: {
  removeOverride: (p: RequestProperty) => void;
}): ListRowTemplate<RequestProperty> => ({
  primary: item => html`${item.PropertyName}`,
  metadata: item => html`${item.PropertyValue}`,
  actions: item => html`
    <property-override-controls
      .propertyOverride="${item}"
      @property-override-removed="${() => host.removeOverride(item)}"
    ></property-override-controls>
  `,
  details: item =>
    renderListDetails([
      { label: 'Name', value: item.PropertyName ?? '—' },
      { label: 'Value', value: item.PropertyValue ?? '—' }
    ])
});
