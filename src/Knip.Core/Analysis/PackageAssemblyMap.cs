using System.Text.Json;
using Microsoft.CodeAnalysis;

namespace Knip.Core.Analysis;

/// <summary>
/// Maps a project's declared NuGet packages to the ASSEMBLIES each delivers, so WS3 can tell whether a
/// &lt;PackageReference&gt; is touched (any of its assemblies appears in the project's external-assembly
/// use set) or unused.
///
/// <para>Preferred source: the restore graph <c>obj/project.assets.json</c>. Its
/// <c>targets[tfm][id/version].compile</c> and <c>runtime</c> entries identify each package's own
/// referenceable assemblies. Ordinary packages are graded against that own surface; dependency assemblies
/// cannot make them appear used.</para>
///
/// <para>The assets dependency graph is retained for packages with no own compile surface. Their closure
/// distinguishes a genuine metapackage whose dependencies provide its API from an analyzer, source
/// generator, or build-only package whose closure is compile-less.</para>
///
/// <para>Fallback (no assets file): the Roslyn <see cref="Project.MetadataReferences"/> paths, where a
/// NuGet assembly lives under <c>…/packages/&lt;id&gt;/&lt;version&gt;/lib/…/X.dll</c> — the path segment
/// after <c>packages/</c> gives the package id. The fallback cannot see build-only packages (they
/// contribute no metadata reference) NOR the dependency graph (no closure), so its per-package assemblies
/// are its own only.</para>
///
/// All keys are strings (assembly / package names) — invariant #1; no symbols are retained.
/// </summary>
internal static class PackageAssemblyMap
{
    /// <summary>
    /// The assemblies a package delivers itself, whether it delivers any referenceable compile assembly of
    /// its own, and the ids of the packages it directly depends on (from the assets <c>dependencies</c>
    /// map). <see cref="Dependencies"/> is empty for the metadata-reference fallback (no graph available).
    /// </summary>
    internal sealed record PackageAssemblies(
        IReadOnlyCollection<string> Assemblies,
        bool DeliversCompileAssembly,
        IReadOnlyCollection<string> Dependencies);

    /// <summary>
    /// The dependency-closure view of a declared package: the assemblies delivered by the package AND every
    /// package it transitively depends on, plus whether ANY of them delivers a referenceable compile
    /// assembly. A metapackage (empty own compile) whose closure delivers used assemblies resolves to
    /// <see cref="DeliversCompileAssembly"/> = true here; a genuine analyzer / build-only package resolves
    /// to false (its whole closure is compile-less).
    /// </summary>
    internal sealed record ClosureAssemblies(
        IReadOnlyCollection<string> Assemblies, bool DeliversCompileAssembly);

    /// <summary>
    /// Package id (case-insensitive, as declared) → the assemblies it delivers for the project's target.
    /// Built from <c>project.assets.json</c> when present, else from metadata-reference paths. Empty when
    /// neither source yields anything (e.g. a project that failed to restore) — the caller then leaves the
    /// declared package references alone (conservative — no restore data means no verdict).
    /// </summary>
    public static IReadOnlyDictionary<string, PackageAssemblies> Build(Project project)
    {
        var fromAssets = TryFromAssets(project);
        if (fromAssets is not null) return fromAssets;
        return FromMetadataReferences(project);
    }

    /// <summary>
    /// The DEPENDENCY-CLOSURE assemblies for <paramref name="packageId"/>: the union of the compile+runtime
    /// assemblies of the package and every package it transitively depends on (per the assets
    /// <c>dependencies</c> graph in <paramref name="map"/>), plus whether ANY package in that closure
    /// delivers a referenceable compile assembly. Null when the id is not in the map (no delivered-assembly
    /// evidence at all — the caller then leaves the reference alone). Cycle-safe.
    /// </summary>
    public static ClosureAssemblies? Closure(
        IReadOnlyDictionary<string, PackageAssemblies> map, string packageId)
    {
        if (!map.TryGetValue(packageId, out _)) return null;

        var assemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deliversCompile = false;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>();
        stack.Push(packageId);

        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (!visited.Add(id)) continue;
            if (!map.TryGetValue(id, out var pkg)) continue; // dependency not in the graph (framework/meta)
            foreach (var assembly in pkg.Assemblies) assemblies.Add(assembly);
            deliversCompile |= pkg.DeliversCompileAssembly;
            foreach (var dep in pkg.Dependencies) stack.Push(dep);
        }

        return new ClosureAssemblies(assemblies, deliversCompile);
    }

    // ---- project.assets.json (preferred, authoritative) ------------------------------------------

    private static Dictionary<string, PackageAssemblies>? TryFromAssets(Project project)
    {
        var assetsPath = AssetsPathFor(project);
        if (assetsPath is null || !File.Exists(assetsPath)) return null;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(File.ReadAllText(assetsPath));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("targets", out var targets)
                || targets.ValueKind != JsonValueKind.Object)
                return null;

            // One assets file can carry multiple targets (TFM, TFM/rid). Take the first target with no
            // runtime id (the plain TFM compile graph); merge is unnecessary — knip loads one TFM.
            var map = new Dictionary<string, PackageAssemblies>(StringComparer.OrdinalIgnoreCase);
            foreach (var target in targets.EnumerateObject())
            {
                if (target.Name.Contains('/')) continue; // skip TFM/rid runtime targets
                foreach (var lib in target.Value.EnumerateObject())
                {
                    // "type" == "package" filters out project-to-project libraries in the same graph.
                    if (lib.Value.TryGetProperty("type", out var type)
                        && !string.Equals(type.GetString(), "package", StringComparison.Ordinal))
                        continue;

                    var (id, _) = SplitLibKey(lib.Name);
                    var assemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var hasCompile = CollectAssemblies(lib.Value, "compile", assemblies);
                    CollectAssemblies(lib.Value, "runtime", assemblies);
                    map[id] = new PackageAssemblies(assemblies, hasCompile, CollectDependencies(lib.Value));
                }
                break; // first plain-TFM target only
            }

            return map.Count > 0 ? map : null;
        }
    }

    /// <summary>Add the DLL basenames under <paramref name="section"/> to <paramref name="into"/>; returns whether any real DLL was present.</summary>
    private static bool CollectAssemblies(JsonElement lib, string section, HashSet<string> into)
    {
        if (!lib.TryGetProperty(section, out var files) || files.ValueKind != JsonValueKind.Object)
            return false;

        var any = false;
        foreach (var file in files.EnumerateObject())
        {
            var path = file.Name;
            // "_._" is the placeholder NuGet uses for "compatible but no assembly" — ignore it.
            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
            var name = Path.GetFileNameWithoutExtension(path.Replace('\\', '/'));
            if (name.Length == 0) continue;
            into.Add(name);
            any = true;
        }
        return any;
    }

    /// <summary>
    /// The package ids under the library's <c>dependencies</c> map (keys are ids, values are version
    /// ranges — invariant #1 keeps only the id). Empty when the library declares no dependencies.
    /// </summary>
    private static IReadOnlyCollection<string> CollectDependencies(JsonElement lib)
    {
        if (!lib.TryGetProperty("dependencies", out var deps) || deps.ValueKind != JsonValueKind.Object)
            return [];

        var ids = new List<string>();
        foreach (var dep in deps.EnumerateObject())
            ids.Add(dep.Name);
        return ids;
    }

    private static string? AssetsPathFor(Project project)
    {
        var dir = project.FilePath is null ? null : Path.GetDirectoryName(project.FilePath);
        return dir is null ? null : Path.Combine(dir, "obj", "project.assets.json");
    }

    /// <summary>"Newtonsoft.Json/13.0.3" → ("Newtonsoft.Json", "13.0.3").</summary>
    private static (string Id, string Version) SplitLibKey(string key)
    {
        var slash = key.IndexOf('/');
        return slash < 0 ? (key, "") : (key.Substring(0, slash), key.Substring(slash + 1));
    }

    // ---- metadata-reference fallback (no assets file) --------------------------------------------

    private static Dictionary<string, PackageAssemblies> FromMetadataReferences(Project project)
    {
        // package id (lower) → delivered assembly names, inferred from …/packages/<id>/<version>/lib/…/X.dll
        var byPackage = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var reference in project.MetadataReferences)
        {
            if (reference is not PortableExecutableReference pe || pe.FilePath is null) continue;
            var id = PackageIdFromPath(pe.FilePath);
            if (id is null) continue;
            var assembly = Path.GetFileNameWithoutExtension(pe.FilePath);
            if (assembly.Length == 0) continue;
            if (!byPackage.TryGetValue(id, out var set))
                byPackage[id] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            set.Add(assembly);
        }

        // No assets graph in the fallback → no dependency closure. Each package's own assemblies stand
        // alone (Dependencies empty). A metapackage would be invisible here (contributes no metadata
        // reference), so it is simply absent from the map and left alone by the caller (conservative).
        var map = new Dictionary<string, PackageAssemblies>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, assemblies) in byPackage)
            map[id] = new PackageAssemblies(
                assemblies, DeliversCompileAssembly: assemblies.Count > 0, Dependencies: []);
        return map;
    }

    /// <summary>
    /// The package id from a NuGet metadata-reference path: the segment after a <c>packages/</c> (global
    /// packages folder) or <c>packages\</c> (packages.config / legacy) directory. Null for framework /
    /// non-NuGet references (BCL reference assemblies, project outputs).
    /// </summary>
    private static string? PackageIdFromPath(string path)
    {
        var segments = path.Replace('\\', '/').Split('/');
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (string.Equals(segments[i], "packages", StringComparison.OrdinalIgnoreCase))
            {
                var candidate = segments[i + 1];
                // Global packages folder: .../packages/<id>/<version>/lib/... → id is segments[i+1].
                // packages.config layout: .../packages/<id>.<version>/lib/... → strip the trailing version.
                if (i + 2 < segments.Length
                    && System.Text.RegularExpressions.Regex.IsMatch(
                        segments[i + 2], @"^\d+(\.\d+)+"))
                    return candidate; // global folder: next segment is a bare version

                var dotVersion = System.Text.RegularExpressions.Regex.Match(candidate, @"\.\d+(\.\d+)+.*$");
                return dotVersion.Success ? candidate.Substring(0, dotVersion.Index) : candidate;
            }
        }
        return null;
    }
}
