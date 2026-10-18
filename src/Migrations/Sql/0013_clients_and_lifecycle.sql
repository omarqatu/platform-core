-- Amendment 02, stage (b) (Amendment 01 §2.8, §2.9, §4.2, §5.1; Amendment 02 §5.1, §7) — the client's own columns
-- and the subscription's lifecycle. client_notification_settings is stage (c) and is not here; neither is the send
-- window (stage (d)). grants.json does not change: clients and subscriptions already grant app_user the four
-- statements table-wide (§3.8), and the two new permissions are seeded rows in permissions and
-- role_template_permissions, not grants.

-- ===== §4.2 — the two permissions, by the guide's §11 recipe. Each goes to ITS OWN template set: clients.write to
-- owner, admin and operator; clients.manage to owner and admin alone. One INSERT with both codes in an IN list
-- would hand operator the power to disable a client — a role widened in silence. Two statements, deliberately.
INSERT INTO permissions (id, module_id, code, name_ar, name_en)
SELECT uuidv7(), m.id, p.code, p.name_ar, p.name_en
FROM modules m JOIN (VALUES
  ('subscriptions', 'subscriptions.clients.write',  'إنشاء العملاء وتعديلهم', 'Create and edit clients'),
  ('subscriptions', 'subscriptions.clients.manage', 'تعطيل العملاء وإعادة تفعيلهم', 'Disable and re-enable clients')
) AS p (module_code, code, name_ar, name_en) ON p.module_code = m.code;

INSERT INTO role_template_permissions (template_id, permission_id)
SELECT t.id, p.id FROM role_templates t JOIN permissions p ON p.code = 'subscriptions.clients.write'
WHERE t.code IN ('owner', 'admin', 'operator');

INSERT INTO role_template_permissions (template_id, permission_id)
SELECT t.id, p.id FROM role_templates t JOIN permissions p ON p.code = 'subscriptions.clients.manage'
WHERE t.code IN ('owner', 'admin');

-- Existing tenants' system roles, from the templates — one source of truth (0008 did the same for T5).
INSERT INTO role_permissions (id, tenant_id, role_id, permission_id)
SELECT uuidv7(), r.tenant_id, r.id, rtp.permission_id
FROM roles r
JOIN role_templates t ON t.code = r.code AND r.is_system
JOIN role_template_permissions rtp ON rtp.template_id = t.id
JOIN permissions p ON p.id = rtp.permission_id
  AND p.code IN ('subscriptions.clients.write', 'subscriptions.clients.manage');

-- Row-count confirmation (v1.16 §3.4, condition 2): two permissions, and 3 + 2 template grants — counted per code,
-- so a template set that drifted onto the wrong permission fails here rather than at the next review.
DO $$
BEGIN
  IF (SELECT count(*) FROM permissions WHERE code IN ('subscriptions.clients.write', 'subscriptions.clients.manage')) <> 2
     OR (SELECT count(*) FROM role_template_permissions rtp JOIN permissions p ON p.id = rtp.permission_id
         WHERE p.code = 'subscriptions.clients.write') <> 3
     OR (SELECT count(*) FROM role_template_permissions rtp JOIN permissions p ON p.id = rtp.permission_id
         WHERE p.code = 'subscriptions.clients.manage') <> 2 THEN
    RAISE EXCEPTION 'stage (b) permissions: expected 2 permissions with 3 and 2 template grants (got % and %)',
      (SELECT count(*) FROM role_template_permissions rtp JOIN permissions p ON p.id = rtp.permission_id
       WHERE p.code = 'subscriptions.clients.write'),
      (SELECT count(*) FROM role_template_permissions rtp JOIN permissions p ON p.id = rtp.permission_id
       WHERE p.code = 'subscriptions.clients.manage');
  END IF;
END $$;

-- ===== Amendment 01 §2.8 — the client's columns. The three contact channels are optional (a client reachable by
-- one channel is not reachable by three); is_active is NOT NULL on a table that has rows, so it follows §2.1:
-- added nullable, backfilled for every row inside this migration, then NOT NULL and a row-count check. No DEFAULT
-- remains: the application writes every column on every insert (v1.16 §2, Test 28a).
ALTER TABLE clients
  ADD COLUMN email            text    NULL,
  ADD COLUMN phone_e164       text    NULL,
  ADD COLUMN telegram_chat_id text    NULL,
  ADD COLUMN is_active        boolean NULL;

-- The backfill value is true: every client that exists today was created to be reminded, and disabling is an act
-- somebody performs, never a state a row falls into. This PR's decision — the amendment does not make it.
UPDATE clients SET is_active = true WHERE is_active IS NULL;
ALTER TABLE clients ALTER COLUMN is_active SET NOT NULL;
DO $$
BEGIN
  IF (SELECT count(*) FROM clients WHERE is_active) <> (SELECT count(*) FROM clients) THEN
    RAISE EXCEPTION 'clients.is_active backfill: % of % rows', (SELECT count(*) FROM clients WHERE is_active),
      (SELECT count(*) FROM clients);
  END IF;
END $$;

-- Closed shapes as CHECKs (v1.16 §2), in the pattern stage (a) used for currency. phone_e164 is E.164 and nothing
-- else, so one client cannot hold "+962791234567" and another "0791234567" for the same line. A Telegram chat id
-- is a signed integer in text: a group's id is negative, which is why it is not stored unsigned. The e-mail is
-- held by length and by the one shape a sender can use; no normalization and no uniqueness — a client is not an
-- account, and two clients of one tenant may share an address (persons.email_normalized, 0009, is the identity
-- axis and is not this).
ALTER TABLE clients ADD CONSTRAINT clients_email_check CHECK (email ~ '^[^@[:space:]]+@[^@[:space:]]+[.][^@[:space:]]+$' AND length(email) <= 320);
ALTER TABLE clients ADD CONSTRAINT clients_phone_e164_check CHECK (phone_e164 ~ '^\+[1-9][0-9]{1,14}$');
ALTER TABLE clients ADD CONSTRAINT clients_telegram_chat_id_check CHECK (telegram_chat_id ~ '^-?[0-9]{1,20}$');

-- ===== Amendment 01 §2.9 — the subscription's lifecycle. status is NOT NULL on a table with rows, so the same
-- rule again. renewed_from_id is the renewal's link to the row it renews: a renewal is a NEW row pointing at the
-- old one, never an extension of it, and nothing is ever deleted.
ALTER TABLE subscriptions
  ADD COLUMN status          text NULL,
  ADD COLUMN renewed_from_id uuid NULL;

-- The backfill value is 'active'. The amendment does not choose it: it states the three values and leaves the
-- existing rows unaddressed. 'active' is the only one that does not invent history — 'cancelled' would assert an
-- act nobody performed, and 'renewed' a successor row that does not exist. Recorded in this PR's description.
UPDATE subscriptions SET status = 'active' WHERE status IS NULL;
ALTER TABLE subscriptions ALTER COLUMN status SET NOT NULL;
DO $$
BEGIN
  IF (SELECT count(*) FROM subscriptions WHERE status = 'active') <> (SELECT count(*) FROM subscriptions) THEN
    RAISE EXCEPTION 'subscriptions.status backfill: % of % rows', (SELECT count(*) FROM subscriptions WHERE status = 'active'),
      (SELECT count(*) FROM subscriptions);
  END IF;
END $$;

ALTER TABLE subscriptions ADD CONSTRAINT subscriptions_status_check CHECK (status IN ('active', 'cancelled', 'renewed'));

-- §3.3 — the FK between two tenant_id tables is composite. Its target is (tenant_id, id) of subscriptions itself,
-- and that pair carried no UNIQUE: 0008 gave clients clients_tenant_id_id_key for exactly this reason and gave
-- subscriptions none, because nothing referenced a subscription then. It does now, so the key is added here.
ALTER TABLE subscriptions ADD CONSTRAINT subscriptions_tenant_id_id_key UNIQUE (tenant_id, id);
ALTER TABLE subscriptions ADD CONSTRAINT subscriptions_renewed_from_fkey
  FOREIGN KEY (tenant_id, renewed_from_id) REFERENCES subscriptions (tenant_id, id);

-- One subscription is renewed once. Without this a second renewal writes a second successor and the history forks
-- in silence; the code refuses it first (409 subscription_not_active), and this is what holds when the code does
-- not. NULL repeats freely in a UNIQUE, so the unrenewed rows are unaffected. The index it creates starts with
-- tenant_id (§3.2).
ALTER TABLE subscriptions ADD CONSTRAINT subscriptions_renewed_from_id_key UNIQUE (tenant_id, renewed_from_id);

-- ===== Amendment 02 §5.1 — the status gate. The row as it stands must be active for any UPDATE to reach it: a
-- cancelled or renewed subscription is not edited, not revived and not renewed again, and the database is what
-- says so. WITH CHECK is written out: PostgreSQL uses USING as WITH CHECK when the latter is omitted, which would
-- require the NEW row to be active too and refuse the cancellation itself (42501) — proven on PostgreSQL 18.6.
-- The three values are the column's CHECK above; the policy only guards which row may be written at all.
CREATE POLICY subscriptions_status_gate ON subscriptions
  AS RESTRICTIVE FOR UPDATE TO app_user
  USING (status = 'active')
  WITH CHECK (true);

-- The grants are unchanged: app_user's SELECT, INSERT, UPDATE, DELETE on both tables is table-wide (§3.8) and
-- covers the new columns. DELETE stays as v1.16 §3.8 gives it to every module table (Amendment 01 §2.9 says so
-- explicitly); disabling instead of deleting is held by there being no endpoint that deletes, not by the grant.
