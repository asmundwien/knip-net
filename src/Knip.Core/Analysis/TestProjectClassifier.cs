using Knip.Core.Configuration;
using Microsoft.CodeAnalysis;

namespace Knip.Core.Analysis;

/// <summary>Whether a project is production code or a test project (WS7 two-color reachability).</summary>
public enum ProjectKind
{
    Production,
    Test,
}

/// <summary>
/// The classification verdict for one project: its <see cref="Kind"/> and WHICH signal decided it — the
/// latter surfaced via <c>-v</c> and the machine reliability block, because nobody trusts an
/// <see cref="Model.FindingKind.OnlyUsedByTests"/> finding without seeing how the project was classified.
/// </summary>
public sealed record ProjectClassification(string Project, ProjectKind Kind, string Signal);

/// <summary>
/// Classifies a Roslyn <see cref="Project"/> as production or test (K7 — DECIDED). Signal order,
/// FIRST MATCH WINS:
/// <list type="number">
///   <item>explicit <c>testProjects</c> config globs (<c>testProjects:&lt;glob&gt;</c>) — override everything;</item>
///   <item>a referenced test-framework ASSEMBLY in the <see cref="Compilation"/>
///     (<c>Microsoft.VisualStudio.TestPlatform.TestFramework</c> / <c>xunit.core</c> / <c>nunit.framework</c>) —
///     (<c>referencedAssembly:&lt;name&gt;</c>) — preferred over the MSBuild <c>IsTestProject</c> property
///     (Roslyn's Project model doesn't surface it; no MSBuild-evaluation machinery is built for it);</item>
///   <item>project-NAME globs (<c>*Tests</c> / <c>*.Test</c> / <c>*.Tests</c>) as a fallback
///     (<c>nameGlob:&lt;glob&gt;</c>).</item>
/// </list>
/// No match → production, signal <c>default</c>.
/// </summary>
internal static class TestProjectClassifier
{
    /// <summary>The test-framework assembly names that identify a test project (signal 2).</summary>
    private static readonly string[] TestFrameworkAssemblies =
        ["Microsoft.VisualStudio.TestPlatform.TestFramework", "xunit.core", "nunit.framework"];

    /// <summary>The default project-NAME globs a test project matches (signal 3).</summary>
    private static readonly string[] NameGlobs = ["*Tests", "*.Test", "*.Tests"];

    public static ProjectClassification Classify(Project project, Compilation compilation, KnipConfig config) =>
        Classify(project.Name, compilation, config);

    /// <summary>Classify by project NAME + compilation (the fields the signals actually read).</summary>
    public static ProjectClassification Classify(string projectName, Compilation compilation, KnipConfig config)
    {
        // Signal 1 — explicit testProjects config globs override everything.
        foreach (var glob in config.TestProjects)
            if (Glob.IsMatch(projectName, glob))
                return new ProjectClassification(projectName, ProjectKind.Test, $"testProjects:{glob}");

        // Signal 2 — a referenced test-framework assembly in the compilation. Preferred over name globs;
        // read straight off the compilation's referenced assembly symbols (no MSBuild property machinery).
        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly) continue;
            var name = assembly.Identity.Name;
            foreach (var framework in TestFrameworkAssemblies)
                if (string.Equals(name, framework, StringComparison.OrdinalIgnoreCase))
                    return new ProjectClassification(
                        projectName, ProjectKind.Test, $"referencedAssembly:{framework}");
        }

        // Signal 3 — project-name globs (fallback).
        foreach (var glob in NameGlobs)
            if (Glob.IsMatch(projectName, glob))
                return new ProjectClassification(projectName, ProjectKind.Test, $"nameGlob:{glob}");

        return new ProjectClassification(projectName, ProjectKind.Production, "default");
    }
}
