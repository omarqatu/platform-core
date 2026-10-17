# A1 — password reset and change, with the session security stamp

**Status: a design for approval. Nothing is built yet.** Governing text, in order:
`docs/PLATFORM_CORE_AMENDMENT_01.md` §2.1, §2.2, §3, §4.1, §5.1, §5.2 → PLATFORM_CORE v1.16 → PROOF_SPEC v1.3 → the
code → the guides. OPEN_ITEMS 47 (the amendment's own item for D6 — the session brief said 45, which is D3′/delivery;
I read the task as 47), and it advances 16, 36, 51 and 52.

Two conflicts stop part of this task before any code (CLAUDE.md: *a conflict → stop and report it; the project owner
decides*). They are **§9** below. Everything else is settled and ready to build.

---

## 1. What this task builds

| | |
|---|---|
| Migration `0010_password_reset.sql` | `users.security_stamp` (NOT NULL, backfilled), `password_reset_tokens` with its three layers, the retention procedure, the column grant |
| `POST /api/auth/password/reset` | UoW ×2 — authenticator consumes the token, app_user sets the new hash and a new stamp |
| `PUT /api/me/password` | UoW ×2 — authenticator verifies the current password, app_user updates; the cookie is re-issued with the new stamp |
| The stamp in the cookie | issued at login and preserved across tenant select/deselect; compared on every request; mismatch → 401 `session_expired` |
| The attempt limiter (§5.2) | escalating delay per (username + ip), no lockout, a per-ip ceiling → 429 `too_many_attempts` before the password is read; every number configuration, marked `[—]` |
| `POST /api/auth/password/forgot` | **blocked** — §9.1. Everything else can ship without it. |

Not in this task, and said so in the PR: the email sender itself (§5.5, M1), the forgot/reset/change **screens**
(§15 — a follow-up PR; this one adds only the two error codes to `apiErrors.ts` and `ar.json`), item 51 (forgot's
response time), item 52 (the identity audit log).

---

## 2. The migration — `src/Migrations/Sql/0010_password_reset.sql` + `M0010_PasswordReset.cs`

One file, forward-only, all three layers for the new table, ending in a row-count check.

### 2.1 `users.security_stamp` (§2.1)

```sql
ALTER TABLE users ADD COLUMN security_stamp app_id;          -- nullable first: the table has rows
UPDATE users SET security_stamp = uuidv7() WHERE security_stamp IS NULL;   -- the backfill, in this migration
ALTER TABLE users ALTER COLUMN security_stamp SET NOT NULL;
```

No `DEFAULT` remains on the column (v1.16 §3.5, Test 28a): the application writes it on every insert. Then the
check §3.4 condition 2 asks for:

```sql
DO $$ BEGIN
  IF EXISTS (SELECT 1 FROM users WHERE security_stamp IS NULL)
     OR (SELECT count(DISTINCT security_stamp) FROM users) <> (SELECT count(*) FROM users) THEN
    RAISE EXCEPTION 'security_stamp: every user row must carry its own stamp';
  END IF;
END $$;
```

`uuidv7()` in the backfill is the migrator idiom already used in `0005`/`0008`; "the application generates every key"
governs the application's paths, and §2.1 names the backfill explicitly.

The grant (§3, the `users` row — `app_user` UPDATE gains `security_stamp`):

```sql
GRANT UPDATE (security_stamp) ON users TO app_user;
```

The write goes through the **existing** `user_self_update` policy — a user writes their own row, nothing new in the
policy layer. `authenticator` already holds table-level `SELECT` on `users`, so it needs nothing.

### 2.2 `password_reset_tokens` (§2.2) — the three layers in this file

```sql
CREATE TABLE password_reset_tokens (
  id           app_id PRIMARY KEY,
  user_id      uuid   NOT NULL REFERENCES users (id),
  token_hash   text   NOT NULL,
  requested_at app_ts NOT NULL,
  expires_at   app_ts NOT NULL,
  used_at      timestamptz NULL,
  request_ip   text   NULL
);
CREATE UNIQUE INDEX password_reset_tokens_token_hash_key ON password_reset_tokens (token_hash);
CREATE INDEX ix_password_reset_tokens_user_id ON password_reset_tokens (user_id);
```

No `tenant_id` — a global identity table beside `auth_attempts`, so §3.2 (an index starts with `tenant_id`) and §3.3
(the composite FK) do not apply; the FK to `users` is the plain one `users` itself carries elsewhere. The unique index
on `token_hash` is mine, not the document's: the lookup path, and a second row with the same hash would be a defect.
It is called out in the PR.

Then `ENABLE` + `FORCE` RLS and the **three §4.1 policies verbatim** (`password_reset_tokens_authenticator_select`,
`…_insert`, `…_use`) — the English text exactly as `generate-manifest.py` extracts it — and the grants of §3:

```sql
GRANT SELECT, INSERT ON password_reset_tokens TO authenticator;
GRANT UPDATE (used_at) ON password_reset_tokens TO authenticator;
```

Consumption is proven on PostgreSQL 18 already (`docs/proposals/amendment-01-pg18-proof/result-2026-10-17.txt`
lines 38–40: consume → 1 row, consume again → 0, un-consume → 0). No new engine assumption is introduced, so no new
proof run is owed; the PR cites that record.

### 2.3 Retention (§2.2 — "the same policy as `auth_attempts`")

`0005_seed_and_retention.sql` owns `prune_auth_attempts()`, a migrator-owned procedure with `REVOKE … FROM PUBLIC` and
no `SECURITY DEFINER`. The same shape, same file pattern:

```sql
CREATE PROCEDURE prune_password_reset_tokens() LANGUAGE sql AS $$
  DELETE FROM password_reset_tokens WHERE requested_at < now() - interval '90 days';
$$;
REVOKE ALL ON PROCEDURE prune_password_reset_tokens() FROM PUBLIC;
```

No application role gets `DELETE`; `grants.json`'s `executable_routines` stays empty, as it is for the existing
procedure.

---

## 3. The audit layer (§2.1, §2.2 — and why nothing throws)

`AutomaticAuditInterceptor` throws `AuditContextMissingException` for any tracked, non-exempt write in a transaction
with no tenant. Both password paths run with `app.user_id` and **no tenant**, so each write must be exempt or the
transaction dies. Two one-line additions, both named by the amendment:

1. `SelfServiceUserProperties` gains `nameof(User.SecurityStamp)` — §2.1: "it joins the exception list of automatic
   auditing beside `last_login_at, language, theme`". The existing guard still holds: exempt only when the actor is
   the row's own user and **only** those properties changed.
2. `Exempt(...)` gains `PasswordResetToken` beside `AuthAttempt` — §2.2: "outside automatic auditing, like
   `auth_attempts`".

`UserPasswordCredential` is already exempt (v1.16 §7). `token_hash` is already on the masking list, so nothing new
there either. The consequence — a password change leaves no trace anywhere — is OPEN_ITEMS 52, which this PR
re-states and does not close.

---

## 4. The three units of work (§5.1)

`PasswordPaths` in `src/Core/Identity/` holds the two halves; `AuthEndpoints` maps the routes. Both endpoints are
`OwnUnitsOfWork`.

### 4.1 `POST /api/auth/password/reset` — `{ token, new_password }`, no session, no cookie issued

**UoW-1, `AuthenticatorDbContext`, `SessionContext.None`:**
`hash = SHA256(token)` (`InvitationToken.Hash`'s rule, a second small helper rather than a shared one — the two token
kinds stay separate), then the single row by `token_hash`; refuse (`400 invalid_token`… see §6) when it is missing,
`used_at IS NOT NULL`, or `expires_at <= now`. Consume it: `row.UsedAt = DateTime.UtcNow`, `CriticalWrite.SaveAsync`
— zero rows (a race, or `used_at` already set) is an error, not a silent pass; the `…_use` policy makes the race
safe in the database. **Returns the token's `user_id`.**

**UoW-2, `CoreDbContext` (app_user), `new SessionContext(userId, null)`:** the new hash into
`user_password_credentials` (`password_self_update`) and a fresh `security_stamp` into `users` (`user_self_update`),
both `CriticalWrite`, one `SaveChanges`. No cookie is issued (§5.1): every session of that user, including the one
that may be sitting in this browser, dies on its next request.

### 4.2 `PUT /api/me/password` — `{ current_password, new_password }`, session required

**UoW-1, authenticator:** read `user_password_credentials` for the **session's** user id and
`PasswordHashing.Verify`; wrong → `401 invalid_credentials`, and an `auth_attempts` row is written exactly as login
writes one, so the limiter sees it. **UoW-2, app_user:** identical to 4.1's UoW-2. Then the cookie is re-issued with
the new stamp (§5.1), so the caller's own session survives and every other session of theirs ends.

### 4.3 Where `app.user_id` comes from — the §0 exception, and Check 9

`app.user_id` is set in exactly one place today and after this change: `UnitOfWork.ApplyContextAsync`, from the
`SessionContext` the caller hands it. Nothing new sets it; what is new is **one caller** that builds that
`SessionContext` from something other than the cookie:

| Caller | `SessionContext.UserId` from |
|---|---|
| `UnitOfWorkMiddleware`, every normal endpoint | the authenticated cookie |
| login UoW-2, `PUT /api/me/password` UoW-2 | the cookie / the just-authenticated user |
| **`POST /api/auth/password/reset` UoW-2** | **the `user_id` of the token UoW-1 verified and consumed** — the named exception to §4.3-b (§0) |

It is a value read from a row the database handed us inside the previous transaction, never from the request, a
header, a query, a token claim or a cookie. The plaintext token from the body never becomes an identity; only its
hash is matched, and only a row that was unused and unexpired yields a user id. Check 9 is untouched — it guards the
four second-axis variables, and this path sets none of them (no tenant, no membership, no scope); the
`scope-variable-writers.json` list stays as it is. A `[W]` test asserts the `SET LOCAL` sequence of UoW-2 is
`app.user_id` alone.

---

## 5. The stamp in the cookie, and the comparison on every request (§2.1, §5.2)

**The cookie** gains a third claim, `security_stamp`, beside `user_id` and `tenant_id` — "plus the security stamp
once Amendment 01 §2.1 is built" (CLAUDE.md). `SessionCookie.Principal(userId, tenantId, stamp)`; `SessionCookie.Read`
returns it beside the `SessionContext`. Still no role, permission, scope or membership.

Issued at login (read in the same app_user transaction that writes `last_login_at`), carried unchanged through
`POST /tenants/{id}/select` and `/tenants/deselect`, replaced by `PUT /api/me/password`.

**The comparison** lives in `UnitOfWorkMiddleware`, one `SELECT security_stamp FROM users WHERE id = app.user_id`
under the existing `user_self_read`:

- for a **normal** or **no-active-tenant** endpoint — the first statement inside the transaction the middleware
  already opens: one extra statement, no extra transaction;
- for an **own-units-of-work** endpoint — the middleware has no transaction, so a short `RunReadOnlyAsync` with
  `app.user_id` alone, before the endpoint runs.

Mismatch, or no row → `401 session_expired` and the cookie is expired in the response. Both call one helper, so the
rule lives in one place.

Skipped when the cookie carries no `user_id` (login, reset, forgot, acceptance of an invitation by a new account),
and skipped for `POST /auth/logout`: logging out must work with a dead session, or a stale cookie can never be
cleared from the browser. That exemption is an attribute on the endpoint, not a path string.

A cookie minted before this migration carries no stamp claim; it is treated as a mismatch → 401 once, and the user
signs in again. Said in the PR, since it logs everyone out on deploy.

**What it closes:** OPEN_ITEMS 36 in part only — a changed password ends every session, logout still does not
(the stamp does not change on logout). The item stays open, with that sentence.

---

## 6. New API error codes

Three, by the §11 recipe (`ApiErrorCodes.cs` → `export-api-error-codes.py` → `apiErrors.ts` → `ar.json`):

| Code | Status | Where |
|---|---|---|
| `session_expired` | 401 | the stamp comparison (§5.2 names this one) |
| `too_many_attempts` | 429 | the per-ip ceiling (§5.2 names this one) |
| `invalid_token` | 400 | reset: the token is unknown, used or expired — one code for all three, so the response says nothing about which |

`invalid_credentials` (the wrong current password) and `invalid_value` (a new password that fails the length rule)
already exist. If you would rather reuse `invalid_invitation`'s pattern with a separate `token_expired`, say so —
I chose one code deliberately, as the reset path has no legitimate reason to distinguish.

---

## 7. The limiter (§5.2, OPEN_ITEMS 16)

In `Authenticator`, reading `auth_attempts` — no new table (the roadmap's M2-هـ), no lockout.

```
Identity:LoginLimits:Window                  15 min   [—]
Identity:LoginLimits:DelaySecondsByFailure   [0,0,1,2,4,8,15]  [—]  escalating, the last value is the cap
Identity:LoginLimits:PerIpCeiling            50       [—]
Identity:PasswordReset:TokenLifetimeMinutes  30       [—]  (§7/2 of the amendment)
```

Every number is configuration bound through `IOptions`, every one marked `[—]` in `appsettings.json` with a comment
pointing at item 16: *settled at launch*.

Inside UoW-1, in order: count the window's failed attempts for this `ip`; at or above the ceiling, write the attempt
row and return a refusal — **before** the password is read or verified — which the endpoint answers `429
too_many_attempts`. Otherwise count the window's failures for (`username_entered`, `ip`), do the full existing work
(the same query, the same PBKDF2, one `auth_attempts` row — T3.5 unchanged), and return the outcome together with the
delay that count earns. The delay is awaited by the endpoint **after the transaction commits** — never with a
transaction open — and only on a failure, so a correct login is never slowed.

The refusal depends on the ip alone, so it reveals nothing about which usernames exist.

`PUT /api/me/password`'s failed verification writes its attempt row through the same path and is counted the same
way. `forgot`'s half of §5.2 (keyed by email + ip) travels with `forgot` — §9.1.

**The mechanism is proven in `[W]`**, where the in-process Api can be configured with small numbers (two failures
→ a measurable delay; a ceiling of three → 429 on the fourth). The dev/CI configuration keeps the first ~40 failures
free and the ceiling high, because the whole `[B]` suite shares one ip. **This is disclosed, not hidden** — see
§9.2: with production numbers, one existing `[B]` test fails, and that is a decision for you, not a knob for me.

---

## 8. Checks, tests, documents

**Checks.** `grants.json`: the new `password_reset_tokens` table row, and `security_stamp` added to the `users`
column-UPDATE list — a change to that file is your decision, so the PR says so plainly.
`amendment-policies-built.json` `"01"` gains exactly `password_reset_tokens.password_reset_tokens_authenticator_select`,
`…_insert`, `…_use`, and `generate-manifest.py` is re-run in the same PR (`check-manifest-extraction` then passes, and
Checks 2 and 3 start requiring those three). `policy-chains.json` needs nothing: the three policies read no other
table. `rls-exemptions.json` unchanged. Then `scripts/dump-schema.sh`, and `schema-allowlist.txt` only if the new
procedure needs naming (it does not — the existing one is not listed).

**Tests.** `[B] tests/Conformance/T3_PasswordTests.cs` (new): a token row inserted by SQL as `authenticator` with a
known plaintext's hash, then reset over HTTP → the old password fails and the new one works; a used token, an expired
token, another user's token → refused and the password unchanged; after a reset, a cookie minted before it →
`401 session_expired`; `PUT /api/me/password` with the wrong current password → 401 and no change; `app_user` cannot
`SELECT` `password_reset_tokens` (42501) and `authenticator` cannot un-consume one (0 rows).
`[W] tests/Core.WhiteBoxTests/`: UoW-2 of reset sets `app.user_id` alone and in that order; the stamp comparison
sends exactly one statement and none at all without a cookie; the audit interceptor writes no row for a stamp change
or a token write **and does not throw** with no tenant; the limiter's numbers. Every new `checks` behaviour that is a
check gets its `--self-test` — here nothing new is added to `checks/core`, so there is none to add.

**A `NOT NULL` column on `users` touches five existing fixtures** that insert users directly:
`tests/seed/seed-contract.sql`, `tests/Core.WhiteBoxTests/WhiteBoxFixture.cs`, `tests/Measurement/Program.cs`,
`tests/Conformance/T4_AcceptanceTests.cs:219`, `tests/Core.WhiteBoxTests/AcceptanceMatchTests.cs:98`. Each gains the
new column in its `INSERT`; `Bootstrap.cs` and the acceptance path gain `SecurityStamp = Guid.CreateVersion7()`.
Nothing is weakened, skipped or re-asserted — it is the schema change reaching its fixtures — but it is an edit to
`[B]` files, so it is listed here before it happens.

**Documents.** `DEVELOPER_GUIDE` §7 (the cookie's third claim, the comparison, the three paths), §14 (the three
endpoints and three codes in the reference, and the §5.1 "specified, not built" note trimmed to what is left),
§11 if the error-code recipe gains nothing new (it does not). `DATABASE.md`: the new table, the new column, the
retention procedure. `docs/schema.sql` regenerated. `OPEN_ITEMS`: 47 closed in the part this builds, 36 and 16
updated, 51 and 52 re-stated as still open, plus the new items §9 produces.

---

## 9. The two conflicts — your decision

### 9.1 `forgot` cannot be built as written: `authenticator` cannot read `persons`

§5.1 makes `POST /api/auth/password/forgot` an `authenticator` unit of work, and §5.2 keys its limiter by
(**email** + ip). The email lives in `persons.email`. The amendment's own grants matrix (§3) carries v1.16's
`persons` row verbatim: `app_user` SELECT, `provisioner` SELECT/INSERT, **`authenticator` nothing** — and there is no
`persons` policy for `authenticator` either. So the role that owns the path cannot resolve an address to a user, and
cannot read an address to send to. No other role can do it without a session: `app_user` sees only its own row
(`user_self_read`), `provisioner`'s policies all require `app.tenant_id`, and a forgot request has no tenant.

This is a conflict inside Amendment 01 (§5.1/§5.2 against §3), not between the amendment and v1.16, so §0's rule
("any other conflict is an error in the amendment") points at the amendment — and the decision is yours.

What I would not do: have a policy on `users` reach into `persons` to launder the read. Whatever PostgreSQL does
there, a pre-session role gaining PII through a policy expression is the kind of thing this architecture exists to
prevent.

The ways out, as I see them:

- **(a) Defer `forgot` to the PR that builds the email sender (my recommendation).** It needs that sender anyway
  (§5.5, M1/M2-ب): built today it would mint a token nobody receives, exactly the position invitations are in
  (OPEN_ITEMS 22). Everything else in A1 ships now and is complete in itself: reset works from a token, and the
  migration path (§6/7) starts at `announce-migration`, which is later still. A new OPEN_ITEMS entry (53) records
  the grant question so it is decided with the sender, not forgotten.
- **(b) Open the narrowest possible read now:** `GRANT SELECT (id, email) ON persons TO authenticator` plus a
  `persons_authenticator_read` policy. That is a change to the grants matrix **and** a new §4.1 policy — the manifest
  is extracted from the amendment's text, so the amendment itself must gain both before the code can. A docs change
  by you, then this PR.
- **(c) Key `forgot` on `username` instead of the email.** `authenticator` reads `users.username` today, so the
  endpoint builds with no new surface — but the address to send to is still unreadable, so it only moves the problem
  to the sender, and it contradicts §5.1/§5.2's wording.

### 9.2 The escalating delay breaks PROOF_SPEC T3.5's timing test

`tests/Conformance/T3_SessionTests.cs::T3_5_…_TakeApproximatelyTheSameTime` makes 12 failed logins against the same
existing username (`khaled`) interleaved with 12 against a **fresh random** missing username each round, and asserts
the medians differ by less than 1.5×. A delay keyed by (username + ip) escalates `khaled` to the cap and leaves each
fresh username at zero: the ratio blows past the bound and the test fails.

The test is not wrong about what it was written for, and the limiter is not wrong either: the delay depends on *what
was typed*, not on whether it exists — repeat a missing username and it escalates identically. The test's two arms
are simply not symmetric in a way §5.2 now makes visible. I will not edit it (CLAUDE.md, PROOF_SPEC rule 3).

- **(a) Restate T3.5's timing arm** so both sides repeat one username — the property it proves is unchanged, and it
  then passes with production numbers. A PROOF_SPEC change: yours, recorded as a new OPEN_ITEMS entry under "for the
  next version of PROOF_SPEC" (54), with the restated wording in the PR.
- **(b) Ship with dev/CI numbers that leave the first ~40 failures free** (§7), so T3.5 passes untouched and the
  mechanism is proven in `[W]` instead. Honest only if it is written down: CI would then not exercise production
  numbers. This is what I will do if you choose nothing else, because it blocks nothing — but (a) is the better
  answer and (a) and (b) together are better still.
- **(c) Key the delay by ip alone.** T3.5 passes untouched with any numbers, because both arms share the ip. It
  contradicts item 16's "(username + IP)", which you decided, so I am not treating it as available without you
  saying so.

### 9.3 Two smaller ones, for a yes or a no

1. **The screens.** §5.1 is an API specification; the forgot/reset/change screens are §15 work and a real amount of
   it (three screens, two languages, e2e). I have them as a follow-up PR, with only the error-code entries added to
   `apiErrors.ts` / `ar.json` here. Say if you want them in this PR instead.
2. **OPEN_ITEMS 45 vs 47.** The brief named 45 (D3′, delivery); D6 — password recovery and change — is 47. I am
   working to 47 and will close 47. Tell me if you meant something else by 45.
