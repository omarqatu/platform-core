# The background-job worker's image (PROOF_SPEC T7). The Worker runs one job once and exits; its schedule is external
# (deploy/docker-compose.prod.yml, service "worker", run by cron — docs/operations.md). Its two connection strings,
# job_runner and app_user, arrive at run time through the environment; it refuses any other.
#
# Build context: repository root. Extra CA certificates, optional — as src/Api/Dockerfile.
FROM scratch AS extra-ca

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json ./
COPY src/Core/ src/Core/
COPY src/Modules.Subscriptions/ src/Modules.Subscriptions/
COPY src/Worker/ src/Worker/
RUN --mount=type=bind,from=extra-ca,target=/extra-ca \
    if ls /extra-ca/*.crt > /dev/null 2>&1; then cat /etc/ssl/certs/ca-certificates.crt /extra-ca/*.crt > /tmp/ca-bundle.pem && SSL_CERT_FILE=/tmp/ca-bundle.pem dotnet publish src/Worker/Worker.csproj -c Release -o /app && rm /tmp/ca-bundle.pem; \
    else dotnet publish src/Worker/Worker.csproj -c Release -o /app; fi

# aspnet, not runtime: Core references Microsoft.AspNetCore.App.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
ENTRYPOINT ["dotnet", "Worker.dll"]
