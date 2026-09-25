# Core.WhiteBoxTests

This project proves the tests marked **[W]** in PROOF_SPEC. When another
implementation is evaluated against the spec, this project is not run; an
equivalent proof is submitted in its place, as §6 describes.

It references `Core` directly, and since T3 `Api` (hosted in-process for T3.6). That is a conscious exception
`Conformance` stays independent of `Core` (PROOF_SPEC §0 and §6), and it applies
to this project only. `Conformance` still reaches the system through SQL and HTTP
alone, and it still holds every [B] test.

| Test | Criterion |
|---|---|
| `T1_1_*` | T1.1: a command with no transaction → an explicit exception, not zero rows (Test 5) |
| `T1_4_*` | T1.4: a critical update to an invisible row → a rows-affected error (Test 14) |
| `T1_5_*` | T1.5: a batch update of twenty rows via EF → no phantom exceptions |
| `UnitOfWork_*`, `Middleware_*`, `Pool_*` | The T1 build items: every request → one transaction; `SET LOCAL` order; state reset on return to the pool; T1.3 and Test 27 repeated through Core's own layer |
| `Test23b_*` | Test 23-b (PLATFORM_CORE v1.14 §3.7): editing a system role through the layer above → an explicit error from the rows-affected guard, while the database below stays silent |
| `Test28a_*`, `Test28b_*`, `Test28c_*` | Test 28 [W] (v1.14 §3.7), an acceptance criterion of T2 under rule 8: (a) no DEFAULT, keys `app_id`, NOT NULL timestamps `app_ts`, each query seen failing on a planted table; (b) every EF property `ValueGenerated.Never`; (c) a forgotten key or timestamp → `23514`, nothing saved, and no `RETURNING` for any of the 19 entity types |
| `Test26_*` | T3.1 — Test 26 (v1.15 §3.7): a single join before `app.membership_id` → zero rows and Rule 7 throws; the order a-b-c → succeeds with each member's values; the guard: exactly three reads, each followed by its `SET LOCAL` — step c's two (v1.16) — 7 commands in order; a membership with no scope row → the resolver throws |
| `T3_6_*` | T3.6: the session cookie, decrypted with the Api's own ticket format, carries `user_id` (after login) and `user_id` + `tenant_id` (after selection) — no scope, no permission; HTTP-only, Secure, SameSite=Strict. Api hosted in-process |
| `Layer1_*`, `Layer2_*` | Decision 36 (amended): scope administration in two layers, each alone — the application refuses without `core.scope.manage` before any command reaches the database; with that check bypassed, the database refuses on its own (an update → zero rows → the rows-affected guard; an insert → `42501`) |
| `RequireHttpsFalse_*`, `AllowedCombinations_*` | Decision 33 (amended): Api refuses to start with `Session:RequireHttps=false` outside an environment named Development or CI; starts otherwise |
| `UnitOfWork_UserWithoutTenant_*` | The tenant-selection path through the unit of work: `app.user_id` alone, no second-axis variable, own memberships only |
| `T4_AutomaticAuditTests` | T4.15 (v1.16 §7): every tracked write audited in its own transaction (read back before commit, rolled back with it, no audit of the audit); a write with no tenant to audit under → loud before any command; login's `last_login_at` exempt (no error, no entry); `user_password_credentials` never audited; `token_hash` kept, valued `"[masked]"` |

Since T2 the tests run on the real schema, through Core's EF model, against two tenants of their
own (W1, W2) that the fixture creates as migrator and deletes afterwards (`WhiteBoxFixture`). Since T3
each has one real member (`W1User`, `W2User`, scope `all`): the unit of work resolves the second axis on
every transaction with a user and a tenant, so the T1 tests that used a random user now use the member
(decided by the project owner in T3). The T3 tests also read the seed contract.
