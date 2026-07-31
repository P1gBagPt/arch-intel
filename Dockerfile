# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore "src/Api/ArchIntel.Api/ArchIntel.Api.csproj"
RUN dotnet restore "src/Cli/Arch.Cli/Arch.Cli.csproj"
RUN dotnet publish "src/Api/ArchIntel.Api/ArchIntel.Api.csproj" -c Release -o /app/publish --no-restore
RUN dotnet publish "src/Cli/Arch.Cli/Arch.Cli.csproj" -c Release -o /app/cli --no-restore

# .arch/graph.db is gitignored (*.db) so it can't be copied straight from the repo — regenerate
# the demo scan fresh from source at build time instead, using the just-published CLI.
RUN dotnet restore samples/SampleErpSolution/SampleErpSolution.sln
RUN dotnet /app/cli/arch.dll scan --config samples/SampleErpSolution/arch.yaml

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# PORT defaults to 8080 for local/Fly-style deploys that expect a fixed port; hosts that assign
# their own port at runtime (e.g. Render, which injects PORT and expects the container to bind to
# it rather than honoring EXPOSE) override it via an env var, picked up by the entrypoint below.
ENV PORT=8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
COPY --from=build /app/publish .

# Bakes in the SampleErp demo repo, scanned fresh during the build stage above, so the live/demo
# deployment has real data to serve without needing a mounted customer repo. ARCH_CONFIG points
# ConfigDiscovery straight at it (bypassing the arch.yml-only walk-up — see
# Configuration/ConfigDiscovery.cs), read-only at runtime since nothing here re-scans.
COPY --from=build /src/samples/SampleErpSolution/arch.yaml /app/demo-repo/arch.yaml
COPY --from=build /src/samples/SampleErpSolution/.arch /app/demo-repo/.arch
ENV ARCH_CONFIG=/app/demo-repo/arch.yaml

# SQLite database file lives on a mounted volume in single-instance deployments (05-rest-api.md
# Section 8.1); Phase 4 with PostgreSQL (02-graph-store.md's own roadmap) removes this requirement.
VOLUME ["/app/data"]

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s \
  CMD wget --spider -q "http://localhost:${PORT}/health" || exit 1

# Shell form (not exec-form JSON) so $PORT is substituted at container start, not image-build time.
ENTRYPOINT ASPNETCORE_URLS="http://+:${PORT}" exec dotnet ArchIntel.Api.dll
