-- The platform's e-mail provider row for Development and CI: the local mail catcher (Mailpit, docker-compose).
--
-- It is seeded HERE, before Api starts, and not only by the e2e fixture — because
-- Core.Notifications.PlatformProviderAccounts holds a resolved account for PlatformProviderOptions.CacheFor
-- (5 minutes by default), and it holds a NEGATIVE result the same way. Any send before the row exists — the
-- Conformance forgot tests are the first — caches "no account", and every send for the next five minutes is
-- dropped with a log line, including the e2e's invitation. The cache is deliberate (OPEN_ITEMS 51: the shorter
-- the forgot path's work, the better its constant-time floor holds), so the harness orders itself around it
-- rather than the other way round.
--
-- credential_ref names a settings key and never a secret (Amendment 01 §2.6): Notifications:Email:Providers:
-- local-mailpit in appsettings.{Development,CI}.json holds host and port. Never used in deploy/.
-- provider_accounts has no INSERT grant for any application role, so only migrator can write this.
INSERT INTO provider_accounts (id, tenant_id, channel, provider_code, sender_id, credential_ref, is_active, created_at)
VALUES (gen_random_uuid(), NULL, 'email', 'smtp', 'platform@e2e.test', 'local-mailpit', true, now())
ON CONFLICT (tenant_id, channel) DO UPDATE
  SET provider_code = 'smtp', sender_id = 'platform@e2e.test', credential_ref = 'local-mailpit', is_active = true;
