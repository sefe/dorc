import { expect, settle } from '../_helpers';
import { render, html } from 'lit';
import '@vaadin/combo-box';

describe('probe combo notify', () => {
  it('fires value-changed when set programmatically', async () => {
    const host = document.createElement('div');
    document.body.appendChild(host);
    const seen: string[] = [];
    render(
      html`<vaadin-combo-box
        .items="${['5.1', '7.4']}"
        .value="${'5.1'}"
        @value-changed="${(e: Event) => {
          seen.push(
            `target=${(e.target as HTMLElement).localName} v=${(e as CustomEvent).detail?.value} cv=${(e.currentTarget as HTMLElement & { value: string }).value}`
          );
        }}"
      ></vaadin-combo-box>`,
      host
    );
    await settle();
    const combo = host.querySelector('vaadin-combo-box') as HTMLElement & {
      value: string;
    };
    seen.length = 0;
    combo.value = '7.4';
    await settle();
    console.log('PROBE seen=' + JSON.stringify(seen));
    host.remove();
    expect(seen.length).to.equal(-1);
  });
});
