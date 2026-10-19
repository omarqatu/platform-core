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
> the first tenant and its owner is `src/Ops create-tenant` (Amendment 01 §5.4) — step 5; the bootstrap route exists
> in Development and CI only (§4.4). So until step 5 has been run and its invitation accepted,
> `https://platform.ask-palestine.com` shows the sign-in screen, and every sign-in fails. That is the expected
> result of the deploy alone: it proves the infrastructure — nginx, Cloudflare, the memory limits, the networks, beside
> the eight containers already on the server — not the product.

### What runs there

| Container | Image | Published | Memory (`mem_limit` = `memswap_limit`) | `oom_score_adj` | CPUs |
|---|---|---|---|---|---|
| `platform-core-postgres-1` | `postgres:18` | nothing | 512 MB | 500 | 1.0 |
| `platform-core-api-1` | `ghcr.io/omarqatu/platform-core-api:<commit>` | `127.0.0.1:5100` only | 512 MB | 800 | 1.0 |
| `migrate` (one-shot, removed on exit) | `…/platform-core-migrations:<commit>` | nothing | 256 MB | 900 | 0.5 |
| `worker` (one-shot, removed on exit) | `…/platform-core-worker:<commit>` | nothing | 256 MB | 900 | 0.5 |
| `ops` (one-shot, removed on exit) | `…/platform-core-api:<commit>`, entry point `ops/Ops.dll` | nothing | 256 MB | 900 | 0.5 |

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
- **The client's real address.** Api's attempt limiter keys on the client's address (Amendment 01 §5.2: a ceiling
  per address, an escalating delay per username and address), and `auth_attempts` and `audit_log` record it.
  Behind Cloudflare and nginx, every connection would otherwise come from one address — one ceiling for every user,
  so 50 failures by anyone in 15 minutes would refuse every sign-in, a correct password included. So, two links,
  each believing only the one before it:
  1. **nginx** takes the client from `CF-Connecting-IP` **only when the connection comes from Cloudflare's
     published ranges** (`set_real_ip_from` + `real_ip_header` in the site file), and passes it on in
     `X-Forwarded-For`, **replacing** whatever the client sent there.
  2. **Api** believes `X-Forwarded-For` only from the address nginx's connections arrive from — the gateway of its
     fixed network, `10.213.47.1` (`Network:TrustedProxies`) — and reads one entry. A request from anywhere else
     keeps its own connection's address, whatever headers it carries.

  Someone reaching the server directly, past Cloudflare, is recorded under their own address: their
  `CF-Connecting-IP` is ignored, because they are not Cloudflare. **Getting either link wrong breaks nothing you
  would notice** — the platform runs, and every user shares one address and one ceiling. Only the `auth_attempts`
  query in step 4 shows it, and its table says which link is wrong.
  **Both this chain and the certificate depend on the same orange cloud.** The `platform` DNS record must stay
  proxied: switched to grey (DNS only), browsers reach this server directly and reject the Cloudflare Origin
  certificate at once — it is valid only behind Cloudflare's proxy — and nginx would see clients directly.
  **The boundary that remains:** any process on the host itself can connect to `127.0.0.1:5100` and claim an
  address — accepted, because the host is the trust boundary (its `docker` group is root anyway). And never set
  `ASPNETCORE_FORWARDEDHEADERS_ENABLED`: it trusts every peer, so Api refuses to start with it.
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

   Api's network has a fixed subnet, `10.213.47.0/29` (its gateway is the address Api trusts as nginx — "The
   client's real address" below). Check nothing on the server uses it:
   ```bash
   ip route | grep -c '10\.213\.47\.'
   docker network inspect $(docker network ls -q) --format '{{.Name}} {{range .IPAM.Config}}{{.Subnet}} {{end}}' | grep -c '10\.213\.47\.'
   ```
   → `0` and `0`. Anything else: report it before going on — the subnet is changed in the compose file and in
   `Network__TrustedProxies__0` together.
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
6. **GitHub — four entries: three Secrets and one Variable.** Repository → Settings → Secrets and variables →
   Actions. The workflow reads exactly these four, and the tab matters: it reads the first three from **Secrets**
   (`secrets.…`) and the fourth from **Variables** (`vars.…`). An entry on the wrong tab is, to the workflow, not
   set at all.

   | Name | Tab | Value |
   |---|---|---|
   | `DEPLOY_SSH_KEY` | **Secrets** | the whole output of `sudo cat /home/deployer/github-deploy`, from `-----BEGIN OPENSSH PRIVATE KEY-----` to `-----END OPENSSH PRIVATE KEY-----` inclusive |
   | `DEPLOY_HOST` | **Secrets** | `62.84.187.143` — the IP, not the domain: the domain goes through Cloudflare's proxy, which does not carry SSH |
   | `DEPLOY_USER` | **Secrets** | `deployer` |
   | `DEPLOY_KNOWN_HOSTS` | **Variables** — not Secrets | the server's host key line, below |

   **`DEPLOY_KNOWN_HOSTS`** pins the server's identity, so the workflow refuses to talk to anything else that
   answers on that address. Produce it **on the server itself** — run there, nothing in between can substitute
   its own key:
   ```bash
   ssh-keyscan -t ed25519 62.84.187.143 2>/dev/null
   ```
   → **one** line: `62.84.187.143 ssh-ed25519 AAAA…`. That line, and only that line, is the value.
   `ssh-keyscan` also prints a comment line first — `# 62.84.187.143:22 SSH-2.0-OpenSSH_…` — on stderr, which
   `2>/dev/null` drops; without it the terminal shows both, and **the comment line is not part of the value**.
   The same line, read from the key file instead of over the network, is
   `awk '{print "62.84.187.143", $1, $2}' /etc/ssh/ssh_host_ed25519_key.pub` (identical output, checked on
   OpenSSH 8.9). It is a public key, which is why it is a Variable and not a Secret.

   **If it was saved as a Secret** (the first deploy's failure: `variable DEPLOY_KNOWN_HOSTS is not set`): add
   it under **Variables** with the same value, then delete the Secret of that name — a Secret named
   `DEPLOY_KNOWN_HOSTS` is never read.

   **Check:** Settings → Secrets and variables → Actions shows **three** repository secrets (`DEPLOY_HOST`,
   `DEPLOY_SSH_KEY`, `DEPLOY_USER`) on the Secrets tab and **one** repository variable (`DEPLOY_KNOWN_HOSTS`) on the
   Variables tab — nothing else, and nothing in the wrong tab.

   **SSH from GitHub must reach the server.** The deploy connects from GitHub-hosted runners, whose addresses are
   many and change. If port 22 is limited by source address — `sudo ufw status` listing `22` or `OpenSSH` with a
   `from`, or a provider firewall in front of the server — the deploy fails at its SSH step with a timeout. And the
   workflow uses port 22: `sudo sshd -T | grep -i '^port '` must say `port 22`. Report either if it differs; do
   not open 22 wider without deciding it.
7. **Delete the private key from the server**, once `DEPLOY_SSH_KEY` is saved:
   ```bash
   sudo shred -u /home/deployer/github-deploy && sudo rm /home/deployer/github-deploy.pub
   ```

**Check — a gate, not a report.** The deploy cannot work without the forced command: with none, `sshd` hands the
commit the workflow sends to `deployer`'s shell, which runs it as a command and exits `127` (the first deploy's
failure). And a key with no forced command is a shell for a member of the `docker` group — root. So this check looks
at the key's line as well as the script, and **exits 1** on anything wrong:
```bash
sudo bash -c '
S=/usr/local/sbin/platform-core-deploy; AK=/home/deployer/.ssh/authorized_keys; bad=0
if [ ! -x "$S" ]; then echo "FAIL: $S does not exist — step 1, item 3"; bad=1
elif [ "$(stat -c %U:%G:%a "$S")" != root:root:755 ]; then echo "FAIL: $S is $(stat -c %U:%G:%a "$S"), not root:root:755"; bad=1; fi
sudo -u deployer test -w "$S" 2>/dev/null && { echo "FAIL: deployer can write $S"; bad=1; }
n=$(grep -cE " github-deploy[[:space:]]*$" "$AK" || true)
[ "$n" = 1 ] || { echo "FAIL: $n lines end in github-deploy in $AK, not 1"; bad=1; }
grep -E " github-deploy[[:space:]]*$" "$AK" | grep -q "^restrict,command=\"$S\" " \
  || { echo "FAIL: the github-deploy key has no forced command — a deploy would hand its SHA to a shell (exit 127)"; bad=1; }
grep -vE "^restrict,command=" "$AK" | grep -qE "^(ssh-|ecdsa-|sk-)" \
  && { echo "FAIL: $AK accepts a key with no forced command — a shell for a docker-group user, i.e. root"; bad=1; }
[ "$bad" = 0 ] && echo "PASS: the forced command is in place" || exit 1'
```
→ `PASS: the forced command is in place`. Any `FAIL` line: fix it before step 2. And `sudo ls /home/deployer` → no
`github-deploy` (item 7).

**If the key's line was written without the forced command** — for example `no-agent-forwarding,no-X11-forwarding,
no-pty ssh-ed25519 … github-deploy`, which also leaves **port forwarding open** (the key could tunnel to any port on
the server's loopback, `127.0.0.1:5100` and the neighbours' services included) — rewrite it around the same key,
keeping a backup; the key bytes never need retyping:
```bash
sudo bash -euo pipefail -c '
AK=/home/deployer/.ssh/authorized_keys
[ "$(grep -cE " github-deploy[[:space:]]*$" "$AK")" = 1 ] || { echo "STOP: not exactly one github-deploy line"; exit 1; }
cp -a "$AK" "$AK.bak.$(date +%Y%m%d%H%M%S)"
awk "/ github-deploy[[:space:]]*\$/ { print \"restrict,command=\\\"/usr/local/sbin/platform-core-deploy\\\" \" \$(NF-2) \" \" \$(NF-1) \" \" \$NF; next } { print }" "$AK" > "$AK.new"
chown deployer:deployer "$AK.new"; chmod 600 "$AK.new"; mv "$AK.new" "$AK"'
```
`restrict` includes every `no-…` option the old line had and adds the rest (port forwarding among them). Then run the
gate again.

**Report back:** the outputs of the prerequisites, of item 5, and of the gate.

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

1. **The certificate: the server's existing Cloudflare Origin certificate, nothing new.** It is a wildcard, so it
   already covers `platform.ask-palestine.com` — no issuance, no certbot, no HTTP-01 challenge. The site file names
   it where it is today:
   `/home/supersetadmin/ssl/cloudflare/ask-palestine-cert.pem` and `…/ask-palestine-key.pem`. Check, rather than
   assume, that it is the wildcard and that the key is its key:
   ```bash
   C=/home/supersetadmin/ssl/cloudflare
   sudo openssl x509 -noout -subject -enddate -ext subjectAltName -in $C/ask-palestine-cert.pem
   [ "$(sudo openssl x509 -noout -pubkey -in $C/ask-palestine-cert.pem | sha256sum)" = \
     "$(sudo openssl pkey -pubout -in $C/ask-palestine-key.pem | sha256sum)" ] && echo "key matches"
   ```
   → `DNS:*.ask-palestine.com, DNS:ask-palestine.com`, an end date in the future, and `key matches`.

   **Who has to read these files.** nginx's **master** process runs as root and loads every `ssl_certificate` and
   `ssl_certificate_key` when it reads its configuration (on start and on every reload); the workers, which run as
   `www-data`, never open them. So `www-data` does **not** need to read the key, and must not be given it. Proven
   on nginx 1.18 (Ubuntu 22.04's) for this PR: key and certificate mode 600, owned by another user, in 700
   directories inside a home directory, `www-data` unable to read the key — `nginx -t` passed, TLS answered, and a
   reload worked. (The one exception is a path containing variables, loaded by the workers during the handshake;
   ours has none.) The check that matters is therefore `nginx -t` **as root**, which is exactly what the reload
   runs:
   ```bash
   sudo stat -c '%U:%G %a %n' /home/supersetadmin /home/supersetadmin/ssl $C $C/ask-palestine-cert.pem $C/ask-palestine-key.pem
   sudo -u www-data test -r $C/ask-palestine-key.pem && echo "www-data CAN read the key" || echo "www-data cannot read the key"
   sudo nginx -t
   ```
   → `nginx -t` successful. If it fails with `cannot load certificate` / `Permission denied`, root cannot read the
   file — that happens on a home directory mounted over NFS or with `root_squash`, or under an AppArmor profile for
   nginx; report the exact error before changing anything.

   **What a home directory costs.** The key is the key of **every** `*.ask-palestine.com` site, production
   Ask Palestine included. In a user's home it is as safe as that account: anyone who becomes `supersetadmin`, any
   process running as it (a container started by it, a tool it runs), any backup or sync of that home directory,
   and anyone at all if a `chmod` on the home directory ever goes wrong. If the first line above shows the key
   readable by its group or by others (anything but `600`/`400`), or `www-data CAN read the key`, that is already
   the exposure, today, for every site that uses it.

   **The better end state — the project owner decides.** Both files in `/etc/ssl/cloudflare/`, owned by root,
   **`600 root:root`** for the key (`644` for the certificate, which is public). Not `640 root:www-data`: since the
   workers never read the key, giving `www-data` read access only widens who can — every site's worker, and any
   PHP-FPM pool on this host that runs as `www-data`.

   > **All the sites, in one go — or not at all.** Moving the key means moving it for **every** site file that
   > names `/home/supersetadmin/ssl/cloudflare/`, the neighbouring sites included, in one change: copy, repoint
   > every site, `nginx -t`, reload, check every site answers, then delete the old copies. A half-move — this site
   > repointed, the others still on the home directory — leaves the same wildcard key in **two** places with two
   > sets of permissions to keep right, and the old copy keeps every exposure it had. That is **strictly worse**
   > than leaving it where it is. If every site cannot be moved now, leave the key where it is.

   List them first:
   ```bash
   sudo grep -rln /home/supersetadmin/ssl/cloudflare /etc/nginx/
   ```
   ```bash
   sudo install -d -o root -g root -m 755 /etc/ssl/cloudflare
   sudo install -o root -g root -m 644 $C/ask-palestine-cert.pem /etc/ssl/cloudflare/ask-palestine-cert.pem
   sudo install -o root -g root -m 600 $C/ask-palestine-key.pem  /etc/ssl/cloudflare/ask-palestine-key.pem
   ```
   Then **every** site file in that list changes to the new paths, then `sudo nginx -t && sudo systemctl reload
   nginx`, and only after every one of those sites answers, the copies in the home directory are removed
   (`sudo shred -u …`).
2. **Compare with the existing sites**, so this file matches them in everything but its name:
   ```bash
   ls /etc/nginx/sites-enabled/
   sudo grep -hE 'listen|ssl_certificate|include|proxy_set_header' /etc/nginx/sites-enabled/*
   ```
3. **Install the site**, with anything the existing files do differently:
   ```bash
   sudo cp /opt/platform-core/deploy/nginx/platform.ask-palestine.com.conf /etc/nginx/sites-available/
   sudo nano /etc/nginx/sites-available/platform.ask-palestine.com.conf
   sudo ln -s /etc/nginx/sites-available/platform.ask-palestine.com.conf /etc/nginx/sites-enabled/
   sudo nginx -t && sudo systemctl reload nginx
   ```
   `nginx -t` must pass before the reload: a failed reload leaves the old configuration running, but a failed
   restart would take the four existing sites down with it — never `restart` here.
4. **Cloudflare:** the `platform` DNS record proxied (orange cloud), and SSL/TLS mode "Full (strict)", as for the
   other subdomains.
5. **The real address** ("The client's real address" above) needs nginx's realip module, and the site file's list of
   Cloudflare ranges must be Cloudflare's current one:
   ```bash
   nginx -V 2>&1 | grep -o with-http_realip_module
   diff <( (curl -s https://www.cloudflare.com/ips-v4; echo; curl -s https://www.cloudflare.com/ips-v6; echo) | grep . | sort ) \
        <( grep -oP 'set_real_ip_from \K[^;]+' /etc/nginx/sites-available/platform.ask-palestine.com.conf | sort ) && echo SAME
   ```
   → `with-http_realip_module`, then `SAME`. If the server already lists Cloudflare's ranges at the `http` level
   (`sudo grep -rn set_real_ip_from /etc/nginx/nginx.conf /etc/nginx/conf.d/`), this site's own list **replaces**
   it for this site rather than adding to it — harmless while both are Cloudflare's full list, which is what the
   check above proves for this one. A difference means Cloudflare changed its list: update the site file
   (and this repository's copy), `nginx -t`, reload. Repeat this check now and then — a range Cloudflare adds
   later is one whose visitors would all share one address.

**Check** (before the first deploy, so Api is not running yet):
```bash
curl -sk -o /dev/null -w '%{http_code}\n' --resolve platform.ask-palestine.com:443:127.0.0.1 https://platform.ask-palestine.com/
```
→ `502`: nginx answers for the name and finds nothing behind it yet. The other sites still answer as before.
**Report back:** the `nginx -t` output, this code, and the site file as you installed it (it holds paths, no secret)
— so the repository's copy can be corrected to match.

### Step 4 — deploy, redeploy, logs

**Before the first Run workflow — and before any run after a change to `deployer` or its key — the gate of step 1
must print `PASS`.** Run it again now. A deploy without the forced command does not fail safely: the commit goes to
a shell, and the workflow reports `exit 127` after the fact.

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
**The client's real address, end to end.** From a phone on mobile data, open `https://platform.ask-palestine.com`,
and sign in as `address-test` with any password (it fails: there are no users). Note the phone's public address
(open `https://ifconfig.me` on it). Then, from a laptop, try to choose an address instead:
```bash
curl -s -o /dev/null -w '%{http_code}\n' https://platform.ask-palestine.com/api/auth/login \
  -H 'Origin: https://platform.ask-palestine.com' -H 'X-Requested-With: platform-web' -H 'Content-Type: application/json' \
  -H 'X-Forwarded-For: 203.0.113.99' -H 'CF-Connecting-IP: 203.0.113.99' -d '{"username":"spoof-test","password":"x"}'
```
→ `401`. On the server:
```bash
docker exec -i platform-core-postgres-1 psql -X -At -U migrator -d platform -c \
  "SELECT username_entered, ip_address FROM auth_attempts ORDER BY created_at DESC LIMIT 2"
```
→ `spoof-test` with the **laptop's** public address and `address-test` with the **phone's** — never `203.0.113.99`,
never `10.213.47.1`, never a Cloudflare address.

> **Run this query after the first deploy, every time — it is the one check that proves the whole chain.** A
> misconfiguration here does not stop anything: the platform deploys, serves, and signs people in, while every
> request appears to come from one place. The limiter's per-address ceiling (`Identity:LoginLimits:PerIpCeiling`,
> 50 failures in 15 minutes) then becomes **one ceiling for every user**: once anyone's failures reach it, every
> sign-in is refused with 429, a correct password included, and the refusals keep it there — anyone can lock
> everyone out on purpose. What the query shows tells you which link is broken:
>
> | `ip_address` shows | Broken link | Fix |
> |---|---|---|
> | the phone's / laptop's own address | nothing | — |
> | `10.213.47.1` for everyone | Api does not trust nginx: `Network__TrustedProxies__0` missing, or nginx's connections do not arrive from the network's gateway on this server | check the api container's environment (`docker inspect`), and report it — the gateway is the assumption proven only locally |
> | a Cloudflare address (`104.…`, `172.6x.…`, `162.158.…`, `2606:4700:…`) | nginx's realip is not applied: the `set_real_ip_from` list or `real_ip_header` missing, the module absent, or the site file not the one installed | step 3, item 5 |
> | `127.0.0.1` | nginx is passing its own peer — the site file's `X-Forwarded-For` line is not `$remote_addr`, or another site file answers this name | `sudo nginx -T \| grep -n -A3 'server_name platform'` |
> | `203.0.113.99` for `spoof-test` | a client can choose its own address — the worst case | stop and report before anyone signs in |

**Report back:** all of the above, and the two durations.

**The platform's e-mail account**, once, after the first deploy — the `provider_accounts` row described above,
with the Gmail address as sender. As `migrator`, through the container's local socket:
```bash
docker exec -i platform-core-postgres-1 psql -X -v ON_ERROR_STOP=1 -U migrator -d platform -c \
  "INSERT INTO provider_accounts (id, tenant_id, channel, provider_code, sender_id, credential_ref, is_active, created_at)
   VALUES (uuidv7(), NULL, 'email', 'smtp', '<the Gmail address>', 'platform-email', true, now());"
```
→ `INSERT 0 1`. Then check that Api's network reaches Gmail — some hosting providers block outbound port 587:
```bash
docker run --rm --network platform-core_egress busybox:1.36 nc -v -w 5 smtp.gmail.com 587 < /dev/null
```
→ a line starting `220` from Google. A timeout means the port is blocked on the way out, and no e-mail will
leave until it is opened (ask the provider). Nothing can send an e-mail to test it end to end yet: the senders are invitations and password reset, and
there are no users until step 5 has been accepted.

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

### Step 5 — the first tenant and its owner

`create-tenant` (Amendment 01 §5.4) creates one tenant from the role templates and an `owner` invitation for one
e-mail address — no person, no user, no membership. The owner then opens the printed link and accepts in person,
on the ordinary acceptance screen, which creates their account. Until that acceptance the tenant has no owner.

**First:** the deployed commit must contain it — migration `0014` and the Api image's `ops/Ops.dll`. If the server
was deployed before this PR was merged, run the deploy workflow again first. Then, on the server:

```bash
cd /opt/platform-core && sudo -u deployer env IMAGE_TAG=$(git rev-parse HEAD) \
  docker compose --project-directory deploy -f deploy/docker-compose.prod.yml --env-file deploy/.env \
  --profile ops run --rm --no-deps ops create-tenant --name "<the organization's name>" --owner-email <the owner's address>
```

It runs the `ops` service: the Api image with Ops's own entry point, the `provisioner` string only, and
`Ops__PublicOrigin=https://$PUBLIC_HOST` — so the printed link is the server's own. It refuses to run if it is given
a `migrator` string, or no origin.

**Check:** it prints four lines and exits 0:
```
tenant_id: <uuid>
owner invitation for <address>, single use, expires <date> UTC:
https://platform.ask-palestine.com/app/invitations/accept#tenant=<uuid>&token=<token>
This link is shown once and stored nowhere. If it is lost, run create-tenant again.
```
A line about `libgssapi_krb5.so.2` on stderr may come before them: it is the database driver looking for Kerberos,
which the image does not carry and the platform does not use. It is not an error.

> **The link is the only copy of a one-use secret, and it lives only in this terminal.** The token is in the
> container's output and nowhere else: the database holds its hash alone, and nothing e-mails it (OPEN_ITEMS 60).
> `--rm` deletes the container when it exits, and its log with it — there is no `docker logs` to go back to. Copy
> the link before you close the terminal. **If it is lost, do not hunt for it: run `create-tenant` again.** That
> creates a second tenant with a new link; the first stays behind with no member and an invitation no one can
> accept, and nothing removes it yet (OPEN_ITEMS 90). Keep the link out of chat messages and screenshots: whoever
> opens it first, with the invited address, becomes the owner.

Then open the link in a browser, as the owner. The acceptance screen asks for the invited address (spaces and case
do not matter), a full name, a username and a password, and creates the account; then sign in.

**Check, after the acceptance:** sign in at `https://platform.ask-palestine.com` with the new username and password;
the organization is in the list and opens. As `migrator`:
```bash
docker exec -i platform-core-postgres-1 psql -X -U migrator -d platform -c \
  "SELECT t.name, m.status, r.code, s.scope_mode, i.status AS invitation
   FROM tenants t JOIN memberships m ON m.tenant_id = t.id
   JOIN membership_roles mr ON mr.tenant_id = m.tenant_id AND mr.membership_id = m.id
   JOIN roles r ON r.tenant_id = mr.tenant_id AND r.id = mr.role_id
   JOIN membership_scope s ON s.tenant_id = m.tenant_id AND s.membership_id = m.id
   JOIN invitations i ON i.tenant_id = t.id;"
```
→ one row: the name, `active`, `owner`, `all`, `accepted`.

**Not done by this step yet (OPEN_ITEMS 89):** §5.4 also gives the new tenant its platform subscription and its
default notification settings. Neither table exists yet; the migrations that create them will write them for this
tenant. Nothing on the screens depends on them today.

**Report back:** the four lines with the token removed, and the row from the check.

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
