-- T2b (PROOF_SPEC v1.2) — PLATFORM_CORE v1.15 §4.1: the four attribution columns leave the scope surface.
-- The audit log (§7) is the sole source of attribution: same transaction, append-only, actor and time
-- of every change. The dropped columns were a second source that drifted from it — updated_by and
-- updated_at stayed stale after an admin update (the grant is UPDATE (scope_mode) alone), and granted_by
-- could be forged at insert. No policy or grant referenced them (v1.15, Appendix A).
ALTER TABLE membership_scope DROP COLUMN updated_by, DROP COLUMN updated_at;
ALTER TABLE scope_assignments DROP COLUMN granted_by, DROP COLUMN granted_at;
