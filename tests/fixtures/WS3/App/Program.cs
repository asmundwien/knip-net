using System;
using Newtonsoft.Json;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace WS3.App;

// The direct Newtonsoft.Json reference is used through JsonConvert. OpenApiInfo comes from the
// NETStandard dependency of the otherwise-unused Swashbuckle.AspNetCore.Swagger reference; that dependency
// usage must not hide the unused ordinary package. SwaggerGenOptions comes from a dependency of the
// compile-less Swashbuckle.AspNetCore metapackage and keeps that metapackage alive.
public static class Program
{
    public static void Main()
    {
        var json = JsonConvert.SerializeObject(new { hello = "world" });
        Console.WriteLine(json);
        var openApiInfo = new OpenApiInfo { Title = "dependency-only usage" };
        Console.WriteLine(openApiInfo.Title);

        var swaggerOptions = new SwaggerGenOptions();
        Console.WriteLine(swaggerOptions.GetType().Name);
    }
}
