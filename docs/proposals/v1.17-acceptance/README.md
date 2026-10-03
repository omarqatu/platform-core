# Proposal for PLATFORM_CORE v1.17: acceptance with an existing account, enforced in the database

**Status:** a draft for the project owner (phase A of the acceptance task). **Not built.** Nothing here enters `src/`
or `Migrations/` until the owner approves it and the document text is issued.
**Base:** PLATFORM_CORE v1.16 (frozen), PROOF_SPEC v1.3, `main` at the merge of #14.
**The text under test:** the six conditions of the owner's §3.10 addition (acceptance by a signed-in user of an
invitation to another tenant) and its closing paragraph (with no session, an email that has an account cannot register
again). The task referred to the text as attached; it was not attached, so the probe tests the conditions as the
owner wrote them in the previous message.

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
- **Where:** PostgreSQL 18 in GitHub Actions (`.github/workflows/probe-v1-17-acceptance.yml`) — the reference. The
  harness was developed against a local PostgreSQL 16, which proves the scripts, not the result.

## Results

*(filled from the CI run — see below)*
