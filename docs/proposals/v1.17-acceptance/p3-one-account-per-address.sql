-- P3 (the closing paragraph: with no session, an email that has an account cannot register again): one person per
-- normalized address. persons_email_key (exact) stays.
CREATE UNIQUE INDEX persons_email_normalized_key ON persons (lower(btrim(email)));
