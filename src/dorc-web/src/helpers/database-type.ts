import { html } from 'lit';
import '@vaadin/icon';
import { DatabaseApiModel, DatabaseType } from '../apis/dorc-api';
import '../icons/dorc-icons.js';

export const databaseTypes = [
  {
    value: DatabaseType.SqlServer,
    label: 'SQL Server',
    icon: 'dorc:brand-sqlserver'
  },
  {
    value: DatabaseType.CosmosDb,
    label: 'Cosmos DB',
    icon: 'dorc:brand-cosmosdb'
  },
  {
    value: DatabaseType.ClickHouse,
    label: 'ClickHouse',
    icon: 'dorc:brand-clickhouse'
  },
  {
    value: DatabaseType.PostgreSql,
    label: 'PostgreSQL',
    icon: 'dorc:brand-postgresql'
  }
];

export const renderDatabaseType = (database: DatabaseApiModel) => {
  const display =
    databaseTypes.find(
      type => type.value === (database.DatabaseType ?? DatabaseType.SqlServer)
    ) ?? databaseTypes[0];

  return html`<vaadin-icon
    class="database-type-icon"
    style="color: var(--dorc-link-color)"
    icon="${display.icon}"
    title="${display.label}"
    aria-label="${display.label}"
  ></vaadin-icon>`;
};
