using Knip.Cli;
using Microsoft.Build.Locator;

// MSBuild must be registered before any Roslyn MSBuild types are touched. Keep this the very
// first thing that runs, and keep all workspace usage behind the Runner method boundary.
if (!MSBuildLocator.IsRegistered)
    MSBuildLocator.RegisterDefaults();

return await Runner.RunAsync(args);
