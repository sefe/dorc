import { confirmPrompt } from '../../../../../src/components/confirm-prompt';

export class Fixture {
  private serverId = 0;

  // The whole post-confirmation body is elsewhere. Following it is the
  // delegate hop this gate deliberately does not take, so it must report the
  // site as uncheckable rather than silently pass it.
  deleteServer() {
    void confirmPrompt('Delete?').then(this.onAnswer);
  }

  private onAnswer = (ok: boolean) => {
    if (ok) this.performDelete(this.serverId);
  };

  private performDelete(id: number) {
    return id;
  }
}
