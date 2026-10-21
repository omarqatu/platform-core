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
| 4 | 1 — read endpoints | Reading the Conformance harness before writing a test in it | 2026-10-21T20:27Z | 2026-10-21T20:31Z | T4_World, Http, Seed, the seed contract's personas. Rami (scope.manage, no members.manage) was already seeded; the inverse persona was not, and World.CustomRoleAsync covers it |
| 5 | 1 — read endpoints | Standing the full local stack up: migrations, seed, Api, Worker, web build | 2026-10-21T20:31Z | 2026-10-21T20:40Z | The first baseline came back 268/272. All four reds were the harness, not the code — no built interface and no Worker binary. Fixed both rather than work against a muddy baseline (OPEN_ITEMS 88's "237/238 — the usual one"): **272/272** |
| 6 | 1 — read endpoints | Writing the 21 tests, red against the old code | 2026-10-21T20:40Z | 2026-10-21T20:45Z | **17 of 20 red.** The 3 greens are the existence-leak tests, which pin behaviour that already holds — so a 404 was planted in the client endpoint to prove they are not vacuous (**2 of 3 red on the plant**), then reverted |
| 7 | 1 — read endpoints | The four endpoints | 2026-10-21T20:45Z | 2026-10-21T20:50Z | Straightforward except the bug in item 8 below |
| 8 | 1 — read endpoints | The two-organization bug the tests found | 2026-10-21T20:48Z | 2026-10-21T20:52Z | The first green run showed Omar twice in Al-Amin's member list: `membership_self` admits the caller's memberships in every tenant, so the list needed the tenant constraint `LastMemberGuards` already applies in SQL. Found by the test, not by review. Pinned with its own test, proven red (1/1) with the fix removed |
| 9 | 1 — read endpoints | Check 9 caught a doc comment | 2026-10-21T20:50Z | 2026-10-21T20:52Z | The static check refused `app.membership_id` written in an XML comment in a file that is not a declared writer. The check is right and was not touched; the comment was reworded. Ten checks green, Check 6 green being the empirical proof that no grant changed |

## A note on this file's own honesty

The first version of this row set carried end times of 20:34 and 20:36, written at 20:13 —
times that had not happened yet. They were replaced with the measured window above.
Recorded here rather than silently corrected, because a live log that can be back-filled
is the same verifier-that-verifies-nothing shape as OPEN_ITEMS 83.
