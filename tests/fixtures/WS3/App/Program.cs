using System;
using Newtonsoft.Json;

namespace WS3.App;

// Production entry point (Main is a default entry-point symbol -> root). It touches the Newtonsoft.Json
// assembly (JsonConvert), so that PackageReference is exercised and NOT flagged. It never touches the
// Humanizer.Core assembly (that reference is flagged unused) nor PolySharp (an analyzer/source-generator
// package that delivers no referenceable assembly -> emitted with a build-only hazard, low confidence).
public static class Program
{
    public static void Main()
    {
        var json = JsonConvert.SerializeObject(new { hello = "world" });
        Console.WriteLine(json);
    }
}
