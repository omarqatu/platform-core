# Build context: repository root. Only Api and what it references (Core, the modules) are copied in —
# the migration tool and its configuration never enter this image (T0.3). The web interface (web/) is built in its
# own stage, and only its build output enters the image: Api serves it from the same origin (Api/WebInterface.cs).
FROM node:22 AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json ./
COPY src/Core/ src/Core/
COPY src/Modules.Subscriptions/ src/Modules.Subscriptions/
COPY src/Api/ src/Api/
RUN dotnet publish src/Api/Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
COPY --from=web /web/dist ./wwwroot
ENTRYPOINT ["dotnet", "Api.dll"]
