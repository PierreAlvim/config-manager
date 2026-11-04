# Generic Dockerfile for every .NET service. Build context = repository root.
#   --build-arg PROJECT=src/Management/ConfigManager.Management.Api/ConfigManager.Management.Api.csproj
#   --build-arg ASSEMBLY=ConfigManager.Management.Api
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT
WORKDIR /repo
COPY Directory.Build.props ./
COPY src/ src/
RUN dotnet publish "$PROJECT" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
ARG ASSEMBLY
ENV ASSEMBLY=${ASSEMBLY} ASPNETCORE_URLS=http://+:8080
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
HEALTHCHECK --interval=5s --timeout=3s --retries=30 CMD curl -fsS http://localhost:8080/health/live || exit 1
ENTRYPOINT ["sh", "-c", "exec dotnet \"$ASSEMBLY.dll\""]
