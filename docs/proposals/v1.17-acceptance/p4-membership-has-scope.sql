-- P4 (condition 4 / T3.8, optional): no membership is committed without its membership_scope row, in any path. A
-- deferred constraint trigger, checked at commit; SECURITY DEFINER (owner: migrator) because the provisioner may not
-- read the scope of an active membership (membership_scope_provisioner_select: 'left' only). EXECUTE revoked from
-- PUBLIC (Check 6: no executable routine). A scope deleted later (e.g. a test fixture) is not this trigger's case.
CREATE FUNCTION membership_has_scope() RETURNS trigger
  LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, public AS $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM public.membership_scope s WHERE s.membership_id = NEW.id) THEN
    RAISE EXCEPTION USING ERRCODE = '23514', MESSAGE = format('membership %s has no membership_scope (T3.8)', NEW.id);
  END IF;
  RETURN NULL;
END $$;
REVOKE EXECUTE ON FUNCTION membership_has_scope() FROM PUBLIC;
CREATE CONSTRAINT TRIGGER memberships_have_scope
  AFTER INSERT ON memberships DEFERRABLE INITIALLY DEFERRED
  FOR EACH ROW EXECUTE FUNCTION membership_has_scope();
