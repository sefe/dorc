import {
  DeploymentRequestApiModel,
  EnvironmentApiModel,
  ProjectApiModel
} from '../apis/dorc-api';

/**
 * Owns the navigation drawer's shortcut state: the environment, project and
 * monitor-result entries users pin by opening them.
 *
 * Single owner by design. Previously "open" lived in one component and "close"
 * and "sync" in another, and the three disagreed about whether identity was the
 * name or the id — which produced duplicate tabs after renames, closed tabs that
 * resurrected, and a cross-window sync that could only ever grow the list.
 *
 * Persistence notes:
 *  - Only identity fields are stored. The previous implementation serialised
 *    whole API DTO graphs — including a recursive ChildEnvironments tree and a
 *    project's SourceDatabase (SQL host, AD group) — into cookies.
 *  - localStorage, not cookies. The cookie form overflowed its 4 KB limit at
 *    around the fifth shortcut, at which point the browser silently discarded
 *    the whole write: additions and renames stopped persisting while closes
 *    still worked, so the visible set and the stored set drifted apart forever.
 */

export interface EnvShortcut {
  EnvironmentId?: number | null;
  EnvironmentName: string;
}

export interface ProjectShortcut {
  ProjectId?: number | null;
  ProjectName: string;
}

export interface ResultShortcut {
  Id: number;
  EnvironmentName?: string | null;
  BuildNumber?: string | null;
}

export interface DrawerShortcutState {
  environments: EnvShortcut[];
  projects: ProjectShortcut[];
  results: ResultShortcut[];
}

type Family = keyof DrawerShortcutState;

const KEYS: Record<Family, string> = {
  environments: 'dorc.shortcuts.environments',
  projects: 'dorc.shortcuts.projects',
  results: 'dorc.shortcuts.results'
};

/**
 * Presence of this key is the migration guard. It is written unconditionally —
 * including when the legacy cookies held nothing — so "migrated to empty" is
 * representable. That is what makes the guard sound: without it, an empty result
 * is indistinguishable from "not yet migrated" and the migration re-runs forever.
 */
const SCHEMA_KEY = 'dorc.shortcuts.schema';

const LEGACY_COOKIES: Record<Family, string> = {
  environments: 'env-detail-tabs',
  projects: 'project-envs-tabs',
  results: 'monitor-result-tabs'
};

/** Identity for de-duplication. Id when present, otherwise the name. */
export const envKey = (e: EnvShortcut) =>
  e.EnvironmentId != null ? `id:${e.EnvironmentId}` : `name:${e.EnvironmentName}`;
export const projectKey = (p: ProjectShortcut) =>
  p.ProjectId != null ? `id:${p.ProjectId}` : `name:${p.ProjectName}`;
export const resultKey = (r: ResultShortcut) => `id:${r.Id}`;

const KEY_OF: Record<Family, (item: never) => string> = {
  environments: envKey as (item: never) => string,
  projects: projectKey as (item: never) => string,
  results: resultKey as (item: never) => string
};

// ── Projection ─────────────────────────────────────────────────────────────
// Callers keep dispatching full API models; projection happens here, at the
// store boundary, so the 20 dispatch sites are untouched.

export const toEnvShortcut = (e: EnvironmentApiModel): EnvShortcut => ({
  EnvironmentId: e.EnvironmentId,
  EnvironmentName: String(e.EnvironmentName ?? '')
});

export const toProjectShortcut = (p: ProjectApiModel): ProjectShortcut => ({
  ProjectId: p.ProjectId,
  ProjectName: String(p.ProjectName ?? '')
});

export const toResultShortcut = (
  r: DeploymentRequestApiModel
): ResultShortcut => ({
  Id: Number(r.Id),
  EnvironmentName: r.EnvironmentName,
  BuildNumber: r.BuildNumber
});

// ── Storage access ─────────────────────────────────────────────────────────

export type StorageProblem = 'unavailable' | 'quota' | 'corrupt';

let probed: Storage | null | undefined;

/**
 * Resolves localStorage once, probing for availability.
 *
 * Cached deliberately. Merely touching `window.localStorage` is not enough — a
 * blocked origin throws on *use* — but re-probing on every read would write on
 * every call, which both defeats the "reconciliation never writes" guarantee
 * and misreports a genuine quota failure as the storage being unavailable.
 */
function storage(): Storage | null {
  if (probed !== undefined) return probed;
  try {
    const s = window.localStorage;
    const probe = '__dorc_probe__';
    s.setItem(probe, '1');
    s.removeItem(probe);
    probed = s;
  } catch {
    probed = null;
  }
  return probed;
}

/** Test seam: forces the next call to re-probe. */
export function resetStorageProbe() {
  probed = undefined;
}

function isEnvShortcut(v: unknown): v is EnvShortcut {
  return (
    typeof v === 'object' &&
    v !== null &&
    typeof (v as EnvShortcut).EnvironmentName === 'string'
  );
}

function isProjectShortcut(v: unknown): v is ProjectShortcut {
  return (
    typeof v === 'object' &&
    v !== null &&
    typeof (v as ProjectShortcut).ProjectName === 'string'
  );
}

function isResultShortcut(v: unknown): v is ResultShortcut {
  return (
    typeof v === 'object' &&
    v !== null &&
    Number.isFinite((v as ResultShortcut).Id)
  );
}

const GUARDS: Record<Family, (v: unknown) => boolean> = {
  environments: isEnvShortcut,
  projects: isProjectShortcut,
  results: isResultShortcut
};

/** Collapses duplicates by identity, keeping first-seen order. */
function dedupe<T>(items: T[], key: (item: T) => string): T[] {
  const seen = new Set<string>();
  const out: T[] = [];
  for (const item of items) {
    const k = key(item);
    if (seen.has(k)) continue;
    seen.add(k);
    out.push(item);
  }
  return out;
}

export class DrawerShortcuts extends EventTarget {
  private state: DrawerShortcutState = {
    environments: [],
    projects: [],
    results: []
  };

  private storageAvailable = true;
  private warnedProblems = new Set<StorageProblem>();
  private started = false;

  private onStorageEvent = (e: StorageEvent) => {
    // A null key means the other tab called localStorage.clear() — the spec's
    // signal that everything changed, not that nothing did. Reconcile in that
    // case as well; only a *named* key belonging to something else is ignored.
    const key = e.key ?? null;
    if (key !== null && !Object.values(KEYS).includes(key)) return;
    if (key === null) {
      const s = storage();
      try {
        if (s?.getItem(SCHEMA_KEY) === null) {
          s.setItem(SCHEMA_KEY, '1');
        }
      } catch {
        this.report(
          'unavailable',
          'Shortcuts cannot be saved in this browser session; they will be lost on reload.'
        );
      }
    }
    // Read-only reconciliation. Deliberately does NOT write: if a storage-event
    // handler persisted what it just read, two windows would echo each other's
    // events indefinitely.
    this.state = this.readAll();
    this.emit();
  };

  /** Reads persisted state, migrating off the legacy cookies on first run. */
  start(): void {
    if (this.started) return;
    this.started = true;

    const s = storage();
    if (!s) {
      this.storageAvailable = false;
      this.report(
        'unavailable',
        'Shortcuts cannot be saved in this browser session; they will be lost on reload.'
      );
      return;
    }

    this.migrateFromCookies(s);
    this.state = this.readAll();
    window.addEventListener('storage', this.onStorageEvent);
    this.emit();
  }

  stop(): void {
    window.removeEventListener('storage', this.onStorageEvent);
    this.started = false;
  }

  snapshot(): DrawerShortcutState {
    return {
      environments: [...this.state.environments],
      projects: [...this.state.projects],
      results: [...this.state.results]
    };
  }

  subscribe(listener: () => void): () => void {
    this.addEventListener('change', listener);
    return () => this.removeEventListener('change', listener);
  }

  private emit() {
    this.dispatchEvent(new Event('change'));
  }

  // ── Mutations ────────────────────────────────────────────────────────────

  add<F extends Family>(family: F, item: DrawerShortcutState[F][number]): void {
    const key = KEY_OF[family] as (i: unknown) => string;
    const list = this.latestFamily(family) as unknown[];
    if (list.some(existing => key(existing) === key(item))) return;
    (this.state[family] as unknown[]) = [...list, item];
    this.persist(family);
    this.emit();
  }

  remove<F extends Family>(
    family: F,
    item: DrawerShortcutState[F][number]
  ): void {
    const key = KEY_OF[family] as (i: unknown) => string;
    const before = this.latestFamily(family) as unknown[];
    const after = before.filter(existing => key(existing) !== key(item));
    if (after.length === before.length) return;
    (this.state[family] as unknown[]) = after;
    this.persist(family);
    this.emit();
  }

  has<F extends Family>(family: F, item: DrawerShortcutState[F][number]): boolean {
    const key = KEY_OF[family] as (i: unknown) => string;
    return (this.state[family] as unknown[]).some(e => key(e) === key(item));
  }

  /**
   * Renames in place. Position is preserved rather than remove-and-append, so a
   * rename does not silently reorder the drawer (WCAG 3.2.3).
   */
  renameEnvironment(oldName: string, next: EnvShortcut): void {
    const current = this.latestFamily('environments');
    const idx = current.findIndex(e => e.EnvironmentName === oldName);
    if (idx < 0) return;
    const copy = [...current];
    copy[idx] = next;
    this.state.environments = dedupe(copy, envKey);
    this.persist('environments');
    this.emit();
  }

  /**
   * Removes an environment shortcut by name.
   *
   * Distinct from remove('environments', …) because identity prefers the id when
   * one is present: a shortcut stored as `id:42` would never match a name-only
   * probe. The 404 path only knows the name from the URL, so it needs this.
   */
  removeEnvironmentByName(name: string): void {
    const before = this.latestFamily('environments');
    const after = before.filter(e => e.EnvironmentName !== name);
    if (after.length === before.length) return;
    this.state.environments = after;
    this.persist('environments');
    this.emit();
  }

  /** Clears every shortcut key, leaving the migration guard armed (see SCHEMA_KEY). */
  clear(): void {
    this.state = { environments: [], projects: [], results: [] };
    const s = storage();
    if (s) {
      try {
        (Object.keys(KEYS) as Family[]).forEach(f => s.removeItem(KEYS[f]));
      } catch {
        this.report(
          'unavailable',
          'Saved drawer shortcuts could not be cleared from browser storage.'
        );
      }
    }
    (Object.keys(LEGACY_COOKIES) as Family[]).forEach(f =>
      deleteLegacyCookie(LEGACY_COOKIES[f])
    );
    this.emit();
  }

  // ── Persistence ──────────────────────────────────────────────────────────

  private readAll(): DrawerShortcutState {
    return {
      environments: this.readFamily('environments'),
      projects: this.readFamily('projects'),
      results: this.readFamily('results')
    };
  }

  private latestFamily<F extends Family>(family: F): DrawerShortcutState[F] {
    return storage() ? this.readFamily(family) : this.state[family];
  }

  private readFamily<F extends Family>(family: F): DrawerShortcutState[F] {
    const s = storage();
    if (!s) return [] as unknown as DrawerShortcutState[F];

    const raw = s.getItem(KEYS[family]);
    if (raw === null) return [] as unknown as DrawerShortcutState[F];

    try {
      const parsed: unknown = JSON.parse(raw);
      if (!Array.isArray(parsed)) throw new Error('not an array');
      const guard = GUARDS[family];
      const valid = parsed.filter(guard);
      const key = KEY_OF[family] as (i: unknown) => string;
      return dedupe(valid, key) as DrawerShortcutState[F];
    } catch {
      // Replace with an empty array rather than removing the key — removing it
      // would disarm nothing here, but keeping the shape consistent means a
      // corrupt value can never leave the in-memory state a non-array, which
      // used to break every later add/close for the rest of the session.
      this.report(
        'corrupt',
        'Saved drawer shortcuts could not be read and have been reset.'
      );
      this.writeRaw(family, []);
      return [] as unknown as DrawerShortcutState[F];
    }
  }

  private persist(family: Family) {
    this.writeRaw(family, this.state[family]);
  }

  private writeRaw(family: Family, value: unknown[]) {
    const s = storage();
    if (!s) return;
    try {
      s.setItem(KEYS[family], JSON.stringify(value));
    } catch {
      // Quota. Surfaced, never swallowed — the cookie implementation's defining
      // failure was that an over-limit write was silently discarded.
      this.report(
        'quota',
        'Drawer shortcuts could not be saved: browser storage is full.'
      );
    }
  }

  // ── Migration ────────────────────────────────────────────────────────────

  /**
   * One-shot, single-release migration off the legacy cookies.
   *
   * Ordering is mandatory: write the shortcut keys, then the guard, and only
   * then delete the cookies. Deleting first and failing the write would destroy
   * the data AND arm the guard on the next load — silent, total, unrecoverable
   * loss. On any read failure nothing is written and nothing is deleted, so the
   * next load simply retries.
   */
  private migrateFromCookies(s: Storage) {
    if (s.getItem(SCHEMA_KEY) !== null) return;

    let migrated: DrawerShortcutState;
    try {
      migrated = {
        environments: dedupe(
          this.readLegacy('environments').map(v =>
            toEnvShortcut(v as EnvironmentApiModel)
          ),
          envKey
        ),
        projects: dedupe(
          this.readLegacy('projects').map(v =>
            toProjectShortcut(v as ProjectApiModel)
          ),
          projectKey
        ),
        results: dedupe(
          this.readLegacy('results').map(v =>
            toResultShortcut(v as DeploymentRequestApiModel)
          ),
          resultKey
        )
      };
    } catch {
      this.report(
        'corrupt',
        'Existing drawer shortcuts could not be read and were left in place.'
      );
      return;
    }

    try {
      (Object.keys(KEYS) as Family[]).forEach(f =>
        s.setItem(KEYS[f], JSON.stringify(migrated[f]))
      );
      s.setItem(SCHEMA_KEY, '1');
    } catch {
      this.report(
        'quota',
        'Drawer shortcuts could not be migrated: browser storage is full.'
      );
      return;
    }

    (Object.keys(LEGACY_COOKIES) as Family[]).forEach(f =>
      deleteLegacyCookie(LEGACY_COOKIES[f])
    );
  }

  /** Throws on a malformed legacy cookie so the caller can abandon the migration. */
  private readLegacy(family: Family): unknown[] {
    const raw = readLegacyCookie(LEGACY_COOKIES[family]);
    if (raw === '') return [];
    const parsed: unknown = JSON.parse(raw);
    if (!Array.isArray(parsed)) throw new Error('legacy cookie was not an array');
    return parsed as unknown[];
  }

  // ── Diagnostics ──────────────────────────────────────────────────────────

  /** Warns once per problem per session rather than on every load. */
  private report(problem: StorageProblem, message: string) {
    if (this.warnedProblems.has(problem)) return;
    this.warnedProblems.add(problem);
    console.warn(`[drawer-shortcuts] ${message}`);
    this.dispatchEvent(
      new CustomEvent('problem', { detail: { problem, message } })
    );
  }

  get isPersistent(): boolean {
    return this.storageAvailable;
  }
}

// ── Legacy cookie access ───────────────────────────────────────────────────
// Deliberately inline and read-only rather than importing helpers/cookies.ts,
// which this change deletes. It must survive the two failure modes that file
// had, because they bite on the one load that matters — the first after upgrade:
//   - a malformed % sequence made decodeURIComponent throw; and
//   - a duplicate-named cookie at two scopes made the old parser return '',
//     which would have silently discarded every shortcut.

function readLegacyCookie(name: string): string {
  const prefix = `${name}=`;
  const match = document.cookie
    .split(';')
    .map(part => part.trim())
    .find(part => part.startsWith(prefix));
  if (!match) return '';
  const value = match.slice(prefix.length);
  try {
    return decodeURIComponent(value);
  } catch {
    throw new Error('legacy cookie was not decodable');
  }
}

function deleteLegacyCookie(name: string) {
  const expiry = new Date(0).toUTCString();
  document.cookie = `${name}=; expires=${expiry}; path=/;`;
}

export const drawerShortcuts = new DrawerShortcuts();
