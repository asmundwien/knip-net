using System;
using Newtonsoft.Json;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace WS3.App;

// Production entry point (Main is a default entry-point symbol -> root). It touches the Newtonsoft.Json
// assembly (JsonConvert), so that PackageReference is exercised and NOT flagged. It never touches the
// Humanizer.Core assembly (that reference is flagged unused) nor PolySharp (an analyzer/source-generator
// package that delivers no referenceable assembly -> emitted with a build-only hazard, low confidence).
//
// It also touches SwaggerGenOptions, whose assembly (Swashbuckle.AspNetCore.SwaggerGen) is delivered by a
// DEPENDENCY of the Swashbuckle.AspNetCore METAPACKAGE — the metapackage's own compile set is empty. WS3
// grades the metapackage against its dependency CLOSURE, sees the touched assembly, and does NOT flag it
// (regression guard against reporting a used metapackage as unused / build-only).
public static class Program
{
    public static void Main()
    {
        var json = JsonConvert.SerializeObject(new { hello = "world" });
        Console.WriteLine(json);

        var swaggerOptions = new SwaggerGenOptions();
        Console.WriteLine(swaggerOptions.GetType().Name);
    }
}
