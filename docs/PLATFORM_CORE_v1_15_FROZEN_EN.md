# Platform Core Document — Tenancy Layer
## A general-purpose SaaS platform — independent greenfield design

**Version:** 1.15 — **Frozen release**
**Date:** 2026-09-24
**Review history:** Seven architecture reviews + two model reviews (through 1.6), then a **requirement-driven change** in 1.7, then an **eighth external review** that settled 1.8, then a **ninth review** that settled 1.9, then **the first finding from running code**, which settled 1.10, then **testing two assumptions before T2**, which settled 1.11, then **two conflicts found by the same procedure**, which settled 1.12, then **a policy draft run before T2**, which settled 1.13, then **running the CI checks as written**, which settled 1.14, then **running a claim about the attribution columns**, which settled 1.15. The full changelog is in the appendices.
**Nature of this document:** Pure technical analysis, not persuasive writing.

**The freeze decision — and the limits of reopening it:** Freezing 1.6 was the right call about the **category** of findings, not about the document: the seventh review found half its findings were about the text's internal consistency — a category that writing the spec and the code exposes faster and more cheaply than an eighth text review. That decision stands: **any consistency defect or implementation-level item is resolved in the spec or the code, not in a new version.**

1.7, 1.8, and 1.9 are not of that category. **1.7** was a change to the scope model driven by a product requirement (Section 4.8). **1.8** fixed a **structural flaw in that model's mechanism**, uncovered by an external review: a nested policy chain that disabled the second axis entirely (3.1, 4.8). **1.9** fixed a **contradiction between a model decision and its mechanism** (scope being used as a substitute for permission) and **a gap that voided the axis's guarantee** (the audit log). All of these sit in the transaction layer and the identity layer — i.e., **before** T0, not after. The governing rule after 1.9: **this is the last text-only revision. The document is reopened only for a finding that survives running the tests and is proven by the code** — the justification is in Section 13. And **1.10 is the first version reopened under this rule**: a finding no text review caught, found by running PostgreSQL 18.6.

The remaining implementation items are explicitly carried forward to the first items of the spec (Section 13).

**Change in 1.15 vs. 1.14 — dropping the attribution columns from the scope surface:** on closing T2, a claim about the two attribution columns in the second axis's tables was run, and each turned out to lie in a different way. On PostgreSQL 18.6, in a rolled-back transaction: `provisioner` wrote a `membership_scope` row as acceptance does, then a scope manager changed `scope_mode`:

| Column | After the manager's change |
|---|---|
| `scope_mode` | Changed ✅ |
| `updated_by` | **Still the name of whoever first wrote the row** — the grant is `UPDATE (scope_mode)` alone, so the update cannot write it |
| `updated_at` | **Still the insertion date** — for the same reason |

So the row says someone else changed it, on another date, and both values look entirely correct — worse than a missing value. And `scope_assignments` has the opposite problem: `granted_by` **can be forged** — a scope manager inserted an assignment attributed to a colleague and it was accepted, because the policy does not bind it to `app.user_id` the way `invited_by` is bound (spec item d).

**The decision: drop all four columns** (`updated_by`, `updated_at`, `granted_by`, `granted_at`). The audit log (7) records the actor and time of every change, in the same transaction, mandatorily — the columns were a second source for the same information, **and it drifted from the first before a single line of application code was written**. The alternative (widening the grant and binding both columns to `app.user_id`) was rejected: four changes to obtain a copy with no consumer — and "design on what you have seen" settles it. A screen needing "who assigned this" reads the audit log, or the column is added then, for a real reason. Proven by running: the 4.8 policies and their grants work unchanged without the four columns.

**Change in 1.14 vs. 1.13 — Check 8 was demanding infinite recursion:** a defect from 1.7, through nine reviews and six versions, found by running the CI checks as written against a full schema before T2. The check requires `client_scope` on every table carrying `scope_ref_id`, and `scope_assignments` carries it — but the template reads it, so satisfying the check is infinite recursion (`42P17`) that fails every read on every scoped table. The fix is one structural exception with a guarding inverse clause (3.6, Check 8). Also proven alongside it: Checks 1, 4, 5, and 10 as written are correct on the full schema, and the 1.13 texts match the executed draft (31 of 32 byte for byte, the last deparse against deparse).

**Change in 1.13 vs. 1.12 — the missing texts, bootstrap from templates, and generated values:** before T2, it emerged that the document writes 22 policies in full plus two templates, while every grant in the 3.8 matrix to a role other than `migrator` needs a policy for the same role and command — so under `FORCE RLS` a grant with no policy is **dead**. A draft was written, run on PostgreSQL 18.6 in both directions (108 SQL cases passing, plus a real EF path in a separate database), and then entered the document. Five things:

**(1) 32 missing policies are now written out (Section 3.9).** The proof they are the ones needed: running **without** them failed 41 of 42 intended-path cases — including the login path (`users_authenticator_read`) and scope-resolution step c (`permissions_read`), which would have returned **zero rows silently**, not an error. The same class the document has hunted since 1.5.

**(2) `provisioner` is confined to one tenant per transaction.** It sets `app.tenant_id` to the target tenant inside its own transaction, and all its policies on tenant tables are `WITH CHECK (tenant_id = app.tenant_id)`. So a defect in the most dangerous role in the system cannot write into two tenants within one transaction. Global tables with no `tenant_id` stay `true`.

**(3) Bootstrap was impossible, and is now possible.** Nothing in the system could create a new tenant's roles at runtime: `provisioner` had no grant on `roles`, `migrator` seeds only at migration time, and `app_user` is blocked from system roles. The fix: `provisioner` creates them from a new global catalog (`role_templates` and `role_template_permissions`) seeded by `migrator` — one source of truth for new tenants and for upgrading existing ones (5). And it yielded a finding: without read access to the templates, `INSERT … SELECT` statements insert **zero rows with no error** — so every template-derived insert is a critical write.

**(4) No `RETURNING` in any command, and no value generated by the database.** Proven by running it: `RETURNING` requires `SELECT` on the returned columns **and subjects the row to `SELECT` policies**. So a `provisioner` insert with `RETURNING` fails on tables it cannot read, and worse: an `assigned` member inserting into `audit_log` with `RETURNING` is rejected by `audit_read` — and **every write by an assigned member would have failed**, because auditing runs in the same transaction. And EF requests `RETURNING` automatically for any database-generated value. So keys and timestamps are generated in the application, and no column in `public` has a `DEFAULT` (2).

**(5) But with EF, forgetting was silent.** The decision "`NOT NULL` with no default makes forgetting loud" held for hand-written SQL only: EF sends `Guid.Empty` and `0001-01-01` as ordinary values, so a row with a nil key and two audit rows dated year 1 were saved, **silently**. The fix sits in the constraints layer, not above it — because it covers every writer, not EF alone — via **two domain types**: `app_id` rejects the nil key, and `app_ts` rejects anything before 2000. Choosing domains over scattered CHECK constraints is deliberate: enforcing their presence becomes a question about a column's **type** in the catalog, not about a constraint's **text** — so the text parsing left behind in 1.9 and 1.11 does not return. Proven via EF: forgetting throws `23514` and nothing is saved.

**And one review question on two conflicting rules was settled:** the two tables whose primary key is also a foreign key (`user_password_credentials.user_id` and `role_template_permissions`) — **the PK rule wins**: `app_id`. It breaks nothing, and keeps Test 28 literal with no exceptions.

**Change in 1.12 vs. 1.11 — two conflicts the document introduced itself, found by testing in isolation:**

**(1) The password command added in 1.11 contradicts the grant in 3.8.** The 1.11 command writes `updated_at`, while the matrix grants `UPDATE (password_hash)` alone:

| Form (as `app_user`, grant as in 3.8) | Result |
|---|---|
| `SET password_hash = $1, updated_at = now()` (the 1.11 text) | `ERROR: permission denied` |
| `SET password_hash = $1` alone | One row ✅ |

**The fix:** widen the grant to `UPDATE (password_hash, updated_at)` — metadata the application legitimately writes, and an `UPDATE` grant on it is a write with no read, so it discloses nothing. Two alternatives were rejected: dropping `updated_at` from the command (a stale column), and a trigger to maintain it (a new mechanism the document does not need). The error message quoted in 1.11 was also corrected: the real one is `permission denied for table …`, not `for column user_id`, and the test asserts on SQLSTATE `42501`, not on the message text.

**(2) `system_role_guard` hid the base roles from reads, and its protection is silent.** Written in 1.8 as `AS RESTRICTIVE FOR ALL`, and `FOR ALL` applies `USING` to `SELECT`:

| Operation as `app_user` (a tenant with system owner and admin + a custom role) | Result |
|---|---|
| Reading the tenant's roles | `custom` only — `owner` and `admin` vanished |
| Editing `owner` | `UPDATE 0` — silent |
| Deleting `admin` | `DELETE 0` — silent |
| Promoting `custom` to `is_system = true` | `ERROR` ✅ |
| Inserting a role with `is_system = true` | `ERROR` ✅ |

**The fix:** three restrictive policies split by command (`UPDATE`, `DELETE`, `INSERT`), with reads left to `tenant_isolation` (5). Test 23 was restated: the protection in the database is **silent** (zero rows, the row untouched), and the loudness lives in the layer above, via the rows-affected guard — the "loud above, silent below" principle itself, with no new mechanism.

**The lesson:** the second conflict is a **literal repeat** of the 1.7 mistake in `scope_assignments`, written by 1.8 in another section while fixing the first. Hence a general rule in 3.1: `AS RESTRICTIVE FOR ALL` only when narrowing reads is **intended**. The first conflict was introduced by 1.11 while fixing a different condition. Both are the same class: **text written to fix something, and never run before being stated as settled.** And both were found by the procedure 1.11 made mandatory — testing in isolation before building — not by reading.

**Change in 1.11 vs. 1.10 — two more engine assumptions, tested before building:** following the 1.10 lesson itself, and before a single line of T2, two assumptions the document builds on were tested in isolation — on PostgreSQL 18.6:

**(1) Checks 2 and 8 would have failed on every correct table.** The document said a policy's text "matches the template exactly (pg_get_expr after whitespace normalization)." But PostgreSQL does not return the text as written; it returns a re-deparsed form:

| Source | Text |
|---|---|
| Template 3.1 (v1.10) | `tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)` |
| `pg_get_expr(polqual)` | `(tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif"))` |
| Equal after whitespace normalization? | **No** — added `::text` casts, parentheses, and an `AS "nullif"` alias |
| Is the deparse stable? (Create a policy from the output, then deparse it again) | **Yes** |

For `client_scope`, the deparse also includes the table's own name, so it differs from table to table. No whitespace normalization closes this. **The fix: compare deparse against deparse** — the check, running as `migrator` inside a transaction it rolls back, creates every expected policy (from a template or from the manifest) as a reference on **the same** table, and compares the actual `pg_get_expr` against the reference's `pg_get_expr`. PostgreSQL produces the reference itself, so no second text needs maintaining, and using the same table resolves the table name inside `client_scope`. Two alternatives were rejected: storing the deparsed output in the manifest (two texts for one template, brittle across PostgreSQL versions), and normalizing both sides with rules (a partial parser of the expression — exactly what 1.9 left behind when Check 10 moved to `pg_depend`).

**(2) "UPDATE with no SELECT" is true under an unwritten condition.** The test result on `user_password_credentials` as `app_user` with no `SELECT` at all:

| Form | Result |
|---|---|
| `UPDATE … SET password_hash = $1 WHERE user_id = $2` (what EF generates) | `permission denied for table …` (SQLSTATE `42501`) — loud *(message corrected in 1.12)* |
| `UPDATE … SET password_hash = $1` with no `WHERE` and no `RETURNING`, RLS selecting the row | Exactly one row ✅ |
| And with `SELECT` granted on the `user_id` column to "fix" the first form | Zero rows, **silently** — because `WHERE` also subjects the update to `SELECT` policies |

The document's claim is true and Test 13 can pass — **but in the second form only**. The third form is a trap: "fixing" the loud error with a column grant turns it into a silent blinding, with only the rows-affected guard left in front of it. So the condition is now written into 3.8 and 4.3, Check 6 explicitly forbids any **column-level** `SELECT` on the table, and Test 13 now guards that the first form stays a loud failure.

**And a third assumption held:** `pg_depend` does record policy-expression dependencies (Check 10 is sound), with one detail: the policy's own table appears as a reference, so `refobjid = polrelid` is excluded.

**The pattern, a third time in two days:** three assumptions about PostgreSQL's behavior — `NULLIF` in 1.10, and the deparse and the `WHERE` condition here — all looked self-evident, none were questioned across nine reviews, and all were found by running, not reading. **Testing in isolation before building is now a fixed step in every task, not an exception.**

**Change in 1.10 vs. 1.9 — the fail-safe assumption had been wrong since 1.0:** at the start of T1, before any code was written, an engine behavior the entire transaction layer relies on was tested in isolation — and it failed. It was reproduced on PostgreSQL 18.6 as `app_user`, on a temporary table carrying the verbatim 1.9 `tenant_isolation` text:

| Step (same connection) | Result |
|---|---|
| 1. New session, no `app.tenant_id` | 0 rows ✅ |
| 2. A transaction with `SET LOCAL` for tenant A, then `COMMIT` | 1 row ✅ |
| 3. The same connection after `COMMIT`, no `app.tenant_id` | `ERROR: invalid input syntax for type uuid: ""` |
| 4. After `DISCARD ALL` (what Npgsql runs when a connection returns to the pool) | The same error |

**The cause:** for a custom variable never set in the session, `current_setting(…, true)` returns `NULL`. But after its first setting — even via `SET LOCAL` — the variable stays defined in the session, and when the transaction ends it reverts to its default value: **an empty string `''`, not `NULL`**. And `DISCARD ALL` does not remove the definition. So `''::uuid` throws, and likewise `''::boolean` on `app.scope_all` and `app.can_manage_scope`.

**The effect:** no leak — the failure is closed. But the "silent below" layer became **non-deterministic**: zero rows or an error depending on the pooled connection's history — exactly what T1.3 hits (a thousand iterations on the same pool). Worse: the tenant-selection path (4.3) — `membership_self` is OR-combined with `membership_tenant_read`, and with `app.user_id` set and `app.tenant_id` absent, the second policy would throw on any reused connection — so Test 7 fails intermittently, and login itself breaks at random.

**The fix:** every read of a context variable in any policy became `NULLIF(current_setting('app.x', true), '')::<type>` — 33 explicit places in the text. `NULLIF` goes **before** the cast, not after it, or the error remains. This restores the document's stated behavior deterministically at the lowest layer, whatever the connection's history. Along with it, four places that had been implicit, and that T2 builds from the text literally, were written out: three `WITH CHECK ( … same expression … )` clauses, and the "matching policy on users" in 4.6 that never had any text. Test 27 was added to guard the case the document missed, along with a rule in Check 2 forbidding any policy from reading a context variable without `NULLIF`.

**The two rejected alternatives:** (a) having the transaction layer set every variable in every transaction, with a sentinel value for absence — rejected because it moves the isolation guarantee to the upper layer, so any path outside it (a direct session, the `authenticator` path, a background job) gets an error instead of zero rows. (b) accepting the error as "loud" and amending Test 4 to "zero rows or an error, never rows" — rejected because it turns the last-resort net into behavior that depends on the connection's history, and does not fix the broken login path.

**And why this matters beyond its size:** nine text reviews — four of them on the transaction layer alone — never questioned this assumption, because there was nothing in the text to review: it lived in PostgreSQL's **behavior**. This is exactly what the 1.9 decision to stop text reviews predicted: the next class of defect is visible only by running code.

**Change in 1.9 vs. 1.8 — a short, final fix in four points:** A ninth review of 1.8 found four issues, **three of which were introduced by versions 1.7–1.8 themselves** — one of them inside the 1.8 fix:

1. **Scope had become a substitute for permission (P0):** 4.8 decides that role and scope are **orthogonal** — "what one can do" vs. "over whom." Yet the write policies on `membership_scope` and `scope_assignments` required only `scope_all`. Result: a member with role `viewer` and scope `all` could, from the database itself, assign, revoke, and raise colleagues' scope. The "over whom" axis had come to answer "what one can do" — the opposite of the decision written just above it. This is the same pattern of application-only protection that was fixed for `is_system` in 1.8. Closed with a third context variable, **`app.can_manage_scope`**, resolved from an explicit permission (3.5, 4.8, 5).
2. **The audit log leaked the content of unassigned entities (P0):** Deferred in 1.8 as a spec item phrased as "the preferred decision." That was a misjudgment of severity: `audit_log` carries `old_value`/`new_value`, and an `assigned` member could read it at tenant scope — thereby reading the **content** of edits to clients not assigned to them, exactly what the axis exists to prevent. Closed with a read policy that requires `scope_all` (7).
3. **A chicken-and-egg problem in scope resolution (P1) — introduced by the 1.8 narrowing:** Reading `membership_scope` was narrowed to "self," defined by `app.membership_id`, while Rule 6 resolves `app.membership_id` and `app.scope_all` "together" from the two tables. With a single join query executed before either is set: zero rows → Rule 7 throws → **every request fails**. The failure is loud, not silent (Rule 7 works as designed), but it is the same class of defect as the 1.7 bug: a chain that depends on a variable not yet set. Closed with a mandatory three-step resolution order, and its chains are now registered (3.5, 4.6).
4. **Check 10 now runs on `pg_depend`, not on text parsing (P2):** Postgres records policy-expression dependencies directly in its catalog — more precise, and it needs no parser (3.6).

Alongside this, a wording contradiction was fixed: the policy block in 4.8 used to show `membership_scope` being read at tenant scope, with the narrowing written separately below it — the block now carries the final text only.

**And a pattern is named explicitly:** each of the last three fixes introduced a new defect — every narrowing spawns a new dependency chain. This is not grounds for a tenth review; it is **the strongest argument for stopping**: the 26 tests, run against real code, are more trustworthy than any text review — and the ninth review itself was the author re-reading their own text (13).

**Change in 1.8 vs. 1.7 — fixing the second axis's mechanism and closing its pending decisions:** An eighth external review of 1.7 produced three genuine findings, the worst of which **disabled the second axis entirely**:

1. **The dead nested chain (a structural bug in 1.7):** A policy expression is evaluated with the querying role's privileges, and the tables it references **are subject to their own policies** — exactly the rule for which the "no-cycle rule" was created in 4.6. `client_scope` reads `scope_assignments`, whose only policy in 1.7 (`FOR ALL` conditioned on `scope_all`) hid it from an `assigned` member. Result: `EXISTS` always returns zero ⇒ the restrictive policy always denies ⇒ **an `assigned` member sees nothing on any scoped table at all**. A total silent blinding — the exact risk this document warns about, in the very text of Section 1 — occurred in the version that expanded that lesson. Closed by separating read policies from write policies, and by explicitly entering the new chain into the no-cycle rule (4.6, 4.8).
2. **The scope-declaration rule was itself a disclosure channel:** Requiring the response to state "visible **out of the total**" disclosed to an `assigned` member the size of their tenant's whole portfolio. The rule's value lies in declaring the **mode**, not the total figure — so `total_count` became conditional on `scope_all` (3.5, 6.4).
3. **Assignment semantics were an accidental side effect of a constraint:** `UNIQUE` plus no `DELETE` ⇒ reactivation, not a new row. This became a declared decision with a declared effect (4.8).

Alongside this, pending decisions were closed: **protection of `is_system` moved to the database** via a restrictive policy (5) instead of application-only protection; **scope resolution became per-transaction** rather than stored in the session — otherwise revoking an assignment would not take effect until the member logged out (3.5); **Check 6 was widened** to cover privilege surfaces that `role_table_grants` alone does not see (3.6); and **`scope_ref_id` became `NOT NULL`** (3.1). The test count rose to 23 (3.7).

**What the review's claim was rejected:** the assertion that the document leaves the `provisioner` mechanism and its use of `BYPASSRLS` unresolved — 3.4 is explicit that `BYPASSRLS` belongs to `migrator` alone, under its conditions, and `provisioner` is governed by a separate connection, `INSERT` policies, a grants matrix, and atomic auditing (3.8, 4.4). The four demands raised against it are already implemented (4.5, 4.4, 3.3). What remained valid was a single item: **the network-level enforcement** confining the `provisioner` and `migrator` connections — asserted in text but not technically enforced, and belonging to operations, not the schema (13).

**Change in 1.7 vs. 1.6 — a second scope axis within the tenant:** Through 1.6 the model recognized a single scope axis: the tenant. Every member of a tenant sees all of its data. A requirement from an upcoming module (a consultancy firm managing a client portfolio, with a consultant working on competing clients) imposes a second axis **beneath** the tenant: a member who sees a subset of its clients. The change is **purely additive and invalidates no decision in 1.6**, and it sits in the same three defense layers:

1. **Policies:** the second scope axis is implemented with a separate **restrictive policy** (`AS RESTRICTIVE`), not by editing `tenant_isolation`. A restrictive policy is combined with AND, so it can never widen access; and keeping `tenant_isolation`'s text identical to the template keeps Check 2 valid (3.1, 3.6).
2. **Grants:** `scope_mode` **is not placed as a column on `memberships`** — column grants are cyclical, not policy-scoped, and adding it to `app_user`'s grant would have let a member escalate their own scope through the self-departure policy. The fix: a separate `membership_scope` table with its own surface (4.8, 3.8).
3. **Constraints:** an assignment references the **membership, not the user** — `user_id` is a global key, so an FK toward it would be single-column and would not guarantee that the assignee is a member of this tenant; referencing the membership via a composite FK guarantees this structurally (3.3, 4.8).

Along with this: two new context variables (`app.membership_id`, `app.scope_all`), CI checks 8 and 9, three adversarial tests (16–18), and a **scope-declaration rule** on aggregate surfaces — the new face of the silent-blinding risk (1, 4.8). And an explicitly declared open question: the home of the "client" entity (9, 12).

**Change in 1.6 vs. 1.5:** The seventh review uncovered a third defense layer that no prior review had examined, plus consistency errors: (1) **referential constraints cross tenants** — FK checks are not subject to RLS, so a single-column foreign key between two tenant-governed tables admits a cross-tenant link (an invitation in Al-Amin with a role from Maan); closed by the governing **composite FK** rule, and by adding `tenant_id` to `membership_roles` and `role_permissions`, bringing them into standard coverage (3.3, 4.1, 5). (2) The grants matrix was preventing `provisioner` from executing its own declared second path — fixed with justified `SELECT` on identity tables (3.8). (3) Section 5's tables were completely in the dark (no grants, no check) — they entered the matrix, and Check 5 was inverted: **coverage is now the default, absence is not** — every table in the schema has RLS enabled and forced, with a declared, currently empty exemption list (3.6). (4) `users.username` lacked a unique constraint despite authentication resolving through it — added, and merged with `persons.email` into a **single declared exception** for the cross-tenant identity-identifier family (3.3). (5) `memberships` policies in the manifest were implicit — they were written out explicitly (4.5). The finer-grained items (last-owner race, `auth_attempts` retention, the invitation's default provider, restricting `invited_by`) were moved to the spec (13).

---

## 0. Governing Decisions

**The product:** a general-purpose greenfield SaaS platform — services (subscriptions, support, CRM…) offered publicly with self-registration, on a domain unrelated to any existing business code.

**The architecture:** a modular monolith, a single PostgreSQL 18 database, RLS-based isolation.

**The technology stack — explicitly settled:** .NET (latest LTS) + EF Core + Npgsql + PostgreSQL 18. Every mechanic in this document assumes this stack. Changing the stack requires a full review of Sections 3.5 and 7, not a mere library-name substitution.

**Relationship between documents:** this document is independent from the existing subscription-platform design document (SYSTEM_DESIGN 2.0 — a TypeScript/PG 16 stack). **Two separate products, until an explicit succession decision from the decision-maker.** Building an implementation spec that draws from both documents at once is forbidden.

**Independence — settled:** no code is pulled from `platform.core`. Everything is built from scratch. The justification is **legal, not time-based**: a public, sellable product requires full independence from another organization's code.

**Comparison with the rejected alternative (documented for the record):** a licensing agreement for `platform.core` instead of a rewrite — rejected because legal dependence on another organization's code, in a product sold to the public, is a bigger risk than the cost of rewriting, not because licensing was more expensive.

**The consent principle:** a person joins a tenant **through their own action** (accepting an invitation or self-registering) — not by a unilateral admin decision. The justification is twofold: ethical (membership discloses the person's PII to that tenant) and security-related (otherwise the visibility policy becomes an enumeration vector). This is what makes `memberships` an exception to the standard isolation template (3.6).

**What survives from the water-authority project:** patterns and lessons as reference knowledge only — no code.

---

## 1. The Core Premise

> Multi-tenant isolation built in from the start is roughly 10× cheaper and safer than isolation retrofitted later. A retrofit is possible and has documented successes — but it is more expensive, riskier, and needs a database-level safety net. Not impossible.

The real risk of a retrofit is **silent leakage**. RLS is the safety net that makes a clean build nearly structurally immune.

**Lesson from the fifth review — the other face of fail-safe:** fail-safe (zero rows) protects against leakage but produces **silent blinding** — a legitimate path that looks empty rather than broken. Every check covers both directions: what leaks in excess, and what is wrongly withheld.

**Lesson from the eighth review (1.8) — a layer can invalidate itself:** the three layers are designed and checked together, and that is correct but incomplete. `client_scope` in 1.7 was **a valid policy, on a table with RLS enabled, with correct grants, and complete composite constraints** — and yet it was dead, because its expression read a table that its own policy had hidden. Nothing in the three layers sees this: the flaw is not within a layer, but in **the chain between two layers of the same kind**.

```
policy A reads table B  →  and so is subject to B's policies
⇒ a correct policy on a correct table may always produce zero rows
⇒ the no-cycle rule (4.6) is not enough: a cycle is not the only
   risk — a hidden linear chain kills just as silently.
```

**The rule derived from this:** any table read inside a policy expression must have **a read policy covering every actor that chain depends on** — and the chain must be declared in text along with its intended actor, not just the table's name (4.6). And the test that would have caught this already exists (16‑b): blinding is checked exactly as leakage is.

**The lesson extended in 1.7 — partial silent blinding:** the second scope axis (4.8) produces a face more dangerous than total blinding: the path does not look empty — it looks **incomplete, with no trace**. A portfolio screen showing five clients out of forty looks entirely correct, and a dashboard that aggregates only what is visible gives wrong numbers no one suspects. Zero rows are noticed; missing rows are not. So the "loud above, silent below" rule (3.5) extends to **scope declaration**: the underlying rule remains a safe, silent failure, and the layer above it is obligated to declare its effective scope with every aggregate output (4.8).

**Lesson from 1.10 — an assumption about engine behavior is not text that can be reviewed:** the fail-safe ("no context → zero rows") had been a premise, never a proof, since the first version, and everything built on it — the last-resort net in 3.4, the "silent below" rule in 3.5, and Test 4 — was logically correct and operationally wrong on a reused connection. Reading checks the consistency between sentences; it does not check a taken-for-granted sentence against the engine. **Every rule that depends on PostgreSQL's behavior is proven by running it before anything is built on it**, not by reasoning.

**Lesson from the sixth and seventh reviews — three defense layers, not one:**

```
policies (RLS)  →  which rows the role sees in its context
grants          →  which tables, columns, and actions are even
                    available to the role
constraints     →  which relationships between rows are legitimate
                    — the only layer that operates outside the
                    authority of the first two: FK checks run with
                    the owner's privileges and are not subject to
                    RLS (3.3)
```

A correct policy on a table whose RLS is disabled is dormant; a missing grant survives even if a policy is wrong; and a single-column FK between two tenant tables admits a cross-tenant link no matter how correct the two layers above it are. The three layers are designed and checked together.

---

## 2. The Conceptual Model

```
tenant            a client organization (self-registered)
  ├── membership       a person's role-bearing membership in a
  │                     tenant — created with the person's consent
  │                     (0, 4.5)
  │     ├── membership_auth    binds the provider required for
  │     │                       this membership
  │     ├── membership_scope   the membership's scope mode:
  │     │                       all | assigned (4.8)
  │     └── scope_assignment   an explicit assignment to a scoped
  │                             entity — when assigned (4.8)
  ├── invitation       a pending join invitation — within the
  │                     tenant's scope (4.5)
  ├── tenant_module    a module enabled for this tenant
  └── <module data>    all of it carries tenant_id
        └── whatever carries scope_ref  is subject to the second
            axis (4.8)

person              a single physical identity — cross-tenant
                     (no tenant_id)
  └── user            a login account, with memberships across
                        multiple tenants
        └── <credentials per provider>   owned by the provider's
                                          implementation (4.2)
```

| Concept | Definition |
|---|---|
| Tenant | A customer of the platform. The unit of isolation. |
| Person | A cross-tenant physical identity. |
| User | A login account tied to a Person, typed employee/customer. |
| Membership | A binding of User to Tenant with a role — by the User's explicit acceptance. |
| Invitation | An offer of membership from a tenant to an email — creates nothing until its owner accepts it. |
| The active tenant | The tenant of the current session context (`app.tenant_id`). |
| Session identity | The authenticated user (`app.user_id`) — independent of the active tenant (4.3). |
| The active membership | The session's membership in the active tenant (`app.membership_id`) — resolved when the tenant is selected (4.8). |
| Scope mode | `all` or `assigned` — a property of the membership, not the role; the two are orthogonal (4.8). |
| Scope management | The `core.scope.manage` permission, resolved into `app.can_manage_scope` — **a permission, not a scope** (1.9, 4.8). |
| The scoped entity | The entity the second axis operates over within a tenant (the client, in the consultancy module). |
| Module / Permission | An activatable unit / an atomic permission the module registers. |

**Key and generated-value pattern (rewritten in 1.13):** `uuidv7` for every key — temporal locality for composite indexes (3.2) — but **generated in the application** (`Guid.CreateVersion7()` in .NET), not in the database, and every timestamp is set explicitly by the application. Three binding rules:

```
1. No RETURNING in any command: it requires SELECT on the returned
   columns and subjects the row to SELECT policies — so it fails on
   any table the role cannot read, including audit_log for an
   assigned member (header, 1.13).
2. No DEFAULT, identity, or generated column on any column in
   public. migrator seeding writes uuidv7() and now() explicitly in
   VALUES.
3. Two domain types for the values EF forgets silently:
     CREATE DOMAIN app_id AS uuid        CHECK (VALUE <> '00000000-0000-0000-0000-000000000000');
     CREATE DOMAIN app_ts AS timestamptz CHECK (VALUE >= '2000-01-01');
   app_id for every column in a primary key — even if it is also a
   foreign key. app_ts for every timestamptz NOT NULL. NOT NULL on
   the column, not in the domain (the PostgreSQL docs warn against it
   inside a domain). Nullable columns and pure foreign keys stay
   uuid/timestamptz — the composite FK catches their omission.
```

**Why the floor is 2000, not a recent date:** its only purpose is catching what EF sends for `DateTime.MinValue` (`0001-01-01`, or `-infinity`), not validating dates. A recent floor would reject old imported data in the future for no reason. Test 28 guards all three rules.

---

## 3. RLS and Role Design — Full Detail

### 3.1 The Policy (mandatory InitPlan, explicit FOR and TO)

```sql
ALTER TABLE <t> ENABLE ROW LEVEL SECURITY;
ALTER TABLE <t> FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON <t>
  FOR ALL TO app_user
  USING (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid))
  WITH CHECK (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));
-- Wrapping in SELECT → evaluated once (InitPlan), not per row. ~100x difference.
```

**Binding rule (1.10) — `NULLIF` around every context variable:** every read of `app.*` in any policy expression is written as `NULLIF(current_setting('app.x', true), '')::<type>`, with no exception. The justification is in the document's header: a custom variable becomes `''`, not `NULL`, after its first setting in the session, and `DISCARD ALL` does not remove it, so a direct cast throws on reused connections. `NULLIF` goes **before** the cast: `NULLIF(...)::uuid` is correct, while `NULLIF(...::uuid, …)` keeps the error. Check 2 enforces the rule on every policy (3.6), and Test 27 guards the case.

**Binding rule (1.12) — `AS RESTRICTIVE FOR ALL` only when narrowing reads is intended:** `FOR ALL` applies `USING` to `SELECT` as well, so every restrictive `FOR ALL` policy filters reads by its condition. That is intended in `client_scope` (the second axis narrows visibility itself), and it was unintended twice: `scope_assignments` in 1.7, and `system_role_guard` in 1.8. The rule: a restrictive policy that guards writes only is written **split by command** (`FOR UPDATE` / `FOR DELETE` / `FOR INSERT`), and `FOR ALL` is written only alongside text stating explicitly that narrowing reads is intended.

**Binding rule:** every policy is written with an explicit `FOR <cmd>` and `TO <role>`. A policy without `TO` applies to PUBLIC, so permissive policies get OR-combined with roles never intended. The CI gate (3.6, Check 4) rejects it.

The template above is the **default** for module tables; core tables, which have their own lifecycle, have declared policy sets in a **manifest** enforced by the CI gate (3.6, Check 2).

**The second template — the within-tenant axis (new in 1.7, only for tables carrying `scope_ref`):**

```sql
CREATE POLICY client_scope ON <t>
  AS RESTRICTIVE FOR ALL TO app_user
  USING (
    COALESCE((SELECT NULLIF(current_setting('app.scope_all', true), '')::boolean), false)
    OR EXISTS (
      SELECT 1 FROM scope_assignments sa
      WHERE sa.tenant_id     = <t>.tenant_id
        AND sa.scope_ref_id  = <t>.scope_ref_id
        AND sa.membership_id = (SELECT NULLIF(current_setting('app.membership_id', true), '')::uuid)
        AND sa.active))
  WITH CHECK (
    COALESCE((SELECT NULLIF(current_setting('app.scope_all', true), '')::boolean), false)
    OR EXISTS (
      SELECT 1 FROM scope_assignments sa
      WHERE sa.tenant_id     = <t>.tenant_id
        AND sa.scope_ref_id  = <t>.scope_ref_id
        AND sa.membership_id = (SELECT NULLIF(current_setting('app.membership_id', true), '')::uuid)
        AND sa.active));
```

**Why `AS RESTRICTIVE` specifically:** permissive policies are combined with **OR** — so adding a second ordinary policy would have **widened** access rather than narrowing it, exactly the opposite of what is intended. A restrictive policy is combined with **AND**, so it can only ever narrow, structurally. Two consequences follow for free:

- `tenant_isolation` remains **the same literal text** on every table — Check 2 stays valid without weakening, and the first axis is untouched.
- Fail-safe extends to the second axis: `app.scope_all` or `app.membership_id` unset → `NULL` → neither `true` nor a match → zero rows. The same last-resort net as 3.4, on the new axis.

**Binding rule:** every table carrying `scope_ref_id` gets this restrictive policy, in its standard text; CI Check 8 enforces it (3.6). A table without `scope_ref_id` does not get it added — the second axis is **an explicit opt-in by carrying the column**, not implicit behavior.

**1.8 rule — `scope_ref_id NOT NULL` wherever the policy applies:** an empty value fails safely (the comparison yields `NULL`, so the restrictive policy denies it), so there is no leak — but it produces an **orphaned row**, one no `assigned` member will ever see, written by a `scope_all` member and invisible to everyone thereafter. It is a fail-safe, yes, but it is also a silent blinding. Check 8 verifies the constraint alongside the policy.

**A dependency warning (1.8) — the policy reads a table with its own policies:** the `EXISTS` expression above reads `scope_assignments`, and so is subject to that table's policies, not to absolute privileges. **This is a declared chain**, not an implementation detail: if an `assigned` member lacks a read policy on `scope_assignments` covering their own rows, `EXISTS` returns zero and the second axis is dead on every table. This is exactly what happened in 1.7 (the header, Section 1). The chain `<scoped table> → scope_assignments → (no further)` is registered in the no-cycle rule with its intended actor (4.6), and the corresponding policy is in 4.8.

### 3.2 Composite Indexes

```sql
CREATE INDEX ix_<t>_tenant_<col> ON <t> (tenant_id, <col>);
-- Every index starts with tenant_id. A standard practice, no exceptions. uuidv7 boosts its locality.
```

### 3.3 Constraints — unique and referential together (this section grew in 1.6)

**Unique constraints scoped to the tenant:**

```sql
UNIQUE (tenant_id, email)
-- Referential constraints bypass RLS; UNIQUE(email) alone leaks the existence of values across other tenants.
```

**The conscious exception — one family, not two exceptions:** cross-tenant identity identifiers — `persons.email` and `users.username` — are **globally unique by design**, because identity is shared and authentication resolves through them before any tenant context. Leaking their existence is acceptable and deliberate, and is mitigated with a single, uniform UX approach across every surface (generic messages at registration, a unified response for invitations — 4.5). Any new cross-tenant identifier joins this family through review, not by oversight.

**The composite-FK rule — the new governing rule (seventh review):** FK checks run with the table owner's privileges and **are not subject to RLS** — so a single-column foreign key between two tenant-governed tables admits a cross-tenant link, with nothing in policies or grants preventing it (an invitation in Al-Amin with a role from Maan: a mixing of privileges at the very heart of the consent path). **The binding rule:**

```
Every FK between two tables that both carry tenant_id must be
composite and include it:
  Parent table:  UNIQUE (tenant_id, id)      -- on top of the PK
  Child table:   FOREIGN KEY (tenant_id, <ref>_id)
                 REFERENCES <parent> (tenant_id, id)
→ tenant consistency is structurally enforced by the database —
  the constraints layer closes what the two layers above it
  cannot see.
An FK toward a global table with no tenant_id (modules,
permissions, persons, users) remains single-column — there is no
tenant to keep consistent.
```

Applied in the model: `roles` and `memberships` carry `UNIQUE(tenant_id, id)`; and `invitations.role_id`, `membership_roles`, and `role_permissions` reference via composite keys (4.1, 4.5, 5). In 1.7, `membership_scope` and `scope_assignments` join in with composite keys toward `memberships` (4.8). A seventh CI check verifies automatically: any single-column FK between two tenant_id tables = a failure (3.6).

**Applying the rule to assignment — why the membership, not the user (1.7):** the naturally intuitive assignment is "user ↔ entity." But `users` is a **global table with no `tenant_id`**, so an FK toward it is single-column under the rule above — and nothing then prevents Al-Amin's admin from assigning a user who has no membership in Al-Amin. This is **exactly** the class of cross-linking the rule was created to close, in a new form: it does not breach the tenant wall, it grants access within it to someone who is not there.

```
scope_assignments.membership_id  →  composite FK toward
   memberships (tenant_id, id)
   ⇒ the assignee is a member of this tenant — guaranteed by the
     database, not by code.
```

**A declared gap in the third layer (1.7):** the other end of the assignment — `scope_ref_id` toward the scoped entity — **remains without an FK** as long as that entity's home is unresolved (module or core, Section 9). So the second axis today is protected by two layers, not three: the membership is structurally guaranteed, and the entity is guaranteed only by policy and code. The third layer is closed as soon as the home is decided, and it is entered in Section 12 as an open risk, not a forgotten item.

### 3.4 Connection Roles — five roles, each on its own surface

| Item | Rule |
|---|---|
| The application role | `app_user` — not an owner, not a superuser. Its context: `app.tenant_id` + `app.user_id` + (1.7) `app.membership_id` + `app.scope_all` + (1.9) `app.can_manage_scope` |
| The authentication role | `authenticator` — resolves credentials + logs attempts (its only write), a separate DataSource (4.3) |
| The background-job role | `job_runner` — reads active `tenants` only to start the fan-out; processing runs under `app_user`'s context per tenant (8) |
| The migration role | `migrator` — an owner, migrations only, **a conscious, documented `BYPASSRLS`** (below); and it alone writes the global catalogs (seeding modules/permissions) and the documented archival/pruning procedures |
| The provisioning role | `provisioner` — **two paths that cross isolation**: bootstrap + accepting invitations (4.4, 4.5). **(1.13) It sets `app.tenant_id` to the target tenant inside its own transaction** — one tenant per transaction (3.9) |
| Connection cleanup | Connection state is reset when it returns to the pool; nothing relies on `SET LOCAL` alone persisting |
| Fail-safe | Without setting app.tenant_id → zero rows (the last-resort net — 3.5). And on the second axis: without `app.scope_all` or `app.membership_id` → zero rows as well (3.1). **(1.10) "Without setting" includes a variable set in a prior transaction on the same connection that has reverted to `''` — guaranteed by `NULLIF`, not by the connection's history (3.1, Test 27)** |

The **grants** for each role — the layer below the policies — are in the binding matrix, 3.8.

**`FORCE RLS` against `migrator` — settled:** `FORCE` cancels the owner exception, so any data migration lacking a tenant context would silently see zero rows. **The decision:** `BYPASSRLS` as a conscious, documented exception, under three conditions: (1) the migrator connection is for migration tooling only — no path from application code ever reaches it. (2) every data migration ends with an explicit row-count confirmation. (3) the rejected alternative is documented: fan-out per tenant within migrations — cleaner in theory, rejected because the cost of running a full-schema backfill once per tenant outweighs the risk of a role confined to migration tooling.
### 3.5 Enforcing the Transaction — "Loud above, silent below"

**The dual problem:** (a) EF does not automatically wrap reads in transactions, so they slip past the wrapper silently. (b) fail-safe on a read taken outside the wrapper **hides the error instead of exposing it**.

```
The upper layer (loud):
  DbCommandInterceptor throws an exception if a command runs
  without an active transaction.
The lower layer (silent):
  the database policy remains a fail-safe (zero rows) as a last
  resort.
The rule: loud above (exposes), silent below (protects).
```

**Binding configuration rules:**

```
1. Enforcement at the unit-of-work level (middleware): every
   request → an explicit transaction; set app.user_id then
   app.tenant_id (if there is an active tenant — 4.3), then the
   second-axis variables **in the order mandated by Rule 6** (if
   there is an active tenant — 4.8), then any access.
2. Variables are set as independent SQL statements (SET LOCAL
   inside the transaction).
3. A documented Npgsql pitfall: never append SET to the command
   text via DbCommandInterceptor — it breaks batch updates with
   phantom exceptions (efcore.pg #2480, #2412).
4. SET LOCAL outside a transaction is a silent no-op. No access
   path exists outside a transaction.
5. A critical write verifies rows-affected — zero rows where one
   is expected is a thrown error, not a silent success.
6. (1.7, amended in 1.8 and 1.9) The source of the second-axis
   variables is exclusive, and they are resolved **on every
   transaction** — never read from a request, a header, or a
   claim, and never stored in the session or the token. **And the
   order is binding (1.9), three steps, not one query:**
     a. memberships, via membership_self (needs only app.user_id)
        → SET LOCAL app.membership_id
     b. membership_scope, via the self branch (needs only
        app.membership_id) → SET LOCAL app.scope_all
     c. the membership's permissions: membership_roles →
        role_permissions → permissions (the standard template +
        the global catalog) → SET LOCAL app.can_manage_scope
   Each step reads only what the variable set before it allows.
   A single join query executed before setting them returns zero,
   so Rule 7 throws on every request (1.9).
   app.scope_all = true is permitted in two contexts only, no
   third:
     a. a membership whose mode is 'all'.
     b. the system context and background jobs (8).
   And app.can_manage_scope is independent of the mode: an
   assigned membership may manage scope, and an all membership may
   not — orthogonal, like role and scope.
   And in every case: app.tenant_id is never bypassed in any
   context.
7. (1.7) A membership without a membership_scope row is an error
   thrown when the tenant is selected — not a silent fallback to a
   default. The rule beneath it fails safely into 'assigned' (zero
   rows), and the layer above it screams — loud above, silent
   below.
```

**Decision in 1.8 — resolution per transaction, not per session:** 1.7 said scope is "resolved when the tenant is selected," a phrasing that admits session storage. And storage means that **revoking a consultant's assignment does not take effect until they log out and back in** — an unbounded excess-permission window, and practically the most dangerous thing about the second axis (assignments are typically revoked at the moment of a dispute or a contract's end). The cost is three small, indexed reads per transaction (1.9: in the order mandated by Rule 6, not a single join) — an acceptable price for immediate propagation, and revoking the management **permission** propagates immediately by the same mechanism. The rejected alternative is documented: session storage with a short TTL — rejected because "short" is a negotiable number, and "immediate" is not.

**The scope-declaration rule (1.7, corrected in 1.8) — the loud upper layer for the second axis:** every aggregate response (a list, a counter, a dashboard, an export, a search) explicitly carries its effective scope in its payload. Total blinding is noticed because it is empty; partial blinding is not noticed because it looks like a plausible number. Declaration is **part of the API contract**, not a UI concern.

**Correction in 1.8 — the declaration was itself a disclosure channel:** the 1.7 wording required "visible **out of the total**." And a tenant's total is, by definition, information outside an `assigned` member's scope: a consultant would learn that their firm manages forty clients while they are assigned three. The rule put in place to prevent silent blinding opened a counting channel.

**The correct split:** the rule's value lies in declaring the **mode**, not the total figure.

```
Always:              scope_mode ∈ {all, assigned}
                      visible_count  (after every constraint:
                                       scope, permission, status,
                                       search)
                      has_more_in_scope  (for pagination within
                                           the scope)
Only under scope_all: total_count
Under assigned:       total_count = null explicitly — the field
                       is not removed
```

An explicit `null`, not an absent field: absence is read as zero or an error by the consumer, whereas the explicit value says "hidden, not nonexistent." And the rule applies to lists, reports, dashboards, exports, and unified search without exception — any surface a count is derived from (Test 19).

### 3.6 The CI Gate — ten checks, coverage is the default, not absence

**Check 5's inversion (seventh review):** the manual list of core tables carried the same drift risk that was fixed for Check 2 — Section 5's tables had escaped it entirely (completely dark: no policies, no grants, and no check noticing). **The new rule:** every table in the schema has RLS enabled **and forced**, and an exemption exists only in a declared, in-repository list — **which is empty today**; even the global catalogs (`modules`, `permissions`) sit under RLS with an explicit read policy, their writes reserved for `migrator` alone (seeding).

```sql
-- Check 1: every table with tenant_id has full RLS (ENABLE + FORCE).
--   (After 1.6 this includes membership_roles and role_permissions —
--   they now carry tenant_id.)
SELECT c.relname FROM pg_class c
JOIN pg_attribute a ON a.attrelid = c.oid AND a.attname = 'tenant_id'
WHERE c.relkind = 'r' AND a.attnum > 0 AND NOT a.attisdropped
  AND (c.relrowsecurity = false OR c.relforcerowsecurity = false);

-- Check 2 (manifest — amended in 1.7): evaluated on two separate
--   policy sets: permissive and restrictive.
--   Outside the manifest:
--     - Permissive: a single tenant_isolation policy matching the
--       standard template **deparse against deparse** (below —
--       amended in 1.11).
--     - Restrictive: empty, or client_scope alone with its
--       standard text (3.1) — mandatory whenever the table
--       carries scope_ref_id (Check 8).
--   Inside the manifest: the actual policy set = the manifest's
--   set exactly (by name, command, roles, permissive/restrictive
--   class, and text). Missing, extra, or drifted = failure.
--   (1.11) The text-comparison mechanism — deparse against
--     deparse, not text against text: pg_get_expr does not return
--     the text as written (it adds ::text, parentheses, and an
--     alias), so matching the document's text fails on every
--     correct table. The check, as migrator inside a transaction it
--     rolls back at the end: for every expected policy — from a
--     template or from the manifest file — creates its reference
--     version under a temporary name on **the same** table, and
--     compares pg_get_expr of the actual qual and with_check
--     against those of the reference. The deparse is stable
--     (proven), and the same table resolves the table name inside
--     client_scope. The manifest stays plain SQL as written —
--     PostgreSQL produces the reference.
--     (The transaction takes an exclusive lock on the table: run
--      in CI and on a test environment, not on a live production
--      database.)
--   (Separating the two sets is substantive, not cosmetic:
--    permissive policies combine with OR and so widen; restrictive
--    ones combine with AND and so narrow — mixing them in one
--    check hides a flip in meaning. polpermissive in pg_policy is
--    the distinguishing field.)
--   (1.10) And on every policy in the schema — inside and outside
--     the manifest: every occurrence of current_setting in qual or
--     with_check sits inside NULLIF(…, '') before any cast. A bare
--     occurrence = failure. This guards manifest policies that the
--     template does not match textually, where the wrapper is easy
--     to forget in a new policy. (1.11: checked on the deparsed
--     form, where current_setting comes right after NULLIF( and ''
--     becomes ''::text — not on the document's text.)
-- The manifest tables as of 1.8: audit_log, memberships,
--   invitations, persons, users, membership_auth,
--   user_password_credentials, tenants, auth_attempts, modules,
--   permissions, membership_scope, scope_assignments (1.7 —
--   dedicated administrative surfaces), roles (1.8, amended 1.12 —
--   carries three system_role_guard_* restrictive policies split by
--   command, not the second template).
--   (1.13) membership_roles and role_permissions return to it, and
--   tenant_modules joins it: each with tenant_isolation **listed
--   explicitly** alongside its provisioner policy — inside the
--   manifest the full set is required. The two catalogs
--   role_templates and role_template_permissions join it too.
--   (membership_roles and role_permissions had left it in 1.6.)

-- Check 3: the required core policies exist as declared in the
--   manifest — part of executing Check 2, called out separately
--   because its absence means either PII leakage or blocked
--   login.

-- Check 4: no policy applies to PUBLIC.
SELECT c.relname, p.polname FROM pg_policy p
JOIN pg_class c ON c.oid = p.polrelid WHERE p.polroles = '{0}';

-- Check 5 (inverted in 1.6): every table in the schema has RLS
--   enabled and forced — except those named in the declared
--   exemption list (empty today).
SELECT c.relname FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE c.relkind = 'r' AND n.nspname = 'public'
  AND (c.relrowsecurity = false OR c.relforcerowsecurity = false)
  AND c.relname <> ALL ($1::text[]);  -- (1.13) $1 from a file in the repo, empty today
-- (1.13) The exemption list is **a file in the repository**, like the
-- manifest and the chain table, passed to the check as input — not an
-- rls_exemptions table. That table would sit in public and need RLS,
-- yet has no tenant_id and no policy — a check exempting itself.

-- Check 6 (grants — widened in 1.8): role_table_grants alone is
--   not enough; Postgres's privilege surfaces are broader. The
--   script compares against the 3.8 matrix, then checks the
--   following surfaces against an approved list:
--     - column privileges (column_privileges), not just tables
--     - schema USAGE / CREATE
--     - EXECUTE on functions and procedures
--     - USAGE / SELECT / UPDATE on sequences
--     - views and materialized views (a common bypass surface: a
--       view reading a hidden table under its owner's privileges)
--     - DEFAULT PRIVILEGES (silently grants on future objects)
--     - CONNECT and TEMP on the database
--     - role memberships: no application role is a member of
--       migrator or provisioner (prevents SET ROLE — Test 22)
--   The sharpest points remain: no SELECT for app_user on
--   user_password_credentials — (1.11) nor on any single column of
--   it (column_privileges), because granting the user_id column to
--   "fix" an update with a WHERE turns the loud error into a silent
--   zero rows (3.8); and (1.12) its UPDATE grant is exactly
--   (password_hash, updated_at), no more and no less — any extra or
--   missing column = failure — no UPDATE/DELETE for any
--   application role on audit_log, no write access for anyone but
--   migrator on the global catalogs, and no DELETE on
--   scope_assignments.

-- Check 7 (1.6, widened in 1.8 — the constraints layer): no
--   single-column FK between two tenant_id tables. For every FK
--   constraint: if both tables carry tenant_id and the column is
--   not part of the constraint's composite key = failure. (Reads
--   pg_constraint/conkey/confkey.)
--   (1.8) And it is tested on UPDATE, not just INSERT: changing
--   tenant_id or the reference after creation is an independent
--   cross-linking path (Test 21).

-- Check 8 (new in 1.7 — second-axis coverage): every table
--   carrying a scope_ref_id column has a restrictive policy named
--   client_scope, TO app_user, matching the second template (3.1)
--   deparse against deparse on the same table (Check 2's
--   mechanism — 1.11). Its absence or drift = failure. Same
--   logic as Check 1, on the new axis: coverage is the default.
SELECT c.relname FROM pg_class c
JOIN pg_attribute a ON a.attrelid = c.oid AND a.attname = 'scope_ref_id'
WHERE c.relkind = 'r' AND a.attnum > 0 AND NOT a.attisdropped
  AND c.relname <> 'scope_assignments'   -- (1.14) the table the template reads
  AND NOT EXISTS (
    SELECT 1 FROM pg_policy p
    WHERE p.polrelid = c.oid AND p.polname = 'client_scope'
      AND p.polpermissive = false);
-- (1.14) And the inverse: client_scope present on scope_assignments = failure
--   (infinite recursion 42P17 on every scoped table) — a guard against the wrong "fix".
SELECT polname FROM pg_policy
WHERE polrelid = 'scope_assignments'::regclass AND polname = 'client_scope';

-- Check 9 (new in 1.7 — source of the scope): a static code
--   script rejects any read of app.scope_all, app.membership_id,
--   or app.can_manage_scope (1.9) from a request payload, a
--   header, or a claim, and any setting of them outside the tenant
--   -selection layer and the background-job layer (3.5/6). The
--   only source: membership_scope. A scope variable arriving from
--   the client = a self-escalation of privilege.

-- Check 10 (new in 1.8, tightened in 1.9 — policy chains): for
--   every table referenced inside a policy expression —
--   extracted from pg_depend (classid = pg_policy, refclassid =
--   pg_class), not from parsing the expression's text — the chain
--   is compared against the list of chains declared in 4.6; an
--   undeclared chain = failure. The list carries, for each chain,
--   **its intended actor**, and enforces the existence of a read
--   policy on the inner table that covers it. This check is static
--   and does not prove access; that is proven by two paired tests
--   (16‑a for leakage, 16‑b for blinding).
--   Rationale: the 1.7 bug was a linear chain, well-formed and
--   silently blocked — no check from 1..9 sees it.
--   (1.11) A proven implementation detail: pg_depend records the
--   policy's own table as a reference (deptype a, with its
--   columns), so refobjid = polrelid is excluded from chain
--   extraction — otherwise every policy would show as a chain to
--   its own table.
```

**Correction in 1.14 — Check 8 was demanding infinite recursion:** as written since 1.7, the check requires `client_scope` on every table carrying `scope_ref_id` — and `scope_assignments` carries it (4.1). But the template **reads `scope_assignments` itself**, so placing it there is infinite recursion by definition. Proven by running it on PostgreSQL 18.6: the check flags a correct table, and satisfying it literally makes reading assignments throw `42P17: infinite recursion detected` — and since `client_scope` on **every** scoped table reads the assignments, the recursion would have failed **every read on every scoped table** from T5. The fix is one **structural** exception: the table the template reads cannot carry it. Not an exception list, but a fact about the template itself. Guarded in both directions: an inverse clause fails the check if the template is present there, and Test 16-d proves reading assignments is correct without it.

Two alternatives were rejected: (a) limiting the check to tables outside the manifest — it would drop "coverage by default" for an entire class, so a manifest table carrying `scope_ref_id` whose template is forgotten both in reality and in the manifest passes Check 2, because the two match in their omission; and (b) renaming the column — a model change touching 3.1, 4.1, and 4.8, bigger than the problem.

**An explicit limit on Check 10:** `pg_depend` records the tables and functions referenced directly in the expression (1.9: more precise than text parsing and needs no parser), but it does not see what that function or view **reads** internally. For this reason it is textually forbidden to reference a view or a non-`SECURITY INVOKER` function inside a policy expression — with one declared exception and no others: a `SECURITY DEFINER` function dedicated to resolving assignments, if it is later adopted to break the chain or reduce its cost (13).

### 3.7 The Adversarial Tests

```
1.  A query with no WHERE from Al-Amin's context → Al-Amin's rows
    only.
2.  An INSERT with Maan's tenant_id from Al-Amin's context → fails.
3.  Two concurrent requests through the same pool, under heavy load
    (≥1000 iterations) → zero leakage.
4.  Without setting app.tenant_id → zero rows.
5.  A read outside a transaction → fails explicitly (the upper
    layer in 3.5).
6.  Al-Amin's admin queries persons → does not see the PII of
    Maan's users (4.6).
7.  The login path: authenticator resolves the credential with no
    tenant context; after authentication, app_user with
    app.user_id but no app.tenant_id sees only their own
    memberships — and other people's memberships never appear
    (4.3).
8.  Nested policy chains execute without recursion under load —
    and a guard test fails loudly if a future policy closes a
    cycle (4.6).
9.  Application roles cannot impersonate migrator, nor reach its
    connection.
10. Consent: app_user — even with an admin role — directly
    inserting into memberships → fails.
11. No enumeration: an invitation to an existing email and to a
    non-existent one → identical responses.
12. The audit trail is immune: UPDATE/DELETE on audit_log from any
    application role → fails at the grant level before any policy
    is even reached.
13. Hashes are hidden: SELECT on user_password_credentials with the
    app_user role → fails on the grant; and a password change
    (UPDATE with no SELECT) affects exactly one row — (1.11) **in
    the form with no WHERE and no RETURNING**. And the test guards
    the other direction: the same update with WHERE user_id = …
    fails **loudly** (SQLSTATE 42501 — 1.12: asserted on the code,
    not the message text), not with zero rows. Zero
    rows instead of the error means someone granted SELECT on a
    column = failure (3.8, Check 6).
14. The silent critical write: an update to a row that is not
    visible → a rows-affected error, not a silent success (3.5/5).
15. (1.6) The constraints layer: an invitation in Al-Amin with a
    role_id from Maan → rejected by the composite FK constraint
    structurally — before any policy or code runs (tested by 3.3).
    Likewise for a cross-tenant membership_roles link.
16. (New in 1.7) The second axis — both directions together:
    a. Leakage: a membership whose mode is assigned, assigned to
       client A, queries with no WHERE → rows of A only; and an
       attempt to write a row with scope_ref_id for client B fails
       under the restrictive WITH CHECK.
    b. Blinding: a membership whose mode is all in the same tenant
       sees A and B together — the test fails if the restrictive
       policy hides anything from it. (A restrictive policy
       narrows only what it is meant to narrow = partial silent
       blinding, Section 1.)
       **(1.8) And its decisive extension:** an assigned membership
       that is actually assigned sees A's rows — not zero. This
       specific branch is exactly what would have caught the 1.7
       bug (the hidden chain) before any text review.
    d. (1.8) Reading assignments: an assigned member reads their
       own assignments and cannot read another membership's
       assignments; and their attempt to INSERT or UPDATE on
       scope_assignments fails (4.8).
    c. Declaration: an aggregate response for an assigned
       membership carries scope_mode and visible_count — with
       total_count = null explicitly (3.5, and Test 19 covers the
       remaining surfaces).
17. (New in 1.7) Self-escalation of scope is blocked by two layers:
    a. app_user — even with an admin role — UPDATE on
       membership_scope for their own membership → fails under the
       restrictive policy on that table (4.8).
    b. The self-departure path (membership_self_leave) has no
       column with which to raise the scope — because scope_mode
       is not on memberships in the first place. (A regression
       test on the decision: that column appearing on memberships
       = failure.)
    c. Setting app.scope_all from a header or request payload → has
       no effect (Check 9), and behavior remains assigned.
18. (1.7) Assigning to a membership from another tenant → rejected
    by the composite FK structurally (3.3). And a membership with
    no membership_scope row → a loud error at tenant selection, and
    zero rows beneath it if that is somehow bypassed (3.5/7).
19. (New in 1.8) The scope contract does not leak the total: an
    assigned member's response on the list, the report, the
    dashboard, the export, and unified search → total_count = null
    explicitly in each; and an all member in the same tenant → a
    real number. Any surface that returns a total to an assigned
    member = failure (3.5, 6.4).
20. (New in 1.8) A scope change propagates immediately: during an
    active session for an assigned member, their assignment is
    revoked → the **next** request sees zero rows for that entity,
    with no re-login. Likewise for lowering a member's mode from
    all to assigned. (The test fails if the value is stored in the
    session — 3.5/6.)
21. (New in 1.8) Cross-linking via UPDATE, not just INSERT: after a
    valid row is created, attempting to change tenant_id,
    membership_id, role_id, or scope_ref_id to a value from another
    tenant → rejected by the composite constraint. (A path that
    Test 15 covers only at creation — 3.6/7.)
22. (New in 1.8) Impersonating privileged roles: SET ROLE migrator
    or provisioner from an application role → fails; and no
    application role is a member of either (Check 6). And an
    attempt to use the provisioner connection for a third path →
    fails from the absence of grants, not from review.
23. (New in 1.8, restated in 1.12) The system role is protected — in
    two layers:
    a. The database (direct SQL as app_user): UPDATE or DELETE on an
       is_system role → **zero rows, the row untouched** (silent —
       the last-resort net); and raising is_system from false to
       true, or inserting a role with is_system = true → **an error**
       (WITH CHECK).
    b. The API: editing or deleting a system role → **an explicit
       error** from the rows-affected guard (3.5/5) — loud above.
    c. **And the opposite direction (blinding):** reading the
       tenant's roles as app_user returns the system and custom
       roles together — any of owner/admin/operator/viewer missing
       = failure (5, 1.12). This branch would have caught the 1.8
       mistake.
24. (New in 1.9) Scope is not a permission — both directions:
    a. A member with role viewer and scope all (with no
       core.scope.manage) → INSERT or UPDATE on scope_assignments
       and UPDATE on membership_scope fail from the database, not
       from the API.
    b. A member holding core.scope.manage with scope assigned →
       manages others' assignments, and by that alone sees no more
       than their own assigned entities on the scoped tables; and
       their attempt to raise their own mode fails (the "not the
       actor's own membership" condition).
    c. Revoking core.scope.manage during an active session → the
       next request fails on any management action, with no
       re-login (3.5/6).
25. (New in 1.9) The audit log does not leak content: an assigned
    member reads audit_log → zero rows, including events for their
    unassigned entities, old_value/new_value included; and an all
    member in the same tenant sees the full log. And the log
    surface in the API declares the blinding (scope_mode) rather
    than showing emptiness.
26. (New in 1.9) Resolution order: executing second-axis resolution
    with a single join query before setting app.membership_id →
    returns zero, so Rule 7 throws explicitly (the test proves the
    failure is **loud**); and the mandatory order (3.5/6, steps
    a‑b‑c) → succeeds. A guard test: any change that reverts
    resolution to a single query fails here before it can be
    merged.
27. (New in 1.10) Fail-safe on a reused connection: on the **same**
    connection — a transaction that sets all five context variables
    with SET LOCAL, then COMMIT, then DISCARD ALL, then a query with
    no context at all against: a table under the first template, a
    table under client_scope, and memberships (with app.user_id
    alone — the tenant-selection path) → zero rows on the first two,
    and only the caller's own memberships on the third — **and no
    error on any of them**. Likewise after ROLLBACK instead of
    COMMIT. The test fails against the 1.9 text — intentionally: it
    is the case the document missed through nine reviews.
28. (New in 1.13) [W] No value generated by the database or by EF,
    and forgetting is loud — on two sides:
    a. The database, three catalog queries, all empty on the schema:
       (1) no atthasdef, attidentity, or attgenerated on any column
           in public.
       (2) every column in a primary key has type app_id
           (pg_index.indisprimary, atttypid <> 'app_id'::regtype).
       (3) every timestamptz NOT NULL has type app_ts.
       And each is seen to fail once on a planted table (a DEFAULT,
       a plain uuid key, a plain timestamptz).
    b. The model: every property of every EF entity has
       ValueGenerated == Never.
    c. The behavior, through real EF commands: a new entity with a
       Guid.Empty key → 23514 (app_id_check); a timestamp left at its
       default → 23514 (app_ts_check); nothing saved. And zero
       commands containing RETURNING across the full bootstrap.
```

**An explicit limit:** a test checks what occurred to its author; the most dangerous flaws are those that occurred to no one. **And the counterpart lesson (1.8–1.9):** the bug that opened 1.8 had its test (16‑b) written into the document before it ever happened — a written test protects nothing unless it is run. Seven reviews uncovered what no written test was positioned to catch — leakage (4), blinding (5), a missing procedure (6), and an entire missing constraints layer (7). A test is a safety net, not a substitute for an eye — hence the decision to move the eye to the spec and the code (the document's header).

### 3.8 The Grants Matrix — the layer beneath the policies

**The principle:** RLS determines **which rows**; grants determine **which tables, columns, and actions are even reachable**. A missing grant is a defense that holds even if a policy is wrong. The matrix is binding, and Check 6 enforces it. (Two 1.6 corrections: SELECT for provisioner on identity tables — the matrix had been preventing it from executing its own declared second path; and Section 5's tables, previously dark, entering it.)

| Table | app_user | authenticator | provisioner | job_runner |
|---|---|---|---|---|
| tenants | SELECT | — | INSERT | SELECT |
| persons | SELECT | — | **SELECT**, INSERT | — |
| users | SELECT, UPDATE (columns: last_login_at, language, theme only) | SELECT | **SELECT**, INSERT | — |
| user_password_credentials | UPDATE (password_hash, updated_at — **1.12**) **with no SELECT** | SELECT | INSERT | — |
| memberships | SELECT, UPDATE (status) | — | **SELECT**, INSERT | — |
| membership_roles | SELECT, INSERT, DELETE | — | INSERT | — |
| membership_auth | SELECT | SELECT | INSERT | — |
| membership_scope | SELECT, UPDATE (scope_mode) — **and the policy requires `can_manage_scope` and forbids editing one's own membership** (4.8) | — | INSERT | — |
| scope_assignments | SELECT, INSERT, UPDATE (active) — **no DELETE**: an assignment is history that gets disabled, never erased | — | — | — |
| invitations | SELECT, INSERT, UPDATE (status) | — | SELECT, UPDATE (status) | — |
| auth_attempts | — | SELECT, INSERT | — | — |
| audit_log | SELECT (**conditional on `scope_all` — 1.9, Section 7**), INSERT — **no UPDATE/DELETE for any application role** | — | INSERT | — |
| roles | SELECT, INSERT, UPDATE, DELETE (within RLS; **and system-seeded `is_system` roles are immune via a restrictive database policy — 1.8, Section 5**) | — | SELECT, **INSERT (1.13 — base roles only, bootstrap)** | — |
| role_permissions | SELECT, INSERT, DELETE | — | **INSERT (1.13 — bootstrap)** | — |
| tenant_modules | SELECT, UPDATE (is_active) | — | INSERT | — |
| modules (global catalog) | SELECT only — writes reserved for migrator (seeding) | — | SELECT | — |
| permissions (global catalog) | SELECT only — writes reserved for migrator (seeding) | — | SELECT | — |
| role_templates (global catalog — 1.13) | — | — | SELECT only — writes reserved for migrator (seeding) | — |
| role_template_permissions (global catalog — 1.13) | — | — | SELECT only — writes reserved for migrator (seeding) | — |
| module tables | SELECT, INSERT, UPDATE, DELETE (within RLS) | — | — | — |

**Justifying provisioner's SELECT (1.6 correction):** the invitation-acceptance path requires matching the invited person's authenticated email against the invitation's email, linking to an existing account rather than creating a duplicate, and checking `UNIQUE(tenant_id, user_id)` with a decent error message — all reads. This transitive read is acceptable **on the very same confined surface** whose transitive write was already accepted; what was rejected was declaring a path and then denying it its tools.

**Why `scope_mode` is a table, not a column (design correction in 1.7):** the natural instinct is a `scope_mode` column on `memberships`. And it is **a mistake under this document's model specifically**: column grants are cyclical, not policy-scoped (a column cannot be granted to one policy and withheld from another), and `memberships` carries `membership_self_leave`, which lets a member `UPDATE` their own row (4.5). So adding `scope_mode` to `app_user`'s `UPDATE` grant would have opened, for every member, **a one-command self-escalation from assigned to all** — with no policy to stop it, because the row is theirs and the tenant is theirs. The separate table splits the two surfaces: `memberships.status` remains self-service, while `membership_scope.scope_mode` is an administrative surface with a restrictive policy that excludes the acting member's own row (4.8, Test 17).

Named design points: **`UPDATE` with no `SELECT`** on `user_password_credentials` is intentional and achievable in Postgres — changing the password without any application code ever being able to read the hash at all. **(1.11) Under a condition proven by running it:** the command is written `UPDATE user_password_credentials SET password_hash = $1, updated_at = now()` **with no `WHERE` and no `RETURNING`**, and the `password_self_update` policy is what selects the row. The reason: PostgreSQL requires `SELECT` on every column read in a `WHERE`, so an ordinary EF update (a tracked entity with `WHERE user_id = …`) fails with `permission denied`. And the trap: granting `SELECT` on the `user_id` column to "fix" it also subjects the update to `SELECT` policies — and the table has no `SELECT` policy — so it becomes **zero rows, silently**. Therefore: the path is a raw SQL command, not a tracked EF update; it is a critical write guarded by rows-affected = 1 (3.5/5); Check 6 forbids any column grant on the table; and Test 13 guards both directions. **(1.12)** The command also writes `updated_at`, so the matrix's `UPDATE` grant is exactly `(password_hash, updated_at)` — the 1.11 text contradicted its grant of `(password_hash)` alone, and running it proved it throws `permission denied`. **Column grants** on `users` prevent the self-update path from tampering with `person_id`/`user_type`/`status`. The global catalogs sit under RLS with a read policy plus the absence of a write grant — two layers even for what is public.

### 3.9 The Complementary Policy Texts (new in 1.13)

**Why this section:** through 1.12 the document wrote 22 policies in full, while every grant in the 3.8 matrix to a role other than `migrator` needs a policy for the same role and command — and under `FORCE RLS` a grant with no policy is dead: reads return zero rows silently, and writes are rejected. These 32 complete the picture. All were run on PostgreSQL 18.6 in both directions (the intended path succeeds, the forbidden one fails), and running without them failed 41 of 42 intended-path cases. The text below is the executed text, verbatim.

**Two rules govern them:**
- `provisioner` **sets `app.tenant_id` to the target tenant** inside its own transaction — the new one at bootstrap, or the invitation's tenant at acceptance (4.5). Its policies on tenant tables are `tenant_id = app.tenant_id`, so it cannot write into two tenants in one transaction. On global tables with no `tenant_id` — `persons`, `users`, `user_password_credentials`, and the two new catalogs — `true`: their protection is the separate connection, the grants, and the two confined paths (4.4).
- **No new chains.** The only one that reads another table is `membership_auth_self_read → memberships`, already declared in 4.6.

```sql
-- ===== provisioner — tenant tables (target tenant: app.tenant_id, 4.4) =====
CREATE POLICY tenants_provisioner_insert ON tenants
  FOR INSERT TO provisioner
  WITH CHECK (id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));

CREATE POLICY memberships_provisioner_select ON memberships
  FOR SELECT TO provisioner
  USING (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));

CREATE POLICY memberships_provisioner_insert ON memberships
  FOR INSERT TO provisioner
  WITH CHECK (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));

CREATE POLICY membership_roles_provisioner_insert ON membership_roles
  FOR INSERT TO provisioner
  WITH CHECK (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));

CREATE POLICY membership_auth_provisioner_insert ON membership_auth
  FOR INSERT TO provisioner
  WITH CHECK (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));

CREATE POLICY membership_scope_provisioner_insert ON membership_scope
  FOR INSERT TO provisioner
  WITH CHECK (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));

CREATE POLICY tenant_modules_provisioner_insert ON tenant_modules
  FOR INSERT TO provisioner
  WITH CHECK (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));

CREATE POLICY roles_provisioner_select ON roles
  FOR SELECT TO provisioner
  USING (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));

CREATE POLICY roles_provisioner_insert ON roles
  FOR INSERT TO provisioner
  WITH CHECK (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
              AND is_system);

CREATE POLICY role_permissions_provisioner_insert ON role_permissions
  FOR INSERT TO provisioner
  WITH CHECK (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));

CREATE POLICY invitations_provisioner_select ON invitations
  FOR SELECT TO provisioner
  USING (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));

CREATE POLICY invitations_provisioner_update ON invitations
  FOR UPDATE TO provisioner
  USING (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid))
  WITH CHECK (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));

CREATE POLICY audit_log_provisioner_insert ON audit_log
  FOR INSERT TO provisioner
  WITH CHECK (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));

-- ===== provisioner — global tables (no tenant_id) =====
CREATE POLICY persons_provisioner_select ON persons
  FOR SELECT TO provisioner
  USING (true);

CREATE POLICY persons_provisioner_insert ON persons
  FOR INSERT TO provisioner
  WITH CHECK (true);

CREATE POLICY users_provisioner_select ON users
  FOR SELECT TO provisioner
  USING (true);

CREATE POLICY users_provisioner_insert ON users
  FOR INSERT TO provisioner
  WITH CHECK (true);

CREATE POLICY user_password_credentials_provisioner_insert ON user_password_credentials
  FOR INSERT TO provisioner
  WITH CHECK (true);

CREATE POLICY role_templates_provisioner_read ON role_templates
  FOR SELECT TO provisioner
  USING (true);

CREATE POLICY role_template_permissions_provisioner_read ON role_template_permissions
  FOR SELECT TO provisioner
  USING (true);

-- ===== authenticator — login resolution (4.3-a) =====
CREATE POLICY users_authenticator_read ON users
  FOR SELECT TO authenticator
  USING (true);

CREATE POLICY user_password_credentials_authenticator_read ON user_password_credentials
  FOR SELECT TO authenticator
  USING (true);

CREATE POLICY membership_auth_authenticator_read ON membership_auth
  FOR SELECT TO authenticator
  USING (true);

CREATE POLICY auth_attempts_authenticator_read ON auth_attempts
  FOR SELECT TO authenticator
  USING (true);

CREATE POLICY auth_attempts_authenticator_insert ON auth_attempts
  FOR INSERT TO authenticator
  WITH CHECK (true);

-- ===== app_user =====
CREATE POLICY membership_auth_self_read ON membership_auth
  FOR SELECT TO app_user
  USING (EXISTS (
    SELECT 1 FROM memberships m
    WHERE m.id = membership_auth.membership_id
      AND m.user_id = (SELECT NULLIF(current_setting('app.user_id', true), '')::uuid)));

CREATE POLICY invitations_tenant_read ON invitations
  FOR SELECT TO app_user
  USING (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));

CREATE POLICY invitations_insert ON invitations
  FOR INSERT TO app_user
  WITH CHECK (
    tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    AND invited_by = (SELECT NULLIF(current_setting('app.user_id', true), '')::uuid)
    AND (intended_scope_mode = 'assigned'
         OR COALESCE((SELECT NULLIF(current_setting('app.can_manage_scope', true), '')::boolean), false)));

CREATE POLICY invitations_tenant_update ON invitations
  FOR UPDATE TO app_user
  USING (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid))
  WITH CHECK (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));

CREATE POLICY audit_log_insert ON audit_log
  FOR INSERT TO app_user
  WITH CHECK (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));

CREATE POLICY modules_read ON modules
  FOR SELECT TO app_user, provisioner
  USING (true);

CREATE POLICY permissions_read ON permissions
  FOR SELECT TO app_user, provisioner
  USING (true);
```

**Notes on the texts:**
- `roles_provisioner_insert` is conditioned on `is_system`: `provisioner` creates only the base roles from templates, so it cannot — even if the code is wrong — create an arbitrary custom role. And `system_role_guard_insert` applies `TO app_user`, so it does not block it, and still blocks `app_user` as before.
- `invitations_insert` enforces spec item f **in the database**: an invitation with scope `'all'` requires `can_manage_scope` — an extension of the 1.9 decision (no application-only protection of a sensitive surface). Along with `invited_by = app.user_id` (spec item d).
- `modules_read` and `permissions_read` name two roles in one policy with an explicit `TO` — valid, and it passes Check 4.
- Updating another tenant's row (`invitations_*_update`) yields **zero rows**, not an error — "silent below," with the acceptance and revocation paths under the rows-affected guard.
- **A policy that 4.2 mentioned was removed:** "tenant-scoped visibility for an admin" on `membership_auth`. The database has no context variable expressing "admin," so it would have been open to every member of the tenant while saying "admin" — text that implies a protection that does not exist is worse than its absence, and nothing consumes it before SSO.

---

## 4. Identity — Person / User / Membership

### 4.1 The Model

```sql
persons(id, full_name, email UNIQUE, phone_e164, created_at)  -- no tenant_id

users(id, person_id, user_type, username UNIQUE,   -- (1.6 correction:
      status, language, theme, last_login_at)      --  this constraint
                                                     --  was missing even
                                                     --  though auth
                                                     --  resolves through it)
-- email and username: the one exception family — cross-tenant identity
-- identifiers (3.3).

user_password_credentials(user_id PK, password_hash, updated_at)

memberships(id, tenant_id, user_id, status, created_at,
            UNIQUE (tenant_id, user_id),
            UNIQUE (tenant_id, id))        -- the base for the composite FK (3.3)

membership_roles(id, tenant_id, membership_id, role_id,   -- (1.6: tenant_id added)
  FOREIGN KEY (tenant_id, membership_id) REFERENCES memberships (tenant_id, id),
  FOREIGN KEY (tenant_id, role_id)       REFERENCES roles (tenant_id, id))
-- With the new column: this entered Check 1's coverage and the
-- standard template, and left the manifest and the nested-chain
-- lists — the constraint structurally enforces tenant consistency.

invitations(id, tenant_id, email, role_id, token_hash,
            status,          -- pending | accepted | revoked | expired
            invited_by, expires_at, created_at,
            intended_scope_mode,  -- (1.13) 'all' | 'assigned', NOT NULL, no default
  FOREIGN KEY (tenant_id, role_id) REFERENCES roles (tenant_id, id))
-- intended_scope_mode is built with the schema (T2); its behavior —
-- validation at creation and copying at acceptance — with T4. Also
-- enforced in the database: invitations_insert (3.9).
-- The composite FK: an invitation with another tenant's role is
-- rejected by the database (3.3, Test 15).

tenants(id, name, status, created_at,          -- (1.13) never defined before
        CHECK (status IN ('active', 'suspended')))

auth_attempts(id, username_entered, ip_address, succeeded, created_at)
-- Append-only; the retention/pruning policy is a spec item (13).

-- (New in 1.7 — the second scope axis, 4.8)
membership_scope(id, tenant_id, membership_id,
                 scope_mode,        -- 'all' | 'assigned'
                 -- (1.15) no updated_by/updated_at — the audit log attributes (7)
                 UNIQUE (tenant_id, membership_id),
  FOREIGN KEY (tenant_id, membership_id) REFERENCES memberships (tenant_id, id))
-- A separate table, not a column on memberships — the justification
-- is in 3.8 (a clash between column grants and the self-departure
-- policy). Its row is created in the same transaction as the
-- membership's creation (4.4), and its absence is a loud error, not
-- a silent default (3.5/7).

scope_assignments(id, tenant_id, membership_id,
                  scope_ref_id NOT NULL,  -- the scoped entity — no FK yet (3.3)
                  assignment_role,  -- lead | contributor | reviewer
                  active, reason,   -- (1.15) no granted_by/granted_at — the audit log (7)
                  UNIQUE (tenant_id, membership_id, scope_ref_id),
  FOREIGN KEY (tenant_id, membership_id) REFERENCES memberships (tenant_id, id))
-- References the membership, not the user — the justification is in 3.3.
-- Index: (tenant_id, membership_id, scope_ref_id) to serve the second
-- template (3.1).
```

Omar = one person, two memberships (Al-Amin, Maan), an independent role in each.

### 4.2 Login via an abstraction layer — binding on the membership, credentials with the provider

```
Provider binding    →  on the membership (membership_auth): which
                        provider is accepted for logging into it.
Credential storage  →  with the provider's implementation:
                        - password  → user_password_credentials
                          (account level)
                        - entra/google → provider_config on
                          membership_auth
```

```sql
membership_auth(
  id, tenant_id, membership_id,
  provider,            -- 'password' today | 'entra' | 'google' later
  provider_config,     -- jsonb: the tenant's IdP reference (empty for password)
  FOREIGN KEY (tenant_id, membership_id) REFERENCES memberships (tenant_id, id))
-- (1.6: tenant_id was added for consistency with the composite-FK
-- rule — and the manifest kept it, because its self-visibility
-- policies are not the standard template.)
```

Omar logs into his Al-Amin membership via Entra, and into Maan via Google — the same Person, two implementations behind the same abstraction layer. Binding on the membership is what makes SSO an addition rather than a tearing-apart.

**Read policies (closing the step-up blindness):** self-visibility via `memberships.user_id = app.user_id` — `membership_auth_self_read` (3.9) — `FOR SELECT TO app_user` in the manifest. (1.13: "tenant-scoped visibility for an admin" was removed — the database has no variable expressing "admin," so it would have been open to every member; it returns with SSO, via a mechanism that enforces it — 3.9.) and the chain `membership_auth → memberships` is linear (the no-cycle rule, 4.6).

**The explicit price of binding on the membership:** switching the active tenant to a membership whose provider differs = **step-up re-authentication**. The session carries a set of satisfied auth grants; switching to a membership whose grant is already satisfied is instant, otherwise login via that provider is required. Intentional behavior: a tenant that mandates Entra is not entered with a password session.

**Timing decision (a market matter, not a technical one):** SSO is deferred — the model is structurally ready for it, so deferring it costs nothing in retrofit.

### 4.3 The login path and tenant selection — closing the silent blinding

**The closed gap (5):** membership policies are conditioned on an active tenant that does not exist at the moment of login — no one could log in at all, and the tenant-selection screen itself was hidden. **And a correction (6):** a failed attempt occurs before any session exists, so it had no writer, and updating `last_login_at` was silently affecting zero rows.

```
a) Resolving credentials and logging attempts — role authenticator:
   1. A separate DataSource/connection with a separate role (the
      provisioner pattern).
   2. Grants: SELECT on users, user_password_credentials,
      membership_auth; and INSERT + SELECT on auth_attempts only —
      its only write, on an append-only table, because a failed
      attempt has no authenticated context. Attempt-limit checks
      read from the same table.
   3. Policies TO authenticator in the manifest: a read policy
      USING (true) on the three resolution tables — the surface is
      structurally confined to the authentication path.
   4. It writes nothing to the identity tables themselves.

b) After authentication — self-visibility via app.user_id:
   the middleware sets app.user_id from the authenticated session
   (3.5).
```

```sql
CREATE POLICY user_self_read ON users
  FOR SELECT TO app_user
  USING (id = (SELECT NULLIF(current_setting('app.user_id', true), '')::uuid));

-- Columns are confined via a column grant (3.8): last_login_at, language, theme.
CREATE POLICY user_self_update ON users
  FOR UPDATE TO app_user
  USING (id = (SELECT NULLIF(current_setting('app.user_id', true), '')::uuid))
  WITH CHECK (id = (SELECT NULLIF(current_setting('app.user_id', true), '')::uuid));

-- Password change — UPDATE with no SELECT (3.8), after verifying
-- the current password via the authenticator surface. (1.11) As a
-- raw command with no WHERE and no RETURNING — the policy below is
-- what selects the row (3.8):
--   UPDATE user_password_credentials
--      SET password_hash = $1, updated_at = now();
CREATE POLICY password_self_update ON user_password_credentials
  FOR UPDATE TO app_user
  USING (user_id = (SELECT NULLIF(current_setting('app.user_id', true), '')::uuid))
  WITH CHECK (user_id = (SELECT NULLIF(current_setting('app.user_id', true), '')::uuid));

CREATE POLICY person_self ON persons
  FOR SELECT TO app_user
  USING (EXISTS (
    SELECT 1 FROM users u
    WHERE u.person_id = persons.id
      AND u.id = (SELECT NULLIF(current_setting('app.user_id', true), '')::uuid)));

CREATE POLICY membership_self ON memberships
  FOR SELECT TO app_user
  USING (user_id = (SELECT NULLIF(current_setting('app.user_id', true), '')::uuid));

CREATE POLICY tenant_visible_to_member ON tenants
  FOR SELECT TO app_user
  USING (EXISTS (
    SELECT 1 FROM memberships m
    WHERE m.tenant_id = tenants.id
      AND m.user_id = (SELECT NULLIF(current_setting('app.user_id', true), '')::uuid)));
```

**Why both layers together:** self-visibility alone cannot resolve search-by-name before `user_id` is known — so `authenticator` is needed. And `authenticator` is not extended past authentication — so the self-context is needed. The OR combination here is intentional: your own rows plus your active tenant's rows. And every write is checked against rows-affected (3.5/5).

### 4.4 The Bootstrap Path — the isolation model's intentional hole

```
1. Core tables have a FOR INSERT TO provisioner WITH CHECK (...)
   policy — not app_user. (The one exception where app_user writes
   directly: invitations.)
2. provisioner runs on a separate DataSource/connection — no
   SET ROLE on an app_user connection.
3. Exactly two confined paths, no more:
   a. bootstrap: a new tenant + the first person/user/membership +
      membership_scope ('all' — the first member is the owner),
      one transaction.
   b. accepting an invitation: token verification + creating (or
      linking) the identity + the membership + membership_auth +
      membership_scope + updating the invitation, one transaction
      (4.5).
   (1.7: the membership_scope row is part of both paths' atomicity
    — a membership with no scope row is an invalid state, not a
    defaulted one, 3.5/7 and 4.8.)
4. Both paths' auditing happens in the same transaction — a
   FOR INSERT TO provisioner policy on audit_log with the
   corresponding tenant_id.
5. No third path writes across isolation boundaries. This surface
   is audited strictly.
```

### 4.5 The Member Lifecycle — invitation, acceptance, and departure

**The design — the invitation is an offer, and acceptance is what creates:**

```
1. The admin creates an invitation (email + a role from their own
   tenant — the composite FK enforces this structurally) within
   their tenant's scope. No search over persons, no disclosure of
   whether the email exists: the response is identical whether it
   was found or not (Test 11) — an extension of the 3.3 mitigation.
2. The invitation message carries a token (only its hash is
   stored) and an expiry.
3. The invitee — under their own authenticated identity — opens
   the invitation:
   - An existing account: they log in, then accept →
     provisioner (path b) creates the membership +
     membership_auth and marks the invitation accepted.
   - No account: they register (person/user within the same
     acceptance transaction).
   - The invitee's authenticated email ≠ the invitation's email →
     rejected. The token is not sufficient: consent is the email
     owner's consent, not the link-holder's.
4. Revocation: revoked before acceptance (an UPDATE within the
   tenant). Expiry: expires_at is checked at acceptance.
```

**The memberships policies in the manifest — written out, not implied (closing a 1.5 wording gap):**

```sql
-- (1) Cross-tenant self-visibility (4.3): membership_self — above.
-- (2) Visibility for the active tenant (the members screen):
CREATE POLICY membership_tenant_read ON memberships
  FOR SELECT TO app_user
  USING (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));
-- (3) The admin enables/disables memberships in their tenant
--     (a column grant: status only):
CREATE POLICY membership_tenant_update ON memberships
  FOR UPDATE TO app_user
  USING (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid))
  WITH CHECK (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));
-- (4) A member departs: disabling their own membership:
CREATE POLICY membership_self_leave ON memberships
  FOR UPDATE TO app_user
  USING (user_id = (SELECT NULLIF(current_setting('app.user_id', true), '')::uuid))
  WITH CHECK (user_id = (SELECT NULLIF(current_setting('app.user_id', true), '')::uuid));
-- (5) Insertion: FOR INSERT TO provisioner only (4.4). No DELETE
--     for anyone: membership is history that gets disabled, never
--     erased — the audit trail refers to it.
```

Disabling cuts off access to that tenant immediately (a status check at authentication and at tenant selection). The "the last owner cannot leave" rule and its race guard: a spec item (13). Identity deletion for compliance: a layer of the sold product, resolved alongside single-tenant restoration (12).

**Why acceptance goes through provisioner, not app_user:** creating a membership touches three scopes at once (a cross-tenant identity + a tenant + a credential) with no valid active-tenant context — this is exactly the definition of "the write that crosses isolation boundaries," confined to provisioner. Going from one path to two is **declared**; silent expansion is the risk, not the number of paths.

### 4.6 Reading identity tables via membership — closing a PII leak

```sql
CREATE POLICY person_visible_via_membership ON persons
  FOR SELECT TO app_user
  USING (EXISTS (
    SELECT 1 FROM users u
    JOIN memberships m ON m.user_id = u.id
    WHERE u.person_id = persons.id
      AND m.tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
  ));
-- (1.10: previously "a matching policy" with no text — T2 builds from
-- the text literally, so it is now written out.)
CREATE POLICY user_visible_via_membership ON users
  FOR SELECT TO app_user
  USING (EXISTS (
    SELECT 1 FROM memberships m
    WHERE m.user_id = users.id
      AND m.tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
  ));
-- (Combined with OR alongside the self-visibility policies in
-- 4.3 — intentional.)
```

**The no-cycle rule — widened in scope in 1.8:** an inner query inside a policy is subject to its own tables' policies. Through 1.7, the rule checked only for **cycles**. The 1.7 bug (the dead chain, Section 1) proved that a well-formed linear chain can still be silently blocked — so the rule widened into **a declared table of chains, each with its intended actor**, enforced by Check 10:

| Chain | Intended actor | Policy covering it |
|---|---|---|
| `persons → users → memberships` | app_user in their active tenant | 4.6 + self-visibility, 4.3 |
| `users → memberships` (1.10 — previously implicit) | app_user in their active tenant | `user_visible_via_membership` (4.6) |
| `membership_auth → memberships` | app_user for their own membership | 4.2 |
| `tenants → memberships` | app_user for their own memberships | 4.3 |
| `<scoped table> → scope_assignments` | **app_user whose mode is assigned** | `scope_assignment_read` (4.8) |
| **Scope resolution, step b:** `membership_scope` (self branch) | app_user after setting `app.membership_id` | `membership_scope_read` (4.8) — **strictly in the order of 3.5/6** (1.9) |
| **Scope resolution, step c:** `membership_roles → role_permissions → permissions` | app_user after setting `app.tenant_id` | The standard template + the global catalog (5) (1.9) |

**Two conditions on every chain:** linear with no cycle (Test 8 guards it), **and reachable by its intended actor** (Test 16, guarded by its two paired branches). **And a third condition for chains that read resolution tables (1.9):** the variable it depends on is set **before** it is read — otherwise a chain that is theoretically reachable becomes unreachable at the moment it actually runs (Test 26). The second condition was added in 1.8, and its absence is exactly what made the second axis dead in 1.7. Any new policy adds its chain to the table above, with its actor — not merely its table's name. (After 1.6 the lists shortened: `membership_roles` dropped out of the chains — the standard template with tenant_id is enough for it.)

**Two warnings:** (a) a subquery inside a policy has a cost — it is monitored, and the index `memberships(user_id, tenant_id)` mitigates it. (b) an open read is acceptable only for non-sensitive fields, if a shared UI needs them — never for PII.

### 4.7 A deferred point — tenant-level policies

Data governed by a tenant policy (a tenant-specific 2FA requirement, security settings) sits at the level of **the membership, not the person**; `Person` carries only the cross-tenant minimum. Added when a tenant requires it.

### 4.8 The Second Axis — scope within the tenant (1.7, its mechanism fixed in 1.8)

**The requirement that forced it:** a client tenant manages a portfolio of entities belonging to external parties — a consultancy firm managing clients, an agency managing accounts, an office managing case files — with members working on competing subsets. A member seeing all of their tenant's data is correct in an internal product, and is an existential error here: a consultant working for two competing clients. The first axis (the tenant) protects **from the outside**; the second axis protects **within the tenant**.

**The precise definition:**

```
Scope mode  (membership_scope.scope_mode)  a membership property:
    all       → the membership sees every entity of its tenant
    assigned  → the membership sees only what has been explicitly
                assigned to it

Assignment  (scope_assignments)  explicit rows: membership ↔
                                  scoped entity.
```

**Decision: scope is orthogonal to role, not merged into it.** Role answers "what one can do," and scope answers "over whom." Merging them into a single value produces a cross-product of roles and modes for every new combination — and the explosion is not hypothetical: a consultant with `operator` permission and an assigned scope, a quality reviewer with `viewer` permission and an assigned scope, an owner with `owner` permission and full scope. Three existing roles with two modes, not six roles.

**Decision: scope belongs to the membership, not the user.** Same reasoning as 4.2 (binding the provider to the membership): a single person may be a full-scope owner in their own tenant and an assigned consultant in another. Placing the mode on `users` mixes tenants into one property — exactly what the model has avoided since 4.1.

**Implementation across the three layers:**

| Layer | Mechanism | Location |
|---|---|---|
| Policies | A restrictive `client_scope` policy (`AS RESTRICTIVE`) on every table carrying `scope_ref_id` | 3.1, Check 8 |
| Grants | `membership_scope` and `scope_assignments` with their surfaces in the matrix; no `DELETE` on either | 3.8 |
| Constraints | A composite `FK` toward `memberships` — the assignee is structurally a member of the tenant. The other end is a declared gap | 3.3, 12 |

**The two tables' policies — in the manifest, written out, not implied:**

```sql
-- (1) Reading the mode: self, or whoever manages scope. (Final text, 1.9)
--     The self branch needs only app.membership_id — step b of
--     Rule 3.5/6.
CREATE POLICY membership_scope_read ON membership_scope
  FOR SELECT TO app_user
  USING (
    tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    AND (COALESCE((SELECT NULLIF(current_setting('app.can_manage_scope', true), '')::boolean), false)
         OR membership_id = (SELECT NULLIF(current_setting('app.membership_id', true), '')::uuid)));

-- (2) Editing the mode — two conditions together: the scope-
--     management permission, and not the actor's own membership.
CREATE POLICY membership_scope_admin_update ON membership_scope
  FOR UPDATE TO app_user
  USING (
    tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    AND COALESCE((SELECT NULLIF(current_setting('app.can_manage_scope', true), '')::boolean), false)
    AND membership_id <> (SELECT NULLIF(current_setting('app.membership_id', true), '')::uuid))
  WITH CHECK (
    tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    AND COALESCE((SELECT NULLIF(current_setting('app.can_manage_scope', true), '')::boolean), false)
    AND membership_id <> (SELECT NULLIF(current_setting('app.membership_id', true), '')::uuid));

-- (3) Insertion: FOR INSERT TO provisioner only — created with the
--     membership, atomically (4.4).

-- (4) Assignments — policies split by command (1.8), and
--     management gated by permission, not scope (1.9).
CREATE POLICY scope_assignment_read ON scope_assignments
  FOR SELECT TO app_user
  USING (
    tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    AND (COALESCE((SELECT NULLIF(current_setting('app.can_manage_scope', true), '')::boolean), false)
         OR membership_id = (SELECT NULLIF(current_setting('app.membership_id', true), '')::uuid)));

CREATE POLICY scope_assignment_insert ON scope_assignments
  FOR INSERT TO app_user
  WITH CHECK (
    tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    AND COALESCE((SELECT NULLIF(current_setting('app.can_manage_scope', true), '')::boolean), false));

CREATE POLICY scope_assignment_update ON scope_assignments
  FOR UPDATE TO app_user
  USING (
    tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    AND COALESCE((SELECT NULLIF(current_setting('app.can_manage_scope', true), '')::boolean), false))
  WITH CHECK (
    tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    AND COALESCE((SELECT NULLIF(current_setting('app.can_manage_scope', true), '')::boolean), false));
-- No DELETE for anyone (the 3.8 matrix) — an assignment is
-- disabled, never erased.
```

**Correction in 1.9 — scope is not a permission:** through 1.8, these policies required only `scope_all`. That contradicts the decision written in this very section: role and scope are orthogonal. `scope_all` answers "over whom do I see" — not "what am I entitled to do." A member with role `viewer` and scope `all` (a quality reviewer reading every file) could, from the database, assign, revoke, and raise colleagues' scope. The application layer did check the permission (Section 5), so this was not an API-open hole — but it was **application-only protection of a sensitive surface**, the very pattern for which `is_system` was moved to the database in 1.8. The fix: `app.can_manage_scope` is resolved, per transaction, from the `core.scope.manage` permission (step c, 3.5/6), and every **administrative** policy checks it. The **visibility** policies (`client_scope`) remain on `scope_all`, because visibility is genuinely a scope matter.

And the administrative branch in both read policies became `can_manage_scope`, not `scope_all` either: someone who sees every file has no corresponding need to see who was assigned to what.

**Wording correction, 1.9:** in 1.8 the block above showed `membership_scope_read` at tenant scope, with the narrowing to "self + administrator" written separately below it — two conflicting texts for the same policy. The block above is now the sole text.

**Correction in 1.8 — why they were split, and why merging them was fatal:** in 1.7 there was one policy, `FOR ALL`, conditioned on `scope_all`. And `FOR ALL` applies `USING` to `SELECT` as well — so an `assigned` member could not read their own assignments. And because `client_scope` on every scoped table **reads this very table** and is subject to its policy (4.6), the result was that `EXISTS` always returned zero and the restrictive policy always denied: **the second axis was disabled entirely, and every `assigned` member saw zero rows everywhere.** Nothing in the three layers reveals this — the flaw is in the chain, not in a layer (Section 1).

Reading, then, **is not a luxury**: it is a condition for the mechanism to function. The adopted model: a member reads their own assignments, an administrator with full scope reads their tenant's assignments, and writing is for the administrator alone.

**Two wording notes (both common sources of error):** `CREATE POLICY` accepts **one command** — not `FOR INSERT, UPDATE`. And `FOR INSERT` does not accept `USING` at all, only `WITH CHECK`. Hence three policies, not one.

**A declared alternative not adopted:** having `client_scope` call a `SECURITY DEFINER` function to resolve assignments instead of reading the table — this would remove the chain from the policy network entirely and lower cost (a `STABLE` function evaluated once). Rejected **for now** because it opens a new bypass surface that needs its own dedicated audit, and the policy-based solution is sufficient and statically checkable (Check 10). It will be revisited if measurement forces it (13/i) — it is the single declared exception to Check 10's limit.

**Decision in 1.8 — reading `membership_scope` narrows to self + whoever manages scope:** the 1.7 wording let every member read every colleague's scope mode — an organizational disclosure with no functional justification. A member needs their own mode (to display and to resolve their own scope, 3.5/6‑b), and whoever manages scope needs everyone's. The final text is in the block above, in the same shape as `scope_assignment_read` — and the consistency of shape is intentional: the two tables are one semantic surface.

**Assignment semantics and its lifecycle — a declared decision, not a side effect (1.8):** `UNIQUE (tenant_id, membership_id, scope_ref_id)` combined with no `DELETE` produces a specific behavior: **one permanent row per (membership, entity), disabled and reactivated — never a new row per period.** This was an accidental side effect of the constraint in 1.7; it is now the decision:

| Alternative | Verdict |
|---|---|
| One row, reactivated ✅ | Adopted — full history lives in `audit_log`, no second table |
| `valid_from`/`valid_to` periods | Rejected — solves a case never observed, and doubles overlap/query logic |
| A current row + a history table | Rejected — duplicates what the audit trail already does |

And the effect of reactivation is declared: **visibility resumes; the past is not rewritten.** What a member created during a prior assignment remains attributed to them, and every change to `active` is logged with its actor, reason, and time (7). Disabling an assignment **does not touch the data** — it only cuts off visibility; the entity remains the tenant's property, not the consultant's. (The effect of disabling on open tasks and pending notifications: a spec item — 13/i.)

**The condition `membership_id <> app.membership_id` is not decorative.** Without it, a member holding `can_manage_scope` with scope `assigned` could raise their own mode to `all` with a single command (1.9: and because permission and scope are now orthogonal, this combination is real, not merely theoretical). The separation between "I manage someone else's scope" and "I manage my own" is enforced by the database, not by code review.

**The invitation carries the mode:** `invitations` gains the intended-mode field, so a `membership_scope` row is written at acceptance with the value the admin approved, not a default. (The field's detail and its validation: a spec item — 13.)

**Its effect on the unified view (6) and background jobs (8):** neither is exempted. The unified view queries each tenant in its own context, setting `app.membership_id` and `app.scope_all` for the user's membership **in that specific tenant** — a member with full scope in Al-Amin and assigned scope in Maan sees the difference in the very same merged result, and the scope declaration shows it. And a background job is a system context: `scope_all = true` with no membership, and `can_manage_scope = false` — the job sees everything and manages no scope.

**The explicit limit:** this axis **does not make the core two-level multi-tenant**. `tenant` remains the sole unit of isolation: fail-safe, auditing, billing, and restoration boundaries are all at the tenant level. The second axis is **a narrowing within the unit**, not a new unit, and any attempt to load it with an isolation role (such as making `scope_ref_id` the basis for billing or restoration) is redirected back to the first axis.

**What is deliberately not resolved here:** the external party's own users logging in themselves (a client's employee uploading their own data). That is a third kind of actor, confined to a single entity, and is a change to the identity model, not the scope model — resolved by a genuine requirement, not anticipation. The second axis neither paves the way for it nor obstructs it.

---

## 5. Modules and Permissions

```sql
modules(id, code, name_ar, name_en, icon, display_order, is_active)
permissions(id, module_id, code, name_ar, name_en)
-- Two global catalogs: RLS enabled with a read policy, writes
-- reserved for migrator, by seeding (3.6, 3.8).

tenant_modules(id, tenant_id, module_id, is_active, activated_at,
               UNIQUE (tenant_id, module_id))

roles(id, tenant_id, code, name_ar, name_en, is_system, is_active,
      UNIQUE (tenant_id, id))            -- the base for the composite FK (3.3)

role_permissions(id, tenant_id, role_id, permission_id,   -- (1.6: tenant_id added)
  FOREIGN KEY (tenant_id, role_id) REFERENCES roles (tenant_id, id))
-- permission_id remains a single-column FK — the catalog is global,
-- with no tenant to keep consistent.

-- (1.13) Base-role templates — a global catalog seeded by migrator:
role_templates(id, code, name_ar, name_en)
role_template_permissions(template_id, permission_id,
  PRIMARY KEY (template_id, permission_id))   -- app_id — the PK rule (2)
-- provisioner alone reads them at bootstrap, creating the new tenant's
-- roles and role permissions from them (3.9). And migrator upgrades
-- existing tenants' roles from them too — one source of truth. Spec
-- item n (core.scope.manage for owner and admin) is now data in this
-- catalog.
-- And bootstrap's template-derived inserts (INSERT … SELECT) are
-- critical writes: without read access to the templates they insert
-- zero rows with no error — proven by running it. The number of roles
-- created = the number of templates, under the rows-affected guard
-- (3.5/5).
```

A new module adds its own permissions without modifying the core. The base roles (owner/admin/operator/viewer) are seeded per tenant with an `is_system` flag. Enforcement runs through an explicit permission at every endpoint, and every screen reaches its data through that same permission (granting one screen's permission to fetch data for another = a lateral-movement leak).

**Protecting the system role — moved to the database (1.8):** through 1.7, `is_system` was protected **only at the application layer**, a breach of the document's own principle: any path holding the same grants bypasses the application layer's protection, and the 3.8 matrix grants `app_user` `UPDATE` and `DELETE` on `roles`. The fix is a single restrictive policy closing both directions at once:

```sql
-- (1.12) Three restrictive policies split by command — not FOR ALL (below).
CREATE POLICY system_role_guard_update ON roles
  AS RESTRICTIVE FOR UPDATE TO app_user
  USING (NOT is_system)         -- no editing a system role
  WITH CHECK (NOT is_system);   -- and no promoting an ordinary role to system

CREATE POLICY system_role_guard_delete ON roles
  AS RESTRICTIVE FOR DELETE TO app_user
  USING (NOT is_system);        -- no deleting a system role

CREATE POLICY system_role_guard_insert ON roles
  AS RESTRICTIVE FOR INSERT TO app_user
  WITH CHECK (NOT is_system);   -- and no inserting a role flagged as system
-- SELECT is left to tenant_isolation alone: seeded roles are visible.
```

`USING` protects the existing row, and `WITH CHECK` protects the resulting value — so all three paths (tampering with a seeded role, deleting it, and seeding a new role flagged as system) are closed. Seeding itself belongs to `migrator` (3.8), and is outside the policies' scope since they apply `TO app_user`. And `roles` enters the manifest with these three restrictive policies (3.6, Check 2), since they are not the second template.

**Correction in 1.12 — `FOR ALL` was hiding the base roles from reads:** the 1.8 wording was a single `AS RESTRICTIVE FOR ALL` policy. And `FOR ALL` applies `USING` to `SELECT` as well — so `NOT is_system` became a condition on **reading**, and `owner`, `admin`, `operator`, and `viewer` vanished entirely from `app_user`'s point of view. Proven by running it: listing the roles of a tenant with two system roles and one custom role returns the custom one alone. So the role list, a member's role name, and choosing a role for an invitation would all have lost the base roles without a trace. **A silent blinding** with no justification at all: the section meant "no editing or deleting," not "no reading." And it is **the very same mistake** 1.8 fixed in `scope_assignments` (a `FOR ALL` policy unintentionally blocking reads), written in 1.8 itself, in a different section. Hence a general rule was added in 3.1.

**And the protection's behavior — silent below, loud above (1.12):** editing or deleting a system role via direct SQL yields **zero rows, with the row untouched** — not an error. That is correct under the document's principle: the database protects silently, as the last-resort net. The loudness belongs in the layer above: editing and deleting roles is a **critical write** under the rows-affected guard (3.5/5), so the API throws an explicit error. Insertion and promotion, by contrast, fail loudly in the database itself (`WITH CHECK` throws). Test 23 was restated to guard both layers — no new mechanism (a trigger) to make the database loud.

**An intentional side effect:** a tenant cannot edit its own seeded roles, so it creates its own custom roles instead of altering the base ones. This makes upgrading the base role definitions safe across migrations — something that was not possible when tenants could tamper with them.

---

## 6. Cross-Tenant Unified View — Paginated Fan-out

### 6.1 The Decision

```
For every tenant among the user's memberships (with read access):
    query it in its own context (app.tenant_id = that tenant, pure
    RLS) — paginated
    along with it (1.7): app.membership_id and app.scope_all (and,
    1.9: app.can_manage_scope) for their membership in that
    specific tenant, in the mandatory order (3.5/6) — never a
    value unified across tenants.
Merge with a sorted merge, paginated — with an explicit cap on the
number of tenants per batch.
```

(The list of memberships is read via the self-visibility policy — 4.3.)

### 6.2 Why fan-out, not `tenant_id = ANY(array)`

**The real reason (not performance):** permissions differ per tenant. A single `ANY` query cannot apply a different permission filter per tenant; fan-out applies each tenant's permission in its own context, then merges.

### 6.3 The guard against collapse (an explicit cap + keyset-paginated merging)

```
- Every tenant is queried paginated (a LIMIT per context).
- An explicit cap on the number of tenants in a single batch.
- Exceeding the cap (a member in dozens of tenants) → progressive
  paging.
```

**Pagination:** "one page per tenant, then sort" works only for the first page. **The rule:** keyset pagination — a cursor per tenant on the sort key, and a k-way merge; "the next page" means advancing the cursors of the consumed tenants, not a global offset.

An external search engine for sorting across hundreds of tenants is **deferred and documented** — invoked with real numbers once a scale justifies it.

### 6.4 The Security Guards (there are now four, since 1.7)

| Guard | Rule |
|---|---|
| Read-only | It shows and does not modify. Actions move to an explicit tenant context. |
| A permission per tenant | The overall set = the sum of what is due in each tenant individually. |
| A scope per tenant (1.7) | Along with the scope mode and assignments specific to their membership in that tenant — a scope from one tenant is never carried into another. |
| Scope declaration (1.7, corrected in 1.8) | The merged result carries the scope mode per tenant, and `total_count` only for tenants under `scope_all` — the unified view is the most dangerous surface for both partial silent blinding **and** count leakage (3.5, Test 19). |

### 6.5 The First Thing Proven in the Proof-of-Concept

This — not the subscriptions screen — is proven first: the hardest point, and it shares its pattern with background jobs (8).

---

## 7. Auditing — same-transaction, append-only

**The dual guarantee:** (a) auditing is written in the **same** transaction as the change — a write with no corresponding log entry is structurally impossible. (b) it is **append-only, never edited or deleted by any application role** — enforced in two layers: the manifest's policies (INSERT + SELECT only) and the absence of UPDATE/DELETE grants (3.8); Test 12 guards both. Archival and retention are `migrator`'s concern alone, via a documented procedure.

```sql
audit_log(id, tenant_id, actor_id, actor_type, action,
          entity_type, entity_id, old_value, new_value,  -- jsonb, secrets masked
          ip_address, created_at)
```

**A binding implementation note:** at the moment of `SavingChanges`, the generated IDs for new entities do not yet exist. The fix: a second save of the audit rows within the same transaction (after IDs are generated), with a guard against recursion — a flag that prevents the interceptor from intercepting the audit save itself.

**There are three writers, with explicit policies in the manifest:** `app_user` (with `WITH CHECK (tenant_id = app.tenant_id)`), `provisioner` (both paths' entries), and background jobs pass through as `app_user` in each tenant's context. No fourth writer. (1.13: the texts of both write policies — `audit_log_insert` and `audit_log_provisioner_insert` — are in 3.9.)

**Readers — the read policy is now written (1.9):** through 1.8, reading the log was tenant-scoped, and deferred as a spec item phrased "the preferred decision." That was a misjudgment of severity: the log carries `old_value` and `new_value`, so an `assigned` member could read the **content** of edits to entities not assigned to them — a gap that voids the second axis's guarantee, not an implementation detail. It is resolved here:

```sql
CREATE POLICY audit_read ON audit_log
  FOR SELECT TO app_user
  USING (
    tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    AND COALESCE((SELECT NULLIF(current_setting('app.scope_all', true), '')::boolean), false));
```

**Why `scope_all`, not `can_manage_scope`:** this read is **visibility**, not administration — and the log discloses the content of every entity, so the question is exactly "over whom do I see." A scope manager with mode `assigned` does not, via the log, see what they cannot see in the tables themselves.

**A declared, not a silent, price:** in 1.9 an `assigned` member sees **no** log entries at all — not even the log of their own assigned entities. This is a blinding, but it is declared and intentional, not silent: the failure here runs in the direction of safety, and the finer-grained alternative (a per-entity read, via `scope_ref_id` on the log) is added when a real user requests it (13/h). And the log's surface in the API declares its scope like any other aggregate surface (3.5), so a member sees that the log is hidden, not that it is empty.

(An asynchronous outbox will be reconsidered once scale justifies it — the interface allows the switch without rewriting consumers.)

---

## 8. Background Jobs — Rate-Limited Fan-out

**The most dangerous leakage vector, and the heart of the subscriptions product.** A job has no HTTP context — so where does `app.tenant_id` come from?

```
Every background job:
1. Fetches active tenants with the job_runner role (3.4):
     CREATE POLICY tenants_for_jobs ON tenants
       FOR SELECT TO job_runner USING (status = 'active');
2. Explicit fan-out: for each tenant — a transaction under
   app_user's context, set app.tenant_id, process, close.
3. An independent transaction per tenant (one tenant's failure
   does not stop the rest).
4. No implicit context or cross-tenant processing in a single
   query is permitted.
   (The only two exceptions: provisioner's two paths, 4.4.)
5. (1.7) A system context on the second axis: app.scope_all = true
   and app.membership_id unset — the job processes every entity of
   the tenant.
   (1.9) And app.can_manage_scope = false: the job sees everything
   and does not manage scope — managing scope is the act of an
   authenticated member, not a system act.
   This is one of the two declared scope_all contexts (3.5/6), no
   third.
   And its effect is declared: logic decided by a user's scope in
   a request is decided by full scope in a job — every
   scope-sensitive piece of logic is passed its scope explicitly,
   never inferred from its own context.
```

**The guard against pool exhaustion:** bounded batches and a cap on concurrent connections; the scheduler throttles the rate. A distributed queue is **deferred and documented** — for hundreds of tenants, not two.

---

## 9. Modular Monolith — Boundaries

Logically isolated modules: each module has its own project, and importing another module's tables directly is forbidden.

**Honesty about separation:** an FK between two modules makes separating them into services later impossible without breakage — **intentional and accepted**. Remaining a well-organized monolith forever is a success. "Separability" is a discipline that keeps the door open should scale demand it — not a promise.

**A declared open question (1.7) — the home of the scoped entity:** the second axis (4.8) revolves around an entity currently called the "client" in the consultancy module. The question: is it a **core** entity shared across modules (as in office-suite systems where one account is seen by CRM, support, and billing alike), or a **module** entity, duplicated in each?

```
Core home    → a composite FK from scope_assignments toward it ⇒
                the third layer is complete on the second axis; and
                a shared entity with no duplication.
Module home  → the core does not define an FK toward a module
                (reversing the direction of dependency); and the
                third layer remains incomplete at the entity's end.
An intermediary registry → (a third option, 1.8) scopable_entities
   in the core: (tenant_id, id, entity_type, source_id) — the
   module registers its entity into it, and scope_assignments
   references it via a composite key. The third layer completes
   **without** the core deciding the entity's home or knowing its
   details.
```

**Assessing the third option (1.8):** it genuinely resolves the knot — and deserves adoption **if the decision on the home is delayed**. But its cost is explicit: two records for the same entity must stay synchronized, and any module row with no counterpart in the registry becomes silently unassignable — a silent blinding of exactly the kind this document hunts down. So its adoption is conditioned on **structurally guaranteed synchronization**: either a trigger on the module's table that writes the registry entry within the same transaction, or — the cleaner route — the module's table itself referencing the registry via a composite FK, making an entity outside it impossible.

**The decision is explicitly deferred, not left unspoken, and its deadline is set: before the second module.** The justification for deferring: deciding now is an abstraction over one imagined consumer; the justification for not deferring further: migrating after two modules is painful, and the third layer remains incomplete until it is decided (3.3, 12). Until then, `scope_ref_id` is **an opaque, `NOT NULL` identifier with no FK**, and the core builds no semantics on top of it.

---

## 10. Core Boundaries — everything built from scratch

| Inside the core | A module on top of it |
|---|---|
| Tenants and isolation (the three layers: policies + grants + composite constraints; the five roles) | Subscriptions |
| Identity + the auth layer + the login and tenant-selection path + member lifecycle + bootstrap | Support |
| Roles and permissions | CRM |
| **The within-tenant scope axis** (1.7): the mode, the assignment, the restrictive template, and scope declaration — the core owns **the mechanism**, and the module owns **the entity's semantics** (4.8, 9) | Consultancy / Compliance |
| Module registration and activation | (Any future service) |
| The unified view (paginated fan-out, cursor-based merge) | |
| The background-job framework (rate-limited fan-out, job_runner) | |
| Auditing (same-transaction, append-only, three writers) | |
| The token system, visual identity, and RTL | |

---

## 11. An Honest Estimate — from proof-of-concept to product

**1.13's effect on the estimate:** the largest text change since 1.7 — 32 policies, two tables, two domain types, and a test — yet the first version written **entirely** from text that was run before it entered the document: 108 SQL cases in both directions, and a real EF path. Of the nine decisions the project owner settled in this version, three could only have been seen by running: `RETURNING`, silent forgetting in EF, and the silent insert from templates. **All of them live in the gap between what SQL says and what the framework actually sends.**

**1.12's effect on the estimate:** minutes of text, and nothing built yet. What stands out is that both mistakes **were written by the document while fixing something** — 1.8 while fixing `scope_assignments`, and 1.11 while fixing the `WHERE` condition. A fix is new text, and new text has not been run. So the procedure in Section 13 applies to **the document's own amendments** too: every policy or SQL command added in a new version is run in isolation before anything is built on it.

**1.11's effect on the estimate:** hours, and not a line of T2 written yet — which is the whole point: had T2 been built on the first assumption, Checks 2 and 8 would have been red on every table from the first run, and the nearest "fix" under pressure is loosening the match — that is, disabling the check that guards the templates. Had it been built on the second, the nearest "fix" for the permission error is a column grant — that is, the silent trap. **An untested assumption costs nothing when it is found; it costs when it is fixed in a hurry.**

**1.10's effect on the estimate:** an hour — a mechanical replacement in 33 places, four texts written out, and one test. The cost is not in the size but in the timing: had it been found after T2, every table would have been built with a wrong template text, and Check 2 matches it literally, so it would not see the error — intersecting here with the 1.8 lesson: **a check that matches the template textually guards the template, not its correctness.**

**1.9's effect on the estimate:** hours to a day: a third context variable, a resolution step, four policies whose condition changes, a read policy for the log, and three tests. The real cost in 1.9 is not code, it is **the decision to stop**: there is no 1.10 from another text review (13).

**1.8's effect on the estimate:** days, mostly tests, not code: three policies instead of one, a restrictive policy on `roles`, an edit in the transaction layer, two checks widened plus a third new one, and five tests. But **its value is not in its size**: without 1.8, an entire scope axis would have shipped disabled — every check from 1..9 passing, failing on the first real user. This is the second bug in this document to slip through the defense layers while each layer looks correct in isolation (the first was the FK gap in 1.6), and the two share one thing in common: **a check looks within a layer; the defect sits between layers.**

**1.7's effect on the estimate:** days, not weeks — two tables, a policy template, two checks, three tests, and two context variables. The real cost is not in building it but in **its placement**: the context variables and `provisioner`'s atomicity sit in two layers that are built once, so adding them after T0 is a migration that touches every request path (13). This alone justifies reopening the document instead of deferring it to the spec.

**A fully independent core: two months or more** — including a bidirectional design system built from scratch (weeks on its own). The 1.6 additions (the composite FK, the two matrix and coverage corrections) are days within the estimate, not on top of it — but the FK gap would have shipped to production with no test ever catching it, had it not been designed now.

**The leanest proof-of-concept** (a minimal core + identity + the login and tenant-selection path + invite-and-accept membership + memberships + three-layer RLS + a unified view + a simple background job + one RTL screen) = **weeks.**

**The product sold to the public** = the proof-of-concept + **hidden months:**

| Layer | Why months |
|---|---|
| Billing | Plans, limits, cycles, a payment gateway, compliance |
| Self-registration | Creating a tenant via bootstrap, activation |
| Provisioning | Setting up a tenant: roles, settings, modules |
| Migration discipline | Schema changes on a live database (and `migrator`'s conditions in 3.4) |
| Support and operations | Single-tenant restoration, identity deletion for compliance, monitoring, backups |
| A complete design system | Beyond the one proof-of-concept screen |

**The rule:** the proof-of-concept proves **isolation**; not **product readiness**.

---

## 12. Operational Risks

| Risk | Status |
|---|---|
| **Attribution columns on the scope surface: stale after an update, or forged at insert** | **Closed in 1.15** — dropped; the audit log is the sole source of attribution (4.1, 7) |
| **Check 8 demands `client_scope` on the very table the template reads — infinite recursion failing every scoped table** | **Closed in 1.14** — a structural exception + a guarding inverse clause + Test 16-d (3.6) |
| **A grant with no policy under FORCE RLS is dead: silent zero rows or rejection** (the login path and step c among them) | **Closed in 1.13** — 32 policies written and run in both directions (3.9) |
| **Bootstrap cannot create the new tenant's roles** | **Closed in 1.13** — `provisioner` from global templates, conditioned on `is_system`, its inserts critical writes (3.9, 5) |
| **`RETURNING` is rejected on what the role cannot read — including every assigned member's write via the audit log** | **Closed in 1.13** — no `RETURNING`, no database-generated values + Test 28 (2) |
| **EF forgets the key and timestamp silently (`Guid.Empty`, year 1)** | **Closed in 1.13** — `app_id` and `app_ts` in the constraints layer + Test 28c (2) |
| **A `FOR ALL` restrictive policy guarding writes silently hides reads** (repeated twice: 1.7 and 1.8) | **Closed in 1.12** — `system_role_guard` split by command + a general rule in 3.1 + the blinding branch in Test 23 (5) |
| **The password command writes a column it has no grant on** | **Closed in 1.12** — the grant is exactly `(password_hash, updated_at)`, and Check 6 enforces an exact match (3.8) |
| **Checks 2 and 8 match the document's text, while pg_get_expr returns a different deparsed form — so they fail on every correct table** | **Closed in 1.11** — deparse-against-deparse comparison on the same table, inside a rolled-back transaction (3.6) |
| **A password change via an ordinary EF update fails; and "fixing" it with a column grant turns it into silent zero rows** | **Closed in 1.11** — the condition is written (no WHERE, no RETURNING), Check 6 forbids column grants, and Test 13 guards both directions (3.8, 4.3) |
| **A custom variable reverts to `''`, not `NULL`, after its first setting, so the cast throws on reused connections** | **Closed in 1.10** — `NULLIF` around every context variable in 33 places + enforced in Check 2 + Test 27. **Found by running code, not by review** (header, 3.1) |
| **Implicit places in the text ("same expression", "a matching policy") that T2 builds literally** | **Closed in 1.10** — all four written out. Any implicit text in a policy is text Check 2 will never match (3.1, 4.6, 4.8) |
| **A hidden policy chain silently disables an entire mechanism** | **Closed in 1.8** — separating `scope_assignments` policies, a declared chain table with its actor, Check 10, and Test 16‑b/d (1, 4.6, 4.8) |
| **The API contract leaks a tenant's entity total** | **Closed in 1.8** — `total_count` only under `scope_all`, explicit `null` under `assigned` + Test 19 (3.5, 6.4) |
| **Revoking an assignment does not take effect until re-login** | **Closed in 1.8** — scope resolved per transaction, not per session + Test 20 (3.5) |
| **`is_system` protected only at the application layer** | **Closed in 1.8** — a restrictive `USING/WITH CHECK (NOT is_system)` policy + Test 23 via direct SQL (5) |
| **Assignment semantics were an accidental effect of a constraint, not a decision** | **Settled in 1.8** — one row, reactivated, with history in the audit log, and the reactivation effect declared (4.8) |
| **Cross-linking via UPDATE, not just INSERT** | **Covered in 1.8** — Check 7 widened to UPDATE + Test 21 (3.6) |
| **Privilege surfaces outside `role_table_grants`** | **Covered in 1.8** — Check 6 widened: columns, schema, functions, sequences, views, DEFAULT PRIVILEGES, CONNECT, role memberships + Test 22 (3.6) |
| **Confining the `provisioner` and `migrator` connections is asserted in text but not technically enforced** | **An open risk (1.8)** — enforcement is a network/operational matter, not a schema one; its controls are in 13/k |
| **An `assigned` member reads `audit_log` — a content leak via old/new values** | **Closed in 1.9** — `audit_read` conditioned on `scope_all` + Test 25 (7). And blinding the log of assigned entities is **declared**, not silent (13/h) |
| **Scope used as a substitute for the management permission** | **Closed in 1.9** — `app.can_manage_scope` from an explicit permission, checked by every administrative policy + Test 24 (4.8) |
| **Scope resolved with a single query before its variable is set** | **Closed in 1.9** — a mandatory three-step order + a resolution-chain table + Test 26 (3.5/6, 4.6) |
| **Every text fix spawns a new dependency chain** | **A pattern, not a single risk** — three consecutive releases. Its remedy is running the tests, not a tenth review (13) |
| **A member self-escalates scope via a column grant on `memberships`** | **Closed in 1.7** — `scope_mode` in a separate table with an administrative surface, plus a "not the actor's own membership" condition + Test 17 (3.8, 4.8) |
| **Assigning a user who has no membership in the tenant** | **Closed in 1.7** — the assignment references the membership via a composite FK + Test 18 (3.3, 4.8) |
| **A second permissive policy widens instead of narrowing** | **Closed in 1.7** — the second axis is `AS RESTRICTIVE` exclusively, and Check 2 separates the two sets (3.1, 3.6) |
| **A scope variable arriving from the client** | **Closed in 1.7** — an exclusive source in `membership_scope` + Check 9 + Test 17‑c (3.5/6) |
| **Partial silent blinding — incomplete numbers that look plausible** | **Mitigated in 1.7, not eliminated** — the scope-declaration rule in the API contract + Test 16‑c. A contractual mitigation, not a structural one: the database itself cannot detect it (1, 3.5, 6.4) |
| **The constraints layer is incomplete at the scoped entity's end** | **A declared open risk (1.7)** — `scope_ref_id` has no FK until the entity's home is decided; deadline: before the second module (3.3, 9) |
| **The cost of the restrictive `EXISTS` on every scoped table** | Monitored — the index `(tenant_id, membership_id, scope_ref_id)` and InitPlan wrapping of context variables (3.1, 3.2). The second subquery in the read path, after 4.6 |
| **A single-column FK admits a cross-tenant link** | **Closed in 1.6** — the composite-FK rule + tenant_id on both linking tables + Check 7 + Test 15 (3.3) |
| **`provisioner` denied the tools for its own second path** | **Closed in 1.6** — justified SELECT on identity tables (3.8) |
| **Section 5's tables were dark (no grants, no check)** | **Closed in 1.6** — entered the matrix, and Check 5 was inverted: coverage is the default, with an empty exemption list (3.6, 3.8) |
| **`username` with no unique constraint, though authentication resolves through it** | **Closed in 1.6** — `UNIQUE` + merged into the one exception family (3.3, 4.1) |
| **`memberships` policies implicit in the manifest** | **Closed in 1.6** — all five written out explicitly (4.5) |
| The member lifecycle was missing | Closed in 1.5 — invitations + the consent principle + a second declared provisioner path |
| An admin inserting memberships without consent → PII enumeration | Closed in 1.5 — INSERT reserved for provisioner alone + Test 10 |
| A tenant's admin forging their own audit trail | Closed in 1.5 — append-only via both layers + Test 12 |
| Password hashes exposed at the application surface | Closed in 1.5 — no SELECT for app_user + Test 13 |
| Login writes silent or without a writer | Closed in 1.5 — auth_attempts + rows-affected + Test 14 |
| `membership_auth` hidden → step-up blindness | Closed in 1.5 — self-visibility and tenant-scoped read policies |
| Dormant policies on disabled RLS | Closed in 1.5, and hardened in 1.6 by inverting Check 5 |
| The login path blocked by design | Closed in 1.4 — authenticator + app.user_id |
| `migrator` under FORCE seeing zero rows | Closed in 1.4 — a conscious `BYPASSRLS`, under its conditions |
| PII leakage in identity tables | Closed in 1.3 — the read policy via membership |
| A wrong, redundant, or PUBLIC-scoped policy | The manifest + Checks 2 and 4 |
| Recursion in nested policies | The no-cycle rule + Test 8 (and the chains shortened in 1.6) |
| Leaking connection state / reading outside the transaction wrapper | SET LOCAL + loud-above-silent-below + Tests 3–5 |
| Enumerating email/username existence | The one exception family + a unified response + Test 11 |
| Fan-out memory collapse / incorrect ordering | A tenant cap + keyset and k-way merging |
| Pool exhaustion in background jobs | Throttling + a dedicated job_runner |
| Building the spec on two conflicting documents | An explicit relationship decision in Section 0 |
| **Single-tenant restoration + identity deletion for compliance** | **An open risk, resolved before launch** — a layer of the sold product |
| The noisy-neighbor problem | Real at dozens of tenants. Monitored. |
| New code with no operational track record | A periodic architecture review on the spec and the code + adversarial testing |

---

## 13. The Next Step — and the Document is Frozen

**The freeze decision:** seven reviews de-escalated in severity, from the very core of isolation, to its edges, to the document's internal consistency — and that last category is exposed faster by writing the spec and the code than by an eighth text review. The next review is on **the spec**, and the one after that on **the code**. Any finding from here on is resolved there, and documented there.

**And the reopening rule (1.7, widened in 1.8, and closed in 1.9 — see the lesson below):** the document is reopened for a change in the **model**, or for a **defect that voids an existing mechanism** — not for a correction to the **text**. 1.7 satisfied the first condition (a new scope axis), and 1.8 the second (a hidden chain that disabled that axis entirely). Whatever satisfies neither stays in the spec or the code.

**A procedural lesson from 1.11 — the procedure is now fixed:** three engine assumptions in two days, all looking self-evident, all found by running. So testing in isolation every engine behavior the document assumes is **a mandatory first step in every PROOF_SPEC task**, before any code — not a reaction when the implementer happens upon something odd. And the reopening rule is unchanged: 1.11 was opened by two findings proven by running, not by review.

**A procedural lesson from 1.10 — the rule worked as designed:** the 1.9 decision said the next class of defect is visible only by running code. The first thing run — before a single line of T1 code, a test of an engine behavior the document assumes — found a defect nine reviews had missed. This does not open the door to a tenth text review; it confirms the opposite. **The reopening rule stays as it is**, and 1.10 is its first application, not an exception to it. And the procedure that produced the finding is generalized: **before building on any engine behavior the document assumes, test it in isolation.**

**A procedural lesson from 1.9 — and the reason to stop:** the ninth review found four items, three of which **were introduced by versions 1.7–1.8 themselves** — one of them inside the 1.8 fix (narrowing the `membership_scope` read produced the chicken-and-egg resolution problem). Every textual narrowing created a dependency chain that had not existed before, and the following review found it — a cycle that does not converge through reading. **This version is the last text-only revision.** Any future finding — however model-like it may look — is resolved first by running the 26 tests against real code; the document is reopened only for a finding that **survives the tests and is proven by the code**, not for a finding in the text.

**A procedural lesson from 1.8:** the bug that opened this version had **its test (16‑b) already written into the document** before it ever occurred. The text review that found it could have been replaced by simply running the test. This supports the freeze decision rather than undermining it: the next eye is on the code, because the code runs the tests and the text does not.

**Mandatory ordering before T0 (1.7, updated in 1.8):** the two new context variables and the `membership_scope` table are a **prerequisite**, not a later addition — the transaction layer and `provisioner` are built once, and introducing a context variable or an atomic field after they are built is a migration that touches every request path. Joining them in 1.8: **scope resolved per transaction** (a decision within the transaction layer itself), **and the scope-declaration contract** (written into the first aggregate endpoint's contract, or the mistake spreads to every endpoint after it). The restrictive template and Checks 8, 9, and 10, however, only need to be written alongside the first table carrying `scope_ref_id` — and Check 10 before any new policy that reads a table.

**1. The leanest proof-of-concept spec,** as Codex tasks, ordered as: the login and tenant-selection path first — **including the second-axis resolution in its mandatory three-step order (3.5/6), with its Test 26 run before anything after it** — then a full invite-and-accept cycle, then the unified view with cursor merging, then adversarial isolation (the twenty-eight tests + the ten CI checks), then identity, bootstrap, and the auth layer, then the minimal subscriptions screen.

**2. Items carried from the document into the first spec items (not into a new version):**

```
a.  The "the last owner cannot leave" rule: a constraint trigger or
    an advisory lock — a purely application-level rule is subject
    to a race between two concurrent departures.
b.  The auth_attempts retention and pruning policy (usernames and
    IPs accumulate with no cap) — a documented migrator procedure.
c.  The membership's provider at invitation acceptance: today's
    default is 'password', and its source becomes a tenant setting
    once SSO arrives — written as one line in the acceptance
    path's contract.
d.  WITH CHECK on creating invitations restricts invited_by =
    app.user_id, not just the tenant.
e.  Protecting seeded roles (is_system) from editing — the exact
    enforcement point (a conditional policy, or the application
    layer) is settled in the spec.

    — 1.7 items —
f.  The scope-mode field on invitations: its name, its values, and
    its validation at creation and at acceptance (4.8).
g.  The last full-scope membership cannot be downgraded: a
    counterpart to the "last owner cannot leave" rule (item a) on
    the second axis — a tenant with no remaining all-scope
    membership becomes unmanageable. The same race guard.
h.  (The core is settled in 1.9 — Section 7) reading audit_log
    requires scope_all. What remains for the spec: a fine-grained
    per-entity read for an assigned member — by adding
    scope_ref_id to events of scoped modules and a policy on the
    model of client_scope — **once a real user requests it**, not
    before.
    n.  (1.9) The permission code core.scope.manage: seed it into
    the catalog and grant it by default to the seeded roles
    (owner, admin) — a migrator-seeding item (3.8).
i.  Disabling an assignment: its effect on what the member
    previously created and on their open tasks — declared
    application behavior, not left to be discovered.
j.  Measuring the cost of the restrictive EXISTS under realistic
    load, with a declared fallback plan if it exceeds the
    threshold: a materialized assignment table, or a
    SECURITY DEFINER function to resolve assignments (the
    alternative declared in 4.8).
    (1.8) The measurement runs against the actual stack, not an
    assumption: PostgreSQL 18, Npgsql/EF Core, small and large
    tables, tenant_isolation and client_scope together, graduated
    assignment sizes, concurrent load, and fan-out with a growing
    tenant count. With an acceptance threshold written before the
    measurement, not after.

    — 1.8 items —
k.  Network-level enforcement confining the provisioner and
    migrator connections: a separate connection string, blocking
    the application network from reaching either account, the web
    service never holding their credentials, and logging every
    migration session. The textual condition in 3.4/4.4 does not
    enforce itself — these are its controls (12).
l.  Large migrations: an expand/contract pattern, resumability, and
    confirming row counts before and after every data migration
    (an extension of the condition in 3.4/2).
m.  The precise meaning of visible_count in the API contract: after
    scope, permission, status, and search all together — not after
    scope alone (3.5).
```

**3. A periodic architecture review** on the spec, then the code — every review checks both directions (leakage/blinding) and all three layers (policies/grants/constraints).

---

## Appendix A — Changelog from 1.14 to 1.15

| Item | 1.14 | 1.15 |
|---|---|---|
| `membership_scope` | `updated_by`, `updated_at` — the update cannot write them, so they stay stale | **Dropped — the audit log is the sole source of attribution** (4.1) |
| `scope_assignments` | `granted_by`, `granted_at` — `granted_by` can be forged | **Dropped** (4.1) |

**What did not change:** every policy and every grant. Neither referenced the columns.

---

## Appendix B — Changelog from 1.13 to 1.14

| Item | 1.13 | 1.14 |
|---|---|---|
| Check 8 | Every table carrying `scope_ref_id` — including `scope_assignments` itself: infinite recursion | **Every table except the one the template reads, + an inverse clause failing if the template is present on it** (3.6) |

**What did not change:** everything else.

---

## Appendix C — Changelog from 1.12 to 1.13

| Item | 1.12 | 1.13 |
|---|---|---|
| Policies for roles other than `app_user` | 22 written; the other grants dead | **+32 written and run (3.9)** |
| `provisioner` | No tenant scope | **Sets `app.tenant_id` to the target tenant; one tenant per transaction** (3.4, 3.9) |
| Bootstrap | Impossible — nothing could create a new tenant's roles | **From a global `role_templates` catalog, with an insert conditioned on `is_system`** (5, 3.9) |
| Keys and timestamps | `uuidv7` in the database | **From the application; no `DEFAULT`, no `RETURNING`; `app_id` and `app_ts`** (2) |
| `rls_exemptions` | A table in public (needing RLS, with no tenant_id) | **A file in the repository** (3.6, Check 5) |
| `tenants` | Undefined | **Defined** (4.1) |
| `invitations.intended_scope_mode` | In the spec only | **In the schema, and enforced in the database** (4.1, 3.9) |
| `membership_auth` | Self + "for an admin" with no enforcement | **Self only** (4.2) |
| The manifest | — | **+ membership_roles, role_permissions, tenant_modules with tenant_isolation listed, + the two catalogs** (3.6) |
| Adversarial tests | 27 | **28 — no generated values, and forgetting is loud** (3.7) |

**What did not change:** the model, the three layers, and every policy in 1.12. 1.13 completes what was never written; it does not amend what was.

---

## Appendix D — Changelog from 1.11 to 1.12

| Item | 1.11 | 1.12 |
|---|---|---|
| `UPDATE` grant on `user_password_credentials` | `(password_hash)` — contradicts the 1.11 command | **`(password_hash, updated_at)`**, with Check 6 enforcing an exact match (3.8) |
| Error message for the `WHERE` form | `permission denied for column user_id` | **`permission denied for table …` — the test asserts on SQLSTATE `42501`** |
| `system_role_guard` | One `FOR ALL` restrictive policy — hides the base roles from reads | **Three restrictive policies by command; reads left to `tenant_isolation`** (5) |
| Test 23 | "Fails" | **Database silent (zero rows, row untouched), API loud (rows-affected), + the blinding branch** (3.7) |
| Restrictive `FOR ALL` policies | No rule | **Only when narrowing reads is intended** (3.1) |

**What did not change:** the model and the three layers. 1.12 corrects two conflicts within the text.

---

## Appendix E — Changelog from 1.10 to 1.11

| Item | 1.10 | 1.11 |
|---|---|---|
| Policy matching in Checks 2 and 8 | The document's text after whitespace normalization — fails on every correct table | **Deparse against deparse**: a reference created on the same table inside a rolled-back transaction (3.6) |
| The `NULLIF` rule in Check 2 | On the text | **On the deparsed form** (3.6) |
| Check 10 | `pg_depend` | **+ excluding `refobjid = polrelid`** (3.6) |
| Password change | "UPDATE with no SELECT", unconditioned | **No `WHERE`, no `RETURNING`; a raw command, not an EF update** (3.8, 4.3) |
| Check 6 on `user_password_credentials` | No `SELECT` on the table | **+ no `SELECT` on any single column** (3.6) |
| Test 13 | Exactly one row | **+ the `WHERE` form fails loudly, not silently** (3.7) |
| Testing in isolation before building | A reaction | **A mandatory first step in every task** (13) |

**What did not change:** the model, the three layers, and the text of every policy. 1.11 corrects how they are checked, and one implementation condition.

---

## Appendix F — Changelog from 1.9 to 1.10

| Item | 1.9 | 1.10 |
|---|---|---|
| Reading context variables in policies | `current_setting('app.x', true)::<type>` — throws on a reused connection | **`NULLIF(current_setting('app.x', true), '')::<type>`** in 33 places (3.1) |
| Fail-safe | An unproven assumption — true only on a fresh connection | **Deterministic, whatever the connection's history** (3.4) |
| Implicit places | Three "same expression" + "a matching policy on users" with no text | **Written out — T2 builds literally** (3.1, 4.6, 4.8) |
| The chain table | `users → memberships` implicit within the persons chain | **Registered with its actor** (4.6) |
| Check 2 | Matches the template text | **+ every `current_setting` in any policy sits inside `NULLIF`** (3.6) |
| Adversarial tests | 26 | **27 — fail-safe on a reused connection** (3.7) |
| Source of the finding | Text reviews | **The first finding from running code — under the 1.9 rule itself** (13) |

**What did not change:** the model, the three layers, and every decision in 1.9. 1.10 corrects an assumption about engine behavior; it is not a design change.

---

## Appendix G — Changelog from 1.8 to 1.9

| Item | 1.8 | 1.9 |
|---|---|---|
| Condition on administrative policies | `scope_all` — **scope as a substitute for permission** | **`app.can_manage_scope` from the `core.scope.manage` permission** — the orthogonality of role and scope enforced by the database (4.8) |
| Visibility policies (`client_scope`) | `scope_all` | Unchanged — visibility genuinely is a scope matter |
| Reading `audit_log` | Tenant-scoped, deferred for the spec | **`audit_read` conditioned on `scope_all`** — closes the old/new-value leak (7) |
| Resolving second-axis variables | "Together" — admits a single join that returns zero | **A mandatory order: membership ← mode ← permission** (3.5/6) |
| The chain table | Four chains | **+ the two resolution chains, and a third condition: the variable is set before it is read** (4.6) |
| `membership_scope_read` text | Two conflicting texts in 4.8 | **One final text** (4.8) |
| Check 10 | Text-expression parsing | **`pg_depend`** (3.6) |
| Context variables | Four | **Five — `app.can_manage_scope`** |
| Adversarial tests | 23 | **26 — permission/scope orthogonality, the log, resolution order** (3.7) |
| The freeze verdict | Reopened for a model change or a mechanism defect | **The last text-only revision: reopened only for a finding that survives the tests and is proven by the code** (13) |

**What did not change:** everything else. 1.9 is four localized fixes and a decision to stop.

---

## Appendix H — Changelog from 1.7 to 1.8

| Item | 1.7 | 1.8 |
|---|---|---|
| `scope_assignments` policies | `FOR ALL` conditioned on `scope_all` — **blocks reading, disabling the entire axis** | **Three policies split by command**: self + administrative read, and administrative write (4.8) |
| The no-cycle rule | Checks only for cycles | **+ a declared chain table with each chain's actor, and a reachability condition** (4.6) |
| Scope declaration | "Visible **out of the total**" — a disclosure channel | **`scope_mode` + `visible_count` always, `total_count` only under `scope_all`, explicit `null` under assigned** (3.5, 6.4) |
| Scope resolution | "At tenant selection" — admits session storage | **Per transaction** — revoking an assignment propagates immediately (3.5) |
| `is_system` | Application-level protection | **A restrictive database policy, closing both editing and promotion at once** (5) |
| Reading `membership_scope` | Tenant-wide, for everyone | **Self + administrator** — the same shape as reading assignments (4.8) |
| Assignment semantics | An accidental effect of a constraint | **A declared decision: one row, reactivated, with history in the audit log** (4.8) |
| `scope_ref_id` | No constraint | **`NOT NULL`** — an empty value fails safely but produces orphaned rows (3.1) |
| Check 6 | `role_table_grants` alone | **+ columns, schema, functions, sequences, views, DEFAULT PRIVILEGES, CONNECT, role memberships** (3.6) |
| Check 7 | Tested at creation | **+ at update** — cross-linking after creation is an independent path (3.6) |
| CI checks | Nine | **Ten — the policy-chain check** (3.6) |
| Adversarial tests | 18 | **23 — the scope contract, revocation propagation, update-based linking, role impersonation, the system role** (3.7) |
| Home of the scoped entity | Two options | **+ a third option: an intermediary `scopable_entities` registry, conditioned on structurally guaranteed synchronization** (9) |
| The reopening rule | A model change | **+ a defect that voids an existing mechanism** (13) |

**What did not change — for emphasis:** the conceptual model as-is; `tenant` remains the sole unit of isolation; the `tenant_isolation` text is untouched; the choice of `AS RESTRICTIVE` for the second axis stands; the composite-FK rule and Checks 1–5 and Tests 1–15 remain as they were. 1.8 is a mechanism fix and a closing of decisions, not a model review.

**What was rejected from the eighth review:** the claim that `provisioner`'s mechanism and its use of `BYPASSRLS` are ambiguous — rebutted by the text of 3.4, 3.8, 4.4, and 4.5; and the remaining valid item is a single operational one (13/k).

---

## Appendix I — Changelog from 1.6 to 1.7

| Item | 1.6 | 1.7 |
|---|---|---|
| Scope axes | One axis: the tenant | **Two axes: the tenant + scope within it** (4.8) |
| Templates | One permissive template (`tenant_isolation`) | **Two templates: the permissive one, unchanged verbatim, + the restrictive `client_scope`** (3.1) |
| Placement of `scope_mode` | — | **A separate `membership_scope` table — not a column on `memberships`** (a clash between column grants and self-departure, 3.8) |
| The assignment's target | — | **The membership, not the user** — a composite FK guarantees the assignee is a tenant member (3.3) |
| Context variables | `app.tenant_id`, `app.user_id` | **+ `app.membership_id`, `app.scope_all`**, with an exclusive source and a static check (3.5, 3.6/9) |
| Check 2 | One policy set | **Two separate sets: permissive and restrictive** — `polpermissive` is the divider (3.6) |
| CI checks | Seven | **Nine — second-axis coverage, and the source of its variables** (3.6) |
| Adversarial tests | 15 | **18 — the second axis in both directions, self-escalation, cross-tenant assignment** (3.7) |
| `provisioner`'s atomicity | Tenant + identity + membership + auth | **+ a `membership_scope` row** — a membership with no scope is an invalid state (4.4) |
| The unified view | A permission per tenant | **+ a scope per tenant + scope declaration in the merged output** (6.1, 6.4) |
| Background jobs | `app_user` context per tenant | **+ a system context on the second axis (`scope_all`) with its declared effect** (8) |
| The silent-blinding lesson | Total blinding — a path that looks empty | **+ partial blinding — an output that looks plausible**, and the scope-declaration rule as a contractual mitigation (1, 3.5) |
| Completeness of the three layers | Complete | **Declared incomplete at the scoped entity's end**, until its home is decided (3.3, 9, 12) |
| The freeze verdict | Absolute: no version 1.7 | **Conditional: reopened for a model change, not a text correction** (the header, 13) |

**What did not change — for emphasis:** `tenant` remains the sole unit of isolation; the `tenant_isolation` text is untouched on any table; the composite-FK rule and Checks 1–7 and Tests 1–15 remain as they were; no decision from 1.6 was invalidated. 1.7 is an addition on top of an existing structure, not a review of it.

---

## Appendix J — Changelog from 1.5 to 1.6

| Item | 1.5 | 1.6 |
|---|---|---|
| The constraints layer | Unchecked — a single-column FK admits a cross-tenant link (an invitation with another tenant's role) | **The governing composite-FK rule + UNIQUE(tenant_id, id) on parents + Check 7 + Test 15** (3.3, 3.6) |
| membership_roles / role_permissions | No tenant_id: outside Check 1, inside the manifest and the policy chains | **tenant_id + composite FK: the standard template suffices for both, and they left the manifest and the chains** (4.1, 5) |
| membership_auth | Single-column FK | **tenant_id + composite FK, remained in the manifest for its self-visibility** (4.2) |
| The provisioner matrix | INSERT only — its declared second path could not execute | **Justified SELECT on persons/users/memberships: the transitive read on the same confined surface** (3.8) |
| Section 5's tables in the matrix | Entirely absent | **roles, role_permissions, tenant_modules, modules, permissions — with two read-only global catalogs** (3.8) |
| Check 5 | A manual core-table list — the same drift as the old Check 2 | **Inverted: every table is covered by default, with a declared, empty exemption list** (3.6) |
| `users.username` | No unique constraint, though authentication resolves through it | **UNIQUE** (4.1) |
| The 3.3 exceptions | One exception (email) and a second, undeclared one (username) | **One declared family: cross-tenant identity identifiers, with a unified UX mitigation** (3.3) |
| `memberships` policies | Implicit — 4.5 assumes them and the manifest never states them | **All five written out explicitly** (4.5) |
| The core-premise lesson | Two layers (policies/grants) | **Three layers — constraints operate outside the authority of the other two** (1) |
| Adversarial tests | 14 | **15 — structurally rejecting cross-tenant linking** (3.7) |
| CI checks | Six | **Seven — the composite-FK check** (3.6) |
| Document status | Open versions | **Frozen — remaining items moved to the spec (13/2), with the next review on the spec, then the code** |
