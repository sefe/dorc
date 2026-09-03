// Fixture: the passing forms. Input for tools/gate-check.mjs, not app code.
import { confirmPrompt } from '../../../../src/components/confirm-prompt';

export class Fixture {
  private serverId = 0;

  private busy = false;

  declare shadowRoot: ShadowRoot | null;

  // Snapshotted before the await, used from then on.
  async deleteServer() {
    const id = this.serverId;
    if (!(await confirmPrompt(`Delete ${id}?`))) return;
    this.performDelete(id);
  }

  // `shadowRoot` is the element itself, not anything it was handed.
  async notifyAfter() {
    if (!(await confirmPrompt('Sure?'))) return;
    this.shadowRoot?.appendChild(document.createElement('div'));
  }

  // A write is not a stale read, and a call is behaviour rather than identity.
  async refreshAfter() {
    if (!(await confirmPrompt('Sure?'))) return;
    this.busy = false;
    this.reload();
  }

  // The inline `.then` form, with the snapshot taken first.
  thenForm() {
    const id = this.serverId;
    void confirmPrompt('Sure?').then(ok => {
      if (ok) this.performDelete(id);
    });
  }

  private performDelete(id: number) {
    return id;
  }

  private reload() {}
}
