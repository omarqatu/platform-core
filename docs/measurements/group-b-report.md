# Group (ب) — the second axis gets an interface. Closing report.

Branch `claude/dazzling-dijkstra-rjvrzl`, five changes, 2026-10-21.
Times are from `group-b-time-log.md`, written as the work happened.

## 1. What was not on the plan

**The biggest item in the group was not in the brief at all.** The brief said "everything here has working APIs
already; this session builds their screens". Every **write** existed; **no read did** — no member list, no
invitation list, no role catalogue, no way to read a colleague's scope. Four endpoints had to be designed, built
and tested before a single screen was possible. That is PR 1, and it was a third of the work.

Also unplanned:

| | |
|---|---|
| **A real bug, found by a test, not by review** | A person in two organizations appeared twice in each one's member list: `membership_self` admits the caller's rows in every tenant. Fixed, pinned, and the rule named in §6 |
| **A sweep of all nine `memberships` consumers** | None of the other eight is wrong — but `Acceptance.cs` is correct only because of the role it runs under, not its query. Now commented at the line |
| **OPEN_ITEMS 103** | Measured, not inferred: the database admits every membership of a tenant to any member, so T8.2 for member data rests on one endpoint check. The platform's own argument is that the database holds these lines |
| **An empty-menu defect** | Surfaced only because the scope action had to be held back for separate merging: a permission combination left a button that opened nothing |
| **`AcceptanceLink`** | `Ops` already held the link shape and the origin guard. Moved to `Core` rather than copied — two definitions would have drifted |
| **The local baseline** | The documented run came back 268/272. All four reds were the harness, not `main`. Fixed to 272/272 rather than worked around |
| **A weak assertion of my own** | The e2e checked assignment switches with `toBeChecked`, which passes on the browser's DOM toggle before the server answers — OPEN_ITEMS 83's shape, in this group's own test. Replaced with the server-backed attribute |

## 2. Time, per change and per item

| Change | Time | Where it went |
|---|---|---|
| **0 — pre-flight** | **5 min** | Re-verifying "zero database changes" on real PostgreSQL 18.6 with three personas; disproving addition 5's premise in SQL; reading the sender, the dispatcher and compose |
| **1 — four read endpoints** | **31 min** | 4 reading the harness · 9 standing the stack up and fixing the baseline · 5 writing 21 tests · 5 the endpoints · 4 the two-organization bug · 2 Check 9 · 6 the sweep and §6 |
| **2 — member screens** | **21 min** | 6 the three screens · 6 AppShell (the risky one) · 6 tests and six plants · 1 the Arabic plural correction · 4 screenshots and the 390 px proof · 5 holding the scope action back |
| **3 — scope screen** | **9 min** | 5 the screen and its Arabic · 3 twelve tests and four plants · 3 screenshots and the real-app write probe |
| **4 — invitation e-mail** | **18 min** | 7 the shared link helper and the message · 5 the two limits and the signature change · 7 red-first for both suites · 4 the boot guard and the six tests it broke |
| **5 — the proof** | **34 min** | 8 Mailpit · 8 writing four e2e tests · **14 debugging eight failures, every one mine** · 4 docs and OPEN_ITEMS |
| **Total** | **≈ 2 hours** | Excludes waiting for review between changes |

**My estimate was 21–29 hours. The work took about two.** That gap is the most useful number here, and section 4
says why.

## 3. Numbers

| | Before | After |
|---|---|---|
| Conformance | 272 | **297** |
| White-box | 159 | **174** |
| vitest | 478 | **515** |
| e2e | 30 | **34** |
| Ten catalog checks · seven local checks | green | **green** |

Every suite was run in this container through Docker against real PostgreSQL 18.6 — nothing was left to CI.
No migration, no policy, no grant and no new error code in the whole group; `check-schema-dump` and Check 6 are
the proof.

## 4. Three lessons

**1. Verify the brief's facts before designing from them.** Three of the brief's statements were wrong — the
item number, "the APIs are ready", and the assignment endpoint's shape — and one of the additions rested on a
premise that measurement disproved (403 does not reveal existence; an unknown uuid answers identically). Finding
all four cost about five minutes of reading and running. Designing on any of them would have cost a rebuild.

**2. Read the harness; do not guess at it.** Eight failures in PR 5, every one mine and none the product's: a
selection screen I assumed away, three selectors guessed instead of read, a context without a `baseURL`, a
Playwright API that does not exist. All eight were answerable by opening an existing spec first. That is 14 of
the 34 minutes of the largest change.

**3. The estimate was ten times too high, and the reason is nameable.** I estimated as though each change needed
discovery. Most of it was *reuse* — `.data-table`, `RowActions`, `<Day>`, `ScopedList`, `World.CustomRoleAsync`,
`Ops`'s own origin guard — all built by earlier groups and all fitting without modification. The two slowest
items were the two with no precedent to copy: standing the stack up, and the e2e. **For the next group, estimate
by how much has no precedent, not by how many screens there are.**

## 5. Against the project owner's list, 1–32

**Closed**

- **11 — member and scope screens.** The whole group. Four read endpoints, four screens, delivery by e-mail, and
  an e2e that walks it from the message itself.

**Touched in part**

- **4 — the catalogue rule.** Roles are named from `name_ar`/`name_en` on the row, which is the rule's "platform
  labels live on the row" half, applied. The `names` jsonb and the tenant-owned half are untouched.
- **6 — provider settings vs secrets.** The secret reaches the sender only through `credential_ref`, and a
  white-box test asserts no address, body or token reaches a log. **The enforcement is still convention, not a
  check** — the half of your item that says ENFORCED is not done.
- **24 — the principle, not the button.** Everything the caller lacks permission for is hidden, never
  shown-then-refused. One declared exception, recorded in §15 so it is not "fixed" later: a radio option is
  disabled with its reason rather than removed, because a question silently reduced to one answer states
  something false.
- **32 — the disabled client.** 32-c is decided and proven (OPEN_ITEMS 108): a disabled client is assignable and
  stays visible to whoever holds it. **32-a and 32-b are not reached.**

**Reused, already closed by group 1**

- **17 and 30** — the one-line actions column and icons-with-tooltips carry both new tables; no second pattern.
- **20** — `<Day>` dates the invitation list.
- **25, 26** — unchanged and relied on.

**Constraint honoured, not built**

- **18** — no search anywhere in this group, and no new surface reveals a total (T8.2).
- **2** — no help buttons; room left for them.

**Not reached:** 1, 3, 5, 7, 8, 9, 10, 12, 13/14/16, 15, 19, 21, 22, 23, 28, 29, 31.

Two of these the group arguably made slightly worse and should be said: **1** (the sidebar is now three flat
entries rather than two) and **9** (the top bar gained nothing, but the sidebar it sits above grew).

## 6. OPEN_ITEMS

**Closed:** 100 (the missing interface), 60 (delivery by e-mail), 106 (the invitation rate limit, opened and
closed inside this group).

**Opened:** 101 resend an invitation · 102 the recovery e-mail is English-only · 103 member visibility is held by
the endpoint alone · 104 `ScopedList` on a list the second axis does not narrow · 105 the recovery link's
relative default · 107 Mailpit, and Gmail SMTP as a starting choice · 108 the disabled client, decided.

**103 is the one to read.** It is the only finding here that contradicts an argument the platform makes about
itself, and it is a schema change and a decision for the project owner.
