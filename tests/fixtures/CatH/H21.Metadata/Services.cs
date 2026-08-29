namespace CatH.DiConstructorActivation.Metadata;

public sealed class MetadataRegisteredService
{
    private readonly string _state = BuildState();

    private static string BuildState() => "ready";

    internal void NeverCalled() => System.Console.WriteLine(_state);
}

public sealed class MetadataFactoryRegisteredService
{
    private readonly string _state = BuildState();

    private static string BuildState() => "ready";

    internal void NeverCalled() => System.Console.WriteLine(_state);
}
