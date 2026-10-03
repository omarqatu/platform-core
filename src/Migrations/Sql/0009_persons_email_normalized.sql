-- Invitation acceptance (option C, decided by the project owner; OPEN_ITEMS 41): one person per normalized address.
-- persons_email_key (§3.3) is exact, so 'Omar@x.test' could register beside 'omar@x.test'. This index refuses it
-- (23505) on every path that creates a person. The application compares addresses under the same rule —
-- surrounding spaces removed, case unified (Core.EmailAddress.Normalize) — and its existence check is written in
-- this index's expression. persons_email_key stays. Run both ways on PostgreSQL 18 as P3 of
-- docs/proposals/v1.17-acceptance before adoption (PROOF_SPEC rule 11).
CREATE UNIQUE INDEX persons_email_normalized_key ON persons (lower(btrim(email)));
