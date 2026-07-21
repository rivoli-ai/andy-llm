# Repository Agent Instructions

This file contains the shared project guidance for coding-agent sessions.

## Authorized Commands

### Testing and Coverage

- `dotnet test --collect:"XPlat Code Coverage" --results-directory ./TestResults` - Run tests with code coverage collection
- `reportgenerator -reports:"./TestResults/*/coverage.cobertura.xml" -targetdir:"./TestResults/CoverageReport" -reporttypes:Html` - Generate HTML coverage report
- `reportgenerator -reports:"./TestResults/*/coverage.cobertura.xml" -targetdir:"./TestResults/CoverageReport" -reporttypes:TextSummary` - Generate text summary coverage report

### Build and Development

- `dotnet build` - Build the solution
- `dotnet restore` - Restore NuGet packages
- `dotnet run --project <project_path>` - Run a .NET project (macOS/cross-platform)

### Platform-Specific Notes (macOS)

- Never use Mono or `.exe` files. This is a .NET 8.0 project running natively on macOS.
- Use `dotnet run` instead of compiling to `.exe` and running with Mono.
- Use `dotnet <command>` for all .NET operations.

## Project Information

- **Target Framework**: .NET 8.0
- **Test Framework**: xUnit
- **Coverage Tool**: Coverlet
- **Report Generator**: ReportGenerator global tool

## Development Workflow

When completing a set of tasks or phase milestones:

1. Review the relevant task list and all subtasks.
2. Mark completed Markdown checklist items with `[x]`.
3. Add a dated completion summary with key achievements.
4. Keep `README.md` and current phase status documentation up to date.
5. Use descriptive commit messages without mentioning a code assistant.

## Code Quality

- Add or update tests in the existing `tests/` assemblies for changes under `src/`.
- Run `dotnet format` before committing.
- Set up the repository pre-commit hooks when the scripts are available.
- Run `dotnet test` and verify actual behavior before claiming completion.
- Generate coverage reports for significant changes.
- Update test expectations when behavior changes intentionally.

## Documentation

- Keep `README.md` current with the target .NET version and supported features.
- Update local-development guidance when adding tools or processes.
- Keep task and conversion-plan progress accurate where those documents exist.

## Notes

- Coverage reports are generated under `TestResults/CoverageReport/` and must not be committed.
- Opt-in integration tests may require provider API keys; unit tests must remain deterministic without them.
