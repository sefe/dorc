import { confirmPrompt } from '../../../../../src/components/confirm-prompt';

export class Fixture {
  private serverId = 0;

  deleteServer() {
    void confirmPrompt('Delete?').then(ok => {
      if (ok) this.performDelete(this.serverId);
    });
  }

  private performDelete(id: number) {
    return id;
  }
}
