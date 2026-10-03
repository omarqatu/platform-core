# Building the Api image behind TLS inspection

Some networks intercept HTTPS and re-sign it with their own CA (a corporate firewall, for example). The machine trusts
that CA, but the containers that build the Api image do not: `npm ci` in the `web` stage and `dotnet publish` in the
`build` stage then fail to reach their registries, with errors such as

```
error NU1301: Unable to load the service index for source https://api.nuget.org/v3/index.json.
  The remote certificate is invalid because of errors in the certificate chain: UntrustedRoot
```

`src/Api/Dockerfile` takes the extra CA certificates as an optional BuildKit build context, `extra-ca`.

## Usage

Put the CA in a directory of its own, PEM-encoded, as `*.crt` (every `*.crt` there is used), and pass the directory:

```bash
mkdir -p ~/.local/share/extra-ca && cp /path/to/your-network-ca.crt ~/.local/share/extra-ca/
docker build --build-context extra-ca="$HOME/.local/share/extra-ca" -f src/Api/Dockerfile -t platform-api:local .
```

On Ubuntu, a CA the machine already trusts is usually in `/usr/local/share/ca-certificates/`; that directory works as it
is (`--build-context extra-ca=/usr/local/share/ca-certificates`).

Never commit a certificate to the repository.

## What it changes, and what it does not

- **Without `--build-context extra-ca`** (CI, and any default build): `extra-ca` is an empty stage, and both steps run
  exactly the commands they ran before.
- **With it:** the directory is bind-mounted, read-only, into the two `RUN` steps that reach a registry — `npm ci`
  (through `NODE_EXTRA_CA_CERTS`) and `dotnet publish` (through `SSL_CERT_FILE`, the image's own bundle plus yours) —
  and into nothing else. It is never copied, so no layer of the image holds it.
- **The final stage is unchanged** either way: it copies only `/app` and `wwwroot` from the build stages. The T0.3 scan
  (`ci/check-api-has-no-migrator.sh`) runs on the image the same way.
