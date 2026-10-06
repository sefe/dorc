import { confirmPrompt } from '../../../../../src/components/confirm-prompt';

export class Fixture {
  private serverId = 0;

  // A `.then` inside a constructor. `enclosingFunction` did not stop at
  // constructors, so the report line dereferenced `undefined.name` and took
  // the whole gate — and `npm run build` with it — down with a stack trace.
  constructor() {
    void confirmPrompt('Delete?').then(ok => {
      if (ok) this.performDelete(this.serverId);
    });
  }

  private performDelete(id: number) {
    return id;
  }
}
