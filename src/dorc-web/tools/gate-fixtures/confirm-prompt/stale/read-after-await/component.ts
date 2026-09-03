import { confirmPrompt } from '../../../../../src/components/confirm-prompt';

export class Fixture {
  private serverId = 0;

  // The grid can recycle this cell while the dialog is open, so `serverId`
  // afterwards may name a different row than the prompt did.
  async deleteServer() {
    if (!(await confirmPrompt('Delete?'))) return;
    this.performDelete(this.serverId);
  }

  private performDelete(id: number) {
    return id;
  }
}
