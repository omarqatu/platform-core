# Amendment 01 §4.1 — proven on PostgreSQL 18

The first governing SQL written under Amendment 01 — the 17 policies of §4.1 — run on a real engine before anything
is built on them (CLAUDE.md: prove engine assumptions on real PostgreSQL 18). On 2026-10-17, on the `postgres:18`
image (PostgreSQL 18.6), `run.sh` created stand-in tables carrying only the columns the policies read, with the
amendment's grants (`01-setup.sql`), then fed the 17 `CREATE POLICY` statements verbatim from
`docs/PLATFORM_CORE_AMENDMENT_01_EN.md` (all were created with no error), then ran each path both ways as the role
that takes it (`03-behaviour.sql`, `04-behaviour-more.sql`; every statement inside a rolled-back call that reports the
rows affected or the error). What they prove: `UNIQUE NULLS NOT DISTINCT` refuses a second platform row per channel
and allows a tenant row beside it; `notification_log_status_forward` (`AS RESTRICTIVE FOR UPDATE`) allows pending →
sent|failed only — 42501 on WITH CHECK, 0 rows on USING — and leaves reading alone; `tenants_provisioner_update`
confines `provisioner` to the tenant in `app.tenant_id` (USING), refuses moving the id (WITH CHECK), and an `UPDATE`
of `status` passes the SELECT policy's re-check of the new row; `invitations_provisioner_insert` admits only
`invited_by IS NULL`; a reset token is consumed once; `platform_subscriptions` is read per tenant and written only by
`provisioner`; `job_runner` reads active tenants and writes nothing; and the NULLIF rule holds on the deparsed text.
`result-2026-10-17.txt` is the recorded output. The run also showed that `current_date` follows the session
`TimeZone`, so the amendment now states `(now() AT TIME ZONE 'UTC')::date` for `suspend-unpaid` (§2.4, §5.4).
