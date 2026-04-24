# Daemons Modernisation — Follow-Up Items

| Field       | Value                                                   |
|-------------|---------------------------------------------------------|
| **Status**  | Active                                                  |
| **Parent**  | HLPS-daemons-modernisation.md, IS-daemons-modernisation.md |
| **Folder**  | docs/daemons-modernisation/                             |

Items captured during delivery but deliberately deferred from the issue #649 PR. Each should become its own ticket / SPEC / PR once the current work lands.

---

## F-1 — Audit pages UI consolidation

**Context**: after this PR lands there are multiple audit views in the UI — Project audit (existing), Scripts audit (existing, `AuditScript`), Daemon audit (new, S-007/S-008). The views are visually and behaviourally inconsistent — different component layouts, different column sets, different filter/sort UX.

**Proposal**: a single unified "Audit" UI surface with consistent columns (Action, Actor, Date, From/To), filters, and pagination. Per-domain audit sources plug into it via a common API contract (`GetXxxAuditListResponseDto`). Today each domain rolls its own — wasteful.

**Out of scope for #649**: this PR's daemon-audit view (S-008) mirrors the existing project-audit view to stay consistent with the per-domain pattern. Consolidation is a separate refactor.

**Watch for**: as more audit domains accumulate, the drift compounds. Tackle before adding the fourth audit view.

---

## F-1a — Delete confirmation warning: show attached-server count and names

**Context**: the HLPS U-7 resolution and SPEC-S-008 §2.5 specified that the Delete confirmation dialog on `page-daemons-list.ts` should warn when the daemon is attached to ≥1 servers, ideally with a count and/or server names. During S-008 execution the per-daemon attached-server query was dropped because the useful endpoint shape doesn't exist — the existing `GET /ServerDaemons/{serverId}` returns daemons on a server, not servers on a daemon. Adding a new endpoint (`GET /ServerDaemons/byDaemon/{daemonId}` or `GET /Daemons/{daemonId}/servers`) is a small addition that stayed out of scope.

The current confirmation text is honest but unconditional: *"Any server mappings for this daemon will also be removed."* This covers the safety case (cascade deletion IS happening) without being alarmist for un-attached daemons.

**Follow-up**: add the daemon-side inverse endpoint (new source method + controller action, all role-gated per SD-4), call it from `requestDelete()` before opening the confirm dialog, and include the count / names in the warning when non-zero.

---

## F-2 — "Last seen" tracking on daemons (replaces the old side-effect)

**Context**: before S-005 the status-probe path silently wrote `SERVER_SERVICE_MAP` rows every time a probe succeeded — a bad side effect, removed in this PR (DF-7). But the **intent** behind that bad side effect was reasonable: know when a given daemon was last observed running on a given server, so stale / unused daemons can be identified and cleaned up.

**Proposal**: add a `LastSeenDate` (and probably `LastSeenStatus`) column on `deploy.ServerDaemon` (or a separate `deploy.DaemonObservation` table). When the "Load Daemons" button runs a probe and gets a valid status back, record the observation — an **explicit** write that happens in a dedicated probe-and-observe endpoint, gated by RBAC, with audit, and visible in the UI (last-seen column on the daemon list, filter for "not seen in >N days").

**Out of scope for #649**: the HLPS explicitly removes the side-effect. Replacing it with a proper observation mechanism is its own feature — new endpoint, new table column, new UI, new audit rows.

**Data model sketch**:
- Extend `deploy.ServerDaemon` with `LastSeenDate DATETIME NULL` and `LastSeenStatus NVARCHAR(50) NULL`, or
- New table `deploy.DaemonObservation` with `(ServerId, DaemonId, ObservedDate, ObservedStatus, ObservedBy)` — keeps ServerDaemon a pure mapping and accumulates history.

The second shape is stronger (history, not just latest) and matches the original intent of tracking staleness over time. Recommend when this lands.

---
