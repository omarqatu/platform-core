# Core.WhiteBoxTests

This project proves the tests marked **[W]** in PROOF_SPEC. When another
implementation is evaluated against the spec, this project is not run; an
equivalent proof is submitted in its place, as §6 describes.

It references `Core` directly. That is a conscious exception to the rule that
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

Since T2 the tests run on the real schema, through Core's EF model, against two tenants of their
own (W1, W2) that the fixture creates as migrator and deletes afterwards (`WhiteBoxFixture`).
