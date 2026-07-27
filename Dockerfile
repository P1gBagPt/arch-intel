# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore "src/Api/ArchIntel.Api/ArchIntel.Api.csproj"
RUN dotnet publish "src/Api/ArchIntel.Api/ArchIntel.Api.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
COPY --from=build /app/publish .

# SQLite database file lives on a mounted volume in single-instance deployments (05-rest-api.md
# Section 8.1); Phase 4 with PostgreSQL (02-graph-store.md's own roadmap) removes this requirement.
VOLUME ["/app/data"]

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s \
  CMD wget --spider -q http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "ArchIntel.Api.dll"]
