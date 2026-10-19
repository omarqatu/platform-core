-- GENERATED FILE — do not edit. Regenerate with scripts/dump-schema.sh after a migration.
-- The schema of a database migrated from clean (src/Migrations/Sql, applied in order), as pg_dump prints it.
-- Schema only: no rows. Roles and database-level grants are not part of a database dump (docs/DATABASE.md).
-- CI fails while this file differs from a fresh dump (checks/local/check-schema-dump.sh).

--
-- PostgreSQL database dump
--

\restrict platformcoreschema


SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: app_id; Type: DOMAIN; Schema: public; Owner: migrator
--

CREATE DOMAIN public.app_id AS uuid
	CONSTRAINT app_id_check CHECK ((VALUE <> '00000000-0000-0000-0000-000000000000'::uuid));


ALTER DOMAIN public.app_id OWNER TO migrator;

--
-- Name: app_ts; Type: DOMAIN; Schema: public; Owner: migrator
--

CREATE DOMAIN public.app_ts AS timestamp with time zone
	CONSTRAINT app_ts_check CHECK ((VALUE >= '2000-01-01 00:00:00+00'::timestamp with time zone));


ALTER DOMAIN public.app_ts OWNER TO migrator;

--
-- Name: prune_auth_attempts(); Type: PROCEDURE; Schema: public; Owner: migrator
--

CREATE PROCEDURE public.prune_auth_attempts()
    LANGUAGE sql
    AS $$
  DELETE FROM auth_attempts WHERE created_at < now() - interval '90 days';
$$;


ALTER PROCEDURE public.prune_auth_attempts() OWNER TO migrator;

--
-- Name: prune_password_reset_tokens(); Type: PROCEDURE; Schema: public; Owner: migrator
--

CREATE PROCEDURE public.prune_password_reset_tokens()
    LANGUAGE sql
    AS $$
  DELETE FROM password_reset_tokens WHERE requested_at < now() - interval '90 days';
$$;


ALTER PROCEDURE public.prune_password_reset_tokens() OWNER TO migrator;

SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: audit_log; Type: TABLE; Schema: public; Owner: migrator
--

CREATE TABLE public.audit_log (
    id public.app_id NOT NULL,
    tenant_id uuid NOT NULL,
    actor_id uuid,
    actor_type text NOT NULL,
    action text NOT NULL,
    entity_type text NOT NULL,
    entity_id uuid,
    old_value jsonb,
    new_value jsonb,
    ip_address text,
    created_at public.app_ts NOT NULL
);

ALTER TABLE ONLY public.audit_log FORCE ROW LEVEL SECURITY;


ALTER TABLE public.audit_log OWNER TO migrator;

--
-- Name: auth_attempts; Type: TABLE; Schema: public; Owner: migrator
--

CREATE TABLE public.auth_attempts (
    id public.app_id NOT NULL,
    username_entered text NOT NULL,
    ip_address text,
    succeeded boolean NOT NULL,
    created_at public.app_ts NOT NULL
);

ALTER TABLE ONLY public.auth_attempts FORCE ROW LEVEL SECURITY;


ALTER TABLE public.auth_attempts OWNER TO migrator;

--
-- Name: categories; Type: TABLE; Schema: public; Owner: migrator
--

CREATE TABLE public.categories (
    id public.app_id NOT NULL,
    code text NOT NULL,
    name_ar text NOT NULL,
    name_en text NOT NULL,
    is_active boolean NOT NULL,
    created_at public.app_ts NOT NULL
);

ALTER TABLE ONLY public.categories FORCE ROW LEVEL SECURITY;


ALTER TABLE public.categories OWNER TO migrator;

--
-- Name: clients; Type: TABLE; Schema: public; Owner: migrator
--

CREATE TABLE public.clients (
    id public.app_id NOT NULL,
    tenant_id uuid NOT NULL,
    scope_ref_id uuid NOT NULL,
    name text NOT NULL,
    created_at public.app_ts NOT NULL,
    email text,
    phone_e164 text,
    telegram_chat_id text,
    is_active boolean NOT NULL,
    CONSTRAINT clients_email_check CHECK (((email ~ '^[^@[:space:]]+@[^@[:space:]]+[.][^@[:space:]]+$'::text) AND (length(email) <= 320))),
    CONSTRAINT clients_phone_e164_check CHECK ((phone_e164 ~ '^\+[1-9][0-9]{1,14}$'::text)),
    CONSTRAINT clients_scope_ref_id_is_id CHECK ((scope_ref_id = (id)::uuid)),
    CONSTRAINT clients_telegram_chat_id_check CHECK ((telegram_chat_id ~ '^-?[0-9]{1,20}$'::text))
);

ALTER TABLE ONLY public.clients FORCE ROW LEVEL SECURITY;


ALTER TABLE public.clients OWNER TO migrator;

--
-- Name: invitations; Type: TABLE; Schema: public; Owner: migrator
--

CREATE TABLE public.invitations (
    id public.app_id NOT NULL,
    tenant_id uuid NOT NULL,
    email text NOT NULL,
    role_id uuid NOT NULL,
    token_hash text NOT NULL,
    status text NOT NULL,
    invited_by uuid,
    expires_at public.app_ts NOT NULL,
    created_at public.app_ts NOT NULL,
    intended_scope_mode text NOT NULL,
    CONSTRAINT invitations_intended_scope_mode_check CHECK ((intended_scope_mode = ANY (ARRAY['all'::text, 'assigned'::text]))),
    CONSTRAINT invitations_status_check CHECK ((status = ANY (ARRAY['pending'::text, 'accepted'::text, 'revoked'::text, 'expired'::text])))
);

ALTER TABLE ONLY public.invitations FORCE ROW LEVEL SECURITY;


ALTER TABLE public.invitations OWNER TO migrator;

--
-- Name: membership_auth; Type: TABLE; Schema: public; Owner: migrator
--

CREATE TABLE public.membership_auth (
    id public.app_id NOT NULL,
    tenant_id uuid NOT NULL,
    membership_id uuid NOT NULL,
    provider text NOT NULL,
    provider_config jsonb
);

ALTER TABLE ONLY public.membership_auth FORCE ROW LEVEL SECURITY;


ALTER TABLE public.membership_auth OWNER TO migrator;

--
-- Name: membership_roles; Type: TABLE; Schema: public; Owner: migrator
--

CREATE TABLE public.membership_roles (
    id public.app_id NOT NULL,
    tenant_id uuid NOT NULL,
    membership_id uuid NOT NULL,
    role_id uuid NOT NULL
);

ALTER TABLE ONLY public.membership_roles FORCE ROW LEVEL SECURITY;


ALTER TABLE public.membership_roles OWNER TO migrator;

--
-- Name: membership_scope; Type: TABLE; Schema: public; Owner: migrator
--

CREATE TABLE public.membership_scope (
    id public.app_id NOT NULL,
    tenant_id uuid NOT NULL,
    membership_id uuid NOT NULL,
    scope_mode text NOT NULL,
    CONSTRAINT membership_scope_scope_mode_check CHECK ((scope_mode = ANY (ARRAY['all'::text, 'assigned'::text])))
);

ALTER TABLE ONLY public.membership_scope FORCE ROW LEVEL SECURITY;


ALTER TABLE public.membership_scope OWNER TO migrator;

--
-- Name: memberships; Type: TABLE; Schema: public; Owner: migrator
--

CREATE TABLE public.memberships (
    id public.app_id NOT NULL,
    tenant_id uuid NOT NULL,
    user_id uuid NOT NULL,
    status text NOT NULL,
    created_at public.app_ts NOT NULL,
    CONSTRAINT memberships_status_check CHECK ((status = ANY (ARRAY['active'::text, 'disabled'::text, 'left'::text])))
);

ALTER TABLE ONLY public.memberships FORCE ROW LEVEL SECURITY;


ALTER TABLE public.memberships OWNER TO migrator;

--
-- Name: modules; Type: TABLE; Schema: public; Owner: migrator
--

CREATE TABLE public.modules (
    id public.app_id NOT NULL,
    code text NOT NULL,
    name_ar text NOT NULL,
    name_en text NOT NULL,
    icon text,
    display_order integer NOT NULL,
    is_active boolean NOT NULL
);

ALTER TABLE ONLY public.modules FORCE ROW LEVEL SECURITY;


ALTER TABLE public.modules OWNER TO migrator;

--
-- Name: password_reset_tokens; Type: TABLE; Schema: public; Owner: migrator
--

CREATE TABLE public.password_reset_tokens (
    id public.app_id NOT NULL,
    user_id uuid NOT NULL,
    token_hash text NOT NULL,
    requested_at public.app_ts NOT NULL,
    expires_at public.app_ts NOT NULL,
    used_at timestamp with time zone,
    request_ip text
);

ALTER TABLE ONLY public.password_reset_tokens FORCE ROW LEVEL SECURITY;


ALTER TABLE public.password_reset_tokens OWNER TO migrator;

--
-- Name: permissions; Type: TABLE; Schema: public; Owner: migrator
--

CREATE TABLE public.permissions (
    id public.app_id NOT NULL,
    module_id uuid NOT NULL,
    code text NOT NULL,
    name_ar text NOT NULL,
    name_en text NOT NULL
);

ALTER TABLE ONLY public.permissions FORCE ROW LEVEL SECURITY;


ALTER TABLE public.permissions OWNER TO migrator;

--
-- Name: persons; Type: TABLE; Schema: public; Owner: migrator
--

CREATE TABLE public.persons (
    id public.app_id NOT NULL,
    full_name text NOT NULL,
    email text NOT NULL,
    phone_e164 text,
    created_at public.app_ts NOT NULL
);

ALTER TABLE ONLY public.persons FORCE ROW LEVEL SECURITY;


ALTER TABLE public.persons OWNER TO migrator;

--
-- Name: provider_accounts; Type: TABLE; Schema: public; Owner: migrator
--

CREATE TABLE public.provider_accounts (
    id public.app_id NOT NULL,
    tenant_id uuid,
    channel text NOT NULL,
    provider_code text NOT NULL,
    sender_id text NOT NULL,
    credential_ref text NOT NULL,
    is_active boolean NOT NULL,
    created_at public.app_ts NOT NULL,
    CONSTRAINT provider_accounts_channel_check CHECK ((channel = ANY (ARRAY['email'::text, 'whatsapp'::text, 'sms'::text, 'telegram'::text])))
);

ALTER TABLE ONLY public.provider_accounts FORCE ROW LEVEL SECURITY;


ALTER TABLE public.provider_accounts OWNER TO migrator;

--
-- Name: role_permissions; Type: TABLE; Schema: public; Owner: migrator
--

CREATE TABLE public.role_permissions (
    id public.app_id NOT NULL,
    tenant_id uuid NOT NULL,
    role_id uuid NOT NULL,
    permission_id uuid NOT NULL
);

ALTER TABLE ONLY public.role_permissions FORCE ROW LEVEL SECURITY;


ALTER TABLE public.role_permissions OWNER TO migrator;

--
-- Name: role_template_permissions; Type: TABLE; Schema: public; Owner: migrator
--

CREATE TABLE public.role_template_permissions (
    template_id public.app_id NOT NULL,
    permission_id public.app_id NOT NULL
);

ALTER TABLE ONLY public.role_template_permissions FORCE ROW LEVEL SECURITY;


ALTER TABLE public.role_template_permissions OWNER TO migrator;

--
-- Name: role_templates; Type: TABLE; Schema: public; Owner: migrator
--

CREATE TABLE public.role_templates (
    id public.app_id NOT NULL,
    code text NOT NULL,
    name_ar text NOT NULL,
    name_en text NOT NULL
);

ALTER TABLE ONLY public.role_templates FORCE ROW LEVEL SECURITY;


ALTER TABLE public.role_templates OWNER TO migrator;

--
-- Name: roles; Type: TABLE; Schema: public; Owner: migrator
--

CREATE TABLE public.roles (
    id public.app_id NOT NULL,
    tenant_id uuid NOT NULL,
    code text NOT NULL,
    name_ar text NOT NULL,
    name_en text NOT NULL,
    is_system boolean NOT NULL,
    is_active boolean NOT NULL
);

ALTER TABLE ONLY public.roles FORCE ROW LEVEL SECURITY;


ALTER TABLE public.roles OWNER TO migrator;

--
-- Name: scope_assignments; Type: TABLE; Schema: public; Owner: migrator
--

CREATE TABLE public.scope_assignments (
    id public.app_id NOT NULL,
    tenant_id uuid NOT NULL,
    membership_id uuid NOT NULL,
    scope_ref_id uuid NOT NULL,
    assignment_role text NOT NULL,
    active boolean NOT NULL,
    reason text,
    CONSTRAINT scope_assignments_assignment_role_check CHECK ((assignment_role = ANY (ARRAY['lead'::text, 'contributor'::text, 'reviewer'::text])))
);

ALTER TABLE ONLY public.scope_assignments FORCE ROW LEVEL SECURITY;


ALTER TABLE public.scope_assignments OWNER TO migrator;

--
-- Name: subcategories; Type: TABLE; Schema: public; Owner: migrator
--

CREATE TABLE public.subcategories (
    id public.app_id NOT NULL,
    category_id uuid NOT NULL,
    code text NOT NULL,
    name_ar text NOT NULL,
    name_en text NOT NULL,
    is_active boolean NOT NULL,
    created_at public.app_ts NOT NULL
);

ALTER TABLE ONLY public.subcategories FORCE ROW LEVEL SECURITY;


ALTER TABLE public.subcategories OWNER TO migrator;

--
-- Name: subscriptions; Type: TABLE; Schema: public; Owner: migrator
--

CREATE TABLE public.subscriptions (
    id public.app_id NOT NULL,
    tenant_id uuid NOT NULL,
    scope_ref_id uuid NOT NULL,
    service_name text NOT NULL,
    ends_on date NOT NULL,
    created_at public.app_ts NOT NULL,
    reminder_type text NOT NULL,
    category_id uuid NOT NULL,
    subcategory_id uuid,
    provider text,
    cost numeric(14,3),
    currency text,
    billing_cycle text NOT NULL,
    auto_renew boolean NOT NULL,
    login_url text,
    status text NOT NULL,
    renewed_from_id uuid,
    CONSTRAINT subscriptions_billing_cycle_check CHECK ((billing_cycle = ANY (ARRAY['monthly'::text, 'yearly'::text]))),
    CONSTRAINT subscriptions_cost_check CHECK ((cost >= (0)::numeric)),
    CONSTRAINT subscriptions_currency_check CHECK ((currency ~ '^[A-Z]{3}$'::text)),
    CONSTRAINT subscriptions_reminder_type_check CHECK ((reminder_type = 'subscription'::text)),
    CONSTRAINT subscriptions_status_check CHECK ((status = ANY (ARRAY['active'::text, 'cancelled'::text, 'renewed'::text])))
);

ALTER TABLE ONLY public.subscriptions FORCE ROW LEVEL SECURITY;


ALTER TABLE public.subscriptions OWNER TO migrator;

--
-- Name: tenant_modules; Type: TABLE; Schema: public; Owner: migrator
--

CREATE TABLE public.tenant_modules (
    id public.app_id NOT NULL,
    tenant_id uuid NOT NULL,
    module_id uuid NOT NULL,
    is_active boolean NOT NULL,
    activated_at timestamp with time zone
);

ALTER TABLE ONLY public.tenant_modules FORCE ROW LEVEL SECURITY;


ALTER TABLE public.tenant_modules OWNER TO migrator;

--
-- Name: tenants; Type: TABLE; Schema: public; Owner: migrator
--

CREATE TABLE public.tenants (
    id public.app_id NOT NULL,
    name text NOT NULL,
    status text NOT NULL,
    created_at public.app_ts NOT NULL,
    CONSTRAINT tenants_status_check CHECK ((status = ANY (ARRAY['active'::text, 'suspended'::text])))
);

ALTER TABLE ONLY public.tenants FORCE ROW LEVEL SECURITY;


ALTER TABLE public.tenants OWNER TO migrator;

--
-- Name: user_password_credentials; Type: TABLE; Schema: public; Owner: migrator
--

CREATE TABLE public.user_password_credentials (
    user_id public.app_id NOT NULL,
    password_hash text NOT NULL,
    updated_at public.app_ts NOT NULL
);

ALTER TABLE ONLY public.user_password_credentials FORCE ROW LEVEL SECURITY;


ALTER TABLE public.user_password_credentials OWNER TO migrator;

--
-- Name: users; Type: TABLE; Schema: public; Owner: migrator
--

CREATE TABLE public.users (
    id public.app_id NOT NULL,
    person_id uuid NOT NULL,
    user_type text NOT NULL,
    username text NOT NULL,
    status text NOT NULL,
    language text,
    theme text,
    last_login_at timestamp with time zone,
    security_stamp public.app_id NOT NULL,
    CONSTRAINT users_user_type_check CHECK ((user_type = ANY (ARRAY['employee'::text, 'customer'::text])))
);

ALTER TABLE ONLY public.users FORCE ROW LEVEL SECURITY;


ALTER TABLE public.users OWNER TO migrator;

--
-- Name: audit_log audit_log_pkey; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.audit_log
    ADD CONSTRAINT audit_log_pkey PRIMARY KEY (id);


--
-- Name: auth_attempts auth_attempts_pkey; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.auth_attempts
    ADD CONSTRAINT auth_attempts_pkey PRIMARY KEY (id);


--
-- Name: categories categories_code_key; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.categories
    ADD CONSTRAINT categories_code_key UNIQUE (code);


--
-- Name: categories categories_pkey; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.categories
    ADD CONSTRAINT categories_pkey PRIMARY KEY (id);


--
-- Name: clients clients_pkey; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.clients
    ADD CONSTRAINT clients_pkey PRIMARY KEY (id);


--
-- Name: clients clients_tenant_id_id_key; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.clients
    ADD CONSTRAINT clients_tenant_id_id_key UNIQUE (tenant_id, id);


--
-- Name: invitations invitations_pkey; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.invitations
    ADD CONSTRAINT invitations_pkey PRIMARY KEY (id);


--
-- Name: membership_auth membership_auth_pkey; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.membership_auth
    ADD CONSTRAINT membership_auth_pkey PRIMARY KEY (id);


--
-- Name: membership_roles membership_roles_pkey; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.membership_roles
    ADD CONSTRAINT membership_roles_pkey PRIMARY KEY (id);


--
-- Name: membership_scope membership_scope_pkey; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.membership_scope
    ADD CONSTRAINT membership_scope_pkey PRIMARY KEY (id);


--
-- Name: membership_scope membership_scope_tenant_id_membership_id_key; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.membership_scope
    ADD CONSTRAINT membership_scope_tenant_id_membership_id_key UNIQUE (tenant_id, membership_id);


--
-- Name: memberships memberships_pkey; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.memberships
    ADD CONSTRAINT memberships_pkey PRIMARY KEY (id);


--
-- Name: memberships memberships_tenant_id_id_key; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.memberships
    ADD CONSTRAINT memberships_tenant_id_id_key UNIQUE (tenant_id, id);


--
-- Name: memberships memberships_tenant_id_user_id_key; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.memberships
    ADD CONSTRAINT memberships_tenant_id_user_id_key UNIQUE (tenant_id, user_id);


--
-- Name: modules modules_pkey; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.modules
    ADD CONSTRAINT modules_pkey PRIMARY KEY (id);


--
-- Name: password_reset_tokens password_reset_tokens_pkey; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.password_reset_tokens
    ADD CONSTRAINT password_reset_tokens_pkey PRIMARY KEY (id);


--
-- Name: permissions permissions_pkey; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.permissions
    ADD CONSTRAINT permissions_pkey PRIMARY KEY (id);


--
-- Name: persons persons_email_key; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.persons
    ADD CONSTRAINT persons_email_key UNIQUE (email);


--
-- Name: persons persons_pkey; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.persons
    ADD CONSTRAINT persons_pkey PRIMARY KEY (id);


--
-- Name: provider_accounts provider_accounts_pkey; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.provider_accounts
    ADD CONSTRAINT provider_accounts_pkey PRIMARY KEY (id);


--
-- Name: provider_accounts provider_accounts_tenant_channel_key; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.provider_accounts
    ADD CONSTRAINT provider_accounts_tenant_channel_key UNIQUE NULLS NOT DISTINCT (tenant_id, channel);


--
-- Name: role_permissions role_permissions_pkey; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.role_permissions
    ADD CONSTRAINT role_permissions_pkey PRIMARY KEY (id);


--
-- Name: role_template_permissions role_template_permissions_pkey; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.role_template_permissions
    ADD CONSTRAINT role_template_permissions_pkey PRIMARY KEY (template_id, permission_id);


--
-- Name: role_templates role_templates_pkey; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.role_templates
    ADD CONSTRAINT role_templates_pkey PRIMARY KEY (id);


--
-- Name: roles roles_pkey; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.roles
    ADD CONSTRAINT roles_pkey PRIMARY KEY (id);


--
-- Name: roles roles_tenant_id_id_key; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.roles
    ADD CONSTRAINT roles_tenant_id_id_key UNIQUE (tenant_id, id);


--
-- Name: scope_assignments scope_assignments_pkey; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.scope_assignments
    ADD CONSTRAINT scope_assignments_pkey PRIMARY KEY (id);


--
-- Name: scope_assignments scope_assignments_tenant_id_membership_id_scope_ref_id_key; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.scope_assignments
    ADD CONSTRAINT scope_assignments_tenant_id_membership_id_scope_ref_id_key UNIQUE (tenant_id, membership_id, scope_ref_id);


--
-- Name: subcategories subcategories_category_id_code_key; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.subcategories
    ADD CONSTRAINT subcategories_category_id_code_key UNIQUE (category_id, code);


--
-- Name: subcategories subcategories_pkey; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.subcategories
    ADD CONSTRAINT subcategories_pkey PRIMARY KEY (id);


--
-- Name: subscriptions subscriptions_pkey; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.subscriptions
    ADD CONSTRAINT subscriptions_pkey PRIMARY KEY (id);


--
-- Name: subscriptions subscriptions_renewed_from_id_key; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.subscriptions
    ADD CONSTRAINT subscriptions_renewed_from_id_key UNIQUE (tenant_id, renewed_from_id);


--
-- Name: subscriptions subscriptions_tenant_id_id_key; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.subscriptions
    ADD CONSTRAINT subscriptions_tenant_id_id_key UNIQUE (tenant_id, id);


--
-- Name: tenant_modules tenant_modules_pkey; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.tenant_modules
    ADD CONSTRAINT tenant_modules_pkey PRIMARY KEY (id);


--
-- Name: tenant_modules tenant_modules_tenant_id_module_id_key; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.tenant_modules
    ADD CONSTRAINT tenant_modules_tenant_id_module_id_key UNIQUE (tenant_id, module_id);


--
-- Name: tenants tenants_pkey; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.tenants
    ADD CONSTRAINT tenants_pkey PRIMARY KEY (id);


--
-- Name: user_password_credentials user_password_credentials_pkey; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.user_password_credentials
    ADD CONSTRAINT user_password_credentials_pkey PRIMARY KEY (user_id);


--
-- Name: users users_pkey; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_pkey PRIMARY KEY (id);


--
-- Name: users users_username_key; Type: CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_username_key UNIQUE (username);


--
-- Name: ix_clients_tenant_scope_ref_id; Type: INDEX; Schema: public; Owner: migrator
--

CREATE INDEX ix_clients_tenant_scope_ref_id ON public.clients USING btree (tenant_id, scope_ref_id);


--
-- Name: ix_password_reset_tokens_user_id; Type: INDEX; Schema: public; Owner: migrator
--

CREATE INDEX ix_password_reset_tokens_user_id ON public.password_reset_tokens USING btree (user_id);


--
-- Name: ix_subscriptions_tenant_scope_ref_id; Type: INDEX; Schema: public; Owner: migrator
--

CREATE INDEX ix_subscriptions_tenant_scope_ref_id ON public.subscriptions USING btree (tenant_id, scope_ref_id);


--
-- Name: password_reset_tokens_token_hash_key; Type: INDEX; Schema: public; Owner: migrator
--

CREATE UNIQUE INDEX password_reset_tokens_token_hash_key ON public.password_reset_tokens USING btree (token_hash);


--
-- Name: persons_email_normalized_key; Type: INDEX; Schema: public; Owner: migrator
--

CREATE UNIQUE INDEX persons_email_normalized_key ON public.persons USING btree (lower(btrim(email)));


--
-- Name: audit_log audit_log_tenant_fkey; Type: FK CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.audit_log
    ADD CONSTRAINT audit_log_tenant_fkey FOREIGN KEY (tenant_id) REFERENCES public.tenants(id);


--
-- Name: clients clients_tenant_fkey; Type: FK CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.clients
    ADD CONSTRAINT clients_tenant_fkey FOREIGN KEY (tenant_id) REFERENCES public.tenants(id);


--
-- Name: invitations invitations_role_fkey; Type: FK CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.invitations
    ADD CONSTRAINT invitations_role_fkey FOREIGN KEY (tenant_id, role_id) REFERENCES public.roles(tenant_id, id);


--
-- Name: membership_auth membership_auth_membership_fkey; Type: FK CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.membership_auth
    ADD CONSTRAINT membership_auth_membership_fkey FOREIGN KEY (tenant_id, membership_id) REFERENCES public.memberships(tenant_id, id);


--
-- Name: membership_roles membership_roles_membership_fkey; Type: FK CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.membership_roles
    ADD CONSTRAINT membership_roles_membership_fkey FOREIGN KEY (tenant_id, membership_id) REFERENCES public.memberships(tenant_id, id);


--
-- Name: membership_roles membership_roles_role_fkey; Type: FK CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.membership_roles
    ADD CONSTRAINT membership_roles_role_fkey FOREIGN KEY (tenant_id, role_id) REFERENCES public.roles(tenant_id, id);


--
-- Name: membership_scope membership_scope_membership_fkey; Type: FK CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.membership_scope
    ADD CONSTRAINT membership_scope_membership_fkey FOREIGN KEY (tenant_id, membership_id) REFERENCES public.memberships(tenant_id, id);


--
-- Name: memberships memberships_tenant_fkey; Type: FK CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.memberships
    ADD CONSTRAINT memberships_tenant_fkey FOREIGN KEY (tenant_id) REFERENCES public.tenants(id);


--
-- Name: memberships memberships_user_fkey; Type: FK CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.memberships
    ADD CONSTRAINT memberships_user_fkey FOREIGN KEY (user_id) REFERENCES public.users(id);


--
-- Name: password_reset_tokens password_reset_tokens_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.password_reset_tokens
    ADD CONSTRAINT password_reset_tokens_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id);


--
-- Name: permissions permissions_module_fkey; Type: FK CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.permissions
    ADD CONSTRAINT permissions_module_fkey FOREIGN KEY (module_id) REFERENCES public.modules(id);


--
-- Name: provider_accounts provider_accounts_tenant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.provider_accounts
    ADD CONSTRAINT provider_accounts_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES public.tenants(id);


--
-- Name: role_permissions role_permissions_permission_fkey; Type: FK CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.role_permissions
    ADD CONSTRAINT role_permissions_permission_fkey FOREIGN KEY (permission_id) REFERENCES public.permissions(id);


--
-- Name: role_permissions role_permissions_role_fkey; Type: FK CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.role_permissions
    ADD CONSTRAINT role_permissions_role_fkey FOREIGN KEY (tenant_id, role_id) REFERENCES public.roles(tenant_id, id);


--
-- Name: role_template_permissions role_template_permissions_permission_fkey; Type: FK CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.role_template_permissions
    ADD CONSTRAINT role_template_permissions_permission_fkey FOREIGN KEY (permission_id) REFERENCES public.permissions(id);


--
-- Name: role_template_permissions role_template_permissions_template_fkey; Type: FK CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.role_template_permissions
    ADD CONSTRAINT role_template_permissions_template_fkey FOREIGN KEY (template_id) REFERENCES public.role_templates(id);


--
-- Name: roles roles_tenant_fkey; Type: FK CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.roles
    ADD CONSTRAINT roles_tenant_fkey FOREIGN KEY (tenant_id) REFERENCES public.tenants(id);


--
-- Name: scope_assignments scope_assignments_membership_fkey; Type: FK CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.scope_assignments
    ADD CONSTRAINT scope_assignments_membership_fkey FOREIGN KEY (tenant_id, membership_id) REFERENCES public.memberships(tenant_id, id);


--
-- Name: subcategories subcategories_category_fkey; Type: FK CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.subcategories
    ADD CONSTRAINT subcategories_category_fkey FOREIGN KEY (category_id) REFERENCES public.categories(id);


--
-- Name: subscriptions subscriptions_category_fkey; Type: FK CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.subscriptions
    ADD CONSTRAINT subscriptions_category_fkey FOREIGN KEY (category_id) REFERENCES public.categories(id);


--
-- Name: subscriptions subscriptions_client_fkey; Type: FK CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.subscriptions
    ADD CONSTRAINT subscriptions_client_fkey FOREIGN KEY (tenant_id, scope_ref_id) REFERENCES public.clients(tenant_id, id);


--
-- Name: subscriptions subscriptions_renewed_from_fkey; Type: FK CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.subscriptions
    ADD CONSTRAINT subscriptions_renewed_from_fkey FOREIGN KEY (tenant_id, renewed_from_id) REFERENCES public.subscriptions(tenant_id, id);


--
-- Name: subscriptions subscriptions_subcategory_fkey; Type: FK CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.subscriptions
    ADD CONSTRAINT subscriptions_subcategory_fkey FOREIGN KEY (subcategory_id) REFERENCES public.subcategories(id);


--
-- Name: subscriptions subscriptions_tenant_fkey; Type: FK CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.subscriptions
    ADD CONSTRAINT subscriptions_tenant_fkey FOREIGN KEY (tenant_id) REFERENCES public.tenants(id);


--
-- Name: tenant_modules tenant_modules_module_fkey; Type: FK CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.tenant_modules
    ADD CONSTRAINT tenant_modules_module_fkey FOREIGN KEY (module_id) REFERENCES public.modules(id);


--
-- Name: tenant_modules tenant_modules_tenant_fkey; Type: FK CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.tenant_modules
    ADD CONSTRAINT tenant_modules_tenant_fkey FOREIGN KEY (tenant_id) REFERENCES public.tenants(id);


--
-- Name: user_password_credentials user_password_credentials_user_fkey; Type: FK CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.user_password_credentials
    ADD CONSTRAINT user_password_credentials_user_fkey FOREIGN KEY (user_id) REFERENCES public.users(id);


--
-- Name: users users_person_fkey; Type: FK CONSTRAINT; Schema: public; Owner: migrator
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_person_fkey FOREIGN KEY (person_id) REFERENCES public.persons(id);


--
-- Name: audit_log; Type: ROW SECURITY; Schema: public; Owner: migrator
--

ALTER TABLE public.audit_log ENABLE ROW LEVEL SECURITY;

--
-- Name: audit_log audit_log_insert; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY audit_log_insert ON public.audit_log FOR INSERT TO app_user WITH CHECK ((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")));


--
-- Name: audit_log audit_log_provisioner_insert; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY audit_log_provisioner_insert ON public.audit_log FOR INSERT TO provisioner WITH CHECK ((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")));


--
-- Name: audit_log audit_read; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY audit_read ON public.audit_log FOR SELECT TO app_user USING (((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")) AND COALESCE(( SELECT (NULLIF(current_setting('app.scope_all'::text, true), ''::text))::boolean AS "nullif"), false)));


--
-- Name: auth_attempts; Type: ROW SECURITY; Schema: public; Owner: migrator
--

ALTER TABLE public.auth_attempts ENABLE ROW LEVEL SECURITY;

--
-- Name: auth_attempts auth_attempts_authenticator_insert; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY auth_attempts_authenticator_insert ON public.auth_attempts FOR INSERT TO authenticator WITH CHECK (true);


--
-- Name: auth_attempts auth_attempts_authenticator_read; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY auth_attempts_authenticator_read ON public.auth_attempts FOR SELECT TO authenticator USING (true);


--
-- Name: categories; Type: ROW SECURITY; Schema: public; Owner: migrator
--

ALTER TABLE public.categories ENABLE ROW LEVEL SECURITY;

--
-- Name: categories categories_read; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY categories_read ON public.categories FOR SELECT TO app_user, job_runner, provisioner USING (true);


--
-- Name: clients client_scope; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY client_scope ON public.clients AS RESTRICTIVE TO app_user USING ((COALESCE(( SELECT (NULLIF(current_setting('app.scope_all'::text, true), ''::text))::boolean AS "nullif"), false) OR (EXISTS ( SELECT 1
   FROM public.scope_assignments sa
  WHERE ((sa.tenant_id = clients.tenant_id) AND (sa.scope_ref_id = clients.scope_ref_id) AND (sa.membership_id = ( SELECT (NULLIF(current_setting('app.membership_id'::text, true), ''::text))::uuid AS "nullif")) AND sa.active))))) WITH CHECK ((COALESCE(( SELECT (NULLIF(current_setting('app.scope_all'::text, true), ''::text))::boolean AS "nullif"), false) OR (EXISTS ( SELECT 1
   FROM public.scope_assignments sa
  WHERE ((sa.tenant_id = clients.tenant_id) AND (sa.scope_ref_id = clients.scope_ref_id) AND (sa.membership_id = ( SELECT (NULLIF(current_setting('app.membership_id'::text, true), ''::text))::uuid AS "nullif")) AND sa.active)))));


--
-- Name: subscriptions client_scope; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY client_scope ON public.subscriptions AS RESTRICTIVE TO app_user USING ((COALESCE(( SELECT (NULLIF(current_setting('app.scope_all'::text, true), ''::text))::boolean AS "nullif"), false) OR (EXISTS ( SELECT 1
   FROM public.scope_assignments sa
  WHERE ((sa.tenant_id = subscriptions.tenant_id) AND (sa.scope_ref_id = subscriptions.scope_ref_id) AND (sa.membership_id = ( SELECT (NULLIF(current_setting('app.membership_id'::text, true), ''::text))::uuid AS "nullif")) AND sa.active))))) WITH CHECK ((COALESCE(( SELECT (NULLIF(current_setting('app.scope_all'::text, true), ''::text))::boolean AS "nullif"), false) OR (EXISTS ( SELECT 1
   FROM public.scope_assignments sa
  WHERE ((sa.tenant_id = subscriptions.tenant_id) AND (sa.scope_ref_id = subscriptions.scope_ref_id) AND (sa.membership_id = ( SELECT (NULLIF(current_setting('app.membership_id'::text, true), ''::text))::uuid AS "nullif")) AND sa.active)))));


--
-- Name: clients; Type: ROW SECURITY; Schema: public; Owner: migrator
--

ALTER TABLE public.clients ENABLE ROW LEVEL SECURITY;

--
-- Name: invitations; Type: ROW SECURITY; Schema: public; Owner: migrator
--

ALTER TABLE public.invitations ENABLE ROW LEVEL SECURITY;

--
-- Name: invitations invitations_insert; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY invitations_insert ON public.invitations FOR INSERT TO app_user WITH CHECK (((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")) AND (invited_by = ( SELECT (NULLIF(current_setting('app.user_id'::text, true), ''::text))::uuid AS "nullif")) AND COALESCE(( SELECT (NULLIF(current_setting('app.can_manage_members'::text, true), ''::text))::boolean AS "nullif"), false) AND (status = 'pending'::text) AND ((intended_scope_mode = 'assigned'::text) OR COALESCE(( SELECT (NULLIF(current_setting('app.can_manage_scope'::text, true), ''::text))::boolean AS "nullif"), false))));


--
-- Name: invitations invitations_provisioner_insert; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY invitations_provisioner_insert ON public.invitations FOR INSERT TO provisioner WITH CHECK (((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")) AND (status = 'pending'::text) AND (invited_by IS NULL)));


--
-- Name: invitations invitations_provisioner_select; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY invitations_provisioner_select ON public.invitations FOR SELECT TO provisioner USING ((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")));


--
-- Name: invitations invitations_provisioner_update; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY invitations_provisioner_update ON public.invitations FOR UPDATE TO provisioner USING ((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif"))) WITH CHECK ((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")));


--
-- Name: invitations invitations_tenant_read; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY invitations_tenant_read ON public.invitations FOR SELECT TO app_user USING ((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")));


--
-- Name: invitations invitations_tenant_update; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY invitations_tenant_update ON public.invitations FOR UPDATE TO app_user USING (((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")) AND COALESCE(( SELECT (NULLIF(current_setting('app.can_manage_members'::text, true), ''::text))::boolean AS "nullif"), false) AND (status = 'pending'::text))) WITH CHECK (((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")) AND COALESCE(( SELECT (NULLIF(current_setting('app.can_manage_members'::text, true), ''::text))::boolean AS "nullif"), false) AND (status = 'revoked'::text)));


--
-- Name: membership_auth; Type: ROW SECURITY; Schema: public; Owner: migrator
--

ALTER TABLE public.membership_auth ENABLE ROW LEVEL SECURITY;

--
-- Name: membership_auth membership_auth_authenticator_read; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY membership_auth_authenticator_read ON public.membership_auth FOR SELECT TO authenticator USING (true);


--
-- Name: membership_auth membership_auth_provisioner_insert; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY membership_auth_provisioner_insert ON public.membership_auth FOR INSERT TO provisioner WITH CHECK ((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")));


--
-- Name: membership_auth membership_auth_self_read; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY membership_auth_self_read ON public.membership_auth FOR SELECT TO app_user USING ((EXISTS ( SELECT 1
   FROM public.memberships m
  WHERE (((m.id)::uuid = membership_auth.membership_id) AND (m.user_id = ( SELECT (NULLIF(current_setting('app.user_id'::text, true), ''::text))::uuid AS "nullif"))))));


--
-- Name: memberships membership_lock; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY membership_lock ON public.memberships FOR UPDATE TO app_user USING (((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")) AND ((id)::uuid <> ( SELECT (NULLIF(current_setting('app.membership_id'::text, true), ''::text))::uuid AS "nullif")) AND (status = 'active'::text) AND (COALESCE(( SELECT (NULLIF(current_setting('app.can_manage_members'::text, true), ''::text))::boolean AS "nullif"), false) OR COALESCE(( SELECT (NULLIF(current_setting('app.can_manage_scope'::text, true), ''::text))::boolean AS "nullif"), false)))) WITH CHECK (false);


--
-- Name: membership_roles; Type: ROW SECURITY; Schema: public; Owner: migrator
--

ALTER TABLE public.membership_roles ENABLE ROW LEVEL SECURITY;

--
-- Name: membership_roles membership_roles_manage_delete; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY membership_roles_manage_delete ON public.membership_roles AS RESTRICTIVE FOR DELETE TO app_user USING ((COALESCE(( SELECT (NULLIF(current_setting('app.can_manage_members'::text, true), ''::text))::boolean AS "nullif"), false) AND (membership_id <> ( SELECT (NULLIF(current_setting('app.membership_id'::text, true), ''::text))::uuid AS "nullif"))));


--
-- Name: membership_roles membership_roles_manage_insert; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY membership_roles_manage_insert ON public.membership_roles AS RESTRICTIVE FOR INSERT TO app_user WITH CHECK ((COALESCE(( SELECT (NULLIF(current_setting('app.can_manage_members'::text, true), ''::text))::boolean AS "nullif"), false) AND (membership_id <> ( SELECT (NULLIF(current_setting('app.membership_id'::text, true), ''::text))::uuid AS "nullif"))));


--
-- Name: membership_roles membership_roles_provisioner_delete; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY membership_roles_provisioner_delete ON public.membership_roles FOR DELETE TO provisioner USING (((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")) AND (EXISTS ( SELECT 1
   FROM public.memberships m
  WHERE (((m.id)::uuid = membership_roles.membership_id) AND (m.status = 'left'::text))))));


--
-- Name: membership_roles membership_roles_provisioner_insert; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY membership_roles_provisioner_insert ON public.membership_roles FOR INSERT TO provisioner WITH CHECK ((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")));


--
-- Name: membership_roles membership_roles_provisioner_select; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY membership_roles_provisioner_select ON public.membership_roles FOR SELECT TO provisioner USING (((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")) AND (EXISTS ( SELECT 1
   FROM public.memberships m
  WHERE (((m.id)::uuid = membership_roles.membership_id) AND (m.status = 'left'::text))))));


--
-- Name: membership_scope; Type: ROW SECURITY; Schema: public; Owner: migrator
--

ALTER TABLE public.membership_scope ENABLE ROW LEVEL SECURITY;

--
-- Name: membership_scope membership_scope_admin_update; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY membership_scope_admin_update ON public.membership_scope FOR UPDATE TO app_user USING (((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")) AND COALESCE(( SELECT (NULLIF(current_setting('app.can_manage_scope'::text, true), ''::text))::boolean AS "nullif"), false) AND (membership_id <> ( SELECT (NULLIF(current_setting('app.membership_id'::text, true), ''::text))::uuid AS "nullif")))) WITH CHECK (((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")) AND COALESCE(( SELECT (NULLIF(current_setting('app.can_manage_scope'::text, true), ''::text))::boolean AS "nullif"), false) AND (membership_id <> ( SELECT (NULLIF(current_setting('app.membership_id'::text, true), ''::text))::uuid AS "nullif"))));


--
-- Name: membership_scope membership_scope_provisioner_insert; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY membership_scope_provisioner_insert ON public.membership_scope FOR INSERT TO provisioner WITH CHECK ((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")));


--
-- Name: membership_scope membership_scope_provisioner_rejoin; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY membership_scope_provisioner_rejoin ON public.membership_scope FOR UPDATE TO provisioner USING (((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")) AND (EXISTS ( SELECT 1
   FROM public.memberships m
  WHERE (((m.id)::uuid = membership_scope.membership_id) AND (m.status = 'left'::text)))))) WITH CHECK (((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")) AND (EXISTS ( SELECT 1
   FROM public.memberships m
  WHERE (((m.id)::uuid = membership_scope.membership_id) AND (m.status = 'left'::text))))));


--
-- Name: membership_scope membership_scope_provisioner_select; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY membership_scope_provisioner_select ON public.membership_scope FOR SELECT TO provisioner USING (((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")) AND (EXISTS ( SELECT 1
   FROM public.memberships m
  WHERE (((m.id)::uuid = membership_scope.membership_id) AND (m.status = 'left'::text))))));


--
-- Name: membership_scope membership_scope_read; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY membership_scope_read ON public.membership_scope FOR SELECT TO app_user USING (((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")) AND (COALESCE(( SELECT (NULLIF(current_setting('app.can_manage_scope'::text, true), ''::text))::boolean AS "nullif"), false) OR (membership_id = ( SELECT (NULLIF(current_setting('app.membership_id'::text, true), ''::text))::uuid AS "nullif")))));


--
-- Name: memberships membership_self; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY membership_self ON public.memberships FOR SELECT TO app_user USING ((user_id = ( SELECT (NULLIF(current_setting('app.user_id'::text, true), ''::text))::uuid AS "nullif")));


--
-- Name: memberships membership_self_leave; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY membership_self_leave ON public.memberships FOR UPDATE TO app_user USING (((user_id = ( SELECT (NULLIF(current_setting('app.user_id'::text, true), ''::text))::uuid AS "nullif")) AND (status = 'active'::text))) WITH CHECK (((user_id = ( SELECT (NULLIF(current_setting('app.user_id'::text, true), ''::text))::uuid AS "nullif")) AND (status = 'left'::text)));


--
-- Name: memberships membership_tenant_read; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY membership_tenant_read ON public.memberships FOR SELECT TO app_user USING ((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")));


--
-- Name: memberships membership_tenant_update; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY membership_tenant_update ON public.memberships FOR UPDATE TO app_user USING (((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")) AND COALESCE(( SELECT (NULLIF(current_setting('app.can_manage_members'::text, true), ''::text))::boolean AS "nullif"), false) AND ((id)::uuid <> ( SELECT (NULLIF(current_setting('app.membership_id'::text, true), ''::text))::uuid AS "nullif")) AND (status = ANY (ARRAY['active'::text, 'disabled'::text])))) WITH CHECK (((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")) AND COALESCE(( SELECT (NULLIF(current_setting('app.can_manage_members'::text, true), ''::text))::boolean AS "nullif"), false) AND ((id)::uuid <> ( SELECT (NULLIF(current_setting('app.membership_id'::text, true), ''::text))::uuid AS "nullif")) AND (status = ANY (ARRAY['active'::text, 'disabled'::text]))));


--
-- Name: memberships; Type: ROW SECURITY; Schema: public; Owner: migrator
--

ALTER TABLE public.memberships ENABLE ROW LEVEL SECURITY;

--
-- Name: memberships memberships_provisioner_insert; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY memberships_provisioner_insert ON public.memberships FOR INSERT TO provisioner WITH CHECK ((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")));


--
-- Name: memberships memberships_provisioner_rejoin; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY memberships_provisioner_rejoin ON public.memberships FOR UPDATE TO provisioner USING (((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")) AND (status = 'left'::text))) WITH CHECK (((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")) AND (status = 'active'::text)));


--
-- Name: memberships memberships_provisioner_select; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY memberships_provisioner_select ON public.memberships FOR SELECT TO provisioner USING ((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")));


--
-- Name: modules; Type: ROW SECURITY; Schema: public; Owner: migrator
--

ALTER TABLE public.modules ENABLE ROW LEVEL SECURITY;

--
-- Name: modules modules_read; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY modules_read ON public.modules FOR SELECT TO app_user, provisioner USING (true);


--
-- Name: password_reset_tokens; Type: ROW SECURITY; Schema: public; Owner: migrator
--

ALTER TABLE public.password_reset_tokens ENABLE ROW LEVEL SECURITY;

--
-- Name: password_reset_tokens password_reset_tokens_authenticator_insert; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY password_reset_tokens_authenticator_insert ON public.password_reset_tokens FOR INSERT TO authenticator WITH CHECK (true);


--
-- Name: password_reset_tokens password_reset_tokens_authenticator_select; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY password_reset_tokens_authenticator_select ON public.password_reset_tokens FOR SELECT TO authenticator USING (true);


--
-- Name: password_reset_tokens password_reset_tokens_authenticator_use; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY password_reset_tokens_authenticator_use ON public.password_reset_tokens FOR UPDATE TO authenticator USING ((used_at IS NULL)) WITH CHECK ((used_at IS NOT NULL));


--
-- Name: user_password_credentials password_self_update; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY password_self_update ON public.user_password_credentials FOR UPDATE TO app_user USING (((user_id)::uuid = ( SELECT (NULLIF(current_setting('app.user_id'::text, true), ''::text))::uuid AS "nullif"))) WITH CHECK (((user_id)::uuid = ( SELECT (NULLIF(current_setting('app.user_id'::text, true), ''::text))::uuid AS "nullif")));


--
-- Name: permissions; Type: ROW SECURITY; Schema: public; Owner: migrator
--

ALTER TABLE public.permissions ENABLE ROW LEVEL SECURITY;

--
-- Name: permissions permissions_read; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY permissions_read ON public.permissions FOR SELECT TO app_user, provisioner USING (true);


--
-- Name: persons person_self; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY person_self ON public.persons FOR SELECT TO app_user USING ((EXISTS ( SELECT 1
   FROM public.users u
  WHERE ((u.person_id = (persons.id)::uuid) AND ((u.id)::uuid = ( SELECT (NULLIF(current_setting('app.user_id'::text, true), ''::text))::uuid AS "nullif"))))));


--
-- Name: persons person_visible_via_membership; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY person_visible_via_membership ON public.persons FOR SELECT TO app_user USING ((EXISTS ( SELECT 1
   FROM (public.users u
     JOIN public.memberships m ON ((m.user_id = (u.id)::uuid)))
  WHERE ((u.person_id = (persons.id)::uuid) AND (m.tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif"))))));


--
-- Name: persons; Type: ROW SECURITY; Schema: public; Owner: migrator
--

ALTER TABLE public.persons ENABLE ROW LEVEL SECURITY;

--
-- Name: persons persons_authenticator_read; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY persons_authenticator_read ON public.persons FOR SELECT TO authenticator USING (true);


--
-- Name: persons persons_provisioner_insert; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY persons_provisioner_insert ON public.persons FOR INSERT TO provisioner WITH CHECK (true);


--
-- Name: persons persons_provisioner_select; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY persons_provisioner_select ON public.persons FOR SELECT TO provisioner USING (true);


--
-- Name: provider_accounts; Type: ROW SECURITY; Schema: public; Owner: migrator
--

ALTER TABLE public.provider_accounts ENABLE ROW LEVEL SECURITY;

--
-- Name: provider_accounts provider_accounts_authenticator_read; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY provider_accounts_authenticator_read ON public.provider_accounts FOR SELECT TO authenticator USING ((tenant_id IS NULL));


--
-- Name: provider_accounts provider_accounts_read; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY provider_accounts_read ON public.provider_accounts FOR SELECT TO app_user USING (((tenant_id IS NULL) OR (tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif"))));


--
-- Name: role_permissions; Type: ROW SECURITY; Schema: public; Owner: migrator
--

ALTER TABLE public.role_permissions ENABLE ROW LEVEL SECURITY;

--
-- Name: role_permissions role_permissions_provisioner_insert; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY role_permissions_provisioner_insert ON public.role_permissions FOR INSERT TO provisioner WITH CHECK ((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")));


--
-- Name: role_template_permissions; Type: ROW SECURITY; Schema: public; Owner: migrator
--

ALTER TABLE public.role_template_permissions ENABLE ROW LEVEL SECURITY;

--
-- Name: role_template_permissions role_template_permissions_provisioner_read; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY role_template_permissions_provisioner_read ON public.role_template_permissions FOR SELECT TO provisioner USING (true);


--
-- Name: role_templates; Type: ROW SECURITY; Schema: public; Owner: migrator
--

ALTER TABLE public.role_templates ENABLE ROW LEVEL SECURITY;

--
-- Name: role_templates role_templates_provisioner_read; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY role_templates_provisioner_read ON public.role_templates FOR SELECT TO provisioner USING (true);


--
-- Name: roles; Type: ROW SECURITY; Schema: public; Owner: migrator
--

ALTER TABLE public.roles ENABLE ROW LEVEL SECURITY;

--
-- Name: roles roles_provisioner_insert; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY roles_provisioner_insert ON public.roles FOR INSERT TO provisioner WITH CHECK (((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")) AND is_system));


--
-- Name: roles roles_provisioner_select; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY roles_provisioner_select ON public.roles FOR SELECT TO provisioner USING ((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")));


--
-- Name: scope_assignments scope_assignment_insert; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY scope_assignment_insert ON public.scope_assignments FOR INSERT TO app_user WITH CHECK (((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")) AND COALESCE(( SELECT (NULLIF(current_setting('app.can_manage_scope'::text, true), ''::text))::boolean AS "nullif"), false)));


--
-- Name: scope_assignments scope_assignment_read; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY scope_assignment_read ON public.scope_assignments FOR SELECT TO app_user USING (((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")) AND (COALESCE(( SELECT (NULLIF(current_setting('app.can_manage_scope'::text, true), ''::text))::boolean AS "nullif"), false) OR (membership_id = ( SELECT (NULLIF(current_setting('app.membership_id'::text, true), ''::text))::uuid AS "nullif")))));


--
-- Name: scope_assignments scope_assignment_update; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY scope_assignment_update ON public.scope_assignments FOR UPDATE TO app_user USING (((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")) AND COALESCE(( SELECT (NULLIF(current_setting('app.can_manage_scope'::text, true), ''::text))::boolean AS "nullif"), false))) WITH CHECK (((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")) AND COALESCE(( SELECT (NULLIF(current_setting('app.can_manage_scope'::text, true), ''::text))::boolean AS "nullif"), false)));


--
-- Name: scope_assignments; Type: ROW SECURITY; Schema: public; Owner: migrator
--

ALTER TABLE public.scope_assignments ENABLE ROW LEVEL SECURITY;

--
-- Name: scope_assignments scope_assignments_provisioner_disable; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY scope_assignments_provisioner_disable ON public.scope_assignments FOR UPDATE TO provisioner USING (((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")) AND (EXISTS ( SELECT 1
   FROM public.memberships m
  WHERE (((m.id)::uuid = scope_assignments.membership_id) AND (m.status = 'left'::text)))))) WITH CHECK (((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")) AND (EXISTS ( SELECT 1
   FROM public.memberships m
  WHERE (((m.id)::uuid = scope_assignments.membership_id) AND (m.status = 'left'::text)))) AND (NOT active)));


--
-- Name: scope_assignments scope_assignments_provisioner_select; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY scope_assignments_provisioner_select ON public.scope_assignments FOR SELECT TO provisioner USING (((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")) AND (EXISTS ( SELECT 1
   FROM public.memberships m
  WHERE (((m.id)::uuid = scope_assignments.membership_id) AND (m.status = 'left'::text))))));


--
-- Name: subcategories; Type: ROW SECURITY; Schema: public; Owner: migrator
--

ALTER TABLE public.subcategories ENABLE ROW LEVEL SECURITY;

--
-- Name: subcategories subcategories_read; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY subcategories_read ON public.subcategories FOR SELECT TO app_user, job_runner, provisioner USING (true);


--
-- Name: subscriptions; Type: ROW SECURITY; Schema: public; Owner: migrator
--

ALTER TABLE public.subscriptions ENABLE ROW LEVEL SECURITY;

--
-- Name: subscriptions subscriptions_status_gate; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY subscriptions_status_gate ON public.subscriptions AS RESTRICTIVE FOR UPDATE TO app_user USING ((status = 'active'::text)) WITH CHECK (true);


--
-- Name: roles system_role_guard_delete; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY system_role_guard_delete ON public.roles AS RESTRICTIVE FOR DELETE TO app_user USING ((NOT is_system));


--
-- Name: roles system_role_guard_insert; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY system_role_guard_insert ON public.roles AS RESTRICTIVE FOR INSERT TO app_user WITH CHECK ((NOT is_system));


--
-- Name: roles system_role_guard_update; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY system_role_guard_update ON public.roles AS RESTRICTIVE FOR UPDATE TO app_user USING ((NOT is_system)) WITH CHECK ((NOT is_system));


--
-- Name: clients tenant_isolation; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY tenant_isolation ON public.clients TO app_user USING ((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif"))) WITH CHECK ((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")));


--
-- Name: membership_roles tenant_isolation; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY tenant_isolation ON public.membership_roles TO app_user USING ((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif"))) WITH CHECK ((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")));


--
-- Name: role_permissions tenant_isolation; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY tenant_isolation ON public.role_permissions TO app_user USING ((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif"))) WITH CHECK ((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")));


--
-- Name: roles tenant_isolation; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY tenant_isolation ON public.roles TO app_user USING ((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif"))) WITH CHECK ((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")));


--
-- Name: subscriptions tenant_isolation; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY tenant_isolation ON public.subscriptions TO app_user USING ((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif"))) WITH CHECK ((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")));


--
-- Name: tenant_modules tenant_isolation; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY tenant_isolation ON public.tenant_modules TO app_user USING ((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif"))) WITH CHECK ((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")));


--
-- Name: tenant_modules; Type: ROW SECURITY; Schema: public; Owner: migrator
--

ALTER TABLE public.tenant_modules ENABLE ROW LEVEL SECURITY;

--
-- Name: tenant_modules tenant_modules_provisioner_insert; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY tenant_modules_provisioner_insert ON public.tenant_modules FOR INSERT TO provisioner WITH CHECK ((tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")));


--
-- Name: tenants tenant_visible_to_member; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY tenant_visible_to_member ON public.tenants FOR SELECT TO app_user USING ((EXISTS ( SELECT 1
   FROM public.memberships m
  WHERE ((m.tenant_id = (tenants.id)::uuid) AND (m.user_id = ( SELECT (NULLIF(current_setting('app.user_id'::text, true), ''::text))::uuid AS "nullif"))))));


--
-- Name: tenants; Type: ROW SECURITY; Schema: public; Owner: migrator
--

ALTER TABLE public.tenants ENABLE ROW LEVEL SECURITY;

--
-- Name: tenants tenants_for_jobs; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY tenants_for_jobs ON public.tenants FOR SELECT TO job_runner USING ((status = 'active'::text));


--
-- Name: tenants tenants_provisioner_insert; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY tenants_provisioner_insert ON public.tenants FOR INSERT TO provisioner WITH CHECK (((id)::uuid = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif")));


--
-- Name: user_password_credentials; Type: ROW SECURITY; Schema: public; Owner: migrator
--

ALTER TABLE public.user_password_credentials ENABLE ROW LEVEL SECURITY;

--
-- Name: user_password_credentials user_password_credentials_authenticator_read; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY user_password_credentials_authenticator_read ON public.user_password_credentials FOR SELECT TO authenticator USING (true);


--
-- Name: user_password_credentials user_password_credentials_provisioner_insert; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY user_password_credentials_provisioner_insert ON public.user_password_credentials FOR INSERT TO provisioner WITH CHECK (true);


--
-- Name: users user_self_read; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY user_self_read ON public.users FOR SELECT TO app_user USING (((id)::uuid = ( SELECT (NULLIF(current_setting('app.user_id'::text, true), ''::text))::uuid AS "nullif")));


--
-- Name: users user_self_update; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY user_self_update ON public.users FOR UPDATE TO app_user USING (((id)::uuid = ( SELECT (NULLIF(current_setting('app.user_id'::text, true), ''::text))::uuid AS "nullif"))) WITH CHECK (((id)::uuid = ( SELECT (NULLIF(current_setting('app.user_id'::text, true), ''::text))::uuid AS "nullif")));


--
-- Name: users user_visible_via_membership; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY user_visible_via_membership ON public.users FOR SELECT TO app_user USING ((EXISTS ( SELECT 1
   FROM public.memberships m
  WHERE ((m.user_id = (users.id)::uuid) AND (m.tenant_id = ( SELECT (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid AS "nullif"))))));


--
-- Name: users; Type: ROW SECURITY; Schema: public; Owner: migrator
--

ALTER TABLE public.users ENABLE ROW LEVEL SECURITY;

--
-- Name: users users_authenticator_read; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY users_authenticator_read ON public.users FOR SELECT TO authenticator USING (true);


--
-- Name: users users_provisioner_insert; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY users_provisioner_insert ON public.users FOR INSERT TO provisioner WITH CHECK (true);


--
-- Name: users users_provisioner_select; Type: POLICY; Schema: public; Owner: migrator
--

CREATE POLICY users_provisioner_select ON public.users FOR SELECT TO provisioner USING (true);


--
-- Name: SCHEMA public; Type: ACL; Schema: -; Owner: pg_database_owner
--

REVOKE USAGE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO app_user;
GRANT USAGE ON SCHEMA public TO authenticator;
GRANT USAGE ON SCHEMA public TO job_runner;
GRANT USAGE ON SCHEMA public TO provisioner;


--
-- Name: PROCEDURE prune_auth_attempts(); Type: ACL; Schema: public; Owner: migrator
--

REVOKE ALL ON PROCEDURE public.prune_auth_attempts() FROM PUBLIC;


--
-- Name: PROCEDURE prune_password_reset_tokens(); Type: ACL; Schema: public; Owner: migrator
--

REVOKE ALL ON PROCEDURE public.prune_password_reset_tokens() FROM PUBLIC;


--
-- Name: TABLE audit_log; Type: ACL; Schema: public; Owner: migrator
--

GRANT SELECT,INSERT ON TABLE public.audit_log TO app_user;
GRANT INSERT ON TABLE public.audit_log TO provisioner;


--
-- Name: TABLE auth_attempts; Type: ACL; Schema: public; Owner: migrator
--

GRANT SELECT,INSERT ON TABLE public.auth_attempts TO authenticator;


--
-- Name: TABLE categories; Type: ACL; Schema: public; Owner: migrator
--

GRANT SELECT ON TABLE public.categories TO app_user;
GRANT SELECT ON TABLE public.categories TO provisioner;


--
-- Name: TABLE clients; Type: ACL; Schema: public; Owner: migrator
--

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE public.clients TO app_user;


--
-- Name: TABLE invitations; Type: ACL; Schema: public; Owner: migrator
--

GRANT SELECT,INSERT ON TABLE public.invitations TO app_user;
GRANT SELECT,INSERT ON TABLE public.invitations TO provisioner;


--
-- Name: COLUMN invitations.status; Type: ACL; Schema: public; Owner: migrator
--

GRANT UPDATE(status) ON TABLE public.invitations TO app_user;
GRANT UPDATE(status) ON TABLE public.invitations TO provisioner;


--
-- Name: TABLE membership_auth; Type: ACL; Schema: public; Owner: migrator
--

GRANT SELECT ON TABLE public.membership_auth TO app_user;
GRANT SELECT ON TABLE public.membership_auth TO authenticator;
GRANT INSERT ON TABLE public.membership_auth TO provisioner;


--
-- Name: TABLE membership_roles; Type: ACL; Schema: public; Owner: migrator
--

GRANT SELECT,INSERT,DELETE ON TABLE public.membership_roles TO app_user;
GRANT SELECT,INSERT,DELETE ON TABLE public.membership_roles TO provisioner;


--
-- Name: TABLE membership_scope; Type: ACL; Schema: public; Owner: migrator
--

GRANT SELECT ON TABLE public.membership_scope TO app_user;
GRANT SELECT,INSERT ON TABLE public.membership_scope TO provisioner;


--
-- Name: COLUMN membership_scope.scope_mode; Type: ACL; Schema: public; Owner: migrator
--

GRANT UPDATE(scope_mode) ON TABLE public.membership_scope TO app_user;
GRANT UPDATE(scope_mode) ON TABLE public.membership_scope TO provisioner;


--
-- Name: TABLE memberships; Type: ACL; Schema: public; Owner: migrator
--

GRANT SELECT ON TABLE public.memberships TO app_user;
GRANT SELECT,INSERT ON TABLE public.memberships TO provisioner;


--
-- Name: COLUMN memberships.status; Type: ACL; Schema: public; Owner: migrator
--

GRANT UPDATE(status) ON TABLE public.memberships TO app_user;
GRANT UPDATE(status) ON TABLE public.memberships TO provisioner;


--
-- Name: TABLE modules; Type: ACL; Schema: public; Owner: migrator
--

GRANT SELECT ON TABLE public.modules TO app_user;
GRANT SELECT ON TABLE public.modules TO provisioner;


--
-- Name: TABLE password_reset_tokens; Type: ACL; Schema: public; Owner: migrator
--

GRANT SELECT,INSERT ON TABLE public.password_reset_tokens TO authenticator;


--
-- Name: COLUMN password_reset_tokens.used_at; Type: ACL; Schema: public; Owner: migrator
--

GRANT UPDATE(used_at) ON TABLE public.password_reset_tokens TO authenticator;


--
-- Name: TABLE permissions; Type: ACL; Schema: public; Owner: migrator
--

GRANT SELECT ON TABLE public.permissions TO app_user;
GRANT SELECT ON TABLE public.permissions TO provisioner;


--
-- Name: TABLE persons; Type: ACL; Schema: public; Owner: migrator
--

GRANT SELECT ON TABLE public.persons TO app_user;
GRANT SELECT,INSERT ON TABLE public.persons TO provisioner;


--
-- Name: COLUMN persons.id; Type: ACL; Schema: public; Owner: migrator
--

GRANT SELECT(id) ON TABLE public.persons TO authenticator;


--
-- Name: COLUMN persons.email; Type: ACL; Schema: public; Owner: migrator
--

GRANT SELECT(email) ON TABLE public.persons TO authenticator;


--
-- Name: TABLE provider_accounts; Type: ACL; Schema: public; Owner: migrator
--

GRANT SELECT ON TABLE public.provider_accounts TO app_user;
GRANT SELECT ON TABLE public.provider_accounts TO authenticator;


--
-- Name: TABLE role_permissions; Type: ACL; Schema: public; Owner: migrator
--

GRANT SELECT,INSERT,DELETE ON TABLE public.role_permissions TO app_user;
GRANT INSERT ON TABLE public.role_permissions TO provisioner;


--
-- Name: TABLE role_template_permissions; Type: ACL; Schema: public; Owner: migrator
--

GRANT SELECT ON TABLE public.role_template_permissions TO provisioner;


--
-- Name: TABLE role_templates; Type: ACL; Schema: public; Owner: migrator
--

GRANT SELECT ON TABLE public.role_templates TO provisioner;


--
-- Name: TABLE roles; Type: ACL; Schema: public; Owner: migrator
--

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE public.roles TO app_user;
GRANT SELECT,INSERT ON TABLE public.roles TO provisioner;


--
-- Name: TABLE scope_assignments; Type: ACL; Schema: public; Owner: migrator
--

GRANT SELECT,INSERT ON TABLE public.scope_assignments TO app_user;
GRANT SELECT ON TABLE public.scope_assignments TO provisioner;


--
-- Name: COLUMN scope_assignments.active; Type: ACL; Schema: public; Owner: migrator
--

GRANT UPDATE(active) ON TABLE public.scope_assignments TO app_user;
GRANT UPDATE(active) ON TABLE public.scope_assignments TO provisioner;


--
-- Name: TABLE subcategories; Type: ACL; Schema: public; Owner: migrator
--

GRANT SELECT ON TABLE public.subcategories TO app_user;
GRANT SELECT ON TABLE public.subcategories TO provisioner;


--
-- Name: TABLE subscriptions; Type: ACL; Schema: public; Owner: migrator
--

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE public.subscriptions TO app_user;


--
-- Name: TABLE tenant_modules; Type: ACL; Schema: public; Owner: migrator
--

GRANT SELECT ON TABLE public.tenant_modules TO app_user;
GRANT INSERT ON TABLE public.tenant_modules TO provisioner;


--
-- Name: COLUMN tenant_modules.is_active; Type: ACL; Schema: public; Owner: migrator
--

GRANT UPDATE(is_active) ON TABLE public.tenant_modules TO app_user;


--
-- Name: TABLE tenants; Type: ACL; Schema: public; Owner: migrator
--

GRANT SELECT ON TABLE public.tenants TO app_user;
GRANT INSERT ON TABLE public.tenants TO provisioner;
GRANT SELECT ON TABLE public.tenants TO job_runner;


--
-- Name: TABLE user_password_credentials; Type: ACL; Schema: public; Owner: migrator
--

GRANT SELECT ON TABLE public.user_password_credentials TO authenticator;
GRANT INSERT ON TABLE public.user_password_credentials TO provisioner;


--
-- Name: COLUMN user_password_credentials.password_hash; Type: ACL; Schema: public; Owner: migrator
--

GRANT UPDATE(password_hash) ON TABLE public.user_password_credentials TO app_user;


--
-- Name: COLUMN user_password_credentials.updated_at; Type: ACL; Schema: public; Owner: migrator
--

GRANT UPDATE(updated_at) ON TABLE public.user_password_credentials TO app_user;


--
-- Name: TABLE users; Type: ACL; Schema: public; Owner: migrator
--

GRANT SELECT ON TABLE public.users TO app_user;
GRANT SELECT ON TABLE public.users TO authenticator;
GRANT SELECT,INSERT ON TABLE public.users TO provisioner;


--
-- Name: COLUMN users.language; Type: ACL; Schema: public; Owner: migrator
--

GRANT UPDATE(language) ON TABLE public.users TO app_user;


--
-- Name: COLUMN users.theme; Type: ACL; Schema: public; Owner: migrator
--

GRANT UPDATE(theme) ON TABLE public.users TO app_user;


--
-- Name: COLUMN users.last_login_at; Type: ACL; Schema: public; Owner: migrator
--

GRANT UPDATE(last_login_at) ON TABLE public.users TO app_user;


--
-- Name: COLUMN users.security_stamp; Type: ACL; Schema: public; Owner: migrator
--

GRANT UPDATE(security_stamp) ON TABLE public.users TO app_user;


--
-- PostgreSQL database dump complete
--

\unrestrict platformcoreschema

