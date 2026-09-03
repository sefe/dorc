import { of } from 'rxjs';
import { vi } from 'vitest';
import { dialogIn, expect, fixture, html, inDialog, settle } from '../_helpers';

const {
  confirmPromptSpy,
  requestRestartSpy,
  requestCancelSpy,
  ajaxSpy,
  aceEditSpy,
  aceDestroySpy,
  aceSetValueSpy,
  aceSetOptionsSpy
} = vi.hoisted(() => {
  const aceDestroySpy = vi.fn();
  const aceSetValueSpy = vi.fn();
  const aceSetOptionsSpy = vi.fn();
  return {
    confirmPromptSpy: vi.fn(),
    requestRestartSpy: vi.fn(() => of({})),
    requestCancelSpy: vi.fn(() => of({})),
    ajaxSpy: vi.fn(() => of({})),
    aceEditSpy: vi.fn(() => ({
      destroy: aceDestroySpy,
      renderer: { attachToShadowRoot: vi.fn() },
      setTheme: vi.fn(),
      session: { setMode: vi.fn() },
      getSession: () => ({ setUseWorker: vi.fn(), setAnnotations: vi.fn() }),
      setReadOnly: vi.fn(),
      setHighlightActiveLine: vi.fn(),
      setOptions: aceSetOptionsSpy,
      setValue: aceSetValueSpy,
      gotoLine: vi.fn(),
      clearSelection: vi.fn(),
      getValue: () => ''
    })),
    aceDestroySpy,
    aceSetValueSpy,
    aceSetOptionsSpy
  };
});

vi.mock('../../src/components/confirm-prompt', () => ({
  confirmPrompt: confirmPromptSpy
}));

vi.mock('../../src/apis/dorc-api', async importOriginal => {
  const actual = (await importOriginal()) as Record<string, unknown>;
  return {
    ...actual,
    RequestApi: class {
      requestRestartPost = requestRestartSpy;
      requestCancelPut = requestCancelSpy;
    }
  };
});

vi.mock('ace-builds', () => ({ edit: aceEditSpy }));
vi.mock('rxjs/ajax', () => ({ ajax: ajaxSpy }));

await import('../../src/components/grid-button-groups/request-controls');
await import('../../src/components/log-dialog');

type RequestControl = HTMLElement & {
  requestId: number;
  restart(): Promise<void>;
  cancel(): Promise<void>;
  pause(): Promise<void>;
  resume(): Promise<void>;
};

describe('request control row snapshots', () => {
  beforeEach(() => {
    confirmPromptSpy.mockReset();
    requestRestartSpy.mockClear();
    requestCancelSpy.mockClear();
    ajaxSpy.mockClear();
  });

  const confirmNextPrompt = () => {
    let resolve!: (value: boolean) => void;
    confirmPromptSpy.mockImplementationOnce(
      () =>
        new Promise<boolean>(response => {
          resolve = response;
        })
    );
    return () => resolve(true);
  };

  it('wires the visible cancel and restart controls to confirmation', async () => {
    confirmPromptSpy.mockResolvedValue(false);
    const el = await fixture<RequestControl>(
      html`<request-controls
        .requestId="${41}"
        .cancelable="${true}"
        .canRestart="${true}"
      ></request-controls>`
    );

    (
      el.shadowRoot?.querySelector(
        'vaadin-button[aria-label="Cancel Request"]'
      ) as HTMLElement
    ).click();
    (
      el.shadowRoot?.querySelector(
        'vaadin-button[aria-label="Restart Request"]'
      ) as HTMLElement
    ).click();
    await settle();

    expect(confirmPromptSpy.mock.calls).to.have.length(2);
    expect(confirmPromptSpy.mock.calls[0][0]).to.contain('cancel');
    expect(confirmPromptSpy.mock.calls[1][0]).to.contain('restart');
    expect(confirmPromptSpy.mock.calls[0][0]).to.not.contain(' ?');
    expect(confirmPromptSpy.mock.calls[1][0]).to.not.contain(' ?');

    for (const button of el.shadowRoot?.querySelectorAll(
      'vaadin-button[theme~="icon"]'
    ) ?? []) {
      expect(button.hasAttribute('title')).to.equal(false);
      expect(
        button.querySelector('vaadin-tooltip')?.getAttribute('text')
      ).to.equal(button.getAttribute('aria-label'));
    }
  });

  it('restarts the row that was confirmed, not the row subsequently recycled into the cell', async () => {
    const el = await fixture<RequestControl>(
      html`<request-controls .requestId="${41}"></request-controls>`
    );
    const accept = confirmNextPrompt();
    let detail: unknown;
    el.addEventListener('request-restarted', event => {
      detail = (event as CustomEvent).detail;
    });

    const restart = el.restart();
    el.requestId = 99;
    accept();
    await restart;

    expect((requestRestartSpy.mock.calls as unknown[][])[0][0]).to.deep.equal({
      requestId: 41
    });
    expect(detail).to.deep.equal({
      requestId: 41,
      message: 'Requested deploy has been restarted'
    });
  });

  it('cancels the originally selected request after an asynchronous confirmation', async () => {
    const el = await fixture<RequestControl>(
      html`<request-controls .requestId="${41}"></request-controls>`
    );
    const accept = confirmNextPrompt();
    let detail: unknown;
    el.addEventListener('request-cancelled', event => {
      detail = (event as CustomEvent).detail;
    });

    const cancel = el.cancel();
    el.requestId = 99;
    accept();
    await cancel;

    expect((requestCancelSpy.mock.calls as unknown[][])[0][0]).to.deep.equal({
      requestId: 41
    });
    expect(detail).to.deep.equal({
      requestId: 41,
      message: 'Requested deploy has been canceled'
    });
  });

  for (const action of ['pause', 'resume'] as const) {
    it(`${action}s the originally selected request after an asynchronous confirmation`, async () => {
      const el = await fixture<RequestControl>(
        html`<request-controls .requestId="${41}"></request-controls>`
      );
      const accept = confirmNextPrompt();
      let detail: unknown;
      el.addEventListener(`request-${action}d`, event => {
        detail = (event as CustomEvent).detail;
      });

      const pending = el[action]();
      el.requestId = 99;
      accept();
      await pending;

      const request = (ajaxSpy.mock.calls as unknown[][])[0][0] as {
        url: string;
      };
      expect(request.url).to.contain(`/Request/${action}?requestId=41`);
      expect(detail).to.deep.equal({
        requestId: 41,
        message: `Requested deploy has been ${action}d`
      });
    });
  }

  it('performs no request when confirmation is declined', async () => {
    confirmPromptSpy.mockResolvedValue(false);
    const el = await fixture<RequestControl>(
      html`<request-controls .requestId="${41}"></request-controls>`
    );

    await el.restart();
    await el.cancel();
    await el.pause();
    await el.resume();

    expect(requestRestartSpy.mock.calls).to.have.length(0);
    expect(requestCancelSpy.mock.calls).to.have.length(0);
    expect(ajaxSpy.mock.calls).to.have.length(0);
  });
});

describe('deployment log editor lifecycle', () => {
  beforeEach(() => {
    aceEditSpy.mockClear();
    aceDestroySpy.mockClear();
    aceSetValueSpy.mockClear();
  });

  it('creates one editor per viewer element, updates its log, and disposes it when loading replaces it', async () => {
    const el = await fixture<HTMLElement>(
      html`<log-dialog
        .isOpened="${true}"
        .selectedLog="${'first log'}"
      ></log-dialog>`
    );
    await settle();

    expect(
      aceEditSpy.mock.calls,
      'the initial viewer creates one Ace editor'
    ).to.have.length(1);
    expect(
      aceSetValueSpy.mock.calls.some(([value]) => value === 'first log')
    ).to.equal(true);
    const editorOptions = aceSetOptionsSpy.mock.calls[0][0] as {
      hScrollBarAlwaysVisible?: boolean;
      wrap?: boolean;
    };
    expect(editorOptions.hScrollBarAlwaysVisible).to.equal(true);
    expect(editorOptions.wrap).to.equal(false);

    (el as HTMLElement & { selectedLog: string }).selectedLog = 'second log';
    await (el as HTMLElement & { updateComplete: Promise<unknown> })
      .updateComplete;
    expect(
      aceSetValueSpy.mock.calls.some(([value]) => value === 'second log'),
      'a changed log updates the retained editor'
    ).to.equal(true);

    (el as HTMLElement & { isLoading: boolean }).isLoading = true;
    await (el as HTMLElement & { updateComplete: Promise<unknown> })
      .updateComplete;
    expect(
      aceDestroySpy.mock.calls,
      'loading removes and destroys the viewer'
    ).to.have.length(1);

    (el as HTMLElement & { isLoading: boolean }).isLoading = false;
    await (el as HTMLElement & { updateComplete: Promise<unknown> })
      .updateComplete;
    expect(
      aceEditSpy.mock.calls,
      'a replacement viewer gets exactly one new editor'
    ).to.have.length(2);

    let closed = 0;
    el.addEventListener('log-dialog-closed', () => closed++);
    const dialog = dialogIn(el);
    (inDialog(dialog, 'vaadin-button') as HTMLElement).click();
    await settle();
    await (el as HTMLElement & { updateComplete: Promise<unknown> })
      .updateComplete;
    expect(closed, 'the close control notifies the parent once').to.equal(1);
    expect((el as HTMLElement & { isOpened: boolean }).isOpened).to.equal(
      false
    );
  });
});
