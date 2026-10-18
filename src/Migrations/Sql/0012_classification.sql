-- Amendment 02, stage (a) (PLATFORM_CORE Amendment 02 §2, §3.1, §3.2, §5, §5.1, §7) — the classification and the
-- subscription's own fields. The catalog is the platform's: a tenant adding its own categories is deferred (§3.1,
-- OPEN_ITEMS 69), and client_notification_settings is stage (c), so neither is here. No new permission: reading the
-- catalog is subscriptions.read, and the new columns are written under subscriptions.write (§5.2).

-- ===== §3.1 — categories and subcategories: a global catalog with no tenant_id, in the pattern of modules and
-- permissions (v1.16 §5) and plans (Amendment 01 §2.3). No tenant_id, so §3.2 (an index starts with tenant_id) and
-- §3.3 (the composite FK) do not apply, and the FK subscriptions → categories below is single-column — Check 7 looks
-- only at links between two tables that both carry tenant_id (OPEN_ITEMS 67).
CREATE TABLE categories (
  id         app_id  PRIMARY KEY,
  code       text    NOT NULL,
  name_ar    text    NOT NULL,
  name_en    text    NOT NULL,
  is_active  boolean NOT NULL,
  created_at app_ts  NOT NULL,
  CONSTRAINT categories_code_key UNIQUE (code)
);

-- The subcategory is optional on a subscription (a domain needs no detail; servers do) — §3.1.
CREATE TABLE subcategories (
  id          app_id  PRIMARY KEY,
  category_id uuid    NOT NULL,
  code        text    NOT NULL,
  name_ar     text    NOT NULL,
  name_en     text    NOT NULL,
  is_active   boolean NOT NULL,
  created_at  app_ts  NOT NULL,
  CONSTRAINT subcategories_category_fkey FOREIGN KEY (category_id) REFERENCES categories (id),
  CONSTRAINT subcategories_category_id_code_key UNIQUE (category_id, code)
);

-- The platform's seven categories (§3.1, the decision of 2026-10-17), seeded by migrator. The names are this PR's.
-- Inserted in the decision's order, which is the order the interface lists them in (by id: uuidv7() is monotonic
-- within one session on PostgreSQL 18), so `other` comes last. No subcategory is seeded.
INSERT INTO categories (id, code, name_ar, name_en, is_active, created_at) VALUES
  (uuidv7(), 'domains',  'النطاقات',          'Domains',  true, now()),
  (uuidv7(), 'hosting',  'الاستضافة',         'Hosting',  true, now()),
  (uuidv7(), 'security', 'الحماية',           'Security', true, now()),
  (uuidv7(), 'software', 'البرمجيات',         'Software', true, now()),
  (uuidv7(), 'backup',   'النسخ الاحتياطي',   'Backup',   true, now()),
  (uuidv7(), 'ai',       'الذكاء الاصطناعي',  'AI',       true, now()),
  (uuidv7(), 'other',    'أخرى',              'Other',    true, now());

-- Row-count confirmation (v1.16 §3.4, condition 2): the seven, in their order, and no subcategory.
DO $$
BEGIN
  IF (SELECT array_agg(code ORDER BY id) FROM categories)
       IS DISTINCT FROM ARRAY['domains', 'hosting', 'security', 'software', 'backup', 'ai', 'other']
     OR (SELECT count(*) FROM subcategories) <> 0 THEN
    RAISE EXCEPTION 'categories: expected the seven of Amendment 02 §3.1 in order and no subcategory (got %, % subcategories)',
      (SELECT array_agg(code ORDER BY id) FROM categories), (SELECT count(*) FROM subcategories);
  END IF;
END $$;

ALTER TABLE categories ENABLE ROW LEVEL SECURITY;
ALTER TABLE categories FORCE ROW LEVEL SECURITY;
ALTER TABLE subcategories ENABLE ROW LEVEL SECURITY;
ALTER TABLE subcategories FORCE ROW LEVEL SECURITY;

-- ===== §5.1 — the two read policies, verbatim. They enter the manifest through
-- checks/core/amendment-policies-built.json ("02"). They name job_runner, and the grants below do not grant it:
-- that is deliberate, not a transcription error (OPEN_ITEMS 76). The policy permits and the grant withholds, and
-- the grant is the guard.
CREATE POLICY categories_read ON categories
  FOR SELECT TO app_user, provisioner, job_runner
  USING (true);   -- a global catalog with no tenant_id, in the same text as plans_read (Amendment 01 §4.1)

CREATE POLICY subcategories_read ON subcategories
  FOR SELECT TO app_user, provisioner, job_runner
  USING (true);

-- ===== §5 — read by app_user and provisioner, written by none: the rows are migrator's, seeding. §5 also grants
-- job_runner SELECT; that row is deferred by the project owner's decision of 2026-10-17 until a job first needs the
-- catalog (stage (d)), because no job reads it before then and v1.16 §8 / PROOF_SPEC T7.4 hold job_runner to the
-- active tenants alone. grants.json lags §5 by that one role, on purpose (OPEN_ITEMS 76).
GRANT SELECT ON categories TO app_user, provisioner;
GRANT SELECT ON subcategories TO app_user, provisioner;

-- ===== §3.2 (and §2) — the subscription's columns. The four NOT NULL ones land on a table that may have rows, so
-- each follows Amendment 01 §2.1: added nullable, backfilled for every row inside this migration, then NOT NULL and
-- a row-count check. No DEFAULT is left on any of them: the application writes every column on every insert
-- (v1.16 §2, Test 28a). The backfilled values are this PR's decision, recorded in its description.
ALTER TABLE subscriptions
  ADD COLUMN reminder_type  text          NULL,
  ADD COLUMN category_id    uuid          NULL,
  ADD COLUMN subcategory_id uuid          NULL,
  ADD COLUMN provider       text          NULL,
  ADD COLUMN cost           numeric(14,3) NULL,
  ADD COLUMN currency       text          NULL,
  ADD COLUMN billing_cycle  text          NULL,
  ADD COLUMN auto_renew     boolean       NULL,
  ADD COLUMN login_url      text          NULL;

-- 1. reminder_type — its one value (§2).
UPDATE subscriptions SET reminder_type = 'subscription' WHERE reminder_type IS NULL;
ALTER TABLE subscriptions ALTER COLUMN reminder_type SET NOT NULL;
DO $$
BEGIN
  IF (SELECT count(*) FROM subscriptions WHERE reminder_type = 'subscription') <> (SELECT count(*) FROM subscriptions) THEN
    RAISE EXCEPTION 'reminder_type backfill: % of % rows', (SELECT count(*) FROM subscriptions WHERE reminder_type = 'subscription'),
      (SELECT count(*) FROM subscriptions);
  END IF;
END $$;

-- 2. category_id — `other`, the category that exists so that nothing has to be invented (§3.1).
UPDATE subscriptions SET category_id = (SELECT id FROM categories WHERE code = 'other') WHERE category_id IS NULL;
ALTER TABLE subscriptions ALTER COLUMN category_id SET NOT NULL;
DO $$
BEGIN
  IF (SELECT count(*) FROM subscriptions s JOIN categories c ON c.id = s.category_id AND c.code = 'other')
     <> (SELECT count(*) FROM subscriptions) THEN
    RAISE EXCEPTION 'category_id backfill: % of % rows', (SELECT count(*) FROM subscriptions s JOIN categories c
      ON c.id = s.category_id AND c.code = 'other'), (SELECT count(*) FROM subscriptions);
  END IF;
END $$;

-- 3. billing_cycle — `yearly`.
UPDATE subscriptions SET billing_cycle = 'yearly' WHERE billing_cycle IS NULL;
ALTER TABLE subscriptions ALTER COLUMN billing_cycle SET NOT NULL;
DO $$
BEGIN
  IF (SELECT count(*) FROM subscriptions WHERE billing_cycle = 'yearly') <> (SELECT count(*) FROM subscriptions) THEN
    RAISE EXCEPTION 'billing_cycle backfill: % of % rows', (SELECT count(*) FROM subscriptions WHERE billing_cycle = 'yearly'),
      (SELECT count(*) FROM subscriptions);
  END IF;
END $$;

-- 4. auto_renew — false: the reminder then says "renew", never "you will be charged" about a charge nobody stated.
UPDATE subscriptions SET auto_renew = false WHERE auto_renew IS NULL;
ALTER TABLE subscriptions ALTER COLUMN auto_renew SET NOT NULL;
DO $$
BEGIN
  IF (SELECT count(*) FROM subscriptions WHERE auto_renew = false) <> (SELECT count(*) FROM subscriptions) THEN
    RAISE EXCEPTION 'auto_renew backfill: % of % rows', (SELECT count(*) FROM subscriptions WHERE auto_renew = false),
      (SELECT count(*) FROM subscriptions);
  END IF;
END $$;

-- The constraints layer. Closed catalogs as CHECKs (v1.16 §2): reminder_type has one value (§2); billing_cycle is
-- monthly or yearly and nothing else — no free text, so `Monthly` and `monthly` cannot both exist (§1, §3.2).
-- cost accepts zero (E5) and three decimals (the Jordanian dinar's fils); currency is an ISO 4217 code.
ALTER TABLE subscriptions ADD CONSTRAINT subscriptions_reminder_type_check CHECK (reminder_type IN ('subscription'));
ALTER TABLE subscriptions ADD CONSTRAINT subscriptions_billing_cycle_check CHECK (billing_cycle IN ('monthly', 'yearly'));
ALTER TABLE subscriptions ADD CONSTRAINT subscriptions_cost_check CHECK (cost >= 0);
ALTER TABLE subscriptions ADD CONSTRAINT subscriptions_currency_check CHECK (currency ~ '^[A-Z]{3}$');
ALTER TABLE subscriptions ADD CONSTRAINT subscriptions_category_fkey FOREIGN KEY (category_id) REFERENCES categories (id);
ALTER TABLE subscriptions ADD CONSTRAINT subscriptions_subcategory_fkey FOREIGN KEY (subcategory_id) REFERENCES subcategories (id);

-- The grants of subscriptions are unchanged: app_user's SELECT, INSERT, UPDATE, DELETE is table-wide (§3.8), so it
-- covers the new columns, and its policies are the two templates as before.
