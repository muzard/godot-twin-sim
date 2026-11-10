# Runs the core test suite in a clean Linux container with only the .NET SDK (no Godot).
# Smoke test I, acceptance criterion 4: "dotnet test is green in a Docker container, under a minute".
#
#   ./dev docker-test                     build the image and run the tests, with timing
#   docker build -f docker/test.Dockerfile -t twin-test . && docker run --rm twin-test
#
# The SDK tag matches global.json. Restore happens at build time, so the timed run is build + test only.
FROM mcr.microsoft.com/dotnet/sdk:10.0.100

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

WORKDIR /repo

# Project files first, so the restore layer is cached until a project or package version changes.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig Twin.sln Twin.Core.slnf ./
COPY src/Twin.Sim/Twin.Sim.csproj src/Twin.Sim/
COPY src/Twin.Headless/Twin.Headless.csproj src/Twin.Headless/
COPY tests/Twin.Testing/Twin.Testing.csproj tests/Twin.Testing/
COPY tests/Twin.Sim.Tests/Twin.Sim.Tests.csproj tests/Twin.Sim.Tests/
COPY tests/Twin.Scenarios.Tests/Twin.Scenarios.Tests.csproj tests/Twin.Scenarios.Tests/
COPY tests/Twin.Architecture.Tests/Twin.Architecture.Tests.csproj tests/Twin.Architecture.Tests/
COPY tests/Twin.Acceptance.Tests/Twin.Acceptance.Tests.csproj tests/Twin.Acceptance.Tests/
RUN dotnet restore Twin.Core.slnf

COPY . .

CMD ["dotnet", "test", "Twin.Core.slnf", "--no-restore", "--configuration", "Release"]
