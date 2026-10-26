# Group (ج) — four findings from the project owner's walk. Closing report.

Four changes merged and two open, 2026-10-24 to 2026-10-26:

| PR | Branch | Content |
|---|---|---|
| #47 | `claude/cool-hamilton-fesn45` | item 35 |
| #48 | `claude/item-37-sign-in-help` | item 37 |
| #49 | `claude/item-36-scope-role-warning` | item 36 |
| open | `claude/pr1-test-warnings` | PR 1's three compiler warnings |
| open | `claude/item-38-proposal` | item 38, a proposal only, with this report |

No time log was written as the work happened, as group (ب)'s was. The times in §2 are read from the commit
timestamps and are estimates of working time, with waiting for the project owner's decisions taken out. That is
itself a gap, and §4 does not hide it.

## 1. What was not on the plan

**The finding that led the group was not the item reported.** Item 35 was "the form accepts `adnan`". Probing
the sender showed something worse: `POST /api/members/invitations` accepted `a@x.com, b@y.com`, and .NET's
`MailMessage(string, string)` reads that as a list. **The acceptance link went to both addresses.** The invitation
limit (OPEN_ITEMS 106) counts invitations, not recipients, so a comma multiplied it, and the second address was one
nobody chose to invite.

Also unplanned:

| | |
|---|---|
| **The sender broke its own contract** | `adnan` threw `FormatException` out of `new MailMessage`, outside the sender's `try`. The dispatcher's catch-all dropped the message with one log line: the two production invitations went nowhere, silently |
| **The refusal had to reach acceptance too** | The two production rows were still live: the creator's screen had shown the link once, and anyone holding it could create an account whose address is `hasan`. Acceptance now refuses a stored undeliverable address (the project owner's addition) |
| **The same hole in two other writers** | `Ops create-tenant --owner-email` and `POST /api/provision/tenants` |
| **Two guards reached only through duplicate invitations** | Refusing `already_member` at creation left the acceptance's own guard reachable only by two invitations pending to one address. The tests now take that path, and OPEN_ITEMS 101 records that "resend" must keep one |
| **A role can be added to a member who left** (OPEN_ITEMS 113) | Measured: `204`. Read from `ReturnAsync`: the roles are deleted on return, so it is cosmetic, not an escalation |
| **An undeliverable member address has no remedy** (114) | There is no endpoint that changes a person's address |
| **The roles screen cannot warn** (116) | A members-only manager cannot read a colleague's mode |
| **T8.2 for the member list** | An `assigned` Admin sees the member count. Now a declared exception under OPEN_ITEMS 104, decided by the project owner |
| **`invitations` has no `UNIQUE (tenant_id, id)`** | Found while writing the item 38 proposal: its composite FK needs one |

## 2. Time, per item

Estimates from the commit timestamps, working time only (waiting for decisions excluded):

| Item | Time | Where it went |
|---|---|---|
| Reading and design | ≈ 1.5 h | the invitation, acceptance, sender and scope code; the `MailMessage` probe in Docker |
| 35 (#47) | ≈ 5 h | a loopback SMTP that records `RCPT TO`; red then green; the acceptance refusal; the two guards' new path; the runbook |
| 37 (#48) | ≈ 3.5 h | the in-memory limiter and the reason it cannot count from `audit_log`; the audit line; two e2e fixes in the test itself |
| 36 (#49) | ≈ 3 h | proving the `role_permissions` read as Rami; the two screens; a CI failure in my own Vitest test |
| 38 (proposal) | ≈ 45 min | reading the existing provisioner policies; the binding question (Q3) |
| PR 1's warnings | ≈ 15 min | |
| **Total** | **≈ 13 h** | of which ≈ 1 h restarting `dockerd`, which stopped between turns several times |

## 3. Numbers

| | Before (main `42189be`, CI) | After (main `53f4284`) |
|---|---|---|
| Conformance | 297 | **322** |
| White-box | 174 | **200** |
| Vitest | 515 | **540** |
| e2e | 34 | **41** |
| Ten catalog checks · local checks | green | **green** |

- **Conformance:** + 13 + 2 (#47) + 8 (#48) + 3 (#49) new cases, and − 1 case removed by #47's declared exception.
  That exception was the `"{0} @t4.test"` case of `ExistingAccount_AnotherAddress_IsRefused_NoMembership`; its claim
  survives in `NewAccount_AnotherAddress_IsRefused_NothingCreated`. 297 + 26 − 1 = 322. (§4b explains a base number
  I first got wrong.)
- **Red, then green:** every new test ran **red on the old code first, 10/10 runs**, then green 10/10. Three tests
  that changed after their red run were run red again, 10/10.
- **What stayed the same:** no migration, no grant, no policy, and no new error code (still 27).
- **Where it ran:** every suite ran in this container through Docker (`mcr.microsoft.com/dotnet/sdk:10.0`) against
  real PostgreSQL 18.6 with Mailpit, in CI's order on a clean database.

## 3b. What was not proven

- **The production provider.** The e-mails of items 35 and 37 are proven against Mailpit. Production's Gmail SMTP is
  untested by construction, as group (ب) recorded.
- **The limits across instances.** The sign-in-help limits are in memory: they reset on restart and count per
  instance (OPEN_ITEMS 112). Nothing tests more than one Api instance.
- **The OPEN_ITEMS 57 case.** Sign-in help cannot detect it (two active accounts behind one address), because
  `app_user` cannot see a person's accounts elsewhere (114).
- **The production rows.** Clearing `adnan` and `hasan` is a runbook step for the project owner
  (`docs/operations.md` › *Clearing an undeliverable invitation*); this group did not do it.

## 4. Three lessons

1. **Read Vitest's exit code, not its summary line.** *My own correction.* In #49 I reported "Vitest 540/540" when
   the run had ended `Errors 1` with exit code 1. "540 passed" sat above it, and I read only that line. CI caught it.
   I checked PR 1's and PR 2's CI logs: no `Errors` line, and `npm run build` ran after the tests, which happens only
   on exit 0.
2. **Read how a component behaves before writing its test.** Three tests changed after their red run, and each
   reason was already written in the code:
   - the scope radio saves at once, so Playwright's `check()` cannot work;
   - every member row carries a folded copy for phone width;
   - the success panel reads the session.
3. **Probe the layer beneath the symptom.** `adnan` was the symptom. Probing `MailMessage` itself found the link
   reaching two people. A form validator alone would have closed the reported case and left the real one open.

## 4b. My corrections, by name

- **To the brief:**
  - The sender already threw; the problem was that nothing could hear it.
  - `announce-migration` is withdrawn (Amendment 02 §0) and may be cited as precedent only. The project owner asked
    for the text, not the claim, and it was quoted.
  - Item 38 needs an amendment, not a migration.
- **To an instruction:** a row planted "inside a rolled-back transaction" cannot reach the Api over HTTP, because the
  Api reads on its own connection. It was committed in the test's own organization instead, the existing pattern,
  and declared in #47.
- **To my own work:**
  - #48's white-box count was 6, not the 7 I reported.
  - An exception the project owner approved on `MembersScreen.test.tsx` turned out unnecessary and was not applied.
  - Three e2e and Vitest tests changed after their red run, each run red again.
  - The Vitest exit code (lesson 1).
  - The item 38 proposal's first draft said Check 9 confines every `app.*` variable. It confines the four
    second-axis variables only.
- **A base number:** this report first gave Conformance **296** before the group, against group (ب)'s 297.
  - **The CI log on main at `42189be` reads `Total tests: 297`. Group (ب)'s number is right, and no test
    disappeared between #45 and `42189be`** (#46 adds four lines to `T7_BackgroundJobTests.cs` and removes nothing).
  - **The error was my subtraction:** I took a local total of 309 and subtracted #47's 13 new cases. But that total
    was counted **after** #47's declared exception had removed one theory case: 297 + 13 − 1 = 309, not 296 + 13.

## 5. Against the project owner's list, 35–38

| # | Item | State |
|---|---|---|
| **35** | The invitation's address | **Closed** — #47. API, acceptance, sender, Ops, bootstrap, screen; `already_member` at creation; the runbook |
| **36** | A warning on a combination that reads as a mistake | **Closed** — #49. Admin or Owner (by permission) with `assigned`; Viewer + `all` removed by decision. The roles-screen gap is OPEN_ITEMS 116 |
| **37** | The username column, and a sign-in-help button | **Closed** — #48. No token, no new permission; audited; two limits, in memory |
| **38** | Assign clients with the invitation | **Not built**, by the project owner's decision. The proposal for Amendment 03 is `docs/proposals/amendment-03-invitation-assignments.md`, with eight questions for him (Q1–Q8, Q3a) |

## 6. OPEN_ITEMS

- **Closed:** 110 (item 35), 112 (item 37, its built part), 115 (item 36).
- **Added and open:**
  - 111: the application's address shape is stricter than `clients_email_check`, by one declared difference;
  - 112: the in-memory limits;
  - 113: roles on a left member;
  - 114: no remedy for an undeliverable member address;
  - 116: the roles screen cannot warn.
- **Amended:**
  - 101: the acceptance guard depends on duplicate invitations;
  - 104: the T8.2 exception, decided by the project owner.
- **Not settled here:** 103, member visibility resting on an endpoint check alone, which remains the project owner's
  separate decision.
