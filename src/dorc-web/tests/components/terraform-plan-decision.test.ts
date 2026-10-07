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
const { TerraformPlanDialog: PlanDialog } =
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

describe('Terraform plan summary', () => {
  it('lifts the add/change/destroy counts out of the plan text', () => {
    expect(
      PlanDialog.summarisePlan(
        '  # azurerm_storage_account.this will be created\n\nPlan: 2 to add, 1 to change, 3 to destroy.\n'
      )
    ).to.deep.equal({ add: 2, change: 1, destroy: 3 });
  });

  it('recognises a plan with no changes and stays quiet otherwise', () => {
    expect(
      PlanDialog.summarisePlan(
        'No changes. Your infrastructure matches the configuration.'
      )
    ).to.equal('none');
    expect(PlanDialog.summarisePlan('Error: something broke')).to.equal(null);
    expect(PlanDialog.summarisePlan(null)).to.equal(null);
  });

  it('renders the summary chips above the plan body', async () => {
    mocks.load.mockReturnValue(
      of({
        DeploymentResultId: 42,
        Status: 'WaitingConfirmation',
        PlanContent:
          '+ storage account\n\nPlan: 1 to add, 0 to change, 0 to destroy.'
      })
    );
    const host = await fixture<InstanceType<typeof TerraformPlanDialog>>(
      html`<terraform-plan-dialog></terraform-plan-dialog>`
    );
    host.open(42);
    await settle();
    const dialog = host.shadowRoot!.querySelector('vaadin-dialog')!;
    const chips = Array.from(dialog.querySelectorAll('.plan-summary__chip'));
    expect(
      chips.map(c => c.textContent?.replace(/\s+/g, ' ').trim())
    ).to.deep.equal(['1 to add', '0 to change', '0 to destroy']);
    expect(chips[0].classList.contains('add')).to.equal(true);
    expect(chips[2].classList.contains('none')).to.equal(true);
  });
});
