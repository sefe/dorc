import { expect, fixture, html } from '../_helpers';
import { of } from 'rxjs';

// tags-input drives @yaireo/tagify via window.Tagify, which the app loads globally but
// the test browser does not. A minimal fake keeps the chip API (value/addTags/
// removeAllTags) honest for these tests.
class FakeTagify {
  value: { value: string }[] = [];
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  constructor(_input: HTMLInputElement, _opts: unknown) {}
  addTags(tags: string[]) {
    this.value.push(...tags.map(t => ({ value: t })));
  }
  removeAllTags() {
    this.value = [];
  }
}
(window as any).Tagify = FakeTagify;
import '../../src/components/server-tags.js';
import type { ServerTags } from '../../src/components/server-tags.js';
import { RefDataServersApi } from '../../src/apis/dorc-api';
import { MAX_TAG_STRING_LENGTH } from '../../src/helpers/tag-limits';

// docs/tag-capacity-expansion IS S-004 (as rescoped to server tags only): the joined
// semicolon string is validated client-side so the UI never submits what the API will
// 400, and the rejection is visible.

describe('server-tags joined-string enforcement', () => {
  it('rejects an over-limit joined string without calling the API', async () => {
    const original = RefDataServersApi.prototype.refDataServersPut;
    let called = 0;
    (RefDataServersApi.prototype as any).refDataServersPut = () => {
      called += 1;
      return of({});
    };
    try {
      const el = await fixture<ServerTags>(html`<server-tags></server-tags>`);
      const oversized = Array.from({ length: 200 }, (_, i) => `tag-${i}-${'x'.repeat(20)}`);
      el.setTags({ ServerId: 1, Name: 's', ApplicationTags: oversized.join(';') });
      await el.updateComplete;

      el.save();
      expect(called).to.equal(0);
      // The rejection is visible: a notification card naming the limit appears.
      await new Promise(r => setTimeout(r, 50));
      const card = document.querySelector('vaadin-notification-card');
      expect(card?.textContent).to.contain('4000');
      document.querySelectorAll('vaadin-notification-card').forEach(c => c.remove());
    } finally {
      (RefDataServersApi.prototype as any).refDataServersPut = original;
    }
  });

  it('saves an exactly-at-limit joined string through the API', async () => {
    const original = RefDataServersApi.prototype.refDataServersPut;
    const payloads: any[] = [];
    (RefDataServersApi.prototype as any).refDataServersPut = (req: any) => {
      payloads.push(req);
      return of({});
    };
    try {
      const el = await fixture<ServerTags>(html`<server-tags></server-tags>`);
      el.setTags({ ServerId: 1, Name: 's', ApplicationTags: 'x'.repeat(MAX_TAG_STRING_LENGTH) });
      await el.updateComplete;

      el.save();
      expect(payloads.length).to.equal(1);
      expect(payloads[0].serverApiModel.ApplicationTags.length).to.equal(MAX_TAG_STRING_LENGTH);
    } finally {
      (RefDataServersApi.prototype as any).refDataServersPut = original;
    }
  });
});
