namespace CatF.F14;

// A conventionally named Startup host. Its public convention methods are invoked by the host even
// though source code never calls them. Both static and instance methods are valid host shapes.
public sealed class Startup
{
    public void Configure()
    {
        var ordinary = new OrdinaryHost();
        ordinary.Touch();
    }

    public static void ConfigureServices() { }

    public void ConfigureContainer() { }

    private void Configure(string value) => _ = value;

    public void Other() { }
}

// Kept alive from Startup.Configure so dead members are reported individually. None of these same-name
// methods is a runtime or framework entry point; overload and static/instance shape must not matter.
public sealed class OrdinaryHost
{
    public void Touch() { }

    public static void Main() { }

    public void Main(string[] args) => _ = args;

    public void Configure() { }

    public static void ConfigureServices() { }

    public void ConfigureContainer() { }
}
