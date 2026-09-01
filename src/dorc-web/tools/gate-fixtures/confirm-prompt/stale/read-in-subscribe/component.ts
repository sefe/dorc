import { confirmPrompt } from '../../../../../src/components/confirm-prompt';

export class Fixture {
  private serverId = 0;

  // The network round trip is a second window in which the row can change.
  async deleteServer() {
    const id = this.serverId;
    if (!(await confirmPrompt('Delete?'))) return;
    void this.api(id).then(() => {
      this.notify(this.serverId);
    });
  }

  private api(id: number) {
    return Promise.resolve(id);
  }

  private notify(id: number) {
    return id;
  }
}
