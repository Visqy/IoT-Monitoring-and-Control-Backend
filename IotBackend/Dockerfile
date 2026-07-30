# syntax=docker/dockerfile:1

# --- Stage 1: build & publish (pakai SDK, image besar tapi tidak ikut ke image final) ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY IotBackend/IotBackend.csproj IotBackend/
RUN dotnet restore IotBackend/IotBackend.csproj

COPY IotBackend/ IotBackend/
RUN dotnet publish IotBackend/IotBackend.csproj -c Release -o /app/publish --no-restore

# --- Stage 2: runtime (cuma ASP.NET runtime, tanpa SDK) ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl dipakai oleh healthcheck di docker-compose.yml (image dasar tidak menyertakannya).
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Kestrel dengar di 8080 (HTTP saja -- lihat catatan HTTPS di README/docker-compose.yml).
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "IotBackend.dll"]
