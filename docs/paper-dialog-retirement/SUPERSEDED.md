# SUPERSEDED

This folder's HLPS and IS are **superseded** by
[`docs/dialog-consistency/`](../dialog-consistency/HLPS-dialog-consistency.md).

Two reasons:

1. **They failed adversarial review round 1** — see
   [`../vaadin-alignment/REVIEW-R1-triage.md`](../vaadin-alignment/REVIEW-R1-triage.md).
   Five load-bearing premises were wrong.
2. **The scope changed.** The user directed that consistency across the UI
   matters more than backwards compatibility, and that dialog usage should be
   the same throughout. That widens the work from "retire `paper-dialog`"
   (12 dialogs) to "unify every dialog on two Vaadin patterns" (31 dialogs,
   4 implementations), and it discards the behaviour-preserving constraint the
   documents here were built on.

Kept rather than deleted because `REVIEW-R1-triage.md` cites these files by
path, and the review record should stay readable.
