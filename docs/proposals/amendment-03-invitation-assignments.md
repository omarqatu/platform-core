# Proposal for Amendment 03: assigning clients with the invitation

**Status:** a proposal, **second draft with the project owner's answers of 2026-10-26** (Q1–Q9). **Not the
amendment, and not built.** Nothing here enters
`src/`, `src/Migrations/` or the checks' inputs until Amendment 03 is written in Arabic, approved and merged, and the
facts in "What must be proven first" have been measured.
**Base:** PLATFORM_CORE v1.16 + Amendment 01 + Amendment 02; PROOF_SPEC v1.3; first draft at `53f4284`, this one
at `6c52311`.
**Origin:** the project owner's list, item 38 (group ج, 2026-10-24). Moved out of the group by his decision of
2026-10-25 because it needs the document, not only a migration.

Every policy below is **illustrative text**, written to make the question concrete — not the text proposed for the
document, and not a migration. Every point marked **Q** was a question for the project owner; each now carries his
answer, marked **Decided**.

---

## 1. The problem, and why it is not a migration

`intended_scope_mode` is carried on the invitation and applied at acceptance. The client assignments are not. So a
manager who invites someone with "assigned clients" has to wait until they accept before saying which ones; until
then the new member is `assigned` to nothing and sees no client.

The assignments cannot be written at invitation time, because `scope_assignments.membership_id` points at a
membership, and **no membership exists before acceptance**. The clean shape follows `intended_scope_mode`: hold the
chosen clients on the invitation, and create the assignments at acceptance.

Acceptance runs as `provisioner`, which today holds `SELECT` and `UPDATE (active)` on `scope_assignments` (both
confined to a `left` membership — the return path, §3.10 D9/D10) and **no `INSERT`**. The grants matrix is
Amendment 01 §3, and the manifest the checks run against is extracted from the documents
(`checks/core/generate-manifest.py`), never hand-edited. A new grant is therefore a change to the document, and the
new table needs its three layers written there first. Hence an amendment.

## 2. What it needs from the document

### 2.1 `invitation_scope_assignments` — a new tenant table (to §4.1, beside `invitations`)

One row per client chosen for an invitation:

| Column | Note |
|---|---|
| `id` | the application's `Guid.CreateVersion7()` |
| `tenant_id` | composite FKs on it (Check 7) |
| `invitation_id` | **composite FK `(tenant_id, invitation_id) → invitations (tenant_id, id)`**. `invitations` has only `PRIMARY KEY (id)` today, so the amendment also adds `UNIQUE (tenant_id, id)` on `invitations` |
| `scope_ref_id` | `NOT NULL` (the 1.8 rule). No FK to `clients`, as `scope_assignments` has none: the scoped entity's home is unsettled (PROOF_SPEC §4) |
| `assignment_role` | nullable, with the same `CHECK` as `scope_assignments` (`lead`/`contributor`/`reviewer`) when given |
| `created_at` | the application's `DateTime.UtcNow` |

`UNIQUE (tenant_id, invitation_id, scope_ref_id)`. No `UPDATE` and no `DELETE` grant for anyone: the rows are written
once and become inert when the invitation stops being pending (§3.3).

**Q1.** Is `assignment_role` carried on the invitation, or does every pre-assignment take `contributor`, as the
assignment screen does today when none is given?
**Decided:** carried on the invitation. When none was chosen the column is `NULL`, and the application writes
`contributor` at acceptance — the default lives in one place, the acceptance, as it does on the assignment screen.

### 2.2 Its policies

- **Write (`app_user`, `FOR INSERT`):** the writer holds `can_manage_scope`, and the invitation is pending and
  addressed to scope `assigned` (Q9: it need not be the writer's own). Illustrative:

  ```sql
  CREATE POLICY invitation_scope_assignments_insert ON invitation_scope_assignments FOR INSERT TO app_user
    WITH CHECK (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                AND COALESCE((SELECT NULLIF(current_setting('app.can_manage_scope', true), '')::boolean), false)
                AND EXISTS (SELECT 1 FROM invitations i
                             WHERE i.tenant_id = invitation_scope_assignments.tenant_id
                               AND i.id = invitation_scope_assignments.invitation_id
                               AND i.status = 'pending' AND i.intended_scope_mode = 'assigned'));
  ```

  `can_manage_scope` is **required, not optional**. Without it, a manager holding `core.members.manage` alone — who
  may invite, but may not assign — would assign clients through the back door of an invitation, crossing from one
  axis into the other.

  **Q9 (new, the project owner's).** The first draft also required `i.invited_by = app.user_id`: only the inviter
  could pre-assign. **Decided: dropped.** A scope manager manages scope for everyone — `scope_assignment_insert`
  admits any holder of `can_manage_scope` to assign any colleague, whoever invited them — so binding the
  pre-assignment to the inviter would make the invitation stricter than the assignment it stands in for. The rule is
  `can_manage_scope` + pending + `assigned`. `client_scope` still confines an assigned scope manager to the clients
  they see.
- **`client_scope` (`AS RESTRICTIVE`, Check 8):** the table carries `scope_ref_id`, so the binding rule of §3.6
  applies and Check 8 demands the standard `client_scope` on it. **This is what makes an assigned inviter pre-assign
  only clients they can see**: the policy's `WITH CHECK` is the inviter's own assignments when they are not
  `scope_all`. The template reads `scope_assignments`, not this table, so there is no self-reference and no need for
  the structural exception `scope_assignments` itself takes (1.14).
- **Read (`app_user`, `FOR SELECT`):** a holder of `can_manage_scope` in this tenant, narrowed by `client_scope` like
  any scoped table.

  **Q2.** Should the pre-assigned clients be readable by a holder of `core.members.manage` who does not manage scope?
  The invitations list is theirs; the assignments are not (§4.8/1.8).
  **Decided: no.** The read policy requires `can_manage_scope`. A members-only manager sees only the count, "N clients
  chosen", which the endpoint computes under its own permission check and returns as a number, never as rows.
- **Read (`provisioner`, `FOR SELECT`):** this tenant's rows (`tenant_id = app.tenant_id`), for acceptance.
  `client_scope` is `TO app_user` and does not apply to it.

### 2.3 `provisioner` writes on `scope_assignments` (amends Amendment 01 §3 and the 1.16 §4.1 policies)

Two writes, both at acceptance, both inside the one provisioner transaction:

1. **`INSERT`** for a pair (membership, client) that has no row — every pair of a new member, and the new pairs of a
   returning one.
2. **`UPDATE (active)` to `true`** for a pair a returning member already holds. `UNIQUE (tenant_id, membership_id,
   scope_ref_id)` keeps one permanent row per pair (§4.8); a returning member's rows were disabled by
   `ReturnAsync`'s step 3 and are reactivated, never duplicated. Today's `scope_assignments_provisioner_disable`
   admits `NOT active` only, so reactivation needs its own policy.

**The binding is the hard part.** A policy has to say "only the pairs this invitation listed, for the membership
this acceptance is creating". The first draft's illustration, deliberately incomplete (the binding below, under
"Decided", replaces it):

```sql
CREATE POLICY scope_assignments_provisioner_insert ON scope_assignments FOR INSERT TO provisioner
  WITH CHECK (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
              AND EXISTS (SELECT 1 FROM invitation_scope_assignments isa
                            JOIN invitations i ON i.tenant_id = isa.tenant_id AND i.id = isa.invitation_id
                           WHERE isa.tenant_id = scope_assignments.tenant_id
                             AND isa.scope_ref_id = scope_assignments.scope_ref_id
                             AND i.status = 'pending'));
```

As written, it admits **any** membership of the tenant being given **any** client that **some** pending invitation
lists. It does not tie the row to *this* acceptance's membership.

**Q3.** How is the row bound to this acceptance? The options found, none of them free:

- **(a)** a transaction-local variable naming the invitation (`app.invitation_id`), set by the acceptance path
  beside `app.tenant_id` in `ProvisionerUnitOfWork`. It reads cleanly in the policy, and it is not one of the four
  second-axis variables Check 9 confines (`app.membership_id`, `app.scope_all`, `app.can_manage_scope`,
  `app.can_manage_members` — `checks/core/check-09.sh`), so Check 9 does not refuse it as written. But it is a
  variable a policy reads to bound a **scope write**, and the document lists the variables policies may read (3.5,
  3.9): it needs its own text there. **Q3a.** Should it join Check 9's confined list, with `ProvisionerUnitOfWork` as
  its one writer, so that a request can never be the source of it?
- **(b)** a column `scope_assignments.invitation_id` (nullable: written only on this path), checked against the
  pending invitation in the policy. It ties the row to its invitation durably, and doubles as the audit link of §3.4 —
  but it changes a core table every scoped read depends on, through `client_scope`.
- **(c)** order and status alone: the membership is `active` and was created in this transaction, which the
  database cannot see; `xmin` is not a contract. Rejected unless (a) and (b) are both refused.

The proposal's preference is **(a)**, because it confines the change to the provisioner path and leaves the table
every scoped read touches as it is.

**Decided: (a)**, and **Q3a: yes** — `app.invitation_id` joins Check 9's confined list
(`checks/core/scope-variable-writers.json` and the pattern in `check-09.sh`), with `ProvisionerUnitOfWork` as its
single writer. A request can never be its source.

**The gap in (a), found in review (the project owner's), and its fix.** `app.invitation_id` binds the row to *the
invitation*, not to *this acceptance's membership*: a policy that checks only `i.id = app.invitation_id` still admits
**any** membership of the tenant being given the invitation's clients. The invitation must name the membership it
produced.

- **Does `invitations` carry the resulting membership today? No.** Its columns are `id, tenant_id, email, role_id,
  token_hash, status, invited_by, expires_at, created_at, intended_scope_mode`; `provisioner` holds
  `UPDATE (status)` only; acceptance writes nothing but `status = 'accepted'`.
- **So the amendment adds `invitations.accepted_membership_id`:** nullable, `NULL` until acceptance, with a composite
  FK `(tenant_id, accepted_membership_id) → memberships (tenant_id, id)` (Check 7). A new column grant,
  `UPDATE (accepted_membership_id)` to `provisioner` only; `app_user` is not given it. The provisioner's update policy
  on `invitations` gains a condition that the column can be set once, from `NULL`, while the invitation is pending.
- **The insert policy then checks the membership, not only the invitation.** Illustrative:

  ```sql
  CREATE POLICY scope_assignments_provisioner_insert ON scope_assignments FOR INSERT TO provisioner
    WITH CHECK (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                AND EXISTS (SELECT 1 FROM invitations i
                              JOIN invitation_scope_assignments isa
                                ON isa.tenant_id = i.tenant_id AND isa.invitation_id = i.id
                             WHERE i.tenant_id = scope_assignments.tenant_id
                               AND i.id = (SELECT NULLIF(current_setting('app.invitation_id', true), '')::uuid)
                               AND i.status = 'pending'
                               AND i.accepted_membership_id = scope_assignments.membership_id
                               AND isa.scope_ref_id = scope_assignments.scope_ref_id));
  ```

  The reactivation policy for a returning member (`UPDATE (active)` to `true`) carries the same condition.
- **The order this imposes on acceptance**, in its one transaction: the membership exists (created, or the returning
  one) → **the invitation is updated with `accepted_membership_id`** while still `pending` → the assignments are
  written or reactivated → the invitation becomes `accepted`, last, as today (its `WHERE status = 'pending'` remains
  the single-use guard). Written before the invitation names the membership, an assignment is refused by the policy;
  that is the ordering's proof, in §5 item 4.
- **A by-product:** the invitation records which membership it produced, which nothing records today.

### 2.4 The grants matrix (Amendment 01 §3)

| Table | `app_user` | `provisioner` |
|---|---|---|
| `invitation_scope_assignments` (new) | `SELECT`, `INSERT` | `SELECT` |
| `scope_assignments` | unchanged (`SELECT`, `INSERT`, `UPDATE (active)`) | **`INSERT`** (new) beside `SELECT`, `UPDATE (active)` |
| `invitations` | unchanged | **`UPDATE (accepted_membership_id)`** (new) beside `SELECT`, `INSERT`, `UPDATE (status)` |

Plus the new policies entering the manifest through `checks/core/amendment-policies-built.json` in the PR that builds
them, and the declared chains in `policy-chains.json`:
`invitation_scope_assignments → scope_assignments` (`client_scope`),
`invitation_scope_assignments → invitations` (the insert check), and
`scope_assignments → invitations → invitation_scope_assignments` (the provisioner insert and reactivation). Changing
either file is a project-owner decision. And Check 9's confined list gains `app.invitation_id` (Q3a), its writers
file `ProvisionerUnitOfWork`.

## 3. The four decisions, as written

1. **A client disabled between invitation and acceptance is assigned anyway.** OPEN_ITEMS 108 decided that disabling
   a client stops its reminders and changes nothing about who sees it; it is assignable like any other. The
   pre-assignment follows the same rule: the acceptance creates the row, and the badge shows on the assignment screen.
2. **There is no deletion path for a client.** `app_user` holds `DELETE` on `clients`, but no endpoint uses it, and
   `subscriptions_client_fkey` refuses deleting a client with subscriptions. `scope_ref_id` has no FK, as on
   `scope_assignments`; if a vertical ever deletes a scoped entity, the pre-assignment becomes an assignment to an
   entity that no longer exists, exactly as an existing assignment would. Nothing is built for it here.

   **Q4.** If a deletion path is ever built, should it withdraw that entity's pending pre-assignments in the same
   transaction?
   **Decided: yes, recorded now.** Whoever builds a deletion path for a scoped entity removes its pending
   pre-assignments in the same transaction — which needs a `DELETE` grant and policy on `invitation_scope_assignments`
   that this amendment does not give, so it is that change's to write. To be carried as an OPEN_ITEMS entry when the
   amendment is built.
3. **Expiry and withdrawal leave the rows inert.** An invitation that expires (seven days) or is withdrawn is refused
   by acceptance, so its rows are never read again. No job deletes them — nothing writes `status = 'expired'` today
   either — and no `DELETE` grant exists. They are history: who intended to assign what, and when.
4. **The audit is split: the decision at invitation, the execution at acceptance.**
   - **The decision:** the `invitation_scope_assignments` insert is a tracked write under `app_user`, so the
     automatic audit records it with the **inviter** as actor and the time of invitation. This answers "who
     assigned, and when".
   - **The execution:** the `scope_assignments` insert or reactivation at acceptance is audited by the provisioner
     path, with the **invitee** as actor (the acceptance's `app.user_id`). Read alone, that row would say the invitee
     assigned themselves, so each one carries **`reason = 'invitation:<invitation id>'`**, which points at the
     decision.

   **Q5.** Is `reason` the right carrier for that link, or should it be option (b)'s `invitation_id` column if Q3
   chooses (b)? `reason` is free text today, written by the assignment screen as `null`.
   **Decided: `reason` carries it.** The structural link is now `invitations.accepted_membership_id` (Q3), so the
   database can always answer "which invitation produced this membership's assignments"; `reason` is the readable
   pointer on each audited row.

## 4. Other open questions

- **Q6.** An invitation in mode `all` with pre-assigned clients: refused (the insert policy above requires
  `intended_scope_mode = 'assigned'`), or allowed so the assignments wait for a later move to `assigned`?
  **Decided: refused** — under `all` they would be invisible and read as a mistake.
- **Q7.** The interface: the invite form gains the client list of the assignment screen, shown only to an inviter
  holding `core.scope.manage` and only under `assigned`. Item 36's warning stays as it is. Does the invitations list
  show the chosen clients, or only their number (see Q2)?
  **Decided: as Q2** — the clients to a scope manager, the count to a members-only manager.
- **Q8.** A returning member (D9) whose invitation names clients: their old assignments are disabled by
  `ReturnAsync` step 3, then the invitation's are inserted or reactivated. Confirm that the invitation replaces
  their past assignments rather than adding to them — which is what step 3 already implies.
  **Decided: replace, not add — and proven with a test** (§5 item 10).

## 5. What must be proven on PostgreSQL 18 before any migration

Each one measured, not argued, in the manner of the earlier proposals' `pg18-proof` folders:

1. `client_scope` on `invitation_scope_assignments` evaluates without recursion, and Check 8's deparse-against-deparse
   comparison accepts it as the standard text.
2. An **assigned** scope manager (the inviter or not — Q9) can insert a row for a client they are assigned and is
   refused (`42501`) for one they are not; a `scope_all` scope manager can insert any; a writer without
   `can_manage_scope` is refused for every client.
3. `provisioner`, with the acceptance's variables, reads the invitation's rows, and `client_scope` does not touch it
   (`TO app_user`).
4. The provisioner insert under the binding of Q3 admits exactly the listed pairs for the membership the invitation
   names, and refuses another membership, another client, another invitation (a different `app.invitation_id`), and
   another tenant's invitation. **The ordering:** an assignment written before the invitation is updated with
   `accepted_membership_id` is refused; the same write after it, in the same transaction, is admitted; and the
   column cannot be set a second time, nor by `app_user`.
5. Reactivation for a returning member, ordered against `ReturnAsync`'s steps while the membership is still `left`;
   and the `UNIQUE` conflict path when a listed client is already held.
6. The composite FK to `invitations (tenant_id, id)`, and the `UNIQUE` it needs, added to a table that holds rows
   (production has invitations).
7. The two audit lines: the inviter at invitation, the invitee at acceptance, with `reason` carrying the link.
8. Check 10 (`pg_depend`) sees the three declared chains of §2.4 and no undeclared one; the no-cycle rule of 4.6
   holds.
9. An expired and a withdrawn invitation: acceptance refused, rows untouched.
10. **Q8, replace not add:** a member who left holding assignments to clients A and B returns through an invitation
    naming B and C. Afterwards A is inactive, B is active (the same row, reactivated), and C is active (a new row) — as
    a Conformance test, red before the amendment is built.
11. **Check 9** refuses `app.invitation_id` written anywhere but `ProvisionerUnitOfWork`, and its self-test fails on a
    planted writer.
12. Adding `accepted_membership_id` and its FK to `invitations` while it holds rows: existing rows stay `NULL`, and an
    accepted invitation from before the amendment is untouched.

---

*Nothing in this file is in force. It becomes Amendment 03 only through the project owner's text in Arabic.*
