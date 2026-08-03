import { expect, settle } from '../_helpers';
import { render } from 'lit';
import '../../src/pages/page-scripts-list';

type Script = {
  Id?: number;
  IsEnabled?: boolean;
  PowerShellVersionNumber?: string;
};

type Page = HTMLElement & {
  userRoles: string[];
  powerShellVersions: string[];
  psVersionRenderer(script: Script): unknown;
};

describe('probe scripts psVersionRenderer', () => {
  it('probe', async () => {
    const el = document.createElement('page-scripts-list') as Page;
    el.userRoles = ['Admin'];
    el.powerShellVersions = ['5.1', '7.4'];
    await settle();

    const host = document.createElement('div');
    document.body.appendChild(host);

    const script: Script = { Id: 1, PowerShellVersionNumber: '5.1' };
    render(el.psVersionRenderer(script) as never, host);
    await settle();

    const combo = host.querySelector('vaadin-combo-box') as HTMLElement & {
      value: string;
      disabled: boolean;
      items: string[];
    };
    const info: string[] = [];
    info.push(
      `roles=${JSON.stringify(el.userRoles)} psv=${JSON.stringify(el.powerShellVersions)}`
    );
    info.push(
      `combo present=${!!combo} disabled=${combo?.disabled} value=${combo?.value} items=${JSON.stringify(combo?.items)}`
    );
    combo.addEventListener('value-changed', e =>
      info.push(`vc detail=${JSON.stringify((e as CustomEvent).detail)}`)
    );
    combo.value = '7.4';
    await settle();
    info.push(`after: combo.value=${combo.value} model=${script.PowerShellVersionNumber}`);
    console.log('PROBE ' + JSON.stringify(info, null, 1));
    host.remove();
    expect(1).to.equal(-1);
  });
});
