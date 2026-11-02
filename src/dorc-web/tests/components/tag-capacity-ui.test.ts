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
import { MAX_TAG_LENGTH } from '../../src/helpers/tag-limits';

// Server tags: each tag is validated client-side against the per-tag limit the API
// and the Tag column both enforce, so the UI never submits what would 400, and the
// rejection is visible. Tags are a set — there is no joined-length ceiling.

describe('server-tags per-tag enforcement', () => {
  it('rejects an over-long tag without calling the API', async () => {
    const original = RefDataServersApi.prototype.refDataServersPut;
    let called = 0;
    (RefDataServersApi.prototype as any).refDataServersPut = () => {
      called += 1;
      return of({});
    };
    try {
      const el = await fixture<ServerTags>(html`<server-tags></server-tags>`);
      el.setTags({ ServerId: 1, Name: 's', Tags: ['ok', 'x'.repeat(MAX_TAG_LENGTH + 1)] });
      await el.updateComplete;

      el.save();
      expect(called).to.equal(0);
      // The rejection is visible: a notification card naming the limit appears.
      await new Promise(r => setTimeout(r, 50));
      const card = document.querySelector('vaadin-notification-card');
      expect(card?.textContent).to.contain(String(MAX_TAG_LENGTH));
      document.querySelectorAll('vaadin-notification-card').forEach(c => c.remove());
    } finally {
      (RefDataServersApi.prototype as any).refDataServersPut = original;
    }
  });

  it('saves an exactly-at-limit tag, and many of them, through the API', async () => {
    const original = RefDataServersApi.prototype.refDataServersPut;
    const payloads: any[] = [];
    (RefDataServersApi.prototype as any).refDataServersPut = (req: any) => {
      payloads.push(req);
      return of({});
    };
    try {
      const el = await fixture<ServerTags>(html`<server-tags></server-tags>`);
      // 200 tags: past what the delimited column could have held once joined.
      const many = Array.from({ length: 200 }, (_, i) => `tag-${i}-${'x'.repeat(20)}`);
      el.setTags({ ServerId: 1, Name: 's', Tags: [...many, 'x'.repeat(MAX_TAG_LENGTH)] });
      await el.updateComplete;

      el.save();
      expect(payloads.length).to.equal(1);
      expect(payloads[0].serverApiModel.Tags.length).to.equal(201);
      expect(
        payloads[0].serverApiModel.Tags.some((t: string) => t.length === MAX_TAG_LENGTH)
      ).to.equal(true);
    } finally {
      (RefDataServersApi.prototype as any).refDataServersPut = original;
    }
  });
});
