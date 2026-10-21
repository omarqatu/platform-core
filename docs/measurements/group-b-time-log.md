# Group (ب) — live time log

Written at the moment each item starts and ends, never reconstructed. All times UTC.
The report's requirement 1 (time per PR and per item) is built from this file alone.

The three pre-flight items ran interleaved inside one 20:08–20:13 window, so they are
recorded with that window and not with invented per-item boundaries.

| # | PR | Item | Start | End | Went into |
|---|---|---|---|---|---|
| 1 | 0 — pre-flight | Re-verify the "zero database changes" claim | 2026-10-21T20:08Z | 2026-10-21T20:13Z | ~2 min reading grants/policies; the rest went into standing a real PostgreSQL 18.6 up (the docker daemon was not running in this container and had to be started by hand) and writing a three-persona fixture, so the claim is measured and not read |
| 2 | 0 — pre-flight | Addition 5 — the 403/404 existence question | 2026-10-21T20:08Z | 2026-10-21T20:13Z | Found the premise is wrong: a nonexistent id answers 403 too. Proved it in SQL rather than argued it |
| 3 | 0 — pre-flight | Additions 6, 7, 8 — tracking, rate limit, Mailpit | 2026-10-21T20:08Z | 2026-10-21T20:13Z | Reading EmailNotificationSender, the dispatcher, Program.cs, compose, dev/CI settings |

## A note on this file's own honesty

The first version of this row set carried end times of 20:34 and 20:36, written at 20:13 —
times that had not happened yet. They were replaced with the measured window above.
Recorded here rather than silently corrected, because a live log that can be back-filled
is the same verifier-that-verifies-nothing shape as OPEN_ITEMS 83.
