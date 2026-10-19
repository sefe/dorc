import { afterEach, beforeEach, describe, it, vi } from 'vitest';
import { of } from 'rxjs';
import type { ComboBox } from '@vaadin/combo-box';
import type { TextField } from '@vaadin/text-field';
import { expect, fixture, html } from '../_helpers';
import {
  DatabaseType,
  RefDataDatabasesApi,
  RefDataGroupsApi,
  type DatabaseApiModel
} from '../../src/apis/dorc-api';
import '../../src/components/add-edit-database';
import type { AddEditDatabase } from '../../src/components/add-edit-database';

class FakeTagify {
  value: { value: string }[] = [];
  addTags(tags: string[]) {
    this.value.push(...tags.map(value => ({ value })));
  }
  removeAllTags() {
    this.value = [];
  }
}
(window as any).Tagify = FakeTagify;

const engines = Object.values(DatabaseType);

async function selectEngine(element: AddEditDatabase, type: DatabaseType) {
  const selector =
    element.shadowRoot!.querySelector<ComboBox>('#database-type')!;
  selector.value = type;
  await element.updateComplete;
  expect(element.databaseType).to.equal(type);
}

describe('add-edit-database engine selection', () => {
  beforeEach(() => {
    vi.spyOn(RefDataGroupsApi.prototype, 'refDataGroupsGet').mockReturnValue(
      of([]) as never
    );
    vi.spyOn(
      RefDataDatabasesApi.prototype,
      'refDataDatabasesGet'
    ).mockReturnValue(of([]) as never);
  });

  afterEach(() => vi.restoreAllMocks());

  it.each(engines)(
    'creates a database with the selected %s engine',
    async type => {
      const post = vi
        .spyOn(RefDataDatabasesApi.prototype, 'refDataDatabasesPost')
        .mockImplementation(
          request => of({ ...request.databaseApiModel, Id: 9 }) as never
        );
      const element = await fixture<AddEditDatabase>(
        html`<add-edit-database></add-edit-database>`
      );
      element.database = {
        Name: 'database',
        ServerName: 'server',
        Tags: ['app']
      };
      await element.updateComplete;
      await selectEngine(element, type);

      element.saveDatabase();

      expect(post.mock.calls[0][0].databaseApiModel).to.include({
        DatabaseType: type,
        Name: 'database',
        ServerName: 'server'
      });
      expect(element.databaseType).to.equal(DatabaseType.SqlServer);
    }
  );

  it.each(engines)(
    'loads %s on edit and submits the changed engine',
    async type => {
      const put = vi
        .spyOn(RefDataDatabasesApi.prototype, 'refDataDatabasesPut')
        .mockImplementation(request => of(request.databaseApiModel) as never);
      const element = await fixture<AddEditDatabase>(
        html`<add-edit-database></add-edit-database>`
      );
      element.database = {
        Id: 7,
        Name: 'database',
        ServerName: 'server',
        Tags: ['app'],
        DatabaseType: type
      };
      await element.updateComplete;
      const selector =
        element.shadowRoot!.querySelector<ComboBox>('#database-type')!;
      expect(selector.value).to.equal(type);
      expect(selector.items?.map(item => item.label)).to.deep.equal([
        'SQL Server',
        'Cosmos DB',
        'ClickHouse',
        'PostgreSQL'
      ]);
      const replacement = engines[(engines.indexOf(type) + 1) % engines.length];
      await selectEngine(element, replacement);

      element.saveDatabase();

      expect(put.mock.calls[0][0].id).to.equal(7);
      expect(put.mock.calls[0][0].databaseApiModel!.DatabaseType).to.equal(
        replacement
      );
      expect(element.databaseType).to.equal(DatabaseType.SqlServer);
    }
  );

  it.each([undefined, null])(
    'defaults legacy %s engine values to SQL Server on edit',
    async type => {
      const put = vi
        .spyOn(RefDataDatabasesApi.prototype, 'refDataDatabasesPut')
        .mockImplementation(request => of(request.databaseApiModel) as never);
      const element = await fixture<AddEditDatabase>(
        html`<add-edit-database></add-edit-database>`
      );
      element.database = {
        Id: 7,
        Name: 'legacy',
        ServerName: 'server',
        Tags: ['app'],
        DatabaseType: type
      } as DatabaseApiModel;
      await element.updateComplete;

      expect(
        element.shadowRoot!.querySelector<ComboBox>('#database-type')!.value
      ).to.equal(DatabaseType.SqlServer);
      element.saveDatabase();
      expect(put.mock.calls[0][0].databaseApiModel!.DatabaseType).to.equal(
        DatabaseType.SqlServer
      );
    }
  );

  it('resets the engine on Clear and when switching to a new database', async () => {
    const element = await fixture<AddEditDatabase>(
      html`<add-edit-database></add-edit-database>`
    );
    element.database = { Id: 7, DatabaseType: DatabaseType.PostgreSql };
    await element.updateComplete;
    const clear = [
      ...element.shadowRoot!.querySelectorAll('vaadin-button')
    ].find(button => button.textContent === 'Clear')!;
    clear.click();
    await element.updateComplete;
    expect(element.databaseType).to.equal(DatabaseType.SqlServer);
    expect(
      element.shadowRoot!.querySelector<ComboBox>('#database-type')!.value
    ).to.equal(DatabaseType.SqlServer);

    element.database = { Id: 8, DatabaseType: DatabaseType.CosmosDb };
    await element.updateComplete;
    element.database = element.getEmptyDatabase();
    await element.updateComplete;
    expect(element.databaseType).to.equal(DatabaseType.SqlServer);
  });

  it('keeps the selected engine if its required selector is cleared', async () => {
    const element = await fixture<AddEditDatabase>(
      html`<add-edit-database></add-edit-database>`
    );
    await selectEngine(element, DatabaseType.PostgreSql);
    const selector =
      element.shadowRoot!.querySelector<ComboBox>('#database-type')!;
    selector.value = '';
    await element.updateComplete;
    expect(selector.value).to.equal(DatabaseType.PostgreSql);
    expect(element.databaseType).to.equal(DatabaseType.PostgreSql);
  });

  it('accepts Aiven project/service and retains SQL instance and Cosmos account formats', async () => {
    const element = await fixture<AddEditDatabase>(
      html`<add-edit-database></add-edit-database>`
    );
    const instance = [
      ...element.shadowRoot!.querySelectorAll<TextField>('vaadin-text-field')
    ].find(field => field.label === 'Instance')!;
    expect(instance.helperText).to.contain('project/service');
    expect(instance.helperText).to.contain('account name');
    for (const value of [
      'project/service',
      'server\\instance',
      'cosmos-account'
    ]) {
      instance.value = value;
      expect(instance.validate()).to.equal(true);
    }
    for (const value of ['project/service/extra', '/service', 'project/']) {
      instance.value = value;
      expect(instance.validate()).to.equal(false);
    }
  });
});
