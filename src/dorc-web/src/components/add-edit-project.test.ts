import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import type { ProjectApiModel } from '../apis/dorc-api';
import { SourceControlType } from '../apis/dorc-api';

// Mock the API to prevent network requests
vi.mock('../apis/dorc-api', async (importOriginal) => {
  const actual = (await importOriginal()) as Record<string, unknown>;
  return {
    ...actual,
    RefDataProjectsApi: vi.fn().mockImplementation(() => ({
      refDataProjectsGet: () => ({ subscribe: vi.fn() }),
      refDataProjectsPost: () => ({ subscribe: vi.fn() }),
      refDataProjectsPut: () => ({ subscribe: vi.fn() })
    }))
  };
});

const { AddEditProject } = await import('./add-edit-project');

// Create element WITHOUT appending to DOM to avoid Vaadin Lumo rendering errors
function createElement(): InstanceType<typeof AddEditProject> {
  return document.createElement('add-edit-project') as InstanceType<typeof AddEditProject>;
}

describe('AddEditProject', () => {
  describe('getEmptyProj', () => {
    it('returns a project with empty string fields and ProjectId 0', () => {
      const el = createElement();
      const empty = el.getEmptyProj();
      expect(empty.ProjectDescription).toBe('');
      expect(empty.ProjectId).toBe(0);
      expect(empty.ProjectName).toBe('');
      expect(empty.ArtefactsBuildRegex).toBe('');
      expect(empty.ArtefactsSubPaths).toBe('');
      expect(empty.ArtefactsUrl).toBe('');
    });

    it('defaults SourceControlType to AzureDevOps string', () => {
      const el = createElement();
      const empty = el.getEmptyProj();
      expect(String(empty.SourceControlType)).toBe('AzureDevOps');
    });
  });

  describe('SourceControlType enum', () => {
    it('has AzureDevOps as string value', () => {
      expect(SourceControlType.AzureDevOps).toBe('AzureDevOps');
    });

    it('has GitHub as string value', () => {
      expect(SourceControlType.GitHub).toBe('GitHub');
    });

    it('has FileShare as string value', () => {
      expect(SourceControlType.FileShare).toBe('FileShare');
    });
  });

  describe('_checkName', () => {
    let el: InstanceType<typeof AddEditProject>;

    beforeEach(() => {
      el = createElement();
      el.projects = [
        { ProjectName: 'AlphaProject' },
        { ProjectName: 'BetaProject' }
      ];
    });

    it('allows a new unique name', () => {
      el['_checkName']('NewProjectName');
      expect(el['isNameValid']).toBe(true);
    });

    it('allows an existing project name (edit scenario)', () => {
      el['_checkName']('AlphaProject');
      expect(el['isNameValid']).toBe(true);
    });

    it('rejects an empty name', () => {
      el['_checkName']('');
      expect(el['isNameValid']).toBe(false);
    });
  });

  describe('_inputValueChanged', () => {
    let el: InstanceType<typeof AddEditProject>;

    beforeEach(() => {
      el = createElement();
    });

    it('sets projValid to false when ProjectName is empty', () => {
      el['_project'] = {
        ProjectName: '',
        ArtefactsUrl: 'https://dev.azure.com/org',
        ArtefactsSubPaths: 'MyProject'
      };
      el['_inputValueChanged']();
      expect(el['projValid']).toBe(false);
    });

    it('sets projValid to false when ArtefactsUrl is empty', () => {
      el['_project'] = {
        ProjectName: 'TestProject',
        ArtefactsUrl: '',
        ArtefactsSubPaths: 'MyProject'
      };
      el['_inputValueChanged']();
      expect(el['projValid']).toBe(false);
    });

    it('sets projValid to false when ArtefactsSubPaths is empty', () => {
      el['_project'] = {
        ProjectName: 'TestProject',
        ArtefactsUrl: 'https://dev.azure.com/org',
        ArtefactsSubPaths: ''
      };
      el['_inputValueChanged']();
      expect(el['projValid']).toBe(false);
    });

    it('sets projValid to true when all required fields are non-empty', () => {
      el['_project'] = {
        ProjectName: 'TestProject',
        ArtefactsUrl: 'https://dev.azure.com/org',
        ArtefactsSubPaths: 'MyProject'
      };
      el['_inputValueChanged']();
      expect(el['projValid']).toBe(true);
    });
  });

  describe('_canSubmit', () => {
    let el: InstanceType<typeof AddEditProject>;

    beforeEach(() => {
      el = createElement();
    });

    it('canSubmit is true when projValid, isNameValid, and not busy', () => {
      el['projValid'] = true;
      el['isNameValid'] = true;
      el['isBusy'] = false;
      el['_canSubmit']();
      expect(el.canSubmit).toBe(true);
    });

    it('canSubmit is false when isBusy', () => {
      el['projValid'] = true;
      el['isNameValid'] = true;
      el['isBusy'] = true;
      el['_canSubmit']();
      expect(el.canSubmit).toBe(false);
    });

    it('canSubmit is false when name is invalid', () => {
      el['projValid'] = true;
      el['isNameValid'] = false;
      el['isBusy'] = false;
      el['_canSubmit']();
      expect(el.canSubmit).toBe(false);
    });

    it('canSubmit is false when project fields are invalid', () => {
      el['projValid'] = false;
      el['isNameValid'] = true;
      el['isBusy'] = false;
      el['_canSubmit']();
      expect(el.canSubmit).toBe(false);
    });
  });

  describe('_setBusy / _setUnbusy', () => {
    let el: InstanceType<typeof AddEditProject>;

    beforeEach(() => {
      el = createElement();
      el['projValid'] = true;
      el['isNameValid'] = true;
    });

    it('_setBusy disables submit', () => {
      el['_setBusy']();
      expect(el['isBusy']).toBe(true);
      expect(el.canSubmit).toBe(false);
    });

    it('_setUnbusy re-enables submit', () => {
      el['_setBusy']();
      el['_setUnbusy']();
      expect(el['isBusy']).toBe(false);
      expect(el.canSubmit).toBe(true);
    });
  });

  describe('errorAlert', () => {
    let el: InstanceType<typeof AddEditProject>;

    beforeEach(() => {
      el = createElement();
    });

    it('sets ErrorMessage from ExceptionMessage', () => {
      el['errorAlert']({ response: { ExceptionMessage: 'Duplicate name' } });
      expect(el.ErrorMessage).toBe('Duplicate name');
    });

    it('sets ErrorMessage from Message', () => {
      el['errorAlert']({ response: { Message: 'Server error' } });
      expect(el.ErrorMessage).toBe('Server error');
    });

    it('sets ErrorMessage from raw response string', () => {
      el['errorAlert']({ response: 'Something went wrong' });
      expect(el.ErrorMessage).toBe('Something went wrong');
    });
  });

  describe('Reset', () => {
    it('clears project and error message', () => {
      const el = createElement();
      el['_project'] = {
        ProjectId: 5,
        ProjectName: 'Test',
        ArtefactsUrl: 'https://example.com'
      };
      el.ErrorMessage = 'Some error';

      el.Reset();

      expect(el['_project'].ProjectId).toBe(0);
      expect(el['_project'].ProjectName).toBe('');
      expect(el.ErrorMessage).toBe('');
    });
  });

  describe('custom events', () => {
    it('projUpdated dispatches project-updated event', () => {
      const el = createElement();
      const project: ProjectApiModel = {
        ProjectId: 1,
        ProjectName: 'TestProj'
      };

      Object.defineProperty(el, 'dialog', {
        value: { close: vi.fn(), open: false },
        writable: true
      });

      let receivedEvent: CustomEvent | null = null;
      el.addEventListener('project-updated', ((e: CustomEvent) => {
        receivedEvent = e;
      }) as EventListener);

      el.projUpdated(project);

      expect(receivedEvent).not.toBeNull();
      expect(receivedEvent!.detail.project).toEqual(project);
      expect(receivedEvent!.bubbles).toBe(true);
      expect(receivedEvent!.composed).toBe(true);
    });

    it('projAdded dispatches project-added event', () => {
      const el = createElement();
      el['_project'] = {
        ProjectId: 0,
        ProjectName: 'NewProject'
      };

      Object.defineProperty(el, 'dialog', {
        value: { close: vi.fn(), open: false },
        writable: true
      });

      let receivedEvent: CustomEvent | null = null;
      el.addEventListener('project-added', ((e: CustomEvent) => {
        receivedEvent = e;
      }) as EventListener);

      el.projAdded();

      expect(receivedEvent).not.toBeNull();
      expect(receivedEvent!.detail.project.ProjectName).toBe('NewProject');
    });
  });

  describe('isGitHub getter', () => {
    it('returns true when SourceControlType is GitHub', () => {
      const el = createElement();
      el['_project'] = { SourceControlType: SourceControlType.GitHub };
      expect(el['isGitHub']).toBe(true);
    });

    it('returns false when SourceControlType is AzureDevOps', () => {
      const el = createElement();
      el['_project'] = { SourceControlType: SourceControlType.AzureDevOps };
      expect(el['isGitHub']).toBe(false);
    });

    it('returns false when SourceControlType is FileShare', () => {
      const el = createElement();
      el['_project'] = { SourceControlType: SourceControlType.FileShare };
      expect(el['isGitHub']).toBe(false);
    });

    it('returns false when SourceControlType is undefined', () => {
      const el = createElement();
      el['_project'] = {};
      expect(el['isGitHub']).toBe(false);
    });
  });

  describe('isFileShare getter', () => {
    it('returns true when SourceControlType is FileShare', () => {
      const el = createElement();
      el['_project'] = { SourceControlType: SourceControlType.FileShare };
      expect(el['isFileShare']).toBe(true);
    });

    it('returns false when SourceControlType is AzureDevOps', () => {
      const el = createElement();
      el['_project'] = { SourceControlType: SourceControlType.AzureDevOps };
      expect(el['isFileShare']).toBe(false);
    });

    it('returns false when SourceControlType is GitHub', () => {
      const el = createElement();
      el['_project'] = { SourceControlType: SourceControlType.GitHub };
      expect(el['isFileShare']).toBe(false);
    });
  });

  describe('conditional field visibility', () => {
    it('showSubPaths is true for AzureDevOps', () => {
      const el = createElement();
      el['_project'] = { SourceControlType: SourceControlType.AzureDevOps };
      expect(el['showSubPaths']).toBe(true);
    });

    it('showSubPaths is true for GitHub', () => {
      const el = createElement();
      el['_project'] = { SourceControlType: SourceControlType.GitHub };
      expect(el['showSubPaths']).toBe(true);
    });

    it('showSubPaths is false for FileShare', () => {
      const el = createElement();
      el['_project'] = { SourceControlType: SourceControlType.FileShare };
      expect(el['showSubPaths']).toBe(false);
    });

    it('showBuildRegex is true for AzureDevOps', () => {
      const el = createElement();
      el['_project'] = { SourceControlType: SourceControlType.AzureDevOps };
      expect(el['showBuildRegex']).toBe(true);
    });

    it('showBuildRegex is true for GitHub', () => {
      const el = createElement();
      el['_project'] = { SourceControlType: SourceControlType.GitHub };
      expect(el['showBuildRegex']).toBe(true);
    });

    it('showBuildRegex is false for FileShare', () => {
      const el = createElement();
      el['_project'] = { SourceControlType: SourceControlType.FileShare };
      expect(el['showBuildRegex']).toBe(false);
    });
  });

  describe('label switching based on SourceControlType', () => {
    it('urlLabel returns GitHub label when isGitHub', () => {
      const el = createElement();
      el['_project'] = { SourceControlType: SourceControlType.GitHub };
      expect(el['urlLabel']).toContain('GitHub API URL');
    });

    it('urlLabel returns Azure DevOps label when AzureDevOps', () => {
      const el = createElement();
      el['_project'] = { SourceControlType: SourceControlType.AzureDevOps };
      expect(el['urlLabel']).toContain('Azure DevOps');
    });

    it('urlLabel returns File Share label when FileShare', () => {
      const el = createElement();
      el['_project'] = { SourceControlType: SourceControlType.FileShare };
      expect(el['urlLabel']).toContain('File Share');
    });

    it('subPathsLabel returns GitHub label when isGitHub', () => {
      const el = createElement();
      el['_project'] = { SourceControlType: SourceControlType.GitHub };
      expect(el['subPathsLabel']).toContain('GitHub Workflow');
    });

    it('subPathsLabel returns Azure DevOps label when AzureDevOps', () => {
      const el = createElement();
      el['_project'] = { SourceControlType: SourceControlType.AzureDevOps };
      expect(el['subPathsLabel']).toContain('Azure DevOps');
    });

    it('subPathsLabel returns Sub-paths label when FileShare', () => {
      const el = createElement();
      el['_project'] = { SourceControlType: SourceControlType.FileShare };
      expect(el['subPathsLabel']).toContain('Sub-paths');
    });

    it('buildRegexLabel returns Workflow Name Regex when isGitHub', () => {
      const el = createElement();
      el['_project'] = { SourceControlType: SourceControlType.GitHub };
      expect(el['buildRegexLabel']).toBe('Workflow Name Regex');
    });

    it('buildRegexLabel returns Build Definition Regex when AzureDevOps', () => {
      const el = createElement();
      el['_project'] = { SourceControlType: SourceControlType.AzureDevOps };
      expect(el['buildRegexLabel']).toBe('Build Definition Regex');
    });

    it('buildRegexLabel returns Build Filter Regex when FileShare', () => {
      const el = createElement();
      el['_project'] = { SourceControlType: SourceControlType.FileShare };
      expect(el['buildRegexLabel']).toBe('Build Filter Regex');
    });
  });

  describe('_sourceControlTypeChanged', () => {
    it('updates SourceControlType from string value', () => {
      const el = createElement();
      el['_project'] = {
        ProjectName: 'TestProj',
        SourceControlType: SourceControlType.AzureDevOps
      };
      el['_sourceControlTypeChanged']({ target: { value: 'GitHub' } });
      expect(el['_project'].SourceControlType).toBe('GitHub');
    });

    it('updates to FileShare string value', () => {
      const el = createElement();
      el['_project'] = {
        ProjectName: 'TestProj',
        SourceControlType: SourceControlType.AzureDevOps
      };
      el['_sourceControlTypeChanged']({ target: { value: 'FileShare' } });
      expect(el['_project'].SourceControlType).toBe('FileShare');
    });

    it('skips update when value is same as current (no-op guard)', () => {
      const el = createElement();
      const original = {
        ProjectName: 'TestProj',
        SourceControlType: SourceControlType.AzureDevOps
      };
      el['_project'] = original;
      el['_sourceControlTypeChanged']({ target: { value: 'AzureDevOps' } });
      // Should be the same reference because guard skipped update
      expect(el['_project']).toBe(original);
    });

    it('does nothing when project is undefined', () => {
      const el = createElement();
      el['_project'] = undefined as any;
      // Should not throw
      el['_sourceControlTypeChanged']({ target: { value: 'GitHub' } });
    });

    it('does nothing when target is undefined', () => {
      const el = createElement();
      el['_project'] = { SourceControlType: SourceControlType.AzureDevOps };
      el['_sourceControlTypeChanged']({ target: undefined });
      expect(el['_project'].SourceControlType).toBe(SourceControlType.AzureDevOps);
    });

    it('deep-copies the project when updating SourceControlType', () => {
      const el = createElement();
      const original = {
        ProjectName: 'TestProj',
        SourceControlType: SourceControlType.AzureDevOps
      };
      el['_project'] = original;
      el['_sourceControlTypeChanged']({ target: { value: 'GitHub' } });
      expect(el['_project']).not.toBe(original);
      expect(el['_project'].SourceControlType).toBe('GitHub');
    });
  });
});
