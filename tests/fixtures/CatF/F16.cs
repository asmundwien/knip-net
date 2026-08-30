using System;

namespace CatF.F16;

[AttributeUsage(AttributeTargets.Method)]
public sealed class RouteAttribute : Attribute { }

public sealed class Endpoint
{
    public void KeepAlive() { }

    [Route]
    public void UserDefinedRoute() { }
}
