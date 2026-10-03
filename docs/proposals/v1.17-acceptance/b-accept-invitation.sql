-- Alternative B (the owner's request): ONE SECURITY DEFINER function for the acceptance by an existing account —
-- in place of P1, P1b, P2 and P4. No new policy, no new chain, no new column, bootstrap unchanged. P3 (one person per
-- normalized address) is applied with it, as in the draft.
--
-- public.accept_invitation(tenant_id, token, ip_address), called by app_user on the tenant-selection path (app.user_id
-- alone, like GET /me): the acceptor is app.user_id — never an argument. In one transaction (the caller's):
--   1. the invitation of this token IN THIS TENANT (T4.11), locked FOR UPDATE — a second acceptance of the same token
--      waits here, then finds it consumed (condition 5);
--      none, not pending, or expired → P0002 (one refusal for the three: the screen's single message);
--   2. the acceptor's address against the invitation's, normalized lower(btrim(…)) → otherwise 42501 (condition 2);
--   3. the membership in that tenant: none → a new one with its role, membership_scope in the invitation's
--      intended_scope_mode and membership_auth 'password' (condition 4, T3.8); 'left' → the return of 3.10 in its
--      binding order; active or disabled → 23505 (condition 3);
--   4. the invitation accepted WHERE status = 'pending';
--   5. one audit_log entry per row written, in the interceptor's shape (7) — these writes are not EF's, so the
--      automatic audit does not see them; token_hash masked.
-- It does not select the tenant (condition 6): it sets no app.tenant_id; the cookie is the application's.
--
-- Owner: migrator (the owner of every object; BYPASSRLS) — inside it RLS does not apply, so every rule the policies
-- would have enforced is written in its body. search_path pinned, pg_temp last; every name schema-qualified.
-- EXECUTE: revoked from PUBLIC, granted to app_user alone.
CREATE FUNCTION public.accept_invitation(p_tenant_id uuid, p_token text, p_ip_address text DEFAULT NULL)
  RETURNS TABLE (accepted_membership_id uuid, returned boolean)
  LANGUAGE plpgsql SECURITY DEFINER
  SET search_path = pg_catalog, public, pg_temp
AS $$
DECLARE
  v_user  uuid := nullif(current_setting('app.user_id', true), '')::uuid;
  v_inv   public.invitations%ROWTYPE;
  v_email text;
  v_m     public.memberships%ROWTYPE;
  v_scope public.membership_scope%ROWTYPE;
  v_old   jsonb;
  v_row   jsonb;
  v_now   timestamptz := now();
  v_id    uuid;
BEGIN
  IF v_user IS NULL THEN
    RAISE EXCEPTION USING ERRCODE = '42501', MESSAGE = 'accept_invitation: no app.user_id';
  END IF;

  -- 1. the token, in this tenant, locked
  SELECT * INTO v_inv FROM public.invitations i
   WHERE i.tenant_id = p_tenant_id AND i.token_hash = encode(sha256(convert_to(p_token, 'UTF8')), 'hex')
   FOR UPDATE;
  IF NOT FOUND OR v_inv.status <> 'pending' OR v_inv.expires_at <= v_now THEN
    RAISE EXCEPTION USING ERRCODE = 'P0002', MESSAGE = 'invalid_invitation';
  END IF;

  -- 2. the acceptor's address
  SELECT p.email INTO v_email FROM public.users u JOIN public.persons p ON p.id = u.person_id WHERE u.id = v_user;
  IF v_email IS NULL OR lower(btrim(v_email)) <> lower(btrim(v_inv.email)) THEN
    RAISE EXCEPTION USING ERRCODE = '42501', MESSAGE = 'email_mismatch';
  END IF;

  -- 3. the membership
  SELECT * INTO v_m FROM public.memberships m WHERE m.tenant_id = v_inv.tenant_id AND m.user_id = v_user FOR UPDATE;
  IF NOT FOUND THEN
    v_m.id := uuidv7(); v_m.tenant_id := v_inv.tenant_id; v_m.user_id := v_user; v_m.status := 'active'; v_m.created_at := v_now;
    INSERT INTO public.memberships (id, tenant_id, user_id, status, created_at)
      VALUES (v_m.id, v_m.tenant_id, v_m.user_id, v_m.status, v_m.created_at);
    INSERT INTO public.audit_log (id, tenant_id, actor_id, actor_type, action, entity_type, entity_id, old_value, new_value, ip_address, created_at)
      VALUES (uuidv7(), v_inv.tenant_id, v_user, 'user', 'insert', 'memberships', v_m.id, NULL, to_jsonb(v_m), p_ip_address, v_now);

    v_id := uuidv7();
    INSERT INTO public.membership_roles AS r (id, tenant_id, membership_id, role_id)
      VALUES (v_id, v_inv.tenant_id, v_m.id, v_inv.role_id) RETURNING to_jsonb(r.*) INTO v_row;
    INSERT INTO public.audit_log (id, tenant_id, actor_id, actor_type, action, entity_type, entity_id, old_value, new_value, ip_address, created_at)
      VALUES (uuidv7(), v_inv.tenant_id, v_user, 'user', 'insert', 'membership_roles', v_id, NULL, v_row, p_ip_address, v_now);

    v_id := uuidv7();
    INSERT INTO public.membership_scope AS s (id, tenant_id, membership_id, scope_mode)
      VALUES (v_id, v_inv.tenant_id, v_m.id, v_inv.intended_scope_mode) RETURNING to_jsonb(s.*) INTO v_row;
    INSERT INTO public.audit_log (id, tenant_id, actor_id, actor_type, action, entity_type, entity_id, old_value, new_value, ip_address, created_at)
      VALUES (uuidv7(), v_inv.tenant_id, v_user, 'user', 'insert', 'membership_scope', v_id, NULL, v_row, p_ip_address, v_now);

    v_id := uuidv7();
    INSERT INTO public.membership_auth AS a (id, tenant_id, membership_id, provider, provider_config)
      VALUES (v_id, v_inv.tenant_id, v_m.id, 'password', NULL) RETURNING to_jsonb(a.*) INTO v_row;
    INSERT INTO public.audit_log (id, tenant_id, actor_id, actor_type, action, entity_type, entity_id, old_value, new_value, ip_address, created_at)
      VALUES (uuidv7(), v_inv.tenant_id, v_user, 'user', 'insert', 'membership_auth', v_id, NULL, v_row, p_ip_address, v_now);
    returned := false;

  ELSIF v_m.status = 'left' THEN
    -- The return (3.10, D9, D10), in its binding order; membership_auth: the existing row is reused.
    FOR v_row IN DELETE FROM public.membership_roles r WHERE r.membership_id = v_m.id RETURNING to_jsonb(r.*) LOOP
      INSERT INTO public.audit_log (id, tenant_id, actor_id, actor_type, action, entity_type, entity_id, old_value, new_value, ip_address, created_at)
        VALUES (uuidv7(), v_inv.tenant_id, v_user, 'user', 'delete', 'membership_roles', (v_row->>'id')::uuid, v_row, NULL, p_ip_address, v_now);
    END LOOP;

    SELECT * INTO v_scope FROM public.membership_scope s WHERE s.membership_id = v_m.id FOR UPDATE;
    IF NOT FOUND THEN
      RAISE EXCEPTION USING ERRCODE = 'P0001', MESSAGE = 'membership_scope_missing';
    END IF;
    UPDATE public.membership_scope s SET scope_mode = v_inv.intended_scope_mode WHERE s.id = v_scope.id RETURNING to_jsonb(s.*) INTO v_row;
    INSERT INTO public.audit_log (id, tenant_id, actor_id, actor_type, action, entity_type, entity_id, old_value, new_value, ip_address, created_at)
      VALUES (uuidv7(), v_inv.tenant_id, v_user, 'user', 'update', 'membership_scope', v_scope.id, to_jsonb(v_scope), v_row, p_ip_address, v_now);

    FOR v_row IN UPDATE public.scope_assignments sa SET active = false WHERE sa.membership_id = v_m.id AND sa.active
                 RETURNING to_jsonb(sa.*) LOOP
      INSERT INTO public.audit_log (id, tenant_id, actor_id, actor_type, action, entity_type, entity_id, old_value, new_value, ip_address, created_at)
        VALUES (uuidv7(), v_inv.tenant_id, v_user, 'user', 'update', 'scope_assignments', (v_row->>'id')::uuid,
                v_row || '{"active": true}', v_row, p_ip_address, v_now);
    END LOOP;

    v_id := uuidv7();
    INSERT INTO public.membership_roles AS r (id, tenant_id, membership_id, role_id)
      VALUES (v_id, v_inv.tenant_id, v_m.id, v_inv.role_id) RETURNING to_jsonb(r.*) INTO v_row;
    INSERT INTO public.audit_log (id, tenant_id, actor_id, actor_type, action, entity_type, entity_id, old_value, new_value, ip_address, created_at)
      VALUES (uuidv7(), v_inv.tenant_id, v_user, 'user', 'insert', 'membership_roles', v_id, NULL, v_row, p_ip_address, v_now);

    UPDATE public.memberships m SET status = 'active' WHERE m.id = v_m.id RETURNING to_jsonb(m.*) INTO v_row;
    INSERT INTO public.audit_log (id, tenant_id, actor_id, actor_type, action, entity_type, entity_id, old_value, new_value, ip_address, created_at)
      VALUES (uuidv7(), v_inv.tenant_id, v_user, 'user', 'update', 'memberships', v_m.id, to_jsonb(v_m), v_row, p_ip_address, v_now);
    returned := true;

  ELSE
    RAISE EXCEPTION USING ERRCODE = '23505', MESSAGE = 'already_member';
  END IF;

  -- 4. the token consumed
  v_old := to_jsonb(v_inv) || '{"token_hash": "[masked]"}';
  UPDATE public.invitations i SET status = 'accepted' WHERE i.id = v_inv.id AND i.status = 'pending'
    RETURNING to_jsonb(i.*) || '{"token_hash": "[masked]"}' INTO v_row;
  INSERT INTO public.audit_log (id, tenant_id, actor_id, actor_type, action, entity_type, entity_id, old_value, new_value, ip_address, created_at)
    VALUES (uuidv7(), v_inv.tenant_id, v_user, 'user', 'update', 'invitations', v_inv.id, v_old, v_row, p_ip_address, v_now);

  accepted_membership_id := v_m.id;
  RETURN NEXT;
END $$;

REVOKE EXECUTE ON FUNCTION public.accept_invitation(uuid, text, text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.accept_invitation(uuid, text, text) TO app_user;
