import { describe, it, expect, vi, beforeEach } from 'vitest';

const { mockApiDelete, mockSubscribe, mockDetailsGet } = vi.hoisted(() => {
  const mockSubscribe = vi.fn();
  const mockApiDelete = vi.fn(() => ({ subscribe: mockSubscribe }));
  const mockDetailsGet = vi.fn(() => ({ subscribe: vi.fn() }));
  return { mockApiDelete, mockSubscribe, mockDetailsGet };
});

vi.mock('@vaadin/button', () => ({}));
vi.mock('@vaadin/details', () => ({}));
vi.mock('@vaadin/dialog', () => ({}));
vi.mock('@vaadin/dialog/lit', () => ({
  dialogRenderer: () => undefined,
  dialogFooterRenderer: () => undefined
}));
vi.mock('@vaadin/grid', () => ({}));
vi.mock('@vaadin/grid/vaadin-grid', () => ({}));
vi.mock('@vaadin/grid/vaadin-grid-column', () => ({}));
vi.mock('@vaadin/grid/vaadin-grid-sort-column', () => ({}));
vi.mock('@vaadin/icon', () => ({}));
vi.mock('@vaadin/notification', () => ({
  Notification: { show: vi.fn() }
}));
vi.mock('@vaadin/text-field', () => ({}));
vi.mock('@vaadin/text-area', () => ({}));
vi.mock('@vaadin/combo-box', () => ({}));
vi.mock('@vaadin/vertical-layout', () => ({}));

vi.mock('../add-edit-api', () => ({
  AddEditApi: class {}
}));
vi.mock('../notifications/error-notification', () => ({
  ErrorNotification: class {}
}));
vi.mock('../add-edit-environment', () => ({}));
vi.mock('../model-extensions/EnvironmentContentBuildsApiModelExtended', () => ({
  EnvironmentContentBuildsApiModelExtended: class {}
}));

vi.mock('../../apis/dorc-api', () => ({
  RefDataApisApi: class {
    refDataApisDelete = mockApiDelete;
  },
  RefDataEnvironmentsDetailsApi: class {
    refDataEnvironmentsDetailsIdGet = mockDetailsGet;
  },
  RefDataEnvironmentsApi: class {
    refDataEnvironmentsGet = vi.fn(() => ({ subscribe: vi.fn() }));
  }
}));

vi.mock('../../apis/dorc-api/models/ApiApiModel', () => ({
  ApiEndpointResolutionStatus: {
    NoTokens: 'NoTokens',
    Resolved: 'Resolved',
    PartiallyResolved: 'PartiallyResolved'
  }
}));

import { EnvApis } from './env-apis';

describe('env-apis', () => {
  let element: EnvApis;

  beforeEach(() => {
    mockApiDelete.mockClear();
    mockSubscribe.mockClear();
    mockDetailsGet.mockClear();
    element = new EnvApis();
  });

  it('sorts apis by name when env content is applied', () => {
    const ready = element as unknown as {
      apis: Array<{ Name?: string }>;
      applyEnvContentApis: (content: unknown) => void;
    };
    ready.applyEnvContentApis({
      Apis: [
        { Id: 1, Name: 'Zeta' },
        { Id: 2, Name: 'Alpha' }
      ]
    });
    expect(ready.apis.map(a => a.Name)).toEqual(['Alpha', 'Zeta']);
  });

  it('deleteApi calls RefDataApisApi.refDataApisDelete with the api id', () => {
    const ready = element as unknown as {
      deleteApi: (api: { Id: number; Name: string }) => void;
    };
    const originalConfirm = window.confirm;
    window.confirm = () => true;
    try {
      ready.deleteApi({ Id: 42, Name: 'Orders' });
    } finally {
      window.confirm = originalConfirm;
    }
    expect(mockApiDelete).toHaveBeenCalledWith({ id: 42 });
    expect(mockSubscribe).toHaveBeenCalled();
  });

  it('deleteApi is a no-op when the user cancels confirm', () => {
    const ready = element as unknown as {
      deleteApi: (api: { Id: number; Name: string }) => void;
    };
    const originalConfirm = window.confirm;
    window.confirm = () => false;
    try {
      ready.deleteApi({ Id: 42, Name: 'Orders' });
    } finally {
      window.confirm = originalConfirm;
    }
    expect(mockApiDelete).not.toHaveBeenCalled();
  });
});
