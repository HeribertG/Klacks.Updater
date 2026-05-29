# Copyright (c) Heribert Gasparoli Private. All rights reserved.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Klacks.Updater/Klacks.Updater.csproj Klacks.Updater/
RUN dotnet restore Klacks.Updater/Klacks.Updater.csproj
COPY Klacks.Updater/ Klacks.Updater/
RUN dotnet publish Klacks.Updater/Klacks.Updater.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0-noble AS final
WORKDIR /app

# Docker CLI + compose plugin so the updater can drive the host's containers via the mounted socket.
# postgresql-client is NOT needed: backups run via `docker exec` against the postgres container so the
# client version always matches the server.
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl gnupg \
    && install -m 0755 -d /etc/apt/keyrings \
    && curl -fsSL https://download.docker.com/linux/debian/gpg -o /etc/apt/keyrings/docker.asc \
    && chmod a+r /etc/apt/keyrings/docker.asc \
    && echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu noble stable" > /etc/apt/sources.list.d/docker.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends docker-ce-cli docker-compose-plugin \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Klacks.Updater.dll"]
