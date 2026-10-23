import { render } from 'lit';
import { describe, expect, it, vi } from 'vitest';
import { DatabaseType } from '../../src/apis/dorc-api';
import '../../src/components/attached-databases';
import '../../src/pages/page-databases-list';
import type { GridItemModel } from '@vaadin/grid';
import type { GridColumn } from '@vaadin/grid/vaadin-grid-column';
import type { DatabaseApiModel } from '../../src/apis/dorc-api';
import { fixture, html, setTheme } from '../_helpers';

type AttachedDatabasesTypeRenderer = HTMLElement & {
  databaseTypeRenderer(database: {
    DatabaseType?: DatabaseType | null;
  }): unknown;
};

describe.each(['attached-databases', 'page-databases-list'])(
  '%s type icons',
  tag => {
    it.each([
      [DatabaseType.SqlServer, 'brand:microsoftsqlserver', 'SQL Server'],
      [DatabaseType.CosmosDb, 'brand:cosmosdb', 'Cosmos DB'],
      [DatabaseType.ClickHouse, 'brand:clickhouse', 'ClickHouse'],
      [DatabaseType.PostgreSql, 'brand:postgresql', 'PostgreSQL']
    ])('renders %s as a compact labelled icon', (databaseType, icon, label) => {
      const element = document.createElement(
        tag
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
      for (const name of ['clickhouse', 'postgresql']) {
        expect(iconset?.querySelector(`g#${name} path`)).not.toBeNull();
      }
      for (const name of ['microsoftsqlserver', 'cosmosdb']) {
        const glyph = iconset?.querySelector(`g#${name}`);
        expect(glyph?.querySelector('path')).not.toBeNull();
        expect(glyph?.getAttribute('viewBox')).toBe('0 0 128 128');
      }
    });

    it.each([undefined, null])('treats legacy %s types as SQL Server', type => {
      const element = document.createElement(
        tag
      ) as AttachedDatabasesTypeRenderer;
      const container = document.createElement('div');

      render(
        element.databaseTypeRenderer({ DatabaseType: type }) as never,
        container
      );

      expect(container.querySelector('vaadin-icon')?.getAttribute('icon')).toBe(
        'brand:microsoftsqlserver'
      );
    });
  }
);

describe('reference database list Type column', () => {
  it.each(['light', 'dark'] as const)(
    'renders an accessible themed icon without changing the other columns in %s mode',
    async theme => {
      setTheme(theme);
      const page = await fixture(html`
        <page-databases-list
          .debouncedDataProvider="${(
            _: unknown,
            callback: (items: unknown[], size: number) => void
          ) => callback([], 0)}"
        ></page-databases-list>
      `);
      const columns = [
        ...page.shadowRoot!.querySelectorAll<GridColumn<DatabaseApiModel>>(
          '#grid > vaadin-grid-column'
        )
      ];
      expect(columns).toHaveLength(6);
      expect(columns[0].path).toBe('ServerName');
      expect(columns[1].path).toBe('Name');
      const typeColumn = columns[2];
      expect(typeColumn.header).toBe('Type');
      expect(typeColumn.width).toBe('64px');
      expect(typeColumn.flexGrow).toBe(0);
      expect(typeColumn.textAlign).toBe('center');
      expect(columns[4].width).toBe('300px');
      expect(columns[5].width).toBe('200px');

      await vi.waitFor(() =>
        expect(typeColumn.renderer).toBeTypeOf('function')
      );
      const root = document.createElement('div');
      root.style.color = 'var(--dorc-link-color)';
      page.shadowRoot!.append(root);
      typeColumn.renderer!(root, typeColumn, {
        item: { DatabaseType: DatabaseType.CosmosDb }
      } as GridItemModel<DatabaseApiModel>);
      const icon = root.querySelector('vaadin-icon')!;
      expect(icon.getAttribute('icon')).toBe('brand:cosmosdb');
      expect(icon.getAttribute('title')).toBe('Cosmos DB');
      expect(icon.getAttribute('aria-label')).toBe('Cosmos DB');
      expect(getComputedStyle(icon).color).toBe(getComputedStyle(root).color);
    }
  );
});
