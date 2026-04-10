import { describe, it, expect, vi, beforeEach } from 'vitest';
import type { ProjectApiModel } from '../apis/dorc-api';

// Mock GlobalCache and API calls so the constructor doesn't make network requests
vi.mock('../global-cache', () => ({
  default: {
    getInstance: () => ({
      userRoles: ['Admin', 'PowerUser'],
      allRolesResp: undefined
    })
  }
}));

const mockSubscribe = vi.fn().mockImplementation((onNext: any) => {
  if (typeof onNext === 'function') onNext([]);
  else if (onNext?.next) onNext.next([]);
});

vi.mock('../apis/dorc-api/apis', () => ({
  RefDataProjectsApi: class {
    refDataProjectsGet() {
      return { subscribe: mockSubscribe };
    }
    refDataProjectsProjectIdDelete() {
      return { subscribe: vi.fn() };
    }
  }
}));

vi.mock('../apis/dorc-api', async (importOriginal) => {
  const actual = (await importOriginal()) as Record<string, unknown>;
  return {
    ...actual,
    RefDataProjectsApi: class {
      refDataProjectsGet() {
        return { subscribe: mockSubscribe };
      }
      refDataProjectsProjectIdDelete() {
        return { subscribe: vi.fn() };
      }
    }
  };
});

// Mock sub-components to avoid transitive import issues
vi.mock('../components/add-edit-project', () => ({ AddEditProject: class {} }));
vi.mock('../components/add-edit-access-control', () => ({ AddEditAccessControl: class {} }));
vi.mock('../components/project-audit-data', () => ({ ProjectAuditData: class {} }));
vi.mock('../components/confirm-dialog', () => ({ ConfirmDialog: class {} }));
vi.mock('../components/notifications/error-notification', () => ({ ErrorNotification: class {} }));
vi.mock('../components/notifications/success-notification', () => ({ SuccessNotification: class {} }));
vi.mock('../helpers/errorMessage-retriever', () => ({ retrieveErrorMessage: vi.fn() }));

// Import after mocks
const { PageProjectsList } = await import('./page-projects-list');

// Create element WITHOUT appending to DOM to avoid Vaadin Lumo rendering errors
function createElement(): InstanceType<typeof PageProjectsList> {
  return document.createElement(
    'page-projects-list'
  ) as InstanceType<typeof PageProjectsList>;
}

const sampleProjects: ProjectApiModel[] = [
  {
    ProjectId: 1,
    ProjectName: 'Zulu',
    ProjectDescription: 'A deployment project',
    ArtefactsUrl: 'https://dev.azure.com/org',
    ArtefactsSubPaths: 'ZuluProject',
    ArtefactsBuildRegex: '.*'
  },
  {
    ProjectId: 2,
    ProjectName: 'Foxtrot',
    ProjectDescription: 'A file-share hosted project',
    ArtefactsUrl: 'file://server/share',
    ArtefactsSubPaths: 'FoxtrotProject',
    ArtefactsBuildRegex: 'build-*'
  },
  {
    ProjectId: 3,
    ProjectName: 'Bravo',
    ProjectDescription: 'Git hosted project',
    ArtefactsUrl: 'https://github.com/org/repo',
    ArtefactsSubPaths: 'BravoSub',
    ArtefactsBuildRegex: ''
  }
];

describe('PageProjectsList', () => {
  let el: InstanceType<typeof PageProjectsList>;

  beforeEach(() => {
    el = createElement();
  });

  describe('sortProjects', () => {
    it('sorts projects alphabetically by name', () => {
      const sorted = [...sampleProjects].sort(el.sortProjects);
      expect(sorted[0].ProjectName).toBe('Bravo');
      expect(sorted[1].ProjectName).toBe('Foxtrot');
      expect(sorted[2].ProjectName).toBe('Zulu');
    });

    it('returns 1 when first name is greater', () => {
      expect(
        el.sortProjects(
          { ProjectName: 'Zulu' },
          { ProjectName: 'Alpha' }
        )
      ).toBe(1);
    });

    it('returns -1 when first name is smaller', () => {
      expect(
        el.sortProjects(
          { ProjectName: 'Alpha' },
          { ProjectName: 'Zulu' }
        )
      ).toBe(-1);
    });
  });

  describe('setProjects', () => {
    it('sets projects and filteredProjects sorted alphabetically', () => {
      el.setProjects([...sampleProjects]);
      expect(el.projects[0].ProjectName).toBe('Bravo');
      expect(el.projects[2].ProjectName).toBe('Zulu');
      expect(el.filteredProjects.length).toBe(3);
    });

    it('sets loading to false', () => {
      el['loading'] = true;
      el.setProjects([]);
      expect(el['loading']).toBe(false);
    });
  });

  describe('updateSearch', () => {
    beforeEach(() => {
      el.setProjects([...sampleProjects]);
    });

    it('filters by project name', () => {
      el.updateSearch({ detail: { value: 'Foxtrot' } } as CustomEvent);
      expect(el.filteredProjects.length).toBe(1);
      expect(el.filteredProjects[0].ProjectName).toBe('Foxtrot');
    });

    it('filters by description', () => {
      el.updateSearch({
        detail: { value: 'Git hosted' }
      } as CustomEvent);
      expect(el.filteredProjects.length).toBe(1);
      expect(el.filteredProjects[0].ProjectName).toBe('Bravo');
    });

    it('filters by ArtefactsBuildRegex', () => {
      el.updateSearch({ detail: { value: 'build-' } } as CustomEvent);
      expect(el.filteredProjects.length).toBe(1);
      expect(el.filteredProjects[0].ProjectName).toBe('Foxtrot');
    });

    it('filters by ArtefactsSubPaths', () => {
      el.updateSearch({ detail: { value: 'BravoSub' } } as CustomEvent);
      expect(el.filteredProjects.length).toBe(1);
      expect(el.filteredProjects[0].ProjectName).toBe('Bravo');
    });

    it('supports pipe-separated multiple search terms', () => {
      el.updateSearch({
        detail: { value: 'Foxtrot|Bravo' }
      } as CustomEvent);
      expect(el.filteredProjects.length).toBe(2);
      const names = el.filteredProjects.map(p => p.ProjectName);
      expect(names).toContain('Foxtrot');
      expect(names).toContain('Bravo');
    });

    it('is case-insensitive', () => {
      el.updateSearch({ detail: { value: 'foxtrot' } } as CustomEvent);
      expect(el.filteredProjects.length).toBe(1);
      expect(el.filteredProjects[0].ProjectName).toBe('Foxtrot');
    });

    it('returns all projects when search is empty', () => {
      el.updateSearch({ detail: { value: '' } } as CustomEvent);
      expect(el.filteredProjects.length).toBe(3);
    });
  });

  describe('getEmptyProj', () => {
    it('returns empty project matching expected shape', () => {
      const empty = el.getEmptyProj();
      expect(empty.ProjectId).toBe(0);
      expect(empty.ProjectName).toBe('');
      expect(empty.ProjectDescription).toBe('');
      expect(empty.ArtefactsBuildRegex).toBe('');
      expect(empty.ArtefactsSubPaths).toBe('');
      expect(empty.ArtefactsUrl).toBe('');
    });
  });

  describe('user roles', () => {
    it('sets isAdmin from role list', () => {
      el['setUserRoles'](['Admin', 'PowerUser']);
      expect(el.isAdmin).toBe(true);
      expect(el.isPowerUser).toBe(true);
    });

    it('isAdmin is false when Admin role not present', () => {
      el['setUserRoles'](['User']);
      expect(el.isAdmin).toBe(false);
      expect(el.isPowerUser).toBe(false);
    });

    it('isPowerUser is true independently of Admin', () => {
      el['setUserRoles'](['PowerUser']);
      expect(el.isAdmin).toBe(false);
      expect(el.isPowerUser).toBe(true);
    });
  });

  describe('_sourceControlTypeRenderer (icon detection)', () => {
    function renderIcon(project: ProjectApiModel) {
      const root = document.createElement('div');
      const column = document.createElement('div');
      el['_sourceControlTypeRenderer'](root, column, { item: project } as any);
      return root.innerHTML;
    }

    it('renders GitHub icon for SourceControlType GitHub', () => {
      const html = renderIcon({ SourceControlType: 'GitHub' as any });
      expect(html).toContain('GitHub');
      expect(html).toContain('svg');
    });

    it('renders folder icon for SourceControlType FileShare', () => {
      const html = renderIcon({ SourceControlType: 'FileShare' as any });
      expect(html).toContain('folder-open');
      expect(html).toContain('File Share');
    });

    it('renders folder icon for file:// URL even without FileShare enum (fallback detection)', () => {
      const html = renderIcon({
        SourceControlType: 'AzureDevOps' as any,
        ArtefactsUrl: 'file://server/share/builds'
      });
      expect(html).toContain('folder-open');
    });

    it('renders Azure DevOps icon for SourceControlType AzureDevOps', () => {
      const html = renderIcon({
        SourceControlType: 'AzureDevOps' as any,
        ArtefactsUrl: 'https://dev.azure.com/org'
      });
      expect(html).toContain('Azure DevOps');
      expect(html).toContain('svg');
    });

    it('renders Azure DevOps icon when SourceControlType is undefined', () => {
      const html = renderIcon({
        ArtefactsUrl: 'https://dev.azure.com/org'
      });
      expect(html).toContain('Azure DevOps');
    });
  });

  describe('_projectNameRenderer (tooltip)', () => {
    function renderName(project: ProjectApiModel) {
      const root = document.createElement('div');
      const column = document.createElement('div');
      el['_projectNameRenderer'](root, column, { item: project } as any);
      return root.innerHTML;
    }

    it('renders project name as text content', () => {
      const html = renderName({ ProjectName: 'MyProject' });
      expect(html).toContain('MyProject');
    });

    it('renders project description as title tooltip', () => {
      const html = renderName({
        ProjectName: 'Proj',
        ProjectDescription: 'A detailed description'
      });
      expect(html).toContain('title="A detailed description"');
    });

    it('renders empty title when no description', () => {
      const html = renderName({ ProjectName: 'Proj' });
      expect(html).toContain('title=""');
    });
  });
});
