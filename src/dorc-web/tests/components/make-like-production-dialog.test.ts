import { Observable, of } from 'rxjs';
import { vi } from 'vitest';
import { expect, fixture, html } from '../_helpers';
import {
  PropertiesApi,
  PropertyApiModel
} from '../../src/apis/dorc-api/index.js';
import '../../src/components/make-like-production-dialog.js';
import '../../src/components/make-like-production.js';
import type { MakeLikeProductionDialog } from '../../src/components/make-like-production-dialog.js';
import type { MakeLikeProduction } from '../../src/components/make-like-production.js';

describe('MakeLikeProductionDialog reset', () => {
  beforeEach(() => {
    const propertiesApi = PropertiesApi.prototype as unknown as {
      propertiesGet: () => Observable<PropertyApiModel[]>;
    };
    vi.spyOn(propertiesApi, 'propertiesGet').mockReturnValue(of([]));
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('clears queued request state when closed', async () => {
    const el = await fixture<MakeLikeProductionDialog>(
      html`<make-like-production-dialog></make-like-production-dialog>`
    );
    el.Open();
    await el.updateComplete;
    const openedResetVersion = (el as unknown as { formResetVersion: number })
      .formResetVersion;

    el.bundleChanged('Bundle A');
    el.backupChanged('Backup A');
    el.propertyAdded({
      PropertyName: 'Property A',
      PropertyValue: 'Value A'
    });

    expect((el as unknown as { canSubmit: boolean }).canSubmit).to.equal(true);

    el.closeDialog();

    expect((el as unknown as { canSubmit: boolean }).canSubmit).to.equal(false);
    expect(el.propertyOverrides).to.deep.equal([]);
    expect(
      (el as unknown as { formResetVersion: number }).formResetVersion
    ).to.equal(openedResetVersion + 1);
  });

  it('clears the visible form values', async () => {
    const el = await fixture<MakeLikeProduction>(
      html`<make-like-production></make-like-production>`
    );
    const state = el as unknown as {
      selectedBundleName: string;
      selectedDataBackup: string;
      propertyName: string;
      propertyValue: string;
    };
    state.selectedBundleName = 'Bundle A';
    state.selectedDataBackup = 'Backup A';
    state.propertyName = 'Property A';
    state.propertyValue = 'Value A';
    el.propertyOverrides = [
      { PropertyName: 'Property A', PropertyValue: 'Value A' }
    ];
    await el.updateComplete;

    el.resetVersion = 1;
    await el.updateComplete;

    const comboBoxes = el.shadowRoot!.querySelectorAll('vaadin-combo-box');
    const propertyValue = el.shadowRoot!.querySelector('vaadin-text-field');
    expect(Array.from(comboBoxes).every(combo => combo.value === '')).to.equal(
      true
    );
    expect(propertyValue!.value).to.equal('');
    expect(el.propertyOverrides).to.deep.equal([]);
  });

  it('resets when the dialog is dismissed externally', async () => {
    const el = await fixture<MakeLikeProductionDialog>(
      html`<make-like-production-dialog></make-like-production-dialog>`
    );
    el.Open();
    await el.updateComplete;
    el.bundleChanged('Bundle A');
    el.backupChanged('Backup A');

    const dialog = el.shadowRoot!.querySelector('vaadin-dialog')!;
    dialog.dispatchEvent(
      new CustomEvent('opened-changed', { detail: { value: false } })
    );
    await el.updateComplete;

    expect((el as unknown as { canSubmit: boolean }).canSubmit).to.equal(false);
  });
});
