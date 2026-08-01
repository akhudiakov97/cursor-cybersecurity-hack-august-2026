# Multi-stage build: the SDK image (large, has compilers) builds the app, then only the
# published output is copied into the much smaller ASP.NET runtime image that actually
# ships and runs in production.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restoring with just the project file first lets Docker cache the (slow) NuGet restore
# layer separately from application code, so editing a .cs file doesn't force a re-restore.
COPY HoneyGuard.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

EXPOSE 8080

# Railway injects $PORT at runtime and expects the app to bind to it, so this must be a
# shell-form CMD (not the exec-form JSON array) for $PORT to actually get expanded.
# ASPNETCORE_HTTP_PORTS is the modern equivalent of setting ASPNETCORE_URLS just for the
# port number. Falls back to 8080 for any host that does not set $PORT itself.
CMD ASPNETCORE_HTTP_PORTS=${PORT:-8080} dotnet HoneyGuard.dll
