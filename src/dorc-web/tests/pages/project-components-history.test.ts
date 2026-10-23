import { expect } from '../_helpers';
import {
  ComponentType,
  TerraformSourceType,
  type ComponentApiModel,
  type EnvironmentApiModel,
  type EnvironmentContentBuildsApiModel
} from '../../src/apis/dorc-api';
import '../../src/pages/page-project-components';

interface HistoryComponent extends ComponentApiModel {
  Children?: HistoryComponent[];
  parentPath?: string[];
  environmentBuilds?: Map<string, EnvironmentContentBuildsApiModel | null>;
}

it('flattens deployable catalog components alongside scripts, without including grouping or incomplete nodes', () => {
  const host = document.createElement('page-project-components') as unknown as {
    environments: EnvironmentApiModel[];
    envBuildsMap: Map<string, EnvironmentContentBuildsApiModel[]>;
    flattenComponents(
      components: HistoryComponent[],
      path: string[],
      visited: Set<string>,
      childIds: Set<string>
    ): HistoryComponent[];
  };
  const catalog: HistoryComponent = {
    ComponentId: 2,
    ComponentName: 'Storage',
    ComponentType: ComponentType.Terraform,
    TerraformSourceType: TerraformSourceType.Catalog,
    TerraformTemplateName: 'storage-account',
    TerraformTemplateVersion: '1.0.0',
    ScriptPath: ''
  };
  const script = {
    ComponentId: 3,
    ComponentName: 'Script',
    ScriptPath: 'deploy.ps1'
  };
  const build = { ComponentName: 'Storage', RequestId: 42 };
  host.environments = [{ EnvironmentName: 'Test' }];
  host.envBuildsMap = new Map([['Test', [build]]]);
  const result = host.flattenComponents(
    [
      { ComponentId: 1, ComponentName: 'Group', Children: [catalog, script] },
      catalog,
      {
        ...catalog,
        ComponentId: 4,
        ComponentName: 'Incomplete',
        TerraformTemplateVersion: ''
      },
      { ComponentId: 5, ComponentName: 'Empty', ScriptPath: ' ' },
      {
        ...catalog,
        ComponentId: 6,
        ComponentName: 'ArtifactWithoutPath',
        TerraformSourceType: TerraformSourceType.AzureArtifact
      },
      {
        ...catalog,
        ComponentId: 7,
        ComponentName: 'GitModule',
        TerraformSourceType: TerraformSourceType.Git,
        TerraformTemplateName: '',
        TerraformTemplateVersion: '',
        TerraformGitBranch: 'main',
        TerraformSubPath: 'modules/network'
      }
    ],
    [],
    new Set(),
    new Set(['2', '3'])
  );
  expect(result.map(component => component.ComponentName)).to.deep.equal([
    'GitModule',
    'Script',
    'Storage'
  ]);
  expect(result[2].parentPath).to.deep.equal(['Group']);
  expect(result[2].environmentBuilds?.get('Test')).to.equal(build);
});
