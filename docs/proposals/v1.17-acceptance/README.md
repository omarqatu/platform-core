# Proposal for PLATFORM_CORE v1.17: acceptance with an existing account, enforced in the database

**Status:** decided by the project owner — **option C**; the draft and alternative B are **rejected** (below). This
directory stays as the record of what was tried and why; its scripts run against a database by hand
(`run.sh draft|altb`) — the probe's workflow was removed before the build.
**Base:** PLATFORM_CORE v1.16 (frozen), PROOF_SPEC v1.3, `main` at the merge of #14.
**The text under test:** the six conditions of the owner's §3.10 addition (acceptance by a signed-in user of an
invitation to another tenant) and its closing paragraph (with no session, an email that has an account cannot register
again). The task referred to the text as attached; it was not attached, so the probe tests the conditions as the
owner wrote them in the previous message.

## The decision — option C

**Adopted (C):**
1. The email match stays the **application's** (§4.5), **normalized** — surrounding spaces removed and case unified,
   under the same rule as P3 (`lower(btrim(…))`) — on **every** acceptance path: a new account, a return, an existing
   account.
2. **P3 is adopted:** one person per normalized address (`persons_email_normalized_key`, migration 0009). On
   PostgreSQL 18, P3 alone breaks no existing test (run 37139863515, below).
3. **P4 is not adopted:** OPEN_ITEMS 39 (no membership without its scope in the database, with new setup for
   `T3_8_Test18` and `T3_Test26`).
4. **No new chain, no bootstrap change, no function, no new column.** Acceptance by an existing account goes through
   path 3b as it is: the provisioner on its own connection. Any new grant or policy goes back to the owner first.
5. **The race:** consuming the token is a critical write under the rows-affected guard (§3.5/5) — of two concurrent
   acceptances one succeeds — proven by test.

**Rejected — the draft (P1, P1b, P2, P4):** it puts the match in the database on every path, at the price of three
rewritten policies, a fourth, and **two new chains** against §3.9's "no new chains"; and it changes **the bootstrap**
(§4.4: a founding invitation, `INSERT` on `invitations` for the provisioner) — the whole draft broke bootstrap, and
with it 34 existing tests, until the code changed.

**Rejected — alternative B (one `SECURITY DEFINER` function):** it keeps §3.9 and the bootstrap, but the function is
owned by `migrator` and so runs with `BYPASSRLS` reached from application code, against §3.4's condition (1); and it
writes `audit_log` itself, a **fourth writer** outside the interceptor, against §7. It also covers only the
existing-account path.

**If the database must one day enforce the match:** OPEN_ITEMS 40 — a function owned by a role without `BYPASSRLS`,
whose writes pass through the audit interceptor; not either variant tried here.

## The mechanism

| Part | What it enforces | How | Text it changes |
|---|---|---|---|
| **P1** | Condition 2: a membership comes into being — inserted, or re-activated from `left` — only for a person with a pending, unexpired invitation in that tenant, the emails compared normalized (`lower(btrim(…))`). | `memberships_provisioner_insert` and `memberships_provisioner_rejoin`: an `EXISTS` over `invitations → users → persons` in `WITH CHECK`. | §3.9, §3.10 policy texts; §3.9 "no new chains" |
| **P1b** | Bootstrap goes through an invitation too (no exception in P1): the provisioner may create an invitation **only in a tenant with no membership yet** — its founding — and only `pending`. In a tenant with members it cannot mint the invitation P1 asks for. | New `invitations_provisioner_found` (`FOR INSERT TO provisioner`) and `GRANT INSERT ON invitations TO provisioner`. | §4.4 (bootstrap), §3.8 grants matrix |
| **P2** | Condition 2 bound to the token, and condition 5: P1 accepts *any* matching invitation of the tenant, so a person holding their own invitation could consume another's token — and its role. The invitation records who accepted it; it may become `accepted` only from `pending`, only with an acceptor whose normalized email is the invitation's and who holds an active membership in the tenant. | New column `invitations.accepted_by` (FK to `users`); `invitations_provisioner_update`: `USING status = 'pending'`, `WITH CHECK status = 'accepted' AND EXISTS (…)`; `GRANT UPDATE (accepted_by)`. | §3.9 policy text, §3.8 matrix, the `invitations` table |
| **P3** | The closing paragraph: one person per **normalized** address. Today `persons_email_key` is exact, so `Omar@x.test` registers a second account beside `omar@x.test`. | `CREATE UNIQUE INDEX persons_email_normalized_key ON persons (lower(btrim(email)))`. | §3.3 constraints |
| **P4** *(optional)* | Condition 4 / T3.8: no membership is **committed** without its `membership_scope` row, in any path. | A deferred constraint trigger, `SECURITY DEFINER` (the provisioner cannot read an active membership's scope), `EXECUTE` revoked from `PUBLIC`. | none today — a new kind of object |

Condition 3 (active membership → refused) is already the database's: `UNIQUE (tenant_id, user_id)` (23505) and
`memberships_provisioner_rejoin`'s `USING status = 'left'`. Condition 6 (acceptance does not select the tenant) is
the application's: the acceptance endpoint never touches the cookie.

The draft is in `p1-…sql` to `p4-…sql`, applied in that order as `migrator`.

## Alternative B (the owner's request)

`b-accept-invitation.sql` with `p3-…sql`, instead of P1, P1b, P2 and P4: **one `SECURITY DEFINER` function**,
`public.accept_invitation(tenant_id, token, ip_address)`, called by `app_user` on the tenant-selection path (`app.user_id`
alone). It locks the token's invitation in that tenant `FOR UPDATE`. None, not pending or expired → `P0002`. The
acceptor's normalized address against the invitation's → otherwise `42501`. No membership → a new one with its role,
`membership_scope` (`intended_scope_mode`) and `membership_auth`; `left` → the return of 3.10 in its binding order;
active or disabled → `23505`. Then the token consumed, and one `audit_log` entry per row in the interceptor's shape,
`token_hash` masked. It sets no `app.tenant_id`. Owner `migrator`; `search_path = pg_catalog, public, pg_temp`;
`EXECUTE` revoked from `PUBLIC`, granted to `app_user` alone. No new policy, chain or column; bootstrap unchanged.

## How it was tested

- **The harness** (`cases.sh`): each case runs **alone**, as its role, with only the context it names
  (`app.tenant_id`), in a transaction that is **always rolled back**. Deferred constraints are forced with
  `SET CONSTRAINTS ALL IMMEDIATE` before the rollback. The result is the command tag of the last statement
  (`rows=N`) or `error=SQLSTATE`. **The expected result of each direction is written into the case before it ran.**
- **Both directions:** every case runs against **v1.16 as migrated** (the baseline — what differs there is the gap
  this draft closes) and against **the draft**, on a migrated and seeded database (`run.sh`).
- **The fixtures** (`fixtures.sql`): an organization of the probe's own (`Probe`) and persons on `probe.test`,
  committed as `migrator`, removed by `teardown.sql` between the directions.
- **The races** (`races.sh`): two provisioner transactions at once, each holding its writes 2 s before committing;
  "assert" adds the application's rows-affected check on the token, "raw" is the database alone.
- **The ten checks** (`checks-draft.sh`): on the draft, first with the repository's v1.16 inputs (which checks the
  draft breaks), then with inputs regenerated for the draft — the manifest by `generate-manifest.py` from a copy of
  the document carrying the draft's policy texts, the new chains and the new grants declared — and each check on its
  plant.
- **Through EF** (`ef-on-p3-p4`): the existing WhiteBox and Conformance suites on a database carrying P3 and P4.
  P1 and P2 cannot go through EF before phase B: the application does not yet create the founding invitation nor
  record the acceptor.
- **Alternative B** runs the same cases and races (B changes no policy, so every direct write keeps its v1.16
  result but for P3's), plus G1–G18 through the function (as `app_user`, and as the roles that must not execute it),
  R5 (two acceptances through the function at once), and `effects.sh`: what the function committed, read back as
  `migrator` — not what it reports. Each variant on its own database (`run.sh draft|altb`).
- **Where:** PostgreSQL 18 in GitHub Actions — the reference — through a probe workflow on the task's branch, removed
  before the build was proposed (it ran `run.sh draft|altb`, `checks-draft.sh` / `checks-altb.sh`, and the existing
  suites on each variant). The
  harness was developed against a local PostgreSQL 16, which proves the scripts, not the result.

## Results — PostgreSQL 18.6, run 37139263296

**Draft:** baseline 42/42 and draft 42/42 cases as written beforehand, races 5/5 in both directions. **B:** baseline
42/42 and B 42/42, races 5/5 in both directions, effects 2/2. (PostgreSQL 16 locally: identical.)

| Case | v1.16 | Draft | B |
|---|---|---|---|
| Matching address (case and spaces differ) | joins | joins | joins (G1) |
| Address does not match, no application check | **joins** (A2) | `42501` (A2) | `42501` through the function (G2); a direct provisioner write still **joins** (A2) |
| Another's token, holding one's own invitation | **consumed** (C2) | `42501` (C2) | `42501` (G3) |
| Expired / consumed token | joins (A3, A4) | `42501` | `P0002` (G4, G5) |
| Active membership | `23505` | `23505` | `23505` (G6) |
| Left + own invitation / + another's | returns / **returns** (B2) | returns / `42501` | returns (G7) / `42501` (G8) |
| Two acceptances at once, same user | 1 membership | 1 | 1 (R5: second `P0002`) |
| Two persons, same address in another case | **2 memberships** (R4) | refused at registration (P3) | refused at registration (P3) |
| Membership committed with no scope row | **commits** (F1, F3) | `23514` (P4) | **commits** directly (F1, F3); the function always writes it (X1) |
| Accepting selects the tenant | — | — | no: `app.tenant_id` unset after it (G12) |
| Who may execute the function | — | — | `app_user` and its owner (G18); provisioner, authenticator, job_runner `42501` (G13–G15) |

**The ten checks.** Draft, with the v1.16 inputs: Checks 2, 6 and 10 fail (three policies drifted plus
`invitations_provisioner_found`; `INSERT` on `invitations` and `UPDATE (accepted_by)`; ten undeclared chain links). With
inputs regenerated for the draft — manifest, two declared chains, the grants — all ten pass, and each fails on its
plant. **B**, with the v1.16 inputs: only Check 6 fails — `app_user can execute public.accept_invitation()`. Check 2
and Check 10 pass on the v1.16 manifest and chains unchanged. With `public.accept_invitation` in `executable_routines`:
all ten pass, each fails on its plant.

**The existing suites through EF** (current code, nothing calling the function yet):

| Database | White-box | Conformance | What fails |
|---|---|---|---|
| Draft P3 + P4 | 84/85 | 143/144 | `T3_Test26…ResolverThrows`, `T3_8_Test18…LoudAtTenantSelection` — their setup commits a membership without its scope (P4: `23514`) |
| Draft whole | 82/85 | 112/144 | the two above; and bootstrap refused (`42501` on `memberships`: no founding invitation yet) — `T4_9`, `T4_10`, and 31 conformance tests whose setup bootstraps a tenant (`Expected: Created`). Phase B's code must change bootstrap and acceptance; not measured past that. |
| B (P3 + function) | 85/85 | 144/144 | none |
| **P3 alone** (option C; run 37139863515) | **85/85** | **144/144** | **none** |

## Draft and B side by side

| | Draft (P1, P1b, P2, P3, P4) | B (P3 + `accept_invitation`) |
|---|---|---|
| **§3.9** | Three policy texts rewritten, one added; **two new chains** (`memberships → invitations, users, persons`; `invitations → memberships, users, persons`) against "no new chains". | Unchanged: no policy, no chain. |
| **§4.4** | Bootstrap (3a) must create a founding invitation; the provisioner gains `INSERT` on `invitations` (4.4/1 names `app_user` as the one direct writer of invitations). | Bootstrap (3a) unchanged. **But** acceptance by an existing account leaves path 3b — "exactly two confined paths", provisioner on its own connection — for `app_user` via the function: 3b's text changes. |
| **§4.5** | Normalized match, enforced in the DB on **every** provisioner write that creates or re-activates a membership — the new-account path included. | Normalized match enforced in the DB **on the function's path only**. The new-account path stays the provisioner's, checked by the application (A2 and B2 keep their v1.16 result for direct writes). |
| **§3.4** | — (P4's trigger function is owned by `migrator` but not executable by any role, and only reads.) | A routine owned by `migrator` (BYPASSRLS) reached from application code: against condition (1) "no path from application code ever reaches it" (the text says the connection; the privileges reach). |
| **§7** | Unchanged: the writes stay EF's, audited by the interceptor. | The function writes `audit_log` itself, as its owner: a **fourth writer** against "no fourth writer", and the masking list repeated in SQL outside the interceptor. |
| **§3.6 (1618)** | — | The document rejected a `SECURITY DEFINER` function "for now" elsewhere (assignment resolution) as "a new bypass surface that needs its own dedicated audit". Not a policy expression, so Check 10's limit does not apply — the reasoning does. |
| **New columns** | `invitations.accepted_by` (FK to `users`). | None. |
| **New grants** | provisioner: `INSERT` on `invitations`; `UPDATE (accepted_by)`. | `app_user`: `EXECUTE` on one function — the first entry in `executable_routines`. |
| **Other objects** | `persons_email_normalized_key`; deferred constraint trigger and its function (P4). | `persons_email_normalized_key`; the function. |
| **Existing tests broken now** | P3+P4: 2 (fixtures committing a membership with no scope — their setup must change, or P4 dropped). Whole: 34 more until phase B's code changes bootstrap and acceptance. | 0 of 229. |
| **Existing tests at risk in phase B** | Those above; then expected to pass with the code changed (not measured). | `T4_10_Test28c` asserts the return path's EF commands (`UPDATE membership_scope`, `DELETE FROM membership_roles`); moved into the function, EF no longer sends them — its expectations would change. |
| **Privileged surface** | No callable routine. The guarantee is in policies the checks read (Check 2 compares their text, Check 10 their chains). Provisioner widened slightly, each widening fenced by a policy. | One callable routine with **all** RLS off inside it: the guarantee is ~100 lines of PL/pgSQL, read by review. The checks see only who may execute it: Check 6 does not tie a routine to a role (any application role on the list passes), and no check reads `prosecdef` or `search_path` (the probe's G13–G18 do). `app_user` — the role on every request — can create a membership in any tenant given a valid token and a matching `app.user_id`. The raw token reaches the database as an argument (the application could pass its hash instead). |
| **Condition 4 (no membership without scope)** | In the DB, every path (P4). | On the function's path; other paths as v1.16. |
