-- T2, layer 1 — the core tables (PLATFORM_CORE v1.14 §4.1, 4.2, 4.5, 4.8, 5, 7; 3.9).
-- Keys and timestamps (§2): no DEFAULT, identity or generated column anywhere in public;
-- app_id for every column in a primary key (even when it is also a foreign key);
-- app_ts for every timestamptz NOT NULL; NOT NULL on the column, not in the domain.
-- Unique constraints and foreign keys are layer 4 (0004_constraints.sql).

CREATE DOMAIN app_id AS uuid        CHECK (VALUE <> '00000000-0000-0000-0000-000000000000');
CREATE DOMAIN app_ts AS timestamptz CHECK (VALUE >= '2000-01-01');

-- §4.1 (tenants: owner decision recorded in v1.13)
CREATE TABLE tenants (
  id         app_id      PRIMARY KEY,
  name       text        NOT NULL,
  status     text        NOT NULL CHECK (status IN ('active', 'suspended')),
  created_at app_ts      NOT NULL
);

CREATE TABLE persons (
  id         app_id PRIMARY KEY,
  full_name  text   NOT NULL,
  email      text   NOT NULL,
  phone_e164 text,
  created_at app_ts NOT NULL
);

CREATE TABLE users (
  id            app_id PRIMARY KEY,
  person_id     uuid   NOT NULL,
  user_type     text   NOT NULL CHECK (user_type IN ('employee', 'customer')),
  username      text   NOT NULL,
  status        text   NOT NULL,
  language      text,
  theme         text,
  last_login_at timestamptz
);

CREATE TABLE user_password_credentials (
  user_id       app_id PRIMARY KEY,
  password_hash text   NOT NULL,
  updated_at    app_ts NOT NULL
);

CREATE TABLE memberships (
  id         app_id PRIMARY KEY,
  tenant_id  uuid   NOT NULL,
  user_id    uuid   NOT NULL,
  status     text   NOT NULL,
  created_at app_ts NOT NULL
);

CREATE TABLE membership_roles (
  id            app_id PRIMARY KEY,
  tenant_id     uuid   NOT NULL,
  membership_id uuid   NOT NULL,
  role_id       uuid   NOT NULL
);

-- §4.1, 4.5; intended_scope_mode per v1.13 (behavior in T4)
CREATE TABLE invitations (
  id                  app_id PRIMARY KEY,
  tenant_id           uuid   NOT NULL,
  email               text   NOT NULL,
  role_id             uuid   NOT NULL,
  token_hash          text   NOT NULL,
  status              text   NOT NULL CHECK (status IN ('pending', 'accepted', 'revoked', 'expired')),
  invited_by          uuid   NOT NULL,
  expires_at          app_ts NOT NULL,
  created_at          app_ts NOT NULL,
  intended_scope_mode text   NOT NULL CHECK (intended_scope_mode IN ('all', 'assigned'))
);

CREATE TABLE auth_attempts (
  id               app_id  PRIMARY KEY,
  username_entered text    NOT NULL,
  ip_address       text,
  succeeded        boolean NOT NULL,
  created_at       app_ts  NOT NULL
);

-- §4.8
CREATE TABLE membership_scope (
  id            app_id PRIMARY KEY,
  tenant_id     uuid   NOT NULL,
  membership_id uuid   NOT NULL,
  scope_mode    text   NOT NULL CHECK (scope_mode IN ('all', 'assigned')),
  updated_by    uuid,
  updated_at    app_ts NOT NULL
);

CREATE TABLE scope_assignments (
  id              app_id  PRIMARY KEY,
  tenant_id       uuid    NOT NULL,
  membership_id   uuid    NOT NULL,
  scope_ref_id    uuid    NOT NULL,  -- the scoped entity — no FK yet (§3.3, 9)
  assignment_role text    NOT NULL CHECK (assignment_role IN ('lead', 'contributor', 'reviewer')),
  active          boolean NOT NULL,
  granted_by      uuid    NOT NULL,
  granted_at      app_ts  NOT NULL,
  reason          text
);

-- §4.2
CREATE TABLE membership_auth (
  id              app_id PRIMARY KEY,
  tenant_id       uuid   NOT NULL,
  membership_id   uuid   NOT NULL,
  provider        text   NOT NULL,
  provider_config jsonb
);

-- §5: the global catalogs
CREATE TABLE modules (
  id            app_id  PRIMARY KEY,
  code          text    NOT NULL,
  name_ar       text    NOT NULL,
  name_en       text    NOT NULL,
  icon          text,
  display_order integer NOT NULL,
  is_active     boolean NOT NULL
);

CREATE TABLE permissions (
  id        app_id PRIMARY KEY,
  module_id uuid   NOT NULL,
  code      text   NOT NULL,
  name_ar   text   NOT NULL,
  name_en   text   NOT NULL
);

CREATE TABLE tenant_modules (
  id           app_id  PRIMARY KEY,
  tenant_id    uuid    NOT NULL,
  module_id    uuid    NOT NULL,
  is_active    boolean NOT NULL,
  activated_at timestamptz
);

CREATE TABLE roles (
  id        app_id  PRIMARY KEY,
  tenant_id uuid    NOT NULL,
  code      text    NOT NULL,
  name_ar   text    NOT NULL,
  name_en   text    NOT NULL,
  is_system boolean NOT NULL,
  is_active boolean NOT NULL
);

CREATE TABLE role_permissions (
  id            app_id PRIMARY KEY,
  tenant_id     uuid   NOT NULL,
  role_id       uuid   NOT NULL,
  permission_id uuid   NOT NULL
);

-- §5 (1.13): the role templates bootstrap copies — seeded by migrator, read by provisioner only
CREATE TABLE role_templates (
  id      app_id PRIMARY KEY,
  code    text   NOT NULL,
  name_ar text   NOT NULL,
  name_en text   NOT NULL
);

CREATE TABLE role_template_permissions (
  template_id   app_id NOT NULL,
  permission_id app_id NOT NULL,
  PRIMARY KEY (template_id, permission_id)
);

-- §7
CREATE TABLE audit_log (
  id          app_id PRIMARY KEY,
  tenant_id   uuid   NOT NULL,
  actor_id    uuid,
  actor_type  text   NOT NULL,
  action      text   NOT NULL,
  entity_type text   NOT NULL,
  entity_id   uuid,
  old_value   jsonb,
  new_value   jsonb,
  ip_address  text,
  created_at  app_ts NOT NULL
);
