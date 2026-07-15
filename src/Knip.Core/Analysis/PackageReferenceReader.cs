using System.Xml;
using System.Xml.Linq;
using Knip.Core.Model;

namespace Knip.Core.Analysis;

/// <summary>
/// Reads the &lt;PackageReference&gt; elements declared in a .csproj so WS3 can decide which are unused and
/// point the deletion <see cref="SourceSpan"/> at the exact element (WS8 §3.3). Only the DECLARED (direct)
/// references are read — transitive dependencies are not authored here and are never flagged.
/// </summary>
internal static class PackageReferenceReader
{
    /// <summary>A directly-declared package reference and the metadata WS3 needs to grade it.</summary>
    internal sealed record DeclaredPackage(string Id, bool PrivateAssetsAll, SourceSpan? Span);

    /// <summary>
    /// The &lt;PackageReference Include="…"&gt; elements in <paramref name="csprojPath"/>. Empty when the
    /// file can't be read/parsed. <see cref="DeclaredPackage.PrivateAssetsAll"/> is true when the reference
    /// carries <c>PrivateAssets="all"</c> (attribute or child element) — the classic build-only marker.
    /// </summary>
    public static IReadOnlyList<DeclaredPackage> Read(string csprojPath)
    {
        if (string.IsNullOrEmpty(csprojPath) || !File.Exists(csprojPath))
            return [];

        XDocument doc;
        try
        {
            doc = XDocument.Load(csprojPath, LoadOptions.SetLineInfo);
        }
        catch (Exception ex) when (ex is IOException or XmlException or UnauthorizedAccessException)
        {
            return [];
        }

        var lines = TryReadAllLines(csprojPath);
        var result = new List<DeclaredPackage>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var element in doc.Descendants().Where(e => e.Name.LocalName == "PackageReference"))
        {
            var id = element.Attribute("Include")?.Value ?? element.Attribute("Update")?.Value;
            if (id is null || id.Trim().Length == 0 || !seen.Add(id)) continue;

            result.Add(new DeclaredPackage(id, HasPrivateAssetsAll(element), SpanFor(csprojPath, element, lines)));
        }

        return result;
    }

    private static bool HasPrivateAssetsAll(XElement element)
    {
        var attr = element.Attribute("PrivateAssets")?.Value;
        if (IsAll(attr)) return true;

        var child = element.Elements().FirstOrDefault(e => e.Name.LocalName == "PrivateAssets");
        return IsAll(child?.Value);

        static bool IsAll(string? value) =>
            value is not null && value.Trim().Equals("all", StringComparison.OrdinalIgnoreCase);
    }

    private static SourceSpan? SpanFor(string csprojPath, XElement element, string[]? lines)
    {
        if (element is not IXmlLineInfo info || !info.HasLineInfo()) return null;

        // XElement line info marks the '<' of the tag (LinePosition is 1-based to the char AFTER '<').
        var line = info.LineNumber;
        var startColumn = Math.Max(1, info.LinePosition - 1);
        var endColumn = lines is not null && line >= 1 && line <= lines.Length
            ? lines[line - 1].TrimEnd().Length + 1
            : startColumn;

        return new SourceSpan(
            csprojPath,
            new SourcePosition(line, startColumn),
            new SourcePosition(line, endColumn));
    }

    private static string[]? TryReadAllLines(string path)
    {
        try { return File.ReadAllLines(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
