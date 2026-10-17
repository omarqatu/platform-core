\set QUIET 1
\pset tuples_only on
INSERT INTO platform_subscriptions VALUES ('88888888-8888-8888-8888-888888888888','11111111-1111-1111-1111-111111111111','trial','active','2026-10-17','2026-11-16',7,now(),now());
SET ROLE app_user;
SELECT t('app_user reads platform_subscriptions, A', $$SELECT * FROM platform_subscriptions$$, $$SET LOCAL app.tenant_id='11111111-1111-1111-1111-111111111111'$$);
SELECT t('app_user reads platform_subscriptions, B', $$SELECT * FROM platform_subscriptions$$, $$SET LOCAL app.tenant_id='22222222-2222-2222-2222-222222222222'$$);
SELECT t('app_user reads platform_subscriptions, empty string (reused connection)', $$SELECT * FROM platform_subscriptions$$, $$SET LOCAL app.tenant_id=''$$);
SELECT t('app_user UPDATE platform_subscriptions (no grant)', $$UPDATE platform_subscriptions SET paid_until='2030-01-01'$$, $$SET LOCAL app.tenant_id='11111111-1111-1111-1111-111111111111'$$);
RESET ROLE;
SET ROLE provisioner;
SELECT t('provisioner suspends A''s subscription (in A)', $$UPDATE platform_subscriptions SET status='suspended' WHERE tenant_id='11111111-1111-1111-1111-111111111111'$$, $$SET LOCAL app.tenant_id='11111111-1111-1111-1111-111111111111'$$);
SELECT t('provisioner updates A''s subscription while in B', $$UPDATE platform_subscriptions SET status='suspended'$$, $$SET LOCAL app.tenant_id='22222222-2222-2222-2222-222222222222'$$);
SELECT t('mark-paid: paid_until + status (in A)', $$UPDATE platform_subscriptions SET paid_until='2027-01-01', status='active' WHERE tenant_id='11111111-1111-1111-1111-111111111111'$$, $$SET LOCAL app.tenant_id='11111111-1111-1111-1111-111111111111'$$);
SELECT t('suspend-unpaid predicate, UTC current_date', $$UPDATE platform_subscriptions SET status='suspended' WHERE paid_until + grace_days < (now() AT TIME ZONE 'UTC')::date$$, $$SET LOCAL app.tenant_id='11111111-1111-1111-1111-111111111111'$$);
RESET ROLE;
-- WITH CHECK on id: give provisioner UPDATE (id) for this one test only, inside the rolled-back call
SELECT t('tenants_provisioner_update WITH CHECK: move A''s id to B (UPDATE(id) granted for the test)',
  $$UPDATE tenants SET id='33333333-0000-0000-0000-000000000000' WHERE id='11111111-1111-1111-1111-111111111111'$$,
  $$GRANT UPDATE (id) ON tenants TO provisioner; SET LOCAL ROLE provisioner; SET LOCAL app.tenant_id='11111111-1111-1111-1111-111111111111'$$);
\echo '== NULLIF rule, every policy that reads a context variable'
SELECT tablename||'.'||policyname||': '||(regexp_count(coalesce(qual,'')||' '||coalesce(with_check,''),'current_setting\(') = regexp_count(coalesce(qual,'')||' '||coalesce(with_check,''),'NULLIF\(current_setting\('))::text
FROM pg_policies WHERE schemaname='public' AND (coalesce(qual,'')||coalesce(with_check,'')) LIKE '%current_setting%' ORDER BY 1;
