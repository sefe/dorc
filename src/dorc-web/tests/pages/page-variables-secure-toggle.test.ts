import { describe, it, expect, vi, beforeEach } from 'vitest';

// The Secure checkbox on the variables page has no `.checked` binding — the
// tick is whatever the DOM holds — so cancelling the confirmation has to put it
// back by hand. `updatePropertySecure` became async when window.confirm() was
// replaced, and two things stopped working at once: preventDefault() after an
// await is a no-op, and `event.target` is the light-DOM <input> inside
// vaadin-checkbox, not the host. Writing `.checked` to the input leaves the
// visible tick alone, so cancelling told an admin a property's values were
// encrypted when they were still plaintext.
//
// variables-secure-toggle.test.ts pins that mechanism on a bare checkbox. This
// drives the shipped handler on the shipped page: reverting `currentTarget` to
// `target` in page-variables.ts fails both tests here.

const { confirmCalls, confirmAnswer } = vi.hoisted(() => ({
  confirmCalls: [] as string[],
  confirmAnswer: { value: false }
}));

vi.mock('../../src/components/confirm-prompt', () => ({
  confirmPrompt: (message: string) => {
    confirmCalls.push(message);
    return Promise.resolve(confirmAnswer.value);
  }
}));

await import('../../src/pages/page-variables');

type Page = HTMLElement & {
  properties?: { Name?: string; Secure?: boolean }[];
  isAdmin: boolean;
  existingPropertySelected: boolean;
};

const settle = async () => {
  for (let i = 0; i < 4; i++) {
    await new Promise(r => requestAnimationFrame(() => r(null)));
  }
};

const mount = async (secure: boolean) => {
  const page = document.createElement('page-variables') as Page;
  page.isAdmin = true;
  document.body.appendChild(page);
  await settle();
  // After the first render, not before: the property combo fires `value-changed`
  // with an empty value on its way up, which clears `propertyName` and
  // `existingPropertySelected` again — leaving the checkbox disabled and the
  // handler's `existingProperty` lookup empty, so nothing would be asked.
  page.existingPropertySelected = true;
  page.properties = [{ Name: 'Var1', Secure: secure }];
  (page as unknown as { propertyName: string }).propertyName = 'Var1';
  await settle();
  const checkbox = page.shadowRoot?.querySelector(
    '#is-variable-secure'
  ) as HTMLElement & { checked: boolean };
  // The checkbox carries no `.checked` binding, so seed it to match the model
  // the way the page's own load path leaves it.
  checkbox.checked = secure;
  await settle();
  return { page, checkbox };
};

describe('page-variables Secure toggle', () => {
  beforeEach(() => {
    confirmCalls.length = 0;
    confirmAnswer.value = false;
  });

  it('clears the tick again when securing is cancelled', async () => {
    const { page, checkbox } = await mount(false);

    (checkbox.querySelector('input') as HTMLInputElement).click();
    await settle();

    // The click has to have reached the handler, or the assertions below hold
    // for the wrong reason.
    expect(confirmCalls.length, 'the handler asked').toBe(1);
    expect(confirmCalls[0]).toContain('mark property "Var1" as secure');

    expect(checkbox.checked, 'host tick reverted on cancel').toBe(false);
    expect(checkbox.hasAttribute('checked'), 'host reflects it').toBe(false);
    expect(page.properties?.[0].Secure, 'model untouched').toBe(false);
    page.remove();
  });

  it('restores the tick again when unsecuring is cancelled', async () => {
    // The mirror image: cancelling must not leave a secured property looking
    // plaintext.
    const { page, checkbox } = await mount(true);

    (checkbox.querySelector('input') as HTMLInputElement).click();
    await settle();

    expect(confirmCalls.length, 'the handler asked').toBe(1);
    expect(confirmCalls[0]).toContain('mark property "Var1" as non-secure');

    expect(checkbox.checked, 'host tick restored on cancel').toBe(true);
    expect(checkbox.hasAttribute('checked'), 'host reflects it').toBe(true);
    expect(page.properties?.[0].Secure, 'model untouched').toBe(true);
    page.remove();
  });
});
