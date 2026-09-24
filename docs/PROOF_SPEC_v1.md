# PROOF_SPEC v1 — Core Proof-of-Concept Specification
## Codex tasks + implementation-independent acceptance criteria

**Governing reference:** `PLATFORM_CORE_v1_9_FROZEN.md` — every section, test, or check number in this spec refers to it.
**Version:** 1.0 — **Date:** 2026-09-23
**Stack:** .NET (latest LTS) + EF Core + Npgsql + PostgreSQL 18 — settled (the document, Section 0).

---

## 0. The Dual Purpose — and What Follows From It

This spec performs two functions in a single text:

1. **Build instructions** for Codex, task by task, starting at T0.
2. **An acceptance criterion** against which any other implementation of the core is measured — including an attempted retrofit built on a different project.

**The design consequence:** acceptance criteria are written as **externally observable behavior** (a SQL query under a given role, or an HTTP request, and its expected result) — not as "use such-and-such class." The tests live in an independent project (`Conformance`) that takes connection strings and the seed contract from configuration, so it runs against Codex's database and against any other database that satisfies the seed contract (Section 7).

**An explicit limit:** some tests are **white-box** — they check an internal mechanism not visible from the outside (such as throwing an exception on a read taken outside a transaction). These are tagged `[W]`, and for these, an **equivalent proof** is accepted from another implementation, not the exact test. The rest, `[B]`, are black-box and run as-is.

---

## 1. Binding Rules of Engagement for Codex

```
1.  One task = one branch = one PR. Do not start a task before its
    predecessor has merged and all of its tests are green.
2.  Definition of "done": every acceptance criterion for the task
    is green in CI, and no test from a prior task has been
    disabled, skipped, or weakened.
3.  Editing a test to make it pass is forbidden. A test that looks
    wrong → stop and write the reason in the PR description. The
    decision belongs to the project owner, not to you.
4.  BYPASSRLS is forbidden for any role other than migrator.
    SECURITY DEFINER is forbidden anywhere at this stage (the
    document, 3.6 Check 10, 4.8).
5.  Writing a policy without explicit FOR and TO is forbidden
    (3.1).
6.  Any database access outside an explicit transaction is
    forbidden (3.5).
7.  Reading scope variables from a request, a header, or a token is
    forbidden (3.5/6, Check 9).
8.  Any conflict between this spec and the document → the document
    governs, and the discrepancy is recorded in the PR description.
9.  Anything mentioned neither here nor in the document → do not
    build it. Ask.
10. No business logic beyond what the task requires. The proof
    demonstrates isolation, not the product.
```

---

## 2. Task Map

| Task | Subject | Depends on | Document tests closed |
|---|---|---|---|
| **T0** | Structure, the five roles, connections, CI pipeline | — | 9, 22 |
| **T1** | The transaction layer — "loud above, silent below" | T0 | 3, 4, 5, 14 |
| **T2** | The core schema: the three layers + the ten CI checks | T1 | 1, 2, 6, 8, 10, 12, 13, 15, 18, 21, 23 |
| **T3** | Login and tenant selection + second-axis resolution | T2 | **26 first**, 7, 11 (partial), 20, 24‑c |
| **T4** | Bootstrap + the invite-and-accept cycle + departure | T3 | 10, 11, 18, spec items a, c, d, f, g |
| **T5** | The first scoped module (a minimal subscriptions module) | T4 | 16, 17, 19, 24, 25 |
| **T6** | The cross-tenant unified view | T5 | 3 (via fan-out), 19 (merged) |
| **T7** | Background jobs | T5 | System context (8/5) |
| **T8** | Measurement + one RTL screen | T6, T7 | Performance threshold (T8) |

**A declared deviation from the document's ordering (13/1):** the document orders **by feature**: login first, then invitation, then the unified view, then adversarial isolation. Here the order is **by dependency**: no login without a schema, and no schema without a transaction layer. A second difference is intentional: **adversarial tests are not batched into a late task** — each is closed in the task that builds its own surface. The isolation that gets tested last elsewhere is, here, built with a safety net the whole way.

**The first thing proven remains as the document says (6.5):** the two hardest points — second-axis resolution (T3, Test 26) and the unified view (T6) — not the subscriptions screen.

---

## 3. The Tasks

### T0 — Structure, Roles, and Connections

**Build:**
- A .NET solution with separate projects: `Core` (the core), `Modules.Subscriptions` (the minimal module — T5), `Api`, `Migrations`, `Conformance` (the tests — independent, referencing `Core` only via HTTP and SQL).
- PostgreSQL 18 in a container for development and CI.
- **The five roles (3.4):** `app_user`, `authenticator`, `job_runner`, `migrator`, `provisioner`. No application role is a superuser or an owner.
- **Five separate connection strings**, one per role. `Api` holds only three: `app_user`, `authenticator`, `provisioner`. `migrator` is loaded only into the migration tool. `job_runner` only into the background-job worker.
- Migrations run under `migrator` alone, from a separate tool, not from application startup.
- CI pipeline: build, migrate on a clean database, catalog checks (T2), the `Conformance` project.

**Spec item k (the technical enforcement confining connections):** at this stage it is enforced by configuration: the `migrator` connection string is absent from `Api`'s configuration and from its image. Network-level enforcement (blocking the application network from reaching the `migrator` account at the `pg_hba.conf` level) **is documented as a production requirement** and is not built into the proof-of-concept.

**Acceptance criteria:**
| # | Criterion | Type |
|---|---|---|
| T0.1 | `SET ROLE migrator` and `SET ROLE provisioner` from an `app_user` connection → fail (Test 22) | [B] |
| T0.2 | No application role is a member of `migrator` or `provisioner` (`pg_auth_members`) (Tests 9, 22) | [B] |
| T0.3 | `Api`'s configuration and image contain no `migrator` connection string | [W] |
| T0.4 | A clean database → a full migration with `migrator` → succeeds with no manual steps | [B] |

---

### T1 — The Transaction Layer

**Build (3.5):**
- A unit-of-work middleware: every request → a single explicit transaction.
- `DbCommandInterceptor` **throws** if a command runs with no active transaction (the loud upper layer).
- Variables are set with `SET LOCAL` as independent statements — **appending them to command text via the interceptor is forbidden** (the documented Npgsql pitfall, 3.5/3).
- The setting order in this task: `app.user_id` then `app.tenant_id`. (Second-axis variables come in T3.)
- Connection state is reset when it returns to the pool — nothing relies on `SET LOCAL` alone persisting.
- A rows-affected guard: a write tagged "critical" that affects zero rows → an exception.

**Acceptance criteria:**
| # | Criterion | Type |
|---|---|---|
| T1.1 | A command with no transaction → an explicit exception, not zero rows (Test 5) | [W] |
| T1.2 | Without `app.tenant_id` → zero rows on a tenant table (Test 4) | [B] |
| T1.3 | A thousand concurrent iterations on the same pool, alternating between two tenants → zero leakage (Test 3) | [B] |
| T1.4 | A critical update to an invisible row → a rows-affected error, not success (Test 14) | [W] |
| T1.5 | A batch update of twenty rows via EF → succeeds with no phantom exceptions | [W] |

---

### T2 — The Core Schema: the Three Layers

**Build:** every table in the document's Sections 4.1, 4.2, 4.5, 5, 7, and 4.8 (`membership_scope`, `scope_assignments`), to the following specification, literally:

- **Policies:** the standard `tenant_isolation` template (3.1) on every table not named in the manifest; and the manifest's sets exactly as written in the document's text, including `system_role_guard` (5), `audit_read` (7), and the five 4.8 policies. **No policy is written that does not exist in the document.**
- **Grants:** the 3.8 matrix, literally — including the column-level grants.
- **Constraints:** every FK between two `tenant_id` tables is composite (3.3), and every parent carries `UNIQUE (tenant_id, id)`.
- `RLS ENABLE` and `FORCE` on **every** table, and an `rls_exemptions` table that exists and is empty.
- The manifest itself is **a file in the repository** (JSON or YAML) that Check 2 reads from — not a list hard-coded inside the check's code.

**Spec item n:** seed the `core.scope.manage` permission into the `permissions` catalog, and grant it by default to the seeded `owner` and `admin` roles — a `migrator` migration item.

**Spec item b (`auth_attempts` retention):** a `migrator` pruning procedure that deletes rows older than **90 days**, documented and scheduled outside the application. The figure is changeable by the project owner's decision, not Codex's.

**The ten CI checks (3.6)** — all written here and run on every subsequent PR. Check 10 is built on `pg_depend` (`classid = 'pg_policy'::regclass`), and the declared chain list (4.6) is a file in the repository, alongside the manifest.

**Acceptance criteria:**
| # | Criterion | Type |
|---|---|---|
| T2.1 | Checks 1–10 are green on the resulting schema | [B] |
| T2.2 | A negative test for every check: a trial migration that deliberately breaks it → the check fails (proving the check actually works, not merely that it is always green) | [W] |
| T2.3 | Document tests 1, 2, 6, 8, 10, 12, 13, 15, 18, 21, 23 | [B] |
| T2.4 | Test 23 is executed **via direct SQL** with the `app_user` role, not via the API | [B] |

**A note to Codex:** T2.2 matters more than T2.1. A check that has never been seen to fail once cannot be trusted for being green.

---

### T3 — Login, Tenant Selection, and Second-Axis Resolution

**Start with Test 26 before any code.** Write it, watch it fail, then build.

**Build:**
- The `authenticator` path (4.3‑a): resolve the credential, and write `auth_attempts` — this role's only write.
- A session via an HTTP-only cookie carrying `user_id` and the active tenant **only** — no scope, no permissions.
- A tenant-selection screen/endpoint via `membership_self` (4.3).
- **Second-axis resolution on every transaction, in the mandatory order (3.5/6):**

```
a. memberships via membership_self       → SET LOCAL app.membership_id
b. membership_scope via the self branch  → SET LOCAL app.scope_all
c. membership_roles → role_permissions
   → permissions                         → SET LOCAL app.can_manage_scope
```

Three separate reads, not a single join. The absence of a `membership_scope` row → a loud exception (3.5/7).

- **Test seeding:** in this task, tenants and members are created by `migrator` seeding (the seed contract, Section 7). The real creation path comes in T4.

**Acceptance criteria:**
| # | Criterion | Type |
|---|---|---|
| T3.1 | **Test 26:** resolution via a single join query before setting `app.membership_id` → a loud exception; the three-step order → succeeds | [W] |
| T3.2 | Test 7: `authenticator` resolves with no tenant context; `app_user` with `app.user_id` alone sees only their own memberships | [B] |
| T3.3 | Test 20: an assignment revoked during an active session → the **next** request no longer sees the entity, with no re-login | [B] |
| T3.4 | Test 24‑c: revoking `core.scope.manage` during a session → the next request fails on administration | [B] |
| T3.5 | A failed login → a row in `auth_attempts`; and an existing versus non-existent username → identical responses, in text and in approximate timing | [B] |
| T3.6 | The cookie contains no scope and no permission (checking its content) | [W] |

---

### T4 — Bootstrap, Invitation, Acceptance, and Departure

**Build:** `provisioner`'s two paths, and no others (4.4), each in a single transaction, with auditing in the same transaction.

**Decisions on carried-forward spec items — settled here:**

| Item | Decision |
|---|---|
| **a.** "The last owner cannot leave" | In the departure or role-change transaction: `SELECT … FROM tenants WHERE id = … FOR UPDATE` first, then count active owners. Locking the tenant row serializes two concurrent departures. **Not** an advisory lock (it does not show up in ordinary lock logs, and is harder to diagnose). |
| **c.** The acceptance provider | `'password'` is fixed in the acceptance path's contract. Its source becomes a tenant setting once SSO arrives — out of scope for the proof. |
| **d.** `invited_by` | `WITH CHECK (invited_by = app.user_id)` on creating an invitation, in addition to the tenant condition. |
| **f.** The invitation's scope mode | An `invitations.intended_scope_mode` column (`'all' \| 'assigned'`, NOT NULL, no default). At creation, it is verified that the creator holds `can_manage_scope` if the value is `'all'`, and it is copied as-is into `membership_scope` at acceptance. |
| **g.** The last `all`-scope membership cannot be downgraded | The same mechanism as item a: lock the tenant row, then count active `all` memberships before the downgrade. |

**Acceptance criteria:**
| # | Criterion | Type |
|---|---|---|
| T4.1 | Test 10: `app_user` with an admin role → a direct INSERT into `memberships` → fails | [B] |
| T4.2 | Test 11: an invitation to an existing email and to a non-existent one → identical responses | [B] |
| T4.3 | Accepting an invitation with an authenticated email ≠ the invitation's email → rejected, even with a valid token | [B] |
| T4.4 | A successful acceptance → membership + `membership_auth` + `membership_scope` + the invitation updated + an audit entry, **atomically**: killing the transaction midway → none of it exists | [B] |
| T4.5 | Two concurrent departures of the last two owners → one succeeds and one fails, never both | [B] |
| T4.6 | Two concurrent downgrades of the last two `all` memberships → the same result | [B] |
| T4.7 | An invitation with `intended_scope_mode = 'all'` from a member with no `can_manage_scope` → fails | [B] |
| T4.8 | bootstrap → a tenant + a first `owner` member with scope `all` + an audit entry, in a single transaction | [B] |

---

### T5 — The First Scoped Module

**Purpose:** to prove the second axis on realistically shaped data. **Not** to build the subscriptions product.

**Build — the bare minimum:**
```
Modules.Subscriptions:
  clients(id, tenant_id, name, …, UNIQUE (tenant_id, id))
  subscriptions(id, tenant_id, scope_ref_id NOT NULL, service_name,
                ends_on DATE, …,
    FOREIGN KEY (tenant_id, scope_ref_id) REFERENCES clients (tenant_id, id))
```

- `clients` is a **module** table, not a core one, and `subscriptions.scope_ref_id` references it via a composite FK **within the module**. This does not settle the question of the entity's home (the document, Section 9) — `scope_assignments.scope_ref_id` in the core remains without an FK, as the document decided. The proof demonstrates the mechanism; it does not settle the home.
- The restrictive `client_scope` policy (3.1) on `subscriptions` **and** on `clients` itself (the client is a scoped entity, and `clients.id` is its own `scope_ref_id` — a generated column or a synonym, decided and justified in the PR).
- API endpoints: the client list, the subscription list, creating a subscription, plus an audit-log read endpoint.
- **The scope-declaration contract (3.5)** on every list endpoint and on the log endpoint:

```json
{
  "scope_mode": "assigned",
  "visible_count": 3,
  "total_count": null,
  "has_more_in_scope": false,
  "items": [ … ]
}
```

**Spec item m:** `visible_count` = the number of items after **every** constraint together (scope, permission, status, search). `total_count` = the tenant's total under the same constraints **except scope**, filled in only under `scope_all`, and explicit `null` otherwise — **the field is always present**.

**Spec item i (the effect of disabling an assignment):** in the proof, it only cuts off visibility. There are no tasks or notifications in the proof, so there is no other effect. Recorded as an open question for the real module.

**Acceptance criteria:**
| # | Criterion | Type |
|---|---|---|
| T5.1 | Test 16 in full (a — leakage, b — blinding, **including its decisive branch: an assigned, actually-assigned member sees their rows, not zero**, c — declaration, d — reading assignments) | [B] |
| T5.2 | Test 17: self-escalation of scope is blocked by both layers | [B] |
| T5.3 | Test 19: `total_count = null` for an `assigned` member on **every** endpoint, and a real number for an `all` member | [B] |
| T5.4 | Test 24 in full: a `viewer` with scope `all` cannot manage assignments from the database; and a scope manager with scope `assigned` manages them and sees only their own assigned entities | [B] |
| T5.5 | Test 25: an `assigned` member → the audit log returns zero rows, and the endpoint declares `scope_mode` rather than showing emptiness | [B] |
| T5.6 | INSERT of a subscription with `scope_ref_id` for an unassigned client, from an `assigned` member → fails under `WITH CHECK` | [B] |
| T5.7 | Check 8 is green: both tables carrying `scope_ref_id` have `client_scope` and `NOT NULL` | [B] |

---

### T6 — The Cross-Tenant Unified View

**Build (6):** fan-out for every membership of the user, **an independent transaction per tenant** with its own three-step resolution, keyset pagination with a cursor per tenant, and a k-way merge. An explicit cap on the number of tenants per batch (default value: **10**, configurable).

**Acceptance criteria:**
| # | Criterion | Type |
|---|---|---|
| T6.1 | A user with scope `all` in one tenant and `assigned` in another → the merged result carries the scope **per tenant**, with `total_count` for the first tenant only | [B] |
| T6.2 | Paginating three consecutive pages over data with interleaved ordering across tenants → no duplication and no loss (compared against a computed reference ordering) | [B] |
| T6.3 | A query failure in one tenant → the rest returns, and the failing tenant's context does not leak into the next | [B] |
| T6.4 | A member in 25 tenants → progressive paging, no memory collapse | [B] |
| T6.5 | The unified view is read-only: no write endpoint runs through it | [W] |

---

### T7 — Background Jobs

**Build (8):** a separate worker with a `job_runner` connection to fetch active tenants only, then an independent `app_user` transaction per tenant with a system context: `app.scope_all = true`, `app.membership_id` unset, `app.can_manage_scope = false` (8/5). One trial job: count subscriptions expiring within 30 days per tenant and write an audit line.

**Acceptance criteria:**
| # | Criterion | Type |
|---|---|---|
| T7.1 | The job sees every subscription of the tenant, with no assignment | [B] |
| T7.2 | The job attempts to write `scope_assignments` → fails (`can_manage_scope = false`) | [B] |
| T7.3 | A failure processing one tenant → other tenants complete, with no context leakage | [B] |
| T7.4 | `job_runner` reads from `tenants` only the active ones, and reads no other table | [B] |

---

### T8 — Measurement and One RTL Screen

**Measurement (spec item j):** the actual stack, not an estimate. **The threshold is written here before the measurement and is not adjusted afterward:**

```
The reference query: a sorted list of 50 subscriptions, for an
assigned member assigned to 20 clients, in a tenant with 500
clients and 20,000 subscriptions.

Acceptance: p95 for the query under both policies
            (tenant_isolation + client_scope)
            ≤ 1.5 × p95 for the same query under tenant_isolation
            alone, under 20 concurrent requests.
```

Exceeding the threshold → **is not fixed in T8**. It is recorded with its numbers, and a decision is made between the two alternatives declared in the document (a materialized table, or a `SECURITY DEFINER` function) by the project owner.

**The screen:** a subscription-list screen, RTL, displaying the scope declaration to the user in human phrasing ("showing 3 clients assigned to you"). No design system — a single component proving the bidirectional layout works.

**Acceptance criteria:**
| # | Criterion | Type |
|---|---|---|
| T8.1 | A measurement report with raw numbers and a verdict against the threshold | [B] |
| T8.2 | The screen displays the scope declaration, and shows no total figure at all to an `assigned` member | [B] |

---

## 4. Explicitly Out of Scope for the Proof

Billing, public self-registration, SSO, identity deletion for compliance, single-tenant restoration, a complete design system, per-entity audit reads (13/h), settling the scoped entity's home (9), a distributed queue, an external search engine, expand/contract migrations (13/l — documented as a production requirement). **The proof demonstrates isolation, not product readiness** (the document, Section 11).

---

## 5. What Is Returned to the Project Owner, Not Decided by Codex

1. Any test that looks wrong.
2. Exceeding the measurement threshold.
3. Any need for `SECURITY DEFINER`, `BYPASSRLS`, or a policy not present in the document.
4. Any conflict between the document and Postgres's actual behavior — this specifically is **the only kind of finding that reopens the document** (Rule 1.9: it must survive the tests and be proven by the code).

---

## 6. Using the Spec as an Acceptance Criterion for Another Implementation

**The principle:** an implementation is measured by its behavior, not by its resemblance to Codex's implementation. The question is never "did you build X" but "does the test pass."

**Procedure:**
1. The implementation being evaluated provides: five connection strings for the five roles (or their functional equivalents, with a mapping table), a database satisfying the seed contract (Section 7), and the API endpoints named in T3–T6 with the same contracts.
2. The `Conformance` project is run against it, unmodified.
3. **`[B]` tests:** pass or fail — no negotiation.
4. **`[W]` tests:** an equivalent, written proof is submitted. Example: T1.1 accepts any mechanism that makes access outside a transaction **fail explicitly** — not one that makes it silent.
5. **The ten CI checks:** run directly against its schema; the manifest and the chain table are **its own**, and are compared against the document manually, once.

**What counts as a categorical failure regardless of everything else:** any failure in Tests 1, 2, 3, 15, 16‑a, 16‑b, 21, 25 — because these are leakage or blinding of the isolation mechanism itself, not of a feature.

**An explicit limit on the comparison:** the other implementation passing every test proves that it **satisfies the written contract**, not that it is free of flaws. The tests check what occurred to their author (the document, 3.7). And the standard for judging both implementations remains the same — including Codex's own.

---

## 7. The Seed Contract — Unified Test Data

Created by `migrator` seeding before every `Conformance` run, and must be achievable on any implementation:

```
Tenants:     Al-Amin (active), Maan (active), Suspended (suspended)

Persons and memberships:
  Omar     — Al-Amin: owner / all          — Maan: operator / assigned
  Sara     — Al-Amin: admin / all
  Khaled   — Al-Amin: operator / assigned  (assigned to: A, B)
  Layla    — Al-Amin: viewer / all         (no core.scope.manage)
  Rami     — Al-Amin: operator / assigned + core.scope.manage (assigned to: A)
  Nour     — Maan: owner / all

Al-Amin's clients:  A, B, C, D   — 5 subscriptions each
Maan's clients:     X, Y          — 3 subscriptions each
```

**The intended coverage:** Omar covers two different scopes across tenants (T6.1). Layla covers a `viewer` with scope `all` and no management permission (Test 24‑a). Rami covers a scope manager with scope `assigned` (24‑b). Khaled covers the standard consultant case. C and D are not assigned to anyone under `assigned` — they reveal any leakage.

---

## 8. The State at Completion

The proof is complete when: T0–T8 are merged, the twenty-six tests and the ten CI checks are green, the acceptance criteria added here (T*.n) are all green, and the measurement report is written. **Only then** is the question of the scoped entity's home opened (the document, Section 9) — with input from working code, not from text.
