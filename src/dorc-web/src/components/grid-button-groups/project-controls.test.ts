import { describe, it, expect, beforeEach, vi } from 'vitest';
import type { ProjectApiModel } from '../../apis/dorc-api';

// Mock the Vaadin imports to prevent Lumo CSS issues
vi.mock('../../icons/iron-icons.js', () => ({}));

const { ProjectControls } = await import('./project-controls');

const testProject: ProjectApiModel = {
  ProjectId: 42,
  ProjectName: 'TestProject',
  ProjectDescription: 'A test project'
};

// Create element WITHOUT appending to DOM to avoid Vaadin Lumo rendering errors
function createElement(): InstanceType<typeof ProjectControls> {
  const el = document.createElement('project-controls') as InstanceType<typeof ProjectControls>;
  el.project = testProject;
  return el;
}

describe('ProjectControls', () => {
  let el: InstanceType<typeof ProjectControls>;

  beforeEach(() => {
    el = createElement();
  });

  describe('deleteHidden property', () => {
    it('defaults deleteHidden to true', () => {
      expect(el.deleteHidden).toBe(true);
    });

    it('can set deleteHidden to false', () => {
      el.deleteHidden = false;
      expect(el.deleteHidden).toBe(false);
    });
  });

  describe('event dispatching', () => {
    it('openProjectMetadata dispatches open-project-metadata event', () => {
      let received: CustomEvent | null = null;
      el.addEventListener('open-project-metadata', ((e: CustomEvent) => {
        received = e;
      }) as EventListener);

      el.openProjectMetadata();

      expect(received).not.toBeNull();
      expect(received!.detail.Project).toEqual(testProject);
      expect(received!.bubbles).toBe(true);
      expect(received!.composed).toBe(true);
    });

    it('openAccessControl dispatches open-access-control event with project name', () => {
      let received: CustomEvent | null = null;
      el.addEventListener('open-access-control', ((e: CustomEvent) => {
        received = e;
      }) as EventListener);

      el.openAccessControl();

      expect(received).not.toBeNull();
      expect(received!.detail.Name).toBe('TestProject');
    });

    it('openEnvironmentDetails dispatches open-project-envs event', () => {
      let received: CustomEvent | null = null;
      el.addEventListener('open-project-envs', ((e: CustomEvent) => {
        received = e;
      }) as EventListener);

      el.openEnvironmentDetails();

      expect(received).not.toBeNull();
      expect(received!.detail.Project).toEqual(testProject);
    });

    it('openRefData dispatches open-project-ref-data event', () => {
      let received: CustomEvent | null = null;
      el.addEventListener('open-project-ref-data', ((e: CustomEvent) => {
        received = e;
      }) as EventListener);

      el.openRefData();

      expect(received).not.toBeNull();
      expect(received!.detail.Project).toEqual(testProject);
    });

    it('openAuditData dispatches open-project-audit-data event', () => {
      let received: CustomEvent | null = null;
      el.addEventListener('open-project-audit-data', ((e: CustomEvent) => {
        received = e;
      }) as EventListener);

      el.openAuditData();

      expect(received).not.toBeNull();
      expect(received!.detail.Project).toEqual(testProject);
    });

    it('deleteProject dispatches delete-project event', () => {
      let received: CustomEvent | null = null;
      el.addEventListener('delete-project', ((e: CustomEvent) => {
        received = e;
      }) as EventListener);

      el.deleteProject();

      expect(received).not.toBeNull();
      expect(received!.detail.Project).toEqual(testProject);
    });
  });

  describe('event properties when project is undefined', () => {
    it('dispatches event with undefined project detail', () => {
      el.project = undefined;
      let received: CustomEvent | null = null;
      el.addEventListener('open-project-metadata', ((e: CustomEvent) => {
        received = e;
      }) as EventListener);

      el.openProjectMetadata();

      expect(received).not.toBeNull();
      expect(received!.detail.Project).toBeUndefined();
    });

    it('dispatches access control with undefined name', () => {
      el.project = undefined;
      let received: CustomEvent | null = null;
      el.addEventListener('open-access-control', ((e: CustomEvent) => {
        received = e;
      }) as EventListener);

      el.openAccessControl();

      expect(received!.detail.Name).toBeUndefined();
    });
  });
});
