import { expect, fixture, html } from '@open-wc/testing';
import './environment-card.js';
import type { EnvironmentCard } from './environment-card.js';

describe('EnvironmentCard responsive layout', () => {
  const mockEnvironment = {
    EnvironmentId: 1,
    EnvironmentName: 'Test Environment',
    Details: {
      Description: 'A test environment description',
      EnvironmentOwner: 'test-owner',
    },
  };

  it('should use flex layout instead of absolute positioning', async () => {
    const el = await fixture<EnvironmentCard>(html`
      <environment-card .environment="${mockEnvironment}"></environment-card>
    `);
    await el.updateComplete;

    const cardElement = el.shadowRoot!.querySelector('.card-element') as HTMLElement;
    const computedStyle = getComputedStyle(cardElement);

    expect(computedStyle.display).to.equal('flex');
    expect(computedStyle.justifyContent).to.equal('space-between');
  });

  it('should use box-sizing border-box for consistent sizing', async () => {
    const el = await fixture<EnvironmentCard>(html`
      <environment-card .environment="${mockEnvironment}"></environment-card>
    `);
    await el.updateComplete;

    const cardElement = el.shadowRoot!.querySelector('.card-element') as HTMLElement;
    const computedStyle = getComputedStyle(cardElement);

    expect(computedStyle.boxSizing).to.equal('border-box');
  });

  it('should have card-content with flex: 1 for flexible width', async () => {
    const el = await fixture<EnvironmentCard>(html`
      <environment-card .environment="${mockEnvironment}"></environment-card>
    `);
    await el.updateComplete;

    const cardContent = el.shadowRoot!.querySelector('.card-content') as HTMLElement;
    expect(cardContent).to.not.be.null;

    const computedStyle = getComputedStyle(cardContent);
    expect(computedStyle.flexGrow).to.equal('1');
    expect(computedStyle.minWidth).to.equal('0px');
  });

  it('should have card-actions using flex column layout', async () => {
    const el = await fixture<EnvironmentCard>(html`
      <environment-card .environment="${mockEnvironment}"></environment-card>
    `);
    await el.updateComplete;

    const cardActions = el.shadowRoot!.querySelector('.card-actions') as HTMLElement;
    expect(cardActions).to.not.be.null;

    const computedStyle = getComputedStyle(cardActions);
    expect(computedStyle.display).to.equal('flex');
    expect(computedStyle.flexDirection).to.equal('column');
  });

  it('should NOT use position: absolute anywhere in the card', async () => {
    const el = await fixture<EnvironmentCard>(html`
      <environment-card .environment="${mockEnvironment}"></environment-card>
    `);
    await el.updateComplete;

    const allDivs = el.shadowRoot!.querySelectorAll('div');
    allDivs.forEach(div => {
      const style = getComputedStyle(div);
      expect(style.position).to.not.equal('absolute',
        `Found position: absolute on element with class="${div.className}"`);
    });
  });

  it('should render environment name and details', async () => {
    const el = await fixture<EnvironmentCard>(html`
      <environment-card .environment="${mockEnvironment}"></environment-card>
    `);
    await el.updateComplete;

    const heading = el.shadowRoot!.querySelector('h3');
    expect(heading).to.not.be.null;
    expect(heading!.textContent!.trim()).to.equal('Test Environment');
  });

  it('should render action buttons', async () => {
    const el = await fixture<EnvironmentCard>(html`
      <environment-card .environment="${mockEnvironment}"></environment-card>
    `);
    await el.updateComplete;

    const buttons = el.shadowRoot!.querySelectorAll('vaadin-button');
    expect(buttons.length).to.equal(4); // Details, History, Detach, Access Control
  });
});
