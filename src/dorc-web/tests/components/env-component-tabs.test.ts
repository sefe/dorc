import { expect, fixture, html, settle } from '../_helpers';
import type { LitElement } from 'lit';
import type { TextField } from '@vaadin/text-field';
import '../../src/components/environment-tabs/env-containers.js';
import '../../src/components/environment-tabs/env-cloud.js';
import '../../src/components/environment-tabs/env-apis.js';
import '../../src/components/add-edit-container.js';
import '../../src/components/add-edit-cloud-resource.js';
import '../../src/components/add-edit-api-registration.js';
import '../../src/components/attach-container.js';
import '../../src/components/resource-tags.js';
import type { AddEditContainer } from '../../src/components/add-edit-container.js';
import type { ResourceTags } from '../../src/components/resource-tags.js';
import {
  RefDataContainersApi,
  RefDataCloudResourcesApi,
  RefDataApiRegistrationsApi
} from '../../src/apis/dorc-api';
import { of } from 'rxjs';
import { vi } from 'vitest';

// tags-input drives @yaireo/tagify via window.Tagify, which the app loads
// globally but the test browser does not (same shim as database-tags-ui.test.ts).
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

// IS S-007 (docs/env-details-component-tabs): the three Components sub-tabs are real
// grid-based tabs — attach + create controls gated on environment editability, per-type
// columns, self-fetching (network calls fail gracefully under test; render shape is
// asserted here, API behaviour is covered by the backend controller tests).

const tabCases = [
  {
    tag: 'env-containers',
    api: RefDataContainersApi,
    prefix: 'refDataContainers',
    list: 'containers',
    newLabel: 'New Container',
    grid: 'containers-grid',
    attachLabel: 'Attach Container',
    form: 'add-edit-container',
    columns: ['Name', 'Image', 'Registry', 'Host Server', 'Tags']
  },
  {
    tag: 'env-cloud',
    api: RefDataCloudResourcesApi,
    prefix: 'refDataCloudResources',
    list: 'cloudResources',
    newLabel: 'New Cloud Resource',
    grid: 'cloud-resources-grid',
    attachLabel: 'Attach Cloud Resource',
    form: 'add-edit-cloud-resource',
    columns: [
      'Name',
      'Provider',
      'Resource Type',
      'Resource Identifier',
      'Subscription',
      'Tags'
    ]
  },
  {
    tag: 'env-apis',
    api: RefDataApiRegistrationsApi,
    prefix: 'refDataApiRegistrations',
    list: 'apiRegistrations',
    newLabel: 'New API',
    grid: 'api-registrations-grid',
    attachLabel: 'Attach API',
    form: 'add-edit-api-registration',
    columns: ['Name', 'Base URL', 'Version', 'Health Check URL', 'Tags']
  }
];

for (const c of tabCases) {
  describe(`${c.tag} tab`, () => {
    afterEach(() => vi.restoreAllMocks());

    it('discards canceled edits on reopening the same row, but retains edits during rerenders', async () => {
      const el = await fixture<LitElement>(
        html`${document.createElement(c.tag)}`
      );
      const tab = el as unknown as {
        openEditDialog(item: { Id: number; Name: string }): void;
        editDialogOpened: boolean;
      };
      const row = { Id: 7, Name: 'Original', CanEditDefinition: true };
      const nameField = () =>
        el
          .shadowRoot!.querySelector(c.form)!
          .shadowRoot!.querySelector<TextField>(
            'vaadin-text-field[label="Name"]'
          )!;
      tab.openEditDialog(row);
      await settle();
      expect(nameField().value).to.equal('Original');
      nameField().value = 'Unsaved';
      nameField().dispatchEvent(
        new Event('input', { bubbles: true, composed: true })
      );
      el.requestUpdate();
      await settle();
      expect(nameField().value).to.equal('Unsaved');
      expect(row.Name).to.equal('Original');
      tab.editDialogOpened = false;
      await settle();
      tab.openEditDialog(row);
      await settle();
      expect(nameField().value).to.equal('Original');
      tab.editDialogOpened = false;
      await settle();
    });

    it('renders a grid with the per-type columns', async () => {
      const el = await fixture(
        document.createElement(c.tag) as unknown as ReturnType<typeof html>
      );
      await (el as any).updateComplete;

      const grid = el.shadowRoot!.getElementById(c.grid);
      expect(grid, `${c.grid} should exist`).to.not.be.null;

      const headers = Array.from(
        grid!.querySelectorAll('vaadin-grid-sort-column, vaadin-grid-column')
      ).map(col => col.getAttribute('header'));
      for (const header of c.columns) {
        expect(headers, `column ${header}`).to.include(header);
      }
    });

    it('renders attach and create controls', async () => {
      const el = await fixture(
        document.createElement(c.tag) as unknown as ReturnType<typeof html>
      );
      await (el as any).updateComplete;

      const buttons = Array.from(
        el.shadowRoot!.querySelectorAll('vaadin-button')
      );
      const titles = buttons.map(b => b.getAttribute('title'));
      expect(titles).to.include(c.attachLabel);
    });

    it('disables write controls when the environment is read-only', async () => {
      const el = await fixture(
        document.createElement(c.tag) as unknown as ReturnType<typeof html>
      );
      (el as any).envReadOnly = true;
      await (el as any).updateComplete;

      const buttons = Array.from(
        el.shadowRoot!.querySelectorAll('vaadin-button')
      );
      expect(buttons.length).to.be.greaterThan(0);
      for (const button of buttons) {
        expect(
          button.disabled,
          `${button.textContent?.trim()} should be disabled`
        ).to.be.true;
      }
    });

    it('separates create permission from environment membership permission', async () => {
      const el = await fixture(
        document.createElement(c.tag) as unknown as ReturnType<typeof html>
      );
      (el as any).envReadOnly = false;
      (el as any).canCreateDefinition = false;
      await (el as any).updateComplete;

      const attach = el.shadowRoot!.querySelector(
        `vaadin-button[title="${c.attachLabel}"]`
      ) as any;
      const create = el.shadowRoot!.querySelector(
        `vaadin-button[title="${c.newLabel}"]`
      ) as any;
      expect(attach.disabled).to.be.false;
      expect(create.disabled).to.be.true;
      (el as any).canCreateDefinition = true;
      await (el as any).updateComplete;
      expect(create.disabled).to.be.false;
    });

    it('requires all-mapped-environment permission for global edit/tags/delete, but only current-environment permission to detach', async () => {
      const el = await fixture<LitElement>(
        html`${document.createElement(c.tag)}`
      );
      (el as any).envReadOnly = false;
      const row = { Id: 7, Name: 'shared', CanEditDefinition: false };
      const actions = await fixture(
        html`<div>${(el as any).actionsRenderer(row)}</div>`
      );
      const buttons = Array.from(actions.querySelectorAll('vaadin-button'));
      expect(buttons).to.have.length(5);
      expect(buttons[0].disabled, 'audit remains readable').to.be.false;
      expect(buttons[1].disabled, 'edit globally').to.be.true;
      expect(buttons[2].disabled, 'edit tags globally').to.be.true;
      expect(buttons[3].disabled, 'detach locally').to.be.false;
      expect(buttons[4].disabled, 'delete globally').to.be.true;
    });

    it('sends only tags plus expected tags, and refreshes after a global tags change', async () => {
      const proto = c.api.prototype as any;
      const requests: unknown[] = [];
      vi.spyOn(proto, `${c.prefix}IdTagsPut`).mockImplementation(
        (request: unknown) => {
          requests.push(request);
          return of({});
        }
      );
      vi.spyOn(proto, `${c.prefix}CapabilitiesEnvIdGet`).mockReturnValue(
        of({
          CanCreateDefinition: false,
          CanManageMembership: true
        })
      );
      const reload = vi
        .spyOn(proto, `${c.prefix}ByEnvIdEnvIdGet`)
        .mockReturnValue(of([]));
      const el = await fixture<LitElement>(
        html`${document.createElement(c.tag)}`
      );
      (el as any).environmentId = 42;
      (el as any).tagsItem = {
        Id: 7,
        Name: 'stale name',
        Tags: 'old',
        Image: 'stale image',
        EnvironmentNames: ['allowed', 'another'],
        CanEditDefinition: true
      };
      const pending = (el as any).saveTags(
        new CustomEvent('save', { detail: { tags: ['new'] } })
      );
      const prompt = document.querySelector('vaadin-confirm-dialog')!;
      expect(prompt.textContent).to.include('ALL attached environments');
      expect(prompt.textContent).to.include('allowed, another');
      prompt.dispatchEvent(new CustomEvent('confirm'));
      await pending;
      expect(requests).to.deep.equal([
        { id: 7, resourceTagsUpdate: { ExpectedTags: 'old', Tags: 'new' } }
      ]);
      expect(reload.mock.calls).to.have.length(1);
      expect((el as any).tagsDialogOpened).to.be.false;
    });

    it('warns before metadata-only global deletion, supports cancel, and refreshes on success', async () => {
      const proto = c.api.prototype as any;
      const remove = vi
        .spyOn(proto, `${c.prefix}IdDelete`)
        .mockReturnValue(of({ Result: true }));
      vi.spyOn(proto, `${c.prefix}CapabilitiesEnvIdGet`).mockReturnValue(
        of({})
      );
      const reload = vi
        .spyOn(proto, `${c.prefix}ByEnvIdEnvIdGet`)
        .mockReturnValue(of([]));
      const el = await fixture<LitElement>(
        html`${document.createElement(c.tag)}`
      );
      (el as any).environmentId = 42;
      const row = {
        Id: 7,
        Name: 'shared',
        EnvironmentNames: ['one', 'two'],
        CanEditDefinition: true
      };
      let pending = (el as any).deleteDefinition(row);
      let prompt = document.querySelector('vaadin-confirm-dialog')!;
      expect(prompt.textContent).to.include('ALL environments');
      expect(prompt.textContent).to.include('metadata only');
      expect(prompt.textContent).to.include(
        'does NOT destroy deployed infrastructure'
      );
      expect(prompt.textContent).to.include('one, two');
      prompt.dispatchEvent(new CustomEvent('cancel'));
      await pending;
      expect(remove.mock.calls).to.have.length(0);
      pending = (el as any).deleteDefinition(row);
      prompt = document.querySelector('vaadin-confirm-dialog')!;
      prompt.dispatchEvent(new CustomEvent('confirm'));
      await pending;
      expect(remove.mock.calls[0]).to.deep.equal([{ id: 7 }]);
      expect(reload.mock.calls).to.have.length(1);
    });
  });
}

describe('env-containers self-fetch lifecycle', () => {
  // Regression for the S-006/S-007 gate HIGH: PageEnvBase assigns `environment`
  // (which fires notifyEnvironmentReady synchronously) BEFORE it assigns
  // `environmentId` on the cold-cache path. The tab must derive the id from the
  // environment itself or a deep link renders a permanently empty grid.
  it('loads items when environment arrives before environmentId (cold cache)', async () => {
    const items = [{ Id: 1, Name: 'web01', Image: 'nginx:1' }];
    const original =
      RefDataContainersApi.prototype.refDataContainersByEnvIdEnvIdGet;
    const capability = vi
      .spyOn(
        RefDataContainersApi.prototype as any,
        'refDataContainersCapabilitiesEnvIdGet'
      )
      .mockReturnValue(
        of({ CanCreateDefinition: false, CanManageMembership: true })
      );
    const calls: number[] = [];
    (RefDataContainersApi.prototype as any).refDataContainersByEnvIdEnvIdGet =
      function (req: { envId: number }) {
        calls.push(req.envId);
        return of(items);
      };

    try {
      const el = await fixture(
        document.createElement('env-containers') as unknown as ReturnType<
          typeof html
        >
      );
      // Reproduce the base-class cold-path ordering: environment set while
      // environmentId is still the default (-1).
      (el as any).environmentId = -1;
      (el as any).environment = { EnvironmentId: 42, UserEditable: true };
      await (el as any).updateComplete;

      expect(calls).to.include(42);
      const grid = el.shadowRoot!.getElementById('containers-grid') as any;
      expect(grid.items).to.deep.equal(items);
      expect((el as any).envReadOnly).to.be.false;
    } finally {
      (RefDataContainersApi.prototype as any).refDataContainersByEnvIdEnvIdGet =
        original;
      capability.mockRestore();
    }
  });

  it('uses server operation capabilities instead of environment.UserEditable', async () => {
    const original =
      RefDataContainersApi.prototype.refDataContainersByEnvIdEnvIdGet;
    (RefDataContainersApi.prototype as any).refDataContainersByEnvIdEnvIdGet =
      () => of([]);
    const capability = vi
      .spyOn(
        RefDataContainersApi.prototype as any,
        'refDataContainersCapabilitiesEnvIdGet'
      )
      .mockReturnValue(
        of({ CanCreateDefinition: true, CanManageMembership: true })
      );

    try {
      const el = await fixture(
        document.createElement('env-containers') as unknown as ReturnType<
          typeof html
        >
      );
      (el as any).environment = { EnvironmentId: 42, UserEditable: false };
      await (el as any).updateComplete;
      expect((el as any).envReadOnly).to.be.false;

      capability.mockReturnValue(
        of({ CanCreateDefinition: false, CanManageMembership: false })
      );
      (el as any).environment = { EnvironmentId: 42, UserEditable: true };
      await (el as any).updateComplete;
      expect((el as any).envReadOnly).to.be.true;
    } finally {
      (RefDataContainersApi.prototype as any).refDataContainersByEnvIdEnvIdGet =
        original;
      capability.mockRestore();
    }
  });
});

describe('add-edit-container form', () => {
  it('binds the container fields into the inputs', async () => {
    const el = await fixture<AddEditContainer>(html`
      <add-edit-container
        .container="${{
          Id: 5,
          Name: 'web01',
          Image: 'nginx:1',
          Registry: 'reg.local',
          HostServerName: 'host01',
          Tags: 'edge;web'
        }}"
      ></add-edit-container>
    `);
    await el.updateComplete;

    const values = Array.from(
      el.shadowRoot!.querySelectorAll('vaadin-text-field')
    ).map(f => f.value);
    expect(values).to.deep.equal([
      'web01',
      'nginx:1',
      'reg.local',
      'host01',
      'edge;web'
    ]);
  });

  it('labels the action Create for a new item and Save for an existing one', async () => {
    const fresh = await fixture<AddEditContainer>(html`
      <add-edit-container .container="${{}}"></add-edit-container>
    `);
    await fresh.updateComplete;
    expect(
      fresh.shadowRoot!.querySelector('vaadin-button')!.textContent!.trim()
    ).to.equal('Create');

    const existing = await fixture<AddEditContainer>(html`
      <add-edit-container
        .container="${{ Id: 5, Name: 'x', Image: 'y' }}"
      ></add-edit-container>
    `);
    await existing.updateComplete;
    expect(
      existing.shadowRoot!.querySelector('vaadin-button')!.textContent!.trim()
    ).to.equal('Save');
  });
});

describe('add-edit-container create with environment context', () => {
  const proto = RefDataContainersApi.prototype as any;

  it('auto-attaches a newly created container to the current environment and fires container-saved', async () => {
    const originalPost = proto.refDataContainersPost;
    const originalAttach = proto.refDataContainersIdEnvironmentsEnvIdPut;
    const attachCalls: Array<{ id: number; envId: number }> = [];
    proto.refDataContainersPost = () =>
      of({ Id: 9, Name: 'web01', Image: 'nginx:1' });
    proto.refDataContainersIdEnvironmentsEnvIdPut = (req: {
      id: number;
      envId: number;
    }) => {
      attachCalls.push(req);
      return of({ Result: true });
    };

    try {
      const el = await fixture<AddEditContainer>(html`
        <add-edit-container
          .container="${{ Name: 'web01', Image: 'nginx:1' }}"
          .envId="${42}"
        ></add-edit-container>
      `);
      await el.updateComplete;

      let saved = false;
      el.addEventListener('container-saved', () => {
        saved = true;
      });
      el.shadowRoot!.querySelector('vaadin-button')!.click();

      expect(attachCalls).to.deep.equal([{ id: 9, envId: 42 }]);
      expect(saved, 'container-saved should fire after create+attach').to.be
        .true;
    } finally {
      proto.refDataContainersPost = originalPost;
      proto.refDataContainersIdEnvironmentsEnvIdPut = originalAttach;
    }
  });

  it('does not attach when no environment context is set or when updating', async () => {
    const originalPost = proto.refDataContainersPost;
    const originalPut = proto.refDataContainersIdPut;
    const originalAttach = proto.refDataContainersIdEnvironmentsEnvIdPut;
    const attachCalls: unknown[] = [];
    proto.refDataContainersPost = () =>
      of({ Id: 9, Name: 'web01', Image: 'nginx:1' });
    proto.refDataContainersIdPut = () =>
      of({ Id: 5, Name: 'web01', Image: 'nginx:1' });
    proto.refDataContainersIdEnvironmentsEnvIdPut = (req: unknown) => {
      attachCalls.push(req);
      return of({ Result: true });
    };

    try {
      // Create without environment context.
      const created = await fixture<AddEditContainer>(html`
        <add-edit-container
          .container="${{ Name: 'web01', Image: 'nginx:1' }}"
        ></add-edit-container>
      `);
      await created.updateComplete;
      created.shadowRoot!.querySelector('vaadin-button')!.click();

      // Update with environment context: already attached, must not re-attach.
      const updated = await fixture<AddEditContainer>(html`
        <add-edit-container
          .container="${{ Id: 5, Name: 'web01', Image: 'nginx:1' }}"
          .envId="${42}"
        ></add-edit-container>
      `);
      await updated.updateComplete;
      updated.shadowRoot!.querySelector('vaadin-button')!.click();
      document
        .querySelector('vaadin-confirm-dialog')!
        .dispatchEvent(new CustomEvent('confirm'));
      await settle();

      expect(attachCalls).to.deep.equal([]);
    } finally {
      proto.refDataContainersPost = originalPost;
      proto.refDataContainersIdPut = originalPut;
      proto.refDataContainersIdEnvironmentsEnvIdPut = originalAttach;
    }
  });
});

describe('resource-tags editor', () => {
  it('normalises the delimited tag string and fires resource-tags-save with the tag set', async () => {
    const el = await fixture<ResourceTags>(html`
      <resource-tags .tags="${'web; edge;web;'}"></resource-tags>
    `);
    await el.updateComplete;

    let detail: { tags: string[] } | undefined;
    el.addEventListener('resource-tags-save', ((e: CustomEvent) => {
      detail = e.detail;
    }) as EventListener);

    el.shadowRoot!.querySelector('vaadin-button')!.click();

    expect(detail?.tags).to.deep.equal(['edge', 'web']);
  });
});

describe('attach-container dialog content', () => {
  it('renders a combo box and an attach button', async () => {
    const el = await fixture(
      html`<attach-container .envId="${1}"></attach-container>`
    );
    await (el as any).updateComplete;

    expect(el.shadowRoot!.querySelector('vaadin-combo-box')).to.not.be.null;
    expect(
      el.shadowRoot!.querySelector('vaadin-button')!.textContent!.trim()
    ).to.equal('Attach');
  });

  // Regression: without item-value-path the combo-box value never matched any
  // item, so Attach always errored with "Select a container to attach".
  it('attaches the container selected in the combo box', async () => {
    const containers = [
      { Id: 7, Name: 'DOrc API' },
      { Id: 8, Name: 'Other' }
    ];
    const originalGet = RefDataContainersApi.prototype.refDataContainersGet;
    const originalAttach = (RefDataContainersApi.prototype as any)
      .refDataContainersIdEnvironmentsEnvIdPut;
    const attachCalls: Array<{ id: number; envId: number }> = [];
    (RefDataContainersApi.prototype as any).refDataContainersGet = () =>
      of(containers);
    (
      RefDataContainersApi.prototype as any
    ).refDataContainersIdEnvironmentsEnvIdPut = function (req: {
      id: number;
      envId: number;
    }) {
      attachCalls.push(req);
      return of(undefined);
    };

    try {
      const el = await fixture(
        html`<attach-container .envId="${42}"></attach-container>`
      );
      await (el as any).updateComplete;

      const combo = el.shadowRoot!.querySelector('vaadin-combo-box') as any;
      combo.value = 'DOrc API';
      await (el as any).updateComplete;

      el.shadowRoot!.querySelector('vaadin-button')!.click();

      expect(attachCalls).to.deep.equal([{ id: 7, envId: 42 }]);
    } finally {
      (RefDataContainersApi.prototype as any).refDataContainersGet =
        originalGet;
      (
        RefDataContainersApi.prototype as any
      ).refDataContainersIdEnvironmentsEnvIdPut = originalAttach;
    }
  });
});
