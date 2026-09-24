-- T2, layer 4 — constraints: which relationships between rows are legitimate
-- (PLATFORM_CORE v1.14 §1, §3.3). FK checks run with the owner's privileges and are
-- not subject to RLS, so this layer closes what policies and grants cannot see.

-- ===== Uniques
-- The one exception family: cross-tenant identity identifiers, globally unique (§3.3).
ALTER TABLE persons ADD CONSTRAINT persons_email_key UNIQUE (email);
ALTER TABLE users ADD CONSTRAINT users_username_key UNIQUE (username);

-- Tenant-scoped uniques (§4.1, 4.8, 5).
ALTER TABLE memberships ADD CONSTRAINT memberships_tenant_id_user_id_key UNIQUE (tenant_id, user_id);
ALTER TABLE membership_scope ADD CONSTRAINT membership_scope_tenant_id_membership_id_key UNIQUE (tenant_id, membership_id);
-- One permanent row per (membership, entity), disabled and reactivated (§4.8, 1.8).
-- Also the index the second template reads through (§3.2, 4.1).
ALTER TABLE scope_assignments ADD CONSTRAINT scope_assignments_tenant_id_membership_id_scope_ref_id_key
  UNIQUE (tenant_id, membership_id, scope_ref_id);
ALTER TABLE tenant_modules ADD CONSTRAINT tenant_modules_tenant_id_module_id_key UNIQUE (tenant_id, module_id);

-- The base for every composite FK: UNIQUE (tenant_id, id) on each parent (§3.3).
ALTER TABLE memberships ADD CONSTRAINT memberships_tenant_id_id_key UNIQUE (tenant_id, id);
ALTER TABLE roles ADD CONSTRAINT roles_tenant_id_id_key UNIQUE (tenant_id, id);

-- ===== Composite FKs: every FK between two tables that both carry tenant_id (§3.3, Check 7)
ALTER TABLE membership_roles ADD CONSTRAINT membership_roles_membership_fkey
  FOREIGN KEY (tenant_id, membership_id) REFERENCES memberships (tenant_id, id);
ALTER TABLE membership_roles ADD CONSTRAINT membership_roles_role_fkey
  FOREIGN KEY (tenant_id, role_id) REFERENCES roles (tenant_id, id);
ALTER TABLE invitations ADD CONSTRAINT invitations_role_fkey
  FOREIGN KEY (tenant_id, role_id) REFERENCES roles (tenant_id, id);
ALTER TABLE membership_auth ADD CONSTRAINT membership_auth_membership_fkey
  FOREIGN KEY (tenant_id, membership_id) REFERENCES memberships (tenant_id, id);
ALTER TABLE membership_scope ADD CONSTRAINT membership_scope_membership_fkey
  FOREIGN KEY (tenant_id, membership_id) REFERENCES memberships (tenant_id, id);
-- The assignee is a member of this tenant — guaranteed by the database (§3.3, 4.8).
ALTER TABLE scope_assignments ADD CONSTRAINT scope_assignments_membership_fkey
  FOREIGN KEY (tenant_id, membership_id) REFERENCES memberships (tenant_id, id);
ALTER TABLE role_permissions ADD CONSTRAINT role_permissions_role_fkey
  FOREIGN KEY (tenant_id, role_id) REFERENCES roles (tenant_id, id);
-- scope_assignments.scope_ref_id: no FK, a declared gap until the entity's home is decided (§3.3, 9).

-- ===== Single-column FKs toward tables with no tenant_id (§3.3: "remains single-column")
ALTER TABLE users ADD CONSTRAINT users_person_fkey FOREIGN KEY (person_id) REFERENCES persons (id);
ALTER TABLE user_password_credentials ADD CONSTRAINT user_password_credentials_user_fkey FOREIGN KEY (user_id) REFERENCES users (id);
ALTER TABLE memberships ADD CONSTRAINT memberships_user_fkey FOREIGN KEY (user_id) REFERENCES users (id);
ALTER TABLE permissions ADD CONSTRAINT permissions_module_fkey FOREIGN KEY (module_id) REFERENCES modules (id);
ALTER TABLE tenant_modules ADD CONSTRAINT tenant_modules_module_fkey FOREIGN KEY (module_id) REFERENCES modules (id);
ALTER TABLE role_permissions ADD CONSTRAINT role_permissions_permission_fkey FOREIGN KEY (permission_id) REFERENCES permissions (id);
ALTER TABLE role_template_permissions ADD CONSTRAINT role_template_permissions_template_fkey FOREIGN KEY (template_id) REFERENCES role_templates (id);
ALTER TABLE role_template_permissions ADD CONSTRAINT role_template_permissions_permission_fkey FOREIGN KEY (permission_id) REFERENCES permissions (id);

-- The tenant itself, on the tables whose rows hang directly off it. The other tenant
-- tables reach tenants through their composite FKs to memberships and roles.
ALTER TABLE memberships ADD CONSTRAINT memberships_tenant_fkey FOREIGN KEY (tenant_id) REFERENCES tenants (id);
ALTER TABLE roles ADD CONSTRAINT roles_tenant_fkey FOREIGN KEY (tenant_id) REFERENCES tenants (id);
ALTER TABLE tenant_modules ADD CONSTRAINT tenant_modules_tenant_fkey FOREIGN KEY (tenant_id) REFERENCES tenants (id);
ALTER TABLE audit_log ADD CONSTRAINT audit_log_tenant_fkey FOREIGN KEY (tenant_id) REFERENCES tenants (id);
