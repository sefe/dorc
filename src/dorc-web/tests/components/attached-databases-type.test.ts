import { render } from 'lit';
import { describe, expect, it } from 'vitest';
import { DatabaseType } from '../../src/apis/dorc-api';
import '../../src/components/attached-databases';

type AttachedDatabasesTypeRenderer = HTMLElement & {
  databaseTypeRenderer(database: { DatabaseType?: DatabaseType }): unknown;
};

describe('attached-databases type icons', () => {
  it.each([
    [DatabaseType.SqlServer, 'vaadin:database', 'SQL Server'],
    [DatabaseType.CosmosDb, 'brand:azure', 'Cosmos DB'],
    [DatabaseType.ClickHouse, 'brand:clickhouse', 'ClickHouse'],
    [DatabaseType.PostgreSql, 'brand:postgresql', 'PostgreSQL']
  ])('renders %s as a compact labelled icon', (databaseType, icon, label) => {
    const element = document.createElement(
      'attached-databases'
    ) as AttachedDatabasesTypeRenderer;
    const container = document.createElement('div');

    render(
      element.databaseTypeRenderer({ DatabaseType: databaseType }) as never,
      container
    );

    const renderedIcon = container.querySelector('vaadin-icon');
    expect(renderedIcon?.getAttribute('icon')).toBe(icon);
    expect(renderedIcon?.getAttribute('title')).toBe(label);
    expect(renderedIcon?.getAttribute('aria-label')).toBe(label);
  });

  it('registers a brand glyph for every brand icon the grid uses', () => {
    const iconset = document.querySelector('vaadin-iconset[name="brand"]');
    for (const name of ['azure', 'clickhouse', 'postgresql']) {
      expect(iconset?.querySelector(`g#${name} path`)).not.toBeNull();
    }
  });

  it('treats legacy records without a type as SQL Server', () => {
    const element = document.createElement(
      'attached-databases'
    ) as AttachedDatabasesTypeRenderer;
    const container = document.createElement('div');

    render(element.databaseTypeRenderer({}) as never, container);

    expect(container.querySelector('vaadin-icon')?.getAttribute('icon')).toBe(
      'vaadin:database'
    );
  });
});
