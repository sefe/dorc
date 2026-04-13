import { describe, it, expect, vi, beforeEach } from 'vitest';
import type { DeployArtefactDto, DeployComponentDto } from '../../apis/dorc-api';

// Mock APIs so the component doesn't make network requests
vi.mock('../../apis/dorc-api', async (importOriginal) => {
  const actual = (await importOriginal()) as Record<string, unknown>;
  return {
    ...actual,
    RequestApi: vi.fn().mockImplementation(() => ({
      requestBuildDefinitionsGet: () => ({
        subscribe: vi.fn()
      }),
      requestBuildsGet: () => ({
        subscribe: vi.fn()
      }),
      requestComponentsGet: () => ({
        subscribe: vi.fn()
      }),
      requestPost: () => ({
        subscribe: vi.fn()
      })
    })),
    PropertiesApi: vi.fn().mockImplementation(() => ({
      propertiesGet: () => ({
        subscribe: vi.fn()
      })
    }))
  };
});

// Import after mocks
const { DeployEnv } = await import('./deploy-env');

// Create element WITHOUT appending to DOM to avoid Vaadin Lumo rendering errors
function createElement(): InstanceType<typeof DeployEnv> {
  return document.createElement('deploy-env') as InstanceType<typeof DeployEnv>;
}

describe('DeployEnv', () => {
  let el: InstanceType<typeof DeployEnv>;

  beforeEach(() => {
    el = createElement();
  });

  describe('isGitHubProject getter', () => {
    it('returns true when SourceControlType is GitHub', () => {
      el['_project'] = { SourceControlType: 'GitHub' as any };
      expect(el['isGitHubProject']).toBe(true);
    });

    it('returns false when SourceControlType is AzureDevOps', () => {
      el['_project'] = { SourceControlType: 'AzureDevOps' as any };
      expect(el['isGitHubProject']).toBe(false);
    });

    it('returns false when SourceControlType is FileShare', () => {
      el['_project'] = { SourceControlType: 'FileShare' as any };
      expect(el['isGitHubProject']).toBe(false);
    });

    it('returns false when SourceControlType is undefined', () => {
      el['_project'] = {};
      expect(el['isGitHubProject']).toBe(false);
    });
  });

  describe('sortBuildDefinitions', () => {
    it('sorts alphabetically by Name', () => {
      const defs: DeployArtefactDto[] = [
        { Id: '1', Name: 'Zeta-Build' },
        { Id: '2', Name: 'Alpha-Build' },
        { Id: '3', Name: 'Mid-Build' }
      ];
      const sorted = [...defs].sort(el.sortBuildDefinitions);
      expect(sorted[0].Name).toBe('Alpha-Build');
      expect(sorted[1].Name).toBe('Mid-Build');
      expect(sorted[2].Name).toBe('Zeta-Build');
    });
  });

  describe('setBuildDefinitions', () => {
    it('detects folder project when first item is "Not an Azure DevOps Server Project"', () => {
      el.setBuildDefinitions([
        { Id: '1', Name: 'Not an Azure DevOps Server Project' }
      ]);
      expect(el['isFolderProject']).toBe(true);
    });

    it('detects folder project when first item is "Not a CI/CD Server Project"', () => {
      el.setBuildDefinitions([
        { Id: '1', Name: 'Not a CI/CD Server Project' }
      ]);
      expect(el['isFolderProject']).toBe(true);
    });

    it('sets isFolderProject to false for normal AzDO build definitions', () => {
      el.setBuildDefinitions([
        { Id: '1', Name: 'MyBuild-CI' },
        { Id: '2', Name: 'MyBuild-Release' }
      ]);
      expect(el['isFolderProject']).toBe(false);
    });

    it('sets isFolderProject to false for empty list', () => {
      el.setBuildDefinitions([]);
      expect(el['isFolderProject']).toBe(false);
    });

    it('sorts build definitions', () => {
      el.setBuildDefinitions([
        { Id: '1', Name: 'Zeta' },
        { Id: '2', Name: 'Alpha' }
      ]);
      expect(el.buildDefinitions[0].Name).toBe('Alpha');
      expect(el.buildDefinitions[1].Name).toBe('Zeta');
    });

    it('stops the loading spinner', () => {
      el['buildDefsLoading'] = true;
      el.setBuildDefinitions([{ Id: '1', Name: 'Build' }]);
      expect(el['buildDefsLoading']).toBe(false);
    });
  });

  describe('convertDeployCompToTree (via createTreeFromList)', () => {
    it('converts flat component list to tree nodes', () => {
      const components: DeployComponentDto[] = [
        { Id: 1, Name: 'Root', ParentId: 0, NumOfChildren: 1, IsEnabled: true },
        { Id: 2, Name: 'Child', ParentId: 1, NumOfChildren: 0, IsEnabled: true }
      ];

      // Use createTreeFromList via the component instance
      const treeNodes = components.map(c => el['convertDeployCompToTree'](c));
      const tree = el['createTreeFromList'](treeNodes, undefined);

      expect(tree.length).toBe(1);
      expect(tree[0].name).toBe('Root');
      expect(tree[0].children.length).toBe(1);
      expect(tree[0].children[0].name).toBe('Child');
    });

    it('converts a single root-level component', () => {
      const components: DeployComponentDto[] = [
        { Id: 1, Name: 'Standalone', ParentId: 0, NumOfChildren: 0, IsEnabled: true }
      ];

      const treeNodes = components.map(c => el['convertDeployCompToTree'](c));
      const tree = el['createTreeFromList'](treeNodes, undefined);

      expect(tree.length).toBe(1);
      expect(tree[0].name).toBe('Standalone');
      expect(tree[0].children.length).toBe(0);
    });

    it('returns empty array for empty input', () => {
      const tree = el['createTreeFromList']([], undefined);
      expect(tree).toEqual([]);
    });

    it('handles multiple root nodes', () => {
      const components: DeployComponentDto[] = [
        { Id: 1, Name: 'Root1', ParentId: 0, NumOfChildren: 0 },
        { Id: 2, Name: 'Root2', ParentId: 0, NumOfChildren: 0 }
      ];

      const treeNodes = components.map(c => el['convertDeployCompToTree'](c));
      const tree = el['createTreeFromList'](treeNodes, undefined);

      expect(tree.length).toBe(2);
    });

    it('sets correct tree node properties', () => {
      const node = el['convertDeployCompToTree']({
        Id: 10,
        Name: 'Component',
        ParentId: 5,
        NumOfChildren: 3,
        IsEnabled: true
      });

      expect(node.id).toBe(10);
      expect(node.name).toBe('Component');
      expect(node.parentId).toBe(5);
      expect(node.numOfChildren).toBe(3);
      expect(node.hasParent).toBe(true);
      expect(node.checked).toBe(false);
      expect(node.indeterminate).toBe(false);
      expect(node.children).toEqual([]);
    });

    it('handles null NumOfChildren', () => {
      const node = el['convertDeployCompToTree']({
        Id: 1,
        Name: 'Test',
        ParentId: 0,
        NumOfChildren: null
      });

      expect(node.numOfChildren).toBe(0);
    });
  });

  describe('removeItem', () => {
    it('removes existing item from array', () => {
      const arr = [1, 2, 3, 4];
      const result = el.removeItem(arr, 3);
      expect(result).toEqual([1, 2, 4]);
    });

    it('returns array unchanged if item not found', () => {
      const arr = [1, 2, 3];
      const result = el.removeItem(arr, 5);
      expect(result).toEqual([1, 2, 3]);
    });

    it('removes only the first occurrence', () => {
      const arr = [1, 2, 2, 3];
      const result = el.removeItem(arr, 2);
      expect(result).toEqual([1, 2, 3]);
    });
  });

  describe('property overrides', () => {
    it('AddOverrideProperty adds a valid property override', () => {
      el.properties = [{ Name: 'DBServer' }];
      el['propertyName'] = 'DBServer';
      el['propertyValue'] = 'sql-prod-01';

      el['AddOverrideProperty']();

      expect(el.propertyOverrides.length).toBe(1);
      expect(el.propertyOverrides[0].PropertyName).toBe('DBServer');
      expect(el.propertyOverrides[0].PropertyValue).toBe('sql-prod-01');
    });

    it('AddOverrideProperty rejects unknown property name', () => {
      el.properties = [{ Name: 'DBServer' }];
      el['propertyName'] = 'UnknownProp';
      el['propertyValue'] = 'value';

      // Stub window.alert since happy-dom may not define it
      const alertFn = vi.fn();
      globalThis.alert = alertFn;

      el['AddOverrideProperty']();

      expect(el.propertyOverrides.length).toBe(0);
      expect(alertFn).toHaveBeenCalledWith(
        'Please select a property from the list!'
      );
    });

    it('AddOverrideProperty rejects empty value', () => {
      el.properties = [{ Name: 'DBServer' }];
      el['propertyName'] = 'DBServer';
      el['propertyValue'] = '';

      const alertFn = vi.fn();
      globalThis.alert = alertFn;

      el['AddOverrideProperty']();

      expect(el.propertyOverrides.length).toBe(0);
      expect(alertFn).toHaveBeenCalledWith(
        'The property must contain a value!'
      );
    });
  });

  describe('EnvironmentChange', () => {
    it('sets envName', () => {
      el.EnvironmentChange('production');
      expect(el.envName).toBe('production');
    });
  });
});
