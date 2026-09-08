import { of, Subject } from 'rxjs';
import { vi } from 'vitest';
import { expect, fixture, html, settle } from '../_helpers';
import type { TerraformPlanDialog } from '../../src/components/terraform-plan-dialog';

const mocks = vi.hoisted(() => ({
  load: vi.fn(),
  confirm: vi.fn(),
  decline: vi.fn()
}));
vi.mock('../../src/apis/dorc-api/apis/TerraformApi', () => ({
  TerraformApi: class {
    terraformPlanDeploymentResultIdGet = mocks.load;
    terraformPlanDeploymentResultIdConfirmPost = mocks.confirm;
    terraformPlanDeploymentResultIdDeclinePost = mocks.decline;
  }
}));
await import('../../src/components/terraform-plan-dialog');

beforeEach(() => {
  vi.clearAllMocks();
  mocks.load.mockReturnValue(
    of({
      DeploymentResultId: 42,
      Status: 'WaitingConfirmation',
      PlanContent: '+ storage account'
    })
  );
});

describe('Terraform plan decisions', () => {
  for (const confirm of [true, false]) {
    it(`waits for a successful ${confirm ? 'confirmation' : 'decline'} and permits retry after failure`, async () => {
      const response = new Subject<void>();
      const operation = confirm ? mocks.confirm : mocks.decline;
      operation.mockReturnValue(response);
      const host = await fixture<InstanceType<typeof TerraformPlanDialog>>(
        html`<terraform-plan-dialog></terraform-plan-dialog>`
      );
      const completed = vi.fn();
      host.addEventListener(
        confirm ? 'terraform-plan-confirmed' : 'terraform-plan-declined',
        completed
      );
      host.open(42);
      await settle();
      const dialog = host.shadowRoot!.querySelector('vaadin-dialog')!;
      const action = () =>
        Array.from(dialog.querySelectorAll('vaadin-button')).find(
          b =>
            b.textContent?.trim() === (confirm ? 'Confirm & Apply' : 'Decline')
        ) as HTMLElement;
      action().click();
      action().click();
      await settle();
      expect(operation.mock.calls).to.have.length(1);
      expect(completed.mock.calls).to.have.length(0);
      expect(host.opened).to.equal(true);
      response.error({ response: 'The server rejected the decision.' });
      await settle();
      expect(dialog.querySelector('[role="alert"]')?.textContent).to.contain(
        'server rejected'
      );
      expect(host.opened).to.equal(true);
      expect(completed.mock.calls).to.have.length(0);
      operation.mockReturnValue(of(undefined));
      action().click();
      await settle();
      expect(completed.mock.calls).to.have.length(1);
      expect(host.opened).to.equal(false);
    });
  }
});
