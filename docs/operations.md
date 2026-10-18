# Operations

Procedures that run outside the application. The first three are run by `migrator`, the only role that
holds them. The last section is the test server's runbook: how it is set up, deployed and backed up.

## `auth_attempts` retention (PROOF_SPEC v1.1 T2, spec item b)

`auth_attempts` is append-only and records usernames and IP addresses with no cap
(PLATFORM_CORE §4.1, 13/b). Rows older than **90 days** are deleted by a `migrator`
procedure, created by the migration `20260924000005_SeedAndRetention`:

```sql
CALL prune_auth_attempts();
```

- **Who runs it:** `migrator`. `EXECUTE` is revoked from `PUBLIC`, and no application role
  holds it. `migrator` has `BYPASSRLS`, so the delete sees every row under `FORCE ROW LEVEL
  SECURITY`.
- **When:** daily, from a scheduler outside the application (for example cron, or the
  platform's job scheduler), for example:
  ```bash
  psql "$MIGRATOR_CONNECTION" -c 'CALL prune_auth_attempts();'
  ```
- **The figure:** 90 days is the project owner's decision and is changed only by that
  decision, through a new migration.
- **Production requirement (spec item k, not built in the proof):** the scheduler that holds
  the `migrator` connection runs on the operations network. The application network cannot
  reach the `migrator` account (`pg_hba.conf`).

## The platform's delivery account (Amendment 01 §2.6)

Nothing is sent until `provider_accounts` holds the platform's row for the channel, and no application
role can write one: the rows are `migrator`'s, seeded. A deployment seeds its own once, as `migrator`:

```sql
INSERT INTO provider_accounts (id, tenant_id, channel, provider_code, sender_id, credential_ref, is_active, created_at)
VALUES (uuidv7(), NULL, 'email', 'smtp', 'no-reply@<your domain>', 'platform-email', true, now());
```

- **`tenant_id IS NULL` is the platform's row**, one per channel — `UNIQUE NULLS NOT DISTINCT` refuses a second.
  The per-tenant override is a reserved slot at this stage; no path writes one.
- **`credential_ref` is a settings key name, never a secret.** The row above names `platform-email`, and the Api
  reads the credentials from `Notifications:Email:Providers:platform-email` — `Host`, `Port`, `UseStartTls`,
  `Username`, `Password` — wherever that deployment keeps its settings, beside the connection strings. Put a
  secret in this table and every member of every tenant is one vulnerability away from it (§2.6).
- **Turning a channel off** is `UPDATE provider_accounts SET is_active = false …` as `migrator`. The sender then
  reports every send as failed and logs it; nothing throws, and no path that sends is broken by it.
- **After changing a row**, the Api holds it for `Notifications:ProviderAccounts:CacheFor` (5 minutes by
  default) before reading it again.

## `password_reset_tokens` retention (Amendment 01 §2.2)

The recovery tokens follow the same policy as `auth_attempts`: rows older than **90 days** are
deleted by a `migrator` procedure, created by the migration `20261017000010_PasswordReset`:

```sql
CALL prune_password_reset_tokens();
```

- **Who runs it:** `migrator` alone, exactly as above — `EXECUTE` is revoked from `PUBLIC`, and no
  application role holds `DELETE` on the table either (§3: `authenticator` has `SELECT`, `INSERT`
  and `UPDATE (used_at)`, nothing more).
- **When:** daily, beside the procedure above:
  ```bash
  psql "$MIGRATOR_CONNECTION" -c 'CALL prune_password_reset_tokens();'
  ```
- **What it does not do:** a token is consumed by being used (`used_at`), never by being deleted;
  this only clears the history afterwards. An unused token is already dead to the reset path once
  `expires_at` has passed.

## The test server: platform.ask-palestine.com

A runbook to follow in order, over SSH, from a phone if need be. **None of it has been run yet** — it was written
without access to the server. Every step ends with a **Check** (a command and what it must print) and a **Report
back** line: send those results, and anything that differed, before going on to the next step.

### Read this first: what the server is on day one

> **After a successful deploy, nobody can sign in.** The database is created empty: the migrations write the
> catalog and the templates, and no tenant and no user. The seed contract (`tests/seed/seed-contract.sql`) is
> **never** applied there — its users' passwords are published in this public repository. The only way to create
> the first tenant and its owner is `src/Ops create-tenant` (Amendment 01 §5.4), **which is not built yet**
> (OPEN_ITEMS 86); the bootstrap route exists in Development and CI only (§4.4). So on day one
> `https://platform.ask-palestine.com` shows the sign-in screen, and every sign-in fails. That is the expected
> result of this stage: it proves the infrastructure — nginx, Cloudflare, the memory limits, the networks, beside
> the eight containers already on the server — not the product.

### What runs there

| Container | Image | Published | Memory (`mem_limit` = `memswap_limit`) | `oom_score_adj` | CPUs |
|---|---|---|---|---|---|
| `platform-core-postgres-1` | `postgres:18` | nothing | 512 MB | 500 | 1.0 |
| `platform-core-api-1` | `ghcr.io/omarqatu/platform-core-api:<commit>` | `127.0.0.1:5100` only | 512 MB | 800 | 1.0 |
| `migrate` (one-shot, removed on exit) | `…/platform-core-migrations:<commit>` | nothing | 256 MB | 900 | 0.5 |
| `worker` (one-shot, removed on exit) | `…/platform-core-worker:<commit>` | nothing | 256 MB | 900 | 0.5 |

- **Memory: our containers die first, never a neighbour.** The server has 8 GB with about 4.3 GB free, a Metabase
  holding 1 GB, and Ask Palestine containers already reporting SIGKILL. `mem_limit` makes a spike in one of ours
  kill that container inside its own cgroup, before it reaches the host. `memswap_limit` equal to it keeps ours
  out of swap, so we do not slow the neighbours by pushing them into it. `oom_score_adj` makes the kernel choose
  ours first if the whole host runs out anyway — PostgreSQL last among ours, since it recovers from its WAL but
  losing it costs more. The ceiling is about 1 GB steady and 1.25 GB while a one-shot runs. .NET sizes its heap
  to the container's limit by itself. Logs are capped at 3 × 10 MB per container: the disk is shared.
- **Networks.** `db` is internal — no route out: PostgreSQL and its clients. `egress`: Api alone, for its published
  port and for SMTP. PostgreSQL is published nowhere, not even on loopback; it does not join the server's existing
  `db:5432`.
- **Connection strings.** Api: `app_user`, `authenticator`, `provisioner`. Worker: `job_runner`, `app_user`. The
  `migrator` string reaches the `migrate` one-shot alone, which is removed as soon as it exits. PostgreSQL's own
  container holds the five role passwords in its environment, because `db/init/00-roles.sh` reads them there on the
  first start, exactly as in development.
- **ufw does not protect a port Docker publishes.** Docker writes its own iptables rules, which packets meet before
  ufw's; a port published on `0.0.0.0` is reachable from the internet even with ufw denying it. What closes a port
  is the address it is bound to. That is why Api is published on `127.0.0.1:5100` and PostgreSQL on nothing — do
  not try to fix a published port with a ufw rule; it will look fixed and not be.
- **The `docker` group is root.** Any member of it can mount the host's filesystem into a container. `deployer` is
  in it, so the GitHub key is restricted to one forced command (step 1) — never a shell.
- **Images are built on GitHub, never on the server.** A build there needs 2 GB+ beside production. The image
  deployed is the one built from a commit on `main` whose CI passed, and the server needs no .NET SDK. The images
  are a **public** GHCR package: the repository is already public, and no image holds a secret (T0.3 proves it for
  migrator; every secret arrives through the environment), so the server pulls them with no credential, and only
  the layers that changed.

### Step 1 — the `deployer` user, the deploy command, and its key

As your own administrator user, with `sudo`.

1. **Prerequisites.**
   ```bash
   sudo apt-get update && sudo apt-get install -y git curl binutils
   docker compose version
   docker info --format '{{.CgroupVersion}}'
   ```
   **Check:** `docker compose version` prints v2 or later; the cgroup version is `2` (the memory limits depend on
   it). `binutils` provides `strings`, which the T0.3 scan uses.
2. **The user, and the directory it owns.**
   ```bash
   sudo adduser --disabled-password --gecos "" deployer
   sudo usermod -aG docker deployer
   sudo install -d -o deployer -g deployer -m 750 /opt/platform-core
   sudo -u deployer git clone https://github.com/omarqatu/platform-core.git /opt/platform-core
   ```
   If `sshd` limits who may log in (`sudo sshd -T | grep -iE '^(allowusers|allowgroups)'` prints something), add
   `deployer` there and `sudo systemctl reload ssh`.
3. **The deploy command — read it before you install it.** The key can run this script and nothing else, so the
   restriction is exactly as good as the script:
   ```bash
   less /opt/platform-core/deploy/platform-core-deploy
   ```
   What it does: reads **one line from stdin**, a 40-character commit SHA, and refuses anything else; never reads
   what the caller asked to run (`SSH_ORIGINAL_COMMAND`); checks the commit is on `main`; checks `deploy/.env`;
   checks the commit out; pulls the images built from it; runs T0.3 on the Api image with the real migrator
   password; starts PostgreSQL; runs the migrations; starts Api and waits for it; checks that only
   `127.0.0.1:5100` is published and that Api holds no migrator string; removes old images. Then install it
   **outside** `/opt/platform-core` (which `deployer` owns), owned by root:
   ```bash
   sudo install -o root -g root -m 755 /opt/platform-core/deploy/platform-core-deploy /usr/local/sbin/
   ```
   A later change to the script in the repository changes nothing on the server until you run this line again.
4. **The key — generated here, on the server.**
   ```bash
   sudo -u deployer ssh-keygen -t ed25519 -N "" -C github-deploy -f /home/deployer/github-deploy
   sudo -u deployer install -d -m 700 /home/deployer/.ssh
   printf 'restrict,command="/usr/local/sbin/platform-core-deploy" %s\n' "$(sudo cat /home/deployer/github-deploy.pub)" \
     | sudo -u deployer tee -a /home/deployer/.ssh/authorized_keys > /dev/null
   sudo chmod 600 /home/deployer/.ssh/authorized_keys
   ```
   `restrict` turns off the pty and every kind of forwarding (port, agent, X11); `command=` replaces whatever the
   caller asks to run with the deploy command.
5. **Prove the restriction, before the private key leaves the server.** Ask the key to run `id`:
   ```bash
   echo not-a-sha | sudo ssh -T -i /home/deployer/github-deploy -o IdentitiesOnly=yes \
     -o StrictHostKeyChecking=accept-new deployer@127.0.0.1 id
   ```
   **Check:** it prints `DEPLOY FAILED: stdin must be one full commit SHA …` — not `uid=…`. The forced command ran,
   and `id` did not.
6. **GitHub** (repository → Settings → Secrets and variables → Actions):
   - Secret `DEPLOY_SSH_KEY`: the whole output of `sudo cat /home/deployer/github-deploy`, from
     `-----BEGIN OPENSSH PRIVATE KEY-----` to `-----END OPENSSH PRIVATE KEY-----` inclusive.
   - Secret `DEPLOY_HOST`: `62.84.187.143` — the IP, not the domain: the domain goes through Cloudflare's proxy,
     which does not carry SSH. (The workflow uses port 22.)
   - Secret `DEPLOY_USER`: `deployer`.
   - **Variable** (the Variables tab, not Secrets) `DEPLOY_KNOWN_HOSTS`: the output of
     ```bash
     awk '{print "62.84.187.143", $1, $2}' /etc/ssh/ssh_host_ed25519_key.pub
     ```
     It is read from the server's own file, so nobody in between can substitute theirs. It is public, which is why
     it is a variable.
7. **Delete the private key from the server**, once the secret is saved:
   ```bash
   sudo shred -u /home/deployer/github-deploy && sudo rm /home/deployer/github-deploy.pub
   ```

**Check:** `ls -l /usr/local/sbin/platform-core-deploy` → `-rwxr-xr-x 1 root root`;
`sudo -u deployer test -w /usr/local/sbin/platform-core-deploy && echo WRITABLE || echo ok` → `ok`;
`sudo ls /home/deployer` → no `github-deploy`.
**Report back:** the outputs of the prerequisites, of step 5, and of this check.

### Step 2 — `deploy/.env`

```bash
sudo -u deployer cp /opt/platform-core/deploy/.env.example /opt/platform-core/deploy/.env
sudo -u deployer chmod 600 /opt/platform-core/deploy/.env
for i in 1 2 3 4 5 6; do openssl rand -hex 32; done
sudo -u deployer nano /opt/platform-core/deploy/.env
```

Every variable is explained in the file. Use the six random values for the six passwords, one each; write values
with no quotes. `PUBLIC_HOST=platform.ask-palestine.com`. For e-mail, `SMTP_USERNAME` is the Gmail address and
`SMTP_PASSWORD` a Google **App Password** for it (it needs 2-Step Verification on that account), without spaces.

**The role passwords are written into PostgreSQL once**, when its volume is first created. Changing one in `.env`
later does not change it in the database; it only stops Api from connecting.

**Check:**
```bash
sudo stat -c '%U %a' /opt/platform-core/deploy/.env
sudo grep -c 'replace-with\|example' /opt/platform-core/deploy/.env
cd /opt/platform-core && sudo -u deployer env IMAGE_TAG=check docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env config --quiet && echo OK
```
→ `deployer 600`; `0`; `OK` (`--quiet` prints no value).
**Report back:** those three lines — never the file itself.

### Step 3 — nginx

1. **Compare with the existing sites first.** This repository's file is a draft whose certificate paths are
   placeholders:
   ```bash
   ls /etc/nginx/sites-enabled/
   sudo grep -hE 'listen|ssl_certificate|include|proxy_set_header' /etc/nginx/sites-enabled/*
   ```
   Find the Cloudflare Origin certificate the other `ask-palestine.com` sites use, and check it covers the new name:
   ```bash
   sudo openssl x509 -noout -ext subjectAltName -in <that certificate>
   ```
   It must list `*.ask-palestine.com` or `platform.ask-palestine.com`. If not, create an Origin certificate for it in
   Cloudflare (SSL/TLS → Origin Server) and install it beside the others.
2. **Install the site** with those paths, and anything else the existing files do differently:
   ```bash
   sudo cp /opt/platform-core/deploy/nginx/platform.ask-palestine.com.conf /etc/nginx/sites-available/
   sudo nano /etc/nginx/sites-available/platform.ask-palestine.com.conf
   sudo ln -s /etc/nginx/sites-available/platform.ask-palestine.com.conf /etc/nginx/sites-enabled/
   sudo nginx -t && sudo systemctl reload nginx
   ```
   `nginx -t` must pass before the reload: a failed reload leaves the old configuration running, but a failed
   restart would take the four existing sites down with it — never `restart` here.
3. **Cloudflare:** the `platform` DNS record proxied (orange cloud), and SSL/TLS mode "Full (strict)", as for the
   other subdomains.

**Check** (before the first deploy, so Api is not running yet):
```bash
curl -sk -o /dev/null -w '%{http_code}\n' --resolve platform.ask-palestine.com:443:127.0.0.1 https://platform.ask-palestine.com/
```
→ `502`: nginx answers for the name and finds nothing behind it yet. The other sites still answer as before.
**Report back:** the `nginx -t` output, this code, and the site file as you installed it (it holds paths, no secret)
— so the repository's copy can be corrected to match.

### Step 4 — deploy, redeploy, logs

**First deploy.** On GitHub: Actions → *deploy (test server)* → Run workflow. Leave the SHA empty to deploy the tip
of `main`. The commit must be on `main` and its `ci` run must have passed; the workflow refuses anything else.

**The first run is expected to fail at the pull.** GitHub creates the three image packages as *private*, and the
server pulls without logging in. After the first run's `images` job has pushed them: your GitHub profile →
Packages → for each of `platform-core-api`, `platform-core-migrations`, `platform-core-worker`: Package settings →
Change visibility → Public. Then run the workflow again. Check from the server that no login is involved:
```bash
sudo -u deployer docker logout ghcr.io; sudo -u deployer docker pull ghcr.io/omarqatu/platform-core-api:<the commit>
```

**How long it takes.** The `images` job: a few minutes (longer on the first run, with no cache). The `deploy` job:
about a minute on the first pull, less after. Every line of the server's output starts with a UTC time, so a
long step is visible as long, not hung. **Note the real durations of the first two runs and report them.**

**Check** — on the server:
```bash
curl -sI -H 'Host: platform.ask-palestine.com' http://127.0.0.1:5100/api/tenants | head -n 1
curl -sI http://127.0.0.1:5100/api/tenants | head -n 1
docker port platform-core-api-1
docker port platform-core-postgres-1
docker stats --no-stream
docker ps --format '{{.Names}}\t{{.Status}}'
free -m
sudo dmesg -T | grep -i 'killed process' | tail -n 5
```
→ `HTTP/1.1 401 Unauthorized` with the real host name, and `HTTP/1.1 400 Bad Request` without it (Api answers
only `PUBLIC_HOST`: `AllowedHosts`); `8080/tcp -> 127.0.0.1:5100`; nothing for PostgreSQL; our two containers within
512 MB each; **every other container still Up, with the same uptime as before the deploy**; no new OOM kill.

And **from anywhere else** (a laptop, or a phone on mobile data):
```bash
curl -I --max-time 5 http://62.84.187.143:5100
```
→ refused or timed out — never an answer. And `https://platform.ask-palestine.com` in a browser shows the sign-in
screen (and every sign-in fails: see "day one" above).
**Report back:** all of the above, and the two durations.

**The platform's e-mail account**, once, after the first deploy — the `provider_accounts` row described above,
with the Gmail address as sender. As `migrator`, through the container's local socket:
```bash
docker exec -i platform-core-postgres-1 psql -X -v ON_ERROR_STOP=1 -U migrator -d platform -c \
  "INSERT INTO provider_accounts (id, tenant_id, channel, provider_code, sender_id, credential_ref, is_active, created_at)
   VALUES (uuidv7(), NULL, 'email', 'smtp', '<the Gmail address>', 'platform-email', true, now());"
```
→ `INSERT 0 1`. Nothing can send an e-mail to test it yet: the senders are invitations and password reset, and
there are no users until step 5.

**Redeploy:** run the workflow again. **Rollback:** run it with an older commit of `main` in the SHA field — this
works only if no migration was added in between: migrations are forward-only, and an older Api against a newer
schema is not supported.

**Logs:**
```bash
docker logs --tail 100 platform-core-api-1
docker logs --tail 100 platform-core-postgres-1
docker logs --since 10m -f platform-core-api-1      # follow; Ctrl-C to stop
```
The workflow's log on GitHub is public, so it shows the deploy's steps and never a container's log: when a deploy
fails, its last line names the step, and the reason is here.

**Restart without deploying:** `docker restart platform-core-api-1`. **Which commit is running:**
`git -C /opt/platform-core rev-parse HEAD`.

### Step 5 — the first tenant — BLOCKED

> **Blocked on `src/Ops`, which is not built (OPEN_ITEMS 86).** When it exists, this step will be:
> `create-tenant --name "<name>" --owner-email <address>` as a one-shot container from the Api image, with the
> `provisioner` and `job_runner` strings only, never baked into a running service (Amendment 01 §5.4). Its owner
> invitation is not e-mailed yet (OPEN_ITEMS 60), so the command will have to print the link for you to open. Until
> then the server has no users (see "day one").

### Step 6 — backups (optional, and OFF until you enable them)

> **Until this line is installed, there is no backup.** A mistake — a dropped volume, a bad migration, a disk
> failure — loses everything entered by hand. And a backup on the same server does not survive losing the server:
> copy the files somewhere else as well.

```bash
sudo install -d -o deployer -g deployer -m 700 /opt/platform-core-backups
sudo -u deployer crontab -e
```
and add (one line; `%` must be written `\%` in a crontab):
```
15 3 * * * docker exec platform-core-postgres-1 pg_dump -U migrator -Fc platform > /opt/platform-core-backups/platform-$(date +\%F).dump && find /opt/platform-core-backups -name 'platform-*.dump' -mtime +14 -delete
```
Daily at 03:15, 14 days kept. `pg_dump` runs as `migrator`, the owner, whose `BYPASSRLS` lets it read every row
under `FORCE ROW LEVEL SECURITY` — any other role would dump nothing.
**Check**, the next day: `ls -l /opt/platform-core-backups` shows a file, and
`docker exec -i platform-core-postgres-1 pg_restore --list < /opt/platform-core-backups/platform-<date>.dump | head`
lists the tables. **Report back:** both.

### Optional — scheduled jobs

The Worker runs one job once and exits; its schedule is cron's. The retention procedures above are daily too. In
`deployer`'s crontab, if and when you want them:
```
0 4 * * * cd /opt/platform-core && IMAGE_TAG=$(git rev-parse HEAD) docker compose --project-directory deploy -f deploy/docker-compose.prod.yml --env-file deploy/.env --profile jobs run --rm worker expiring-subscriptions 2>&1 | logger -t platform-core-worker
30 4 * * * docker exec platform-core-postgres-1 psql -X -q -U migrator -d platform -c 'CALL prune_auth_attempts(); CALL prune_password_reset_tokens();' 2>&1 | logger -t platform-core-retention
```
Their output goes to the system log: `journalctl -t platform-core-worker`.
