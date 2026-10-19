# Build context: repository root. Only Api and what it references (Core, the modules) are copied in, and the
# operations procedure, src/Ops (Amendment 01 §5.4; OPEN_ITEMS 86) — the migration tool and its configuration never
# enter this image (T0.3), and the T0.3 scan covers Ops too, since it scans all of /app. Ops is published to
# /app/ops, beside Api and not over it: each keeps its own entry point, and Ops runs only as a one-shot container
# (`--entrypoint dotnet <image> ops/Ops.dll create-tenant …`, docs/operations.md step 5), never as a service. The web interface (web/) is built in its
# own stage, and only its build output enters the image: Api serves it from the same origin (Api/WebInterface.cs).
#
# Extra CA certificates, optional (docs/building-behind-tls-inspection.md): a network that intercepts TLS needs its CA
# trusted by npm and NuGet. Empty by default; a build supplies it with --build-context extra-ca=<dir of *.crt>. It is
# bind-mounted into the two RUN steps that reach a registry and never copied: no layer holds it, the final stage least.
FROM scratch AS extra-ca

FROM node:22 AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN --mount=type=bind,from=extra-ca,target=/extra-ca \
    if ls /extra-ca/*.crt > /dev/null 2>&1; then cat /extra-ca/*.crt > /tmp/extra-ca.pem && NODE_EXTRA_CA_CERTS=/tmp/extra-ca.pem npm ci && rm /tmp/extra-ca.pem; \
    else npm ci; fi
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json ./
COPY src/Core/ src/Core/
COPY src/Modules.Subscriptions/ src/Modules.Subscriptions/
COPY src/Api/ src/Api/
COPY src/Ops/ src/Ops/
RUN --mount=type=bind,from=extra-ca,target=/extra-ca \
    if ls /extra-ca/*.crt > /dev/null 2>&1; then cat /etc/ssl/certs/ca-certificates.crt /extra-ca/*.crt > /tmp/ca-bundle.pem && export SSL_CERT_FILE=/tmp/ca-bundle.pem; fi; \
    dotnet publish src/Api/Api.csproj -c Release -o /app && dotnet publish src/Ops/Ops.csproj -c Release -o /app/ops \
    && rm -f /tmp/ca-bundle.pem

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
COPY --from=web /web/dist ./wwwroot
ENTRYPOINT ["dotnet", "Api.dll"]
