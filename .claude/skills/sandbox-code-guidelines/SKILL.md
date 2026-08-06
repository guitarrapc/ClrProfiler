---
name: sandbox-code-guidelines
description: Guidelines for ClrProfiler experiments and manual demonstrations using `src/ConsoleApp` and `src/CustomConsoleApp`. Use for exploratory CLR event generation, timer sampling, lifecycle checks, custom callback handlers, console or Datadog metric inspection, and small performance probes that are not yet production tests.
---

# Sandbox Code Guidelines

Use the existing sample applications for manual or exploratory work:

- `src/ConsoleApp`: exercise the packaged Datadog adapter and local DogStatsD-style flow.
- `src/CustomConsoleApp`: exercise `IClrTrackerCallbackHandler` and custom metric handling.

Do not use `dotnet-script` or assume that `sandbox/DotnetFiles` exists. Prefer a focused xUnit test when the behavior can be verified deterministically.

## Workflow

1. Choose the smallest existing sample that exposes the behavior.
2. Keep the experiment isolated from library API changes until it proves the approach.
3. Run the sample in Release mode from the repository root:

```powershell
dotnet run -c Release --project src/ConsoleApp/ConsoleApp.csproj
dotnet run -c Release --project src/CustomConsoleApp/CustomConsoleApp.csproj
```

4. Convert confirmed behavior into the appropriate xUnit project before changing production code.
5. Remove task-specific probes, delays, and forced allocations unless they improve the maintained sample itself.

## Guardrails

- Treat sample output as manual evidence, not a regression test. CLR scheduling, GC timing, process counters, and UDP delivery vary by machine.
- Use bounded waits and cancellation. Do not add infinite loops that cannot terminate through `CancellationToken`, disposal, or a clear user action.
- Do not add secrets, production Datadog endpoints, private tags, host identities, or user payloads.
- Keep network tests local. Use loopback or a fake callback/handler; do not require a running cloud agent for automated verification.
- Do not infer channel losslessness or event ordering from a console run. Verify those properties with synthetic listener inputs in `tests/ClrProfiler.UnitTest`.
- Do not use a sample stopwatch as a benchmark conclusion. Follow `performance-requirements` for repeatable measurements.
- Preserve the purpose of each sample. Add a new sample project only when the scenario cannot fit either existing application without obscuring it.
- Keep sample target frameworks and package versions centralized and consistent with the repository project files.
