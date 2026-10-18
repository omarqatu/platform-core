# The migration tool's image: the only image that holds Migrations (T0.3 keeps it out of Api's). It runs once per
# deploy as a one-shot container (deploy/docker-compose.prod.yml, service "migrate") and is removed when it exits, so
# migrator's connection string lives in nothing long-running. The string itself arrives at run time, through the
# environment (ConnectionStrings__migrator); appsettings.json carries only the development value.
#
# Build context: repository root. Extra CA certificates, optional — as src/Api/Dockerfile.
FROM scratch AS extra-ca

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json ./
COPY src/Core/ src/Core/
COPY src/Migrations/ src/Migrations/
RUN --mount=type=bind,from=extra-ca,target=/extra-ca \
    if ls /extra-ca/*.crt > /dev/null 2>&1; then cat /etc/ssl/certs/ca-certificates.crt /extra-ca/*.crt > /tmp/ca-bundle.pem && SSL_CERT_FILE=/tmp/ca-bundle.pem dotnet publish src/Migrations/Migrations.csproj -c Release -o /app && rm /tmp/ca-bundle.pem; \
    else dotnet publish src/Migrations/Migrations.csproj -c Release -o /app; fi

# aspnet, not runtime: Core references Microsoft.AspNetCore.App.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
ENTRYPOINT ["dotnet", "Migrations.dll"]
