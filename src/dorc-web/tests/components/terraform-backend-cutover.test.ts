import { afterEach, beforeEach, describe, it, vi } from 'vitest';
import { of, throwError } from 'rxjs';
import type { Button } from '@vaadin/button';
import type { Checkbox } from '@vaadin/checkbox';
import type { TextArea } from '@vaadin/text-area';
import type { TextField } from '@vaadin/text-field';
import { expect, fixture, html } from '../_helpers';
import {
  TerraformBackendCutoversApi,
  type TerraformBackendCutoverPreviewApiModel
} from '../../src/apis/dorc-api';
import '../../src/components/terraform-backend-cutover';
import type { TerraformBackendCutover } from '../../src/components/terraform-backend-cutover';

const sha = 'f'.repeat(64);

const preview: TerraformBackendCutoverPreviewApiModel = {
  Revision: 'REV1',
  EnvironmentId: 7,
  EnvironmentName: 'Terraform Dev',
  CurrentBackend: {
    EnvironmentId: 7,
    StorageAccount: 'oldstate',
    ContainerName: 'old-container',
    SubscriptionId: 'sub-1',
    ResourceGroup: 'rg-old'
  },
  LegacyRequests: [],
  PinnedRequestCount: 3,
  Blockers: []
};

function mockPreview(value: TerraformBackendCutoverPreviewApiModel) {
  vi.spyOn(
    TerraformBackendCutoversApi.prototype,
    'projectsProjectIdTerraformStateBackendCutoversPreviewGet'
  ).mockReturnValue(of(value) as never);
}

async function open(): Promise<TerraformBackendCutover> {
  const element = await fixture<TerraformBackendCutover>(
    html`<terraform-backend-cutover
      .projectId=${5}
      .environmentId=${7}
      environmentName="Terraform Dev"
    ></terraform-backend-cutover>`
  );
  await element.updateComplete;
  await element.updateComplete;
  return element;
}

function field(element: TerraformBackendCutover, key: string) {
  return element.shadowRoot!.querySelector<TextField>(
    `vaadin-text-field[data-field="${key}"]`
  )!;
}

async function type(
  element: TerraformBackendCutover,
  key: string,
  value: string
) {
  const input = field(element, key);
  input.value = value;
  input.dispatchEvent(new Event('input'));
  await element.updateComplete;
}

async function fillValidForm(element: TerraformBackendCutover) {
  await type(element, 'StorageAccount', 'newstate');
  await type(element, 'ContainerName', 'new-container');
  await type(element, 'SubscriptionId', 'sub-1');
  await type(element, 'VerificationSha256', sha);
  const checkbox =
    element.shadowRoot!.querySelector<Checkbox>('vaadin-checkbox')!;
  checkbox.checked = true;
  await element.updateComplete;
}

describe('terraform-backend-cutover', () => {
  beforeEach(() => {
    vi.spyOn(
      TerraformBackendCutoversApi.prototype,
      'projectsProjectIdTerraformStateBackendCutoversPost'
    ).mockReturnValue(of({ Revision: 'REV2' }) as never);
  });
  afterEach(() => vi.restoreAllMocks());

  it('requests the preview for the project and environment', async () => {
    mockPreview(preview);
    const element = await open();
    const get = vi.mocked(
      TerraformBackendCutoversApi.prototype
        .projectsProjectIdTerraformStateBackendCutoversPreviewGet
    );
    expect(get.mock.calls[0][0]).to.deep.equal({
      projectId: 5,
      environmentId: 7
    });
    expect(element.shadowRoot!.textContent).to.include('oldstate');
    expect(element.shadowRoot!.textContent).to.include(
      '3 request(s) are pinned'
    );
  });

  it('keeps Review disabled until the attestation is complete and the backend differs', async () => {
    mockPreview(preview);
    const element = await open();
    const review = () =>
      element.shadowRoot!.querySelector<Button>('vaadin-button.review')!;
    expect(review().disabled).to.equal(true);

    await type(element, 'StorageAccount', 'oldstate');
    await type(element, 'ContainerName', 'old-container');
    await type(element, 'SubscriptionId', 'sub-1');
    await type(element, 'VerificationSha256', sha);
    element.shadowRoot!.querySelector<Checkbox>('vaadin-checkbox')!.checked =
      true;
    await element.updateComplete;
    expect(element.validationProblems).to.deep.equal([
      'The new backend is identical to the current backend.'
    ]);
    expect(review().disabled).to.equal(true);

    await type(element, 'ContainerName', 'new-container');
    expect(element.validationProblems).to.deep.equal([]);
    expect(review().disabled).to.equal(false);
  });

  it('surfaces server blockers and never enables Review while any remain', async () => {
    mockPreview({
      ...preview,
      Blockers: [
        'All requests for this project/environment must be terminal before a cutover; found 1 active.'
      ]
    });
    const element = await open();
    await fillValidForm(element);
    expect(
      element.shadowRoot!.querySelector('.blockers')!.textContent
    ).to.include('must be terminal');
    expect(
      element.shadowRoot!.querySelector<Button>('vaadin-button.review')!
        .disabled
    ).to.equal(true);
  });

  it('requires a state disposition when legacy requests exist and sends them with the cutover', async () => {
    mockPreview({
      ...preview,
      LegacyRequests: [
        {
          Id: 101,
          Status: 'Completed',
          UserName: 'alice',
          Components: 'service-bus'
        },
        { Id: 102, Status: 'Failed', UserName: 'bob', Components: 'kafka' }
      ]
    });
    const element = await open();
    await fillValidForm(element);
    expect(element.validationProblems[0]).to.include('state disposition');

    const area =
      element.shadowRoot!.querySelector<TextArea>('vaadin-text-area')!;
    area.value =
      'Old container verified empty; both legacy runs left no state objects behind.';
    area.dispatchEvent(new Event('input'));
    await element.updateComplete;
    expect(element.validationProblems).to.deep.equal([]);

    element.shadowRoot!.querySelector<Button>('vaadin-button.review')!.click();
    await element.updateComplete;
    const completed = new Promise<CustomEvent>(resolve =>
      element.addEventListener(
        'terraform-backend-cutover-completed',
        e => resolve(e as CustomEvent),
        { once: true }
      )
    );
    element.shadowRoot!.querySelector<Button>('vaadin-button.submit')!.click();
    const event = await completed;

    const post = vi.mocked(
      TerraformBackendCutoversApi.prototype
        .projectsProjectIdTerraformStateBackendCutoversPost
    );
    const body = post.mock.calls[0][0].terraformBackendCutoverApiModel!;
    expect(body.Revision).to.equal('REV1');
    expect(body.EnvironmentId).to.equal(7);
    expect(body.ExpectedBackend).to.deep.equal(preview.CurrentBackend);
    expect(body.Backend).to.deep.equal({
      EnvironmentId: 7,
      StorageAccount: 'newstate',
      ContainerName: 'new-container',
      SubscriptionId: 'sub-1',
      ResourceGroup: 'rg-old'
    });
    expect(body.ConfirmBothContainersEmpty).to.equal(true);
    expect(body.VerificationSha256).to.equal(sha);
    expect(body.LegacyRetirement!.RequestIds).to.deep.equal([101, 102]);
    expect(event.detail).to.deep.equal({ Revision: 'REV2' });
  });

  it('shows the server rejection verbatim and returns to the edit step', async () => {
    mockPreview(preview);
    vi.mocked(
      TerraformBackendCutoversApi.prototype
        .projectsProjectIdTerraformStateBackendCutoversPost
    ).mockReturnValue(
      throwError(() => ({
        status: 409,
        response: 'Archived deployment history exists for this environment.'
      })) as never
    );
    const element = await open();
    await fillValidForm(element);
    element.shadowRoot!.querySelector<Button>('vaadin-button.review')!.click();
    await element.updateComplete;
    element.shadowRoot!.querySelector<Button>('vaadin-button.submit')!.click();
    await element.updateComplete;
    await element.updateComplete;
    expect(element.shadowRoot!.querySelector('.error')!.textContent).to.include(
      'Archived deployment history'
    );
    expect(
      element.shadowRoot!.querySelector('vaadin-button.review')
    ).to.not.equal(null);
  });
});
