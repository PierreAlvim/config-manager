# Runs the whole .NET test suite (unit + integration via Testcontainers, needs docker.sock mounted).
FROM mcr.microsoft.com/dotnet/sdk:10.0
WORKDIR /repo
COPY Directory.Build.props ConfigManager.slnx ./
COPY src/ src/
COPY tests/ tests/
RUN dotnet restore ConfigManager.slnx
ENTRYPOINT ["dotnet", "test", "ConfigManager.slnx", "--no-restore", "--logger", "console;verbosity=normal"]
