import { expect, fixture, html } from '../_helpers';
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
import { RefDataContainersApi } from '../../src/apis/dorc-api';
import { of } from 'rxjs';

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
    grid: 'containers-grid',
    attachLabel: 'Attach Container',
    columns: ['Name', 'Image', 'Registry', 'Host Server', 'Tags']
  },
  {
    tag: 'env-cloud',
    grid: 'cloud-resources-grid',
    attachLabel: 'Attach Cloud Resource',
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
    grid: 'api-registrations-grid',
    attachLabel: 'Attach API',
    columns: ['Name', 'Base URL', 'Version', 'Health Check URL', 'Tags']
  }
];

for (const c of tabCases) {
  describe(`${c.tag} tab`, () => {
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

    it('enables write controls when the environment is editable', async () => {
      const el = await fixture(
        document.createElement(c.tag) as unknown as ReturnType<typeof html>
      );
      (el as any).envReadOnly = false;
      await (el as any).updateComplete;

      const buttons = Array.from(
        el.shadowRoot!.querySelectorAll('vaadin-button')
      );
      for (const button of buttons) {
        expect(button.disabled).to.be.false;
      }
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
    }
  });

  it('derives read-only gating from environment.UserEditable', async () => {
    const original =
      RefDataContainersApi.prototype.refDataContainersByEnvIdEnvIdGet;
    (RefDataContainersApi.prototype as any).refDataContainersByEnvIdEnvIdGet =
      () => of([]);

    try {
      const el = await fixture(
        document.createElement('env-containers') as unknown as ReturnType<
          typeof html
        >
      );
      (el as any).environment = { EnvironmentId: 42, UserEditable: false };
      await (el as any).updateComplete;
      expect((el as any).envReadOnly).to.be.true;

      (el as any).environment = { EnvironmentId: 42, UserEditable: true };
      await (el as any).updateComplete;
      expect((el as any).envReadOnly).to.be.false;
    } finally {
      (RefDataContainersApi.prototype as any).refDataContainersByEnvIdEnvIdGet =
        original;
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
