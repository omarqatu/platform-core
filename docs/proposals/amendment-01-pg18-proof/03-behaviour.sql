\set QUIET 1
\pset tuples_only on
-- seed (as the owner)
INSERT INTO tenants VALUES ('11111111-1111-1111-1111-111111111111','A','active',now()), ('22222222-2222-2222-2222-222222222222','B','active',now());
INSERT INTO users VALUES ('33333333-3333-3333-3333-333333333333','u');
INSERT INTO plans VALUES (gen_random_uuid(),'trial','تجربة','Trial',true);
INSERT INTO subscriptions VALUES ('44444444-4444-4444-4444-444444444444','11111111-1111-1111-1111-111111111111','55555555-5555-5555-5555-555555555555');
INSERT INTO notification_log VALUES ('66666666-6666-6666-6666-666666666661','11111111-1111-1111-1111-111111111111','44444444-4444-4444-4444-444444444444','55555555-5555-5555-5555-555555555555','email','2026-11-01',NULL,'pending',now(),NULL,NULL),
                                    ('66666666-6666-6666-6666-666666666662','11111111-1111-1111-1111-111111111111','44444444-4444-4444-4444-444444444444','55555555-5555-5555-5555-555555555555','sms','2026-11-01',NULL,'sent',now(),now(),NULL);

CREATE FUNCTION t(label text, sql text, setup text DEFAULT '') RETURNS text LANGUAGE plpgsql AS $f$
DECLARE n int; BEGIN
  BEGIN
    IF setup <> '' THEN EXECUTE setup; END IF;
    EXECUTE sql; GET DIAGNOSTICS n = ROW_COUNT;
    RAISE EXCEPTION 'rollback:%', n;
  EXCEPTION WHEN OTHERS THEN
    IF SQLERRM LIKE 'rollback:%' THEN RETURN label || ' → ' || substr(SQLERRM, 10) || ' row(s)'; END IF;
    RETURN label || ' → ERROR ' || SQLSTATE || ': ' || SQLERRM;
  END;
END $f$;
GRANT EXECUTE ON FUNCTION t TO PUBLIC;

\echo '== 1. provider_accounts — UNIQUE NULLS NOT DISTINCT (as owner)'
SELECT t('two platform rows, same channel', $$INSERT INTO provider_accounts VALUES (gen_random_uuid(),NULL,'email','x','s','R',true,now()),(gen_random_uuid(),NULL,'email','y','s','R',true,now())$$);
SELECT t('one platform row + one tenant row, same channel', $$INSERT INTO provider_accounts VALUES (gen_random_uuid(),NULL,'email','x','s','R',true,now()),(gen_random_uuid(),'11111111-1111-1111-1111-111111111111','email','y','s','R',true,now())$$);
SELECT t('two rows, same tenant, same channel', $$INSERT INTO provider_accounts VALUES (gen_random_uuid(),'11111111-1111-1111-1111-111111111111','email','x','s','R',true,now()),(gen_random_uuid(),'11111111-1111-1111-1111-111111111111','email','y','s','R',true,now())$$);
INSERT INTO provider_accounts VALUES (gen_random_uuid(),NULL,'email','x','s','R',true,now()),(gen_random_uuid(),'22222222-2222-2222-2222-222222222222','sms','y','s','R',true,now());
SET ROLE app_user;
SELECT t('app_user, tenant A: provider_accounts visible', $$SELECT * FROM provider_accounts$$, $$SET LOCAL app.tenant_id='11111111-1111-1111-1111-111111111111'$$);
SELECT t('app_user, tenant B: provider_accounts visible', $$SELECT * FROM provider_accounts$$, $$SET LOCAL app.tenant_id='22222222-2222-2222-2222-222222222222'$$);
SELECT t('app_user, no tenant: provider_accounts visible', $$SELECT * FROM provider_accounts$$);
RESET ROLE;

\echo '== 2. notification_log — AS RESTRICTIVE FOR UPDATE (app_user, tenant A, scope_all)'
SET ROLE app_user;
SELECT t('pending → sent', $$UPDATE notification_log SET status='sent', sent_at=now() WHERE id='66666666-6666-6666-6666-666666666661'$$, $$SET LOCAL app.tenant_id='11111111-1111-1111-1111-111111111111'; SET LOCAL app.scope_all='true'$$);
SELECT t('pending → failed', $$UPDATE notification_log SET status='failed', error='e' WHERE id='66666666-6666-6666-6666-666666666661'$$, $$SET LOCAL app.tenant_id='11111111-1111-1111-1111-111111111111'; SET LOCAL app.scope_all='true'$$);
SELECT t('pending → pending', $$UPDATE notification_log SET status='pending' WHERE id='66666666-6666-6666-6666-666666666661'$$, $$SET LOCAL app.tenant_id='11111111-1111-1111-1111-111111111111'; SET LOCAL app.scope_all='true'$$);
SELECT t('sent → failed (moving back)', $$UPDATE notification_log SET status='failed' WHERE id='66666666-6666-6666-6666-666666666662'$$, $$SET LOCAL app.tenant_id='11111111-1111-1111-1111-111111111111'; SET LOCAL app.scope_all='true'$$);
SELECT t('reading still sees both rows (restrictive is FOR UPDATE only)', $$SELECT * FROM notification_log$$, $$SET LOCAL app.tenant_id='11111111-1111-1111-1111-111111111111'; SET LOCAL app.scope_all='true'$$);
SELECT t('pending → sent, tenant B context', $$UPDATE notification_log SET status='sent' WHERE id='66666666-6666-6666-6666-666666666661'$$, $$SET LOCAL app.tenant_id='22222222-2222-2222-2222-222222222222'; SET LOCAL app.scope_all='true'$$);
SELECT t('pending → sent, assigned member with no assignment', $$UPDATE notification_log SET status='sent' WHERE id='66666666-6666-6666-6666-666666666661'$$, $$SET LOCAL app.tenant_id='11111111-1111-1111-1111-111111111111'; SET LOCAL app.scope_all='false'; SET LOCAL app.membership_id='77777777-7777-7777-7777-777777777777'$$);
SELECT t('insert pending row, scope_all', $$INSERT INTO notification_log VALUES (gen_random_uuid(),'11111111-1111-1111-1111-111111111111','44444444-4444-4444-4444-444444444444','55555555-5555-5555-5555-555555555555','whatsapp','2026-11-01',NULL,'pending',now(),NULL,NULL)$$, $$SET LOCAL app.tenant_id='11111111-1111-1111-1111-111111111111'; SET LOCAL app.scope_all='true'$$);
SELECT t('DELETE (no grant)', $$DELETE FROM notification_log$$, $$SET LOCAL app.tenant_id='11111111-1111-1111-1111-111111111111'; SET LOCAL app.scope_all='true'$$);
RESET ROLE;

\echo '== 3. tenants_provisioner_update / _select (provisioner, app.tenant_id = A)'
SET ROLE provisioner;
SELECT t('suspend A (UPDATE … WHERE id = A)', $$UPDATE tenants SET status='suspended' WHERE id='11111111-1111-1111-1111-111111111111'$$, $$SET LOCAL app.tenant_id='11111111-1111-1111-1111-111111111111'$$);
SELECT t('suspend B while in A', $$UPDATE tenants SET status='suspended' WHERE id='22222222-2222-2222-2222-222222222222'$$, $$SET LOCAL app.tenant_id='11111111-1111-1111-1111-111111111111'$$);
SELECT t('UPDATE with no WHERE, in A', $$UPDATE tenants SET status='suspended'$$, $$SET LOCAL app.tenant_id='11111111-1111-1111-1111-111111111111'$$);
SELECT t('suspend with no tenant context', $$UPDATE tenants SET status='suspended'$$);
SELECT t('change id (no column grant)', $$UPDATE tenants SET id='22222222-2222-2222-2222-222222222222' WHERE id='11111111-1111-1111-1111-111111111111'$$, $$SET LOCAL app.tenant_id='11111111-1111-1111-1111-111111111111'$$);
SELECT t('change name (no column grant)', $$UPDATE tenants SET name='Z' WHERE id='11111111-1111-1111-1111-111111111111'$$, $$SET LOCAL app.tenant_id='11111111-1111-1111-1111-111111111111'$$);
SELECT t('read tenants, in A', $$SELECT * FROM tenants$$, $$SET LOCAL app.tenant_id='11111111-1111-1111-1111-111111111111'$$);
RESET ROLE;
UPDATE tenants SET status='suspended' WHERE id='11111111-1111-1111-1111-111111111111';
SET ROLE provisioner;
SELECT t('restore A: suspended → active (mark-paid)', $$UPDATE tenants SET status='active' WHERE id='11111111-1111-1111-1111-111111111111'$$, $$SET LOCAL app.tenant_id='11111111-1111-1111-1111-111111111111'$$);
RESET ROLE;
SET ROLE job_runner;
SELECT t('job_runner: list active tenants (A suspended)', $$SELECT * FROM tenants$$);
SELECT t('job_runner: UPDATE tenants (no grant)', $$UPDATE tenants SET status='suspended'$$);
RESET ROLE;

\echo '== 4. the rest'
SET ROLE provisioner;
SELECT t('invitation, invited_by NULL', $$INSERT INTO invitations VALUES (gen_random_uuid(),'11111111-1111-1111-1111-111111111111','o@x','pending',NULL,now())$$, $$SET LOCAL app.tenant_id='11111111-1111-1111-1111-111111111111'$$);
SELECT t('invitation, invited_by forged', $$INSERT INTO invitations VALUES (gen_random_uuid(),'11111111-1111-1111-1111-111111111111','o@x','pending','33333333-3333-3333-3333-333333333333',now())$$, $$SET LOCAL app.tenant_id='11111111-1111-1111-1111-111111111111'$$);
SELECT t('invitation, other tenant', $$INSERT INTO invitations VALUES (gen_random_uuid(),'22222222-2222-2222-2222-222222222222','o@x','pending',NULL,now())$$, $$SET LOCAL app.tenant_id='11111111-1111-1111-1111-111111111111'$$);
SELECT t('platform_subscriptions insert + update (A)', $$WITH i AS (INSERT INTO platform_subscriptions VALUES ('88888888-8888-8888-8888-888888888888','11111111-1111-1111-1111-111111111111','trial','active','2026-10-17','2026-11-16',7,now(),now())) SELECT 1$$, $$SET LOCAL app.tenant_id='11111111-1111-1111-1111-111111111111'$$);
SELECT t('notification settings insert (A)', $$INSERT INTO tenant_notification_settings VALUES (gen_random_uuid(),'11111111-1111-1111-1111-111111111111','email',true,'{30,7,1}',NULL,now(),now())$$, $$SET LOCAL app.tenant_id='11111111-1111-1111-1111-111111111111'$$);
SELECT t('plans read', $$SELECT * FROM plans$$);
RESET ROLE;
SET ROLE authenticator;
INSERT INTO password_reset_tokens VALUES ('99999999-9999-9999-9999-999999999999','33333333-3333-3333-3333-333333333333','h',now(),now()+interval '30 min',NULL,NULL);
SELECT t('consume token (used_at NULL → now)', $$UPDATE password_reset_tokens SET used_at=now() WHERE token_hash='h'$$);
UPDATE password_reset_tokens SET used_at=now() WHERE token_hash='h';
SELECT t('consume again', $$UPDATE password_reset_tokens SET used_at=now() WHERE token_hash='h'$$);
SELECT t('un-consume (used_at → NULL)', $$UPDATE password_reset_tokens SET used_at=NULL WHERE token_hash='h'$$);
RESET ROLE;
SET ROLE app_user;
SELECT t('app_user reads platform_subscriptions, A', $$SELECT * FROM platform_subscriptions$$, $$SET LOCAL app.tenant_id='11111111-1111-1111-1111-111111111111'$$);
SELECT t('app_user reads platform_subscriptions, B', $$SELECT * FROM platform_subscriptions$$, $$SET LOCAL app.tenant_id='22222222-2222-2222-2222-222222222222'$$);
SELECT t('app_user reads platform_subscriptions, reused-connection empty string', $$SELECT * FROM platform_subscriptions$$, $$SET LOCAL app.tenant_id=''$$);
RESET ROLE;

\echo '== 5. Check 2 NULLIF rule on the deparsed text of every policy'
SELECT tablename||'.'||policyname||': '||(regexp_count(coalesce(qual,'')||coalesce(with_check,''),'current_setting\(') = regexp_count(coalesce(qual,'')||coalesce(with_check,''),'NULLIF\(current_setting\('))::text FROM pg_policies WHERE schemaname='public' AND (qual||coalesce(with_check,'')) LIKE '%current_setting%' ORDER BY 1;
