using System.Xml;
using System.Xml.Linq;
using Knip.Core.Model;
using Microsoft.CodeAnalysis;

namespace Knip.Core.Analysis;

/// <summary>
/// Locates the &lt;ProjectReference/&gt; element for a referenced project inside a .csproj, so the finding's
/// deletion <see cref="SourceSpan"/> points at the exact element an agent removes (WS8 §3.3). Best-effort:
/// returns null if the file can't be read/parsed or the element can't be matched, leaving Line/Column 0.
/// </summary>
internal static class ProjectFileSpan
{
    public static SourceSpan? ForProjectReference(string csprojPath, Project referenced)
    {
        if (string.IsNullOrEmpty(csprojPath) || !File.Exists(csprojPath)) return null;

        var referencedFile = referenced.FilePath is null ? null : Path.GetFileName(referenced.FilePath);

        XDocument doc;
        try
        {
            doc = XDocument.Load(csprojPath, LoadOptions.SetLineInfo);
        }
        catch (Exception ex) when (ex is IOException or XmlException or UnauthorizedAccessException)
        {
            return null;
        }

        foreach (var element in doc.Descendants().Where(e => e.Name.LocalName == "ProjectReference"))
        {
            var include = element.Attribute("Include")?.Value;
            if (include is null) continue;

            var includeFile = Path.GetFileName(include.Replace('\\', '/'));
            var matches =
                (referencedFile is not null && string.Equals(includeFile, referencedFile, StringComparison.OrdinalIgnoreCase))
                || includeFile.IndexOf(referenced.Name, StringComparison.OrdinalIgnoreCase) >= 0;
            if (!matches) continue;

            if (element is not IXmlLineInfo start || !start.HasLineInfo()) return null;

            // XElement line info marks the '<' of the tag; span its single line to end-of-element text.
            var lines = TryReadLine(csprojPath, start.LineNumber);
            var endColumn = lines is null ? start.LinePosition : lines.TrimEnd().Length + 1;

            return new SourceSpan(
                csprojPath,
                new SourcePosition(start.LineNumber, Math.Max(1, start.LinePosition - 1)),
                new SourcePosition(start.LineNumber, endColumn));
        }

        return null;
    }

    private static string? TryReadLine(string path, int oneBasedLine)
    {
        try
        {
            var lines = File.ReadAllLines(path);
            return oneBasedLine >= 1 && oneBasedLine <= lines.Length ? lines[oneBasedLine - 1] : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
