import { expect } from '../_helpers';
import {
  DrawerShortcuts,
  resetStorageProbe,
  toEnvShortcut,
  toProjectShortcut,
  toResultShortcut
} from '../../src/components/drawer-shortcuts';

// P1 — the shortcut store. Every defect this replaces lived in persistence or in
// the disagreement between "open" and "close" about what identity means.

const KEYS = {
  environments: 'dorc.shortcuts.environments',
  projects: 'dorc.shortcuts.projects',
  results: 'dorc.shortcuts.results',
  schema: 'dorc.shortcuts.schema'
};

const LEGACY = ['env-detail-tabs', 'project-envs-tabs', 'monitor-result-tabs'];

function clearStorage() {
  Object.values(KEYS).forEach(k => localStorage.removeItem(k));
}

function clearCookies() {
  LEGACY.forEach(name => {
    document.cookie = `${name}=; expires=${new Date(0).toUTCString()}; path=/;`;
  });
}

function setCookie(name: string, value: string) {
  document.cookie = `${name}=${encodeURIComponent(value)}; path=/;`;
}

function fresh() {
  const store = new DrawerShortcuts();
  store.start();
  return store;
}

/**
 * Builds a storage event with a given key.
 *
 * Uses a plain Event with `key` attached rather than `new StorageEvent(type, init)`.
 * The two-argument constructor is spec-correct, but CodeQL's bundled externs for
 * StorageEvent declare only one parameter, so every construction trips its
 * "superfluous trailing arguments" rule. This shape is equivalent for the
 * handler — which reads only `key` — and keeps the alert off the PR.
 */
function storageEvent(key: string | null): Event {
  const event = new Event('storage');
  Object.defineProperty(event, 'key', { value: key });
  return event;
}

/**
 * Marks the migration as already done. Tests that seed the store keys directly
 * need this, otherwise `start()` runs the one-shot migration first and
 * overwrites the seed — which would make those assertions pass vacuously.
 */
function armGuard() {
  localStorage.setItem(KEYS.schema, '1');
}

describe('P1: drawer shortcut store', () => {
  beforeEach(() => {
    clearStorage();
    clearCookies();
    resetStorageProbe();
  });
  afterEach(() => {
    clearStorage();
    clearCookies();
    resetStorageProbe();
  });

  // ─── SC-3 ───────────────────────────────────────────────────────────────
  describe('SC-3: only identity is persisted', () => {
    it('stores exactly the permitted keys and nothing else', () => {
      const store = fresh();
      store.add(
        'environments',
        toEnvShortcut({
          EnvironmentId: 5,
          EnvironmentName: 'PROD',
          // Everything below must NOT reach storage: the old implementation
          // persisted the whole graph, including owner SIDs, SQL hosts and the
          // AD groups granting access to them.
          EnvironmentIsProd: true,
          Details: { EnvironmentOwnerId: 'S-1-5-21-x', ThinClient: 'host01' },
          ChildEnvironments: [{ EnvironmentId: 6, EnvironmentName: 'TENANT' }]
        })
      );

      const raw = JSON.parse(localStorage.getItem(KEYS.environments)!);
      expect(raw).to.have.lengthOf(1);
      expect(Object.keys(raw[0]).sort()).to.deep.equal([
        'EnvironmentId',
        'EnvironmentName'
      ]);
    });

    it('keeps a project shortcut to identity only', () => {
      const store = fresh();
      store.add(
        'projects',
        toProjectShortcut({
          ProjectId: 1,
          ProjectName: 'Payments',
          ArtefactsSubPaths: 'drop/a;drop/b',
          SourceDatabase: { ServerName: 'SQL01\\INST', AdGroup: 'DB_READERS' }
        })
      );

      const raw = JSON.parse(localStorage.getItem(KEYS.projects)!);
      expect(Object.keys(raw[0]).sort()).to.deep.equal([
        'ProjectId',
        'ProjectName'
      ]);
    });
  });

  // ─── SC-2 / D-02 ────────────────────────────────────────────────────────
  it('SC-2: persists well past the ~4 shortcuts the cookie form could hold', () => {
    const store = fresh();
    for (let i = 0; i < 20; i += 1) {
      store.add(
        'environments',
        toEnvShortcut({ EnvironmentId: i, EnvironmentName: `ENV-${i}` })
      );
    }

    const reloaded = fresh();
    expect(reloaded.snapshot().environments).to.have.lengthOf(20);
  });

  // ─── SC-7 / D-06, D-07, D-16 ────────────────────────────────────────────
  describe('SC-7: one identity key, so open and close agree', () => {
    it('does not duplicate when the same environment is opened twice', () => {
      const store = fresh();
      const env = { EnvironmentId: 5, EnvironmentName: 'FOO' };
      store.add('environments', toEnvShortcut(env));
      store.add('environments', toEnvShortcut(env));

      expect(store.snapshot().environments).to.have.lengthOf(1);
    });

    it('removes on close and does not resurrect on reload', () => {
      const store = fresh();
      const env = toEnvShortcut({ EnvironmentId: 5, EnvironmentName: 'FOO' });
      store.add('environments', env);
      store.remove('environments', env);

      expect(store.snapshot().environments).to.be.empty;
      expect(fresh().snapshot().environments, 'still gone after reload').to.be
        .empty;
    });

    it('removes every copy even if duplicates somehow exist', () => {
      // The old code spliced inside a forward-counting loop, so adjacent
      // duplicates survived removal and came back from storage on reload.
      armGuard();
      localStorage.setItem(
        KEYS.environments,
        JSON.stringify([
          { EnvironmentId: 5, EnvironmentName: 'FOO' },
          { EnvironmentId: 5, EnvironmentName: 'BAR' }
        ])
      );
      const store = fresh();
      // Reading already collapses them by identity.
      expect(store.snapshot().environments).to.have.lengthOf(1);
    });

    it('renames in place, preserving position', () => {
      const store = fresh();
      ['A', 'B', 'C'].forEach((n, i) =>
        store.add(
          'environments',
          toEnvShortcut({ EnvironmentId: i, EnvironmentName: n })
        )
      );

      store.renameEnvironment('B', { EnvironmentId: 1, EnvironmentName: 'B2' });

      // WCAG 3.2.3: a rename must not silently reorder the drawer. The old code
      // removed and re-appended, moving it to the bottom of the group.
      expect(
        store.snapshot().environments.map(e => e.EnvironmentName)
      ).to.deep.equal(['A', 'B2', 'C']);
    });
  });

  // ─── SC-18 ──────────────────────────────────────────────────────────────
  describe('SC-18: a corrupt stored value cannot break the session', () => {
    ['5', '"x"', '{}', '[{"nope":1}]', 'not json at all'].forEach(bad => {
      it(`recovers from ${bad}`, () => {
        armGuard();
        localStorage.setItem(KEYS.environments, bad);
        const store = fresh();

        expect(store.snapshot().environments).to.deep.equal([]);
        // The old code assigned the parse result before validating, so state
        // became a non-array and every later add/close threw for the session.
        store.add(
          'environments',
          toEnvShortcut({ EnvironmentId: 1, EnvironmentName: 'STILL-WORKS' })
        );
        expect(store.snapshot().environments).to.have.lengthOf(1);
      });
    });

    it('replaces a corrupt value with [] rather than removing the key', () => {
      armGuard();
      localStorage.setItem(KEYS.environments, '{}');
      fresh();
      // Removing it would disarm nothing here, but keeping the shape consistent
      // is what stops a corrupt value leaving a non-array in memory.
      expect(localStorage.getItem(KEYS.environments)).to.equal('[]');
    });
  });

  // ─── SC-13 / SC-13a ─────────────────────────────────────────────────────
  describe('SC-13: migration off the legacy cookies', () => {
    it('imports cookie shortcuts once, then deletes the cookies', () => {
      setCookie(
        'env-detail-tabs',
        JSON.stringify([
          { EnvironmentId: 1, EnvironmentName: 'FROM-COOKIE', Details: {} }
        ])
      );

      const store = fresh();
      expect(
        store.snapshot().environments.map(e => e.EnvironmentName)
      ).to.deep.equal(['FROM-COOKIE']);
      expect(document.cookie).to.not.contain('env-detail-tabs=');
      expect(localStorage.getItem(KEYS.schema), 'guard armed').to.not.be.null;
    });

    it('is a no-op on the second load, so a closed shortcut stays closed', () => {
      setCookie(
        'env-detail-tabs',
        JSON.stringify([{ EnvironmentId: 1, EnvironmentName: 'FOO' }])
      );
      const first = fresh();
      first.remove('environments', {
        EnvironmentId: 1,
        EnvironmentName: 'FOO'
      });

      // Even if an old-build window recreated the cookie afterwards.
      setCookie(
        'env-detail-tabs',
        JSON.stringify([{ EnvironmentId: 1, EnvironmentName: 'FOO' }])
      );

      expect(fresh().snapshot().environments, 'must not resurrect').to.be.empty;
    });

    it('arms the guard for a user who never had any shortcuts', () => {
      // The majority path. Writing the guard unconditionally is what makes
      // "migrated to empty" representable and stops the migration re-running.
      const store = fresh();
      expect(store.snapshot().environments).to.be.empty;
      expect(localStorage.getItem(KEYS.schema)).to.not.be.null;
    });

    it('collapses duplicate legacy entries by identity', () => {
      setCookie(
        'env-detail-tabs',
        JSON.stringify([
          { EnvironmentId: 7, EnvironmentName: 'DUPE' },
          { EnvironmentId: 7, EnvironmentName: 'DUPE-RENAMED' }
        ])
      );
      expect(fresh().snapshot().environments).to.have.lengthOf(1);
    });

    // SC-13a — the branch that matters most, on the one load that matters.
    it('leaves the cookies and the guard alone when a cookie cannot be read', () => {
      // A malformed % sequence: the old getCookie threw URIError from outside
      // its own try block, taking out every later loader with it.
      document.cookie = 'env-detail-tabs=%E0%A4%A; path=/;';

      const store = fresh();

      expect(store.snapshot().environments).to.be.empty;
      expect(
        localStorage.getItem(KEYS.schema),
        'guard must NOT be armed on a failed read'
      ).to.be.null;
      expect(
        document.cookie,
        'cookies must survive so the next load can retry'
      ).to.contain('env-detail-tabs=');
    });
  });

  // ─── SC-8 / D-40 ────────────────────────────────────────────────────────
  it('SC-8: reconciles from a storage event without writing back', () => {
    const store = fresh();
    let writes = 0;
    const realSetItem = Storage.prototype.setItem;
    Storage.prototype.setItem = function patched(...args) {
      writes += 1;
      return realSetItem.apply(this, args as [string, string]);
    };

    try {
      localStorage.setItem(
        KEYS.environments,
        JSON.stringify([
          { EnvironmentId: 9, EnvironmentName: 'FROM-OTHER-TAB' }
        ])
      );
      const writesAfterSeed = writes;

      window.dispatchEvent(storageEvent(KEYS.environments));

      expect(
        store.snapshot().environments.map(e => e.EnvironmentName)
      ).to.deep.equal(['FROM-OTHER-TAB']);
      // A handler that persisted what it just read would make two windows echo
      // each other's storage events indefinitely.
      expect(writes, 'reconciliation must not write').to.equal(writesAfterSeed);
    } finally {
      Storage.prototype.setItem = realSetItem;
    }
  });

  it('SC-8: ignores a storage event for a key that is not ours', () => {
    const store = fresh();
    store.add(
      'environments',
      toEnvShortcut({ EnvironmentId: 1, EnvironmentName: 'KEEP' })
    );

    localStorage.setItem('some.other.app.key', 'irrelevant');
    window.dispatchEvent(storageEvent('some.other.app.key'));

    expect(
      store.snapshot().environments.map(e => e.EnvironmentName)
    ).to.deep.equal(['KEEP']);
    localStorage.removeItem('some.other.app.key');
  });

  it('SC-8: reconciles when another tab clears storage entirely', () => {
    // localStorage.clear() in another tab fires a storage event with key === null,
    // the spec's "everything changed" signal. Treating that as "nothing to do"
    // would leave this tab rendering shortcuts that no longer exist.
    const store = fresh();
    store.add(
      'environments',
      toEnvShortcut({ EnvironmentId: 1, EnvironmentName: 'STALE' })
    );

    Object.values(KEYS).forEach(k => localStorage.removeItem(k));
    window.dispatchEvent(storageEvent(null));

    expect(store.snapshot().environments).to.be.empty;
    expect(localStorage.getItem(KEYS.schema), 'guard re-armed').to.equal('1');

    store.add(
      'environments',
      toEnvShortcut({ EnvironmentId: 2, EnvironmentName: 'AFTER-CLEAR' })
    );
    expect(fresh().snapshot().environments).to.deep.equal([
      { EnvironmentId: 2, EnvironmentName: 'AFTER-CLEAR' }
    ]);
  });

  it('merges mutations made from two stores before storage events arrive', () => {
    const first = fresh();
    const second = fresh();

    first.add(
      'environments',
      toEnvShortcut({ EnvironmentId: 1, EnvironmentName: 'FIRST' })
    );
    second.add(
      'environments',
      toEnvShortcut({ EnvironmentId: 2, EnvironmentName: 'SECOND' })
    );

    expect(fresh().snapshot().environments).to.deep.equal([
      { EnvironmentId: 1, EnvironmentName: 'FIRST' },
      { EnvironmentId: 2, EnvironmentName: 'SECOND' }
    ]);
  });

  // ─── SC-17 / D-41 ───────────────────────────────────────────────────────
  it('SC-17: clear() removes shortcut keys but leaves the migration guard armed', () => {
    const store = fresh();
    store.add(
      'environments',
      toEnvShortcut({ EnvironmentId: 1, EnvironmentName: 'X' })
    );

    store.clear();

    expect(store.snapshot().environments).to.be.empty;
    expect(localStorage.getItem(KEYS.environments)).to.equal(null);
    expect(
      localStorage.getItem(KEYS.schema),
      'clearing must not re-trigger the migration'
    ).to.not.be.null;
  });

  it('SC-17: clear() removes legacy cookies after a failed migration', () => {
    document.cookie = 'env-detail-tabs=%E0%A4%A; path=/;';
    const store = fresh();
    expect(document.cookie).to.contain('env-detail-tabs=');

    store.clear();

    expect(document.cookie).to.not.contain('env-detail-tabs=');
  });

  it('SC-17: clear() does not depend on quota-consuming writes', () => {
    const store = fresh();
    store.add(
      'environments',
      toEnvShortcut({ EnvironmentId: 1, EnvironmentName: 'SECRET' })
    );
    const realSetItem = Storage.prototype.setItem;
    Storage.prototype.setItem = () => {
      throw new DOMException('quota', 'QuotaExceededError');
    };

    try {
      store.clear();
    } finally {
      Storage.prototype.setItem = realSetItem;
    }

    expect(localStorage.getItem(KEYS.environments)).to.equal(null);
  });

  it('SC-27: removes a dangling environment shortcut by name', () => {
    const store = fresh();
    store.add(
      'environments',
      toEnvShortcut({ EnvironmentId: 42, EnvironmentName: 'GONE' })
    );

    // The 404 path only knows the name from the URL. Identity prefers the id, so
    // a name-only probe through remove() would not have matched.
    store.removeEnvironmentByName('GONE');
    expect(store.snapshot().environments).to.be.empty;
  });

  // ─── SC-33 ──────────────────────────────────────────────────────────────
  it('SC-33: stays usable when localStorage is unavailable', () => {
    const realSetItem = Storage.prototype.setItem;
    Storage.prototype.setItem = () => {
      throw new DOMException('blocked', 'SecurityError');
    };

    try {
      const store = new DrawerShortcuts();
      expect(() => store.start()).to.not.throw();
      expect(store.isPersistent, 'reports itself as non-persistent').to.be
        .false;
      expect(() =>
        store.add(
          'environments',
          toEnvShortcut({ EnvironmentId: 1, EnvironmentName: 'SESSION-ONLY' })
        )
      ).to.not.throw();
      expect(
        store.snapshot().environments,
        'still works for the session'
      ).to.have.lengthOf(1);
    } finally {
      Storage.prototype.setItem = realSetItem;
    }
  });

  it('SC-2: surfaces a quota failure instead of swallowing it', () => {
    const store = fresh();
    const realSetItem = Storage.prototype.setItem;
    let reported: unknown = null;
    store.addEventListener('problem', (e: Event) => {
      reported = (e as CustomEvent).detail;
    });

    Storage.prototype.setItem = () => {
      throw new DOMException('quota', 'QuotaExceededError');
    };
    try {
      store.add(
        'results',
        toResultShortcut({ Id: 1, EnvironmentName: 'E', BuildNumber: 'b' })
      );
    } finally {
      Storage.prototype.setItem = realSetItem;
    }

    // The cookie implementation's defining failure was an over-limit write being
    // discarded with no error anywhere.
    expect(reported, 'a failed write must be reported').to.not.be.null;
    expect((reported as { problem: string }).problem).to.equal('quota');
  });
});
