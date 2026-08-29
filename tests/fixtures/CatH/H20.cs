using System.Collections.Generic;

namespace CatH.H20;

// A serializer sees a collection-derived target. Its element type is part of the serialized payload even
// though source code never reads the element's data members.
public static class JsonSerializer
{
    public static T Deserialize<T>(string json)
    {
        _ = json;
        return default!;
    }
}

public interface IHandles<T>
{
    void Handle(T value);
}

public sealed class SerializedItems : List<SerializedItem>, IHandles<UnrelatedCollaborator>
{
    public string BatchName { get; set; } = "";
    public void Handle(UnrelatedCollaborator value) => _ = value;
}

public sealed class SerializedItem
{
    public string Value { get; set; } = "";
    public string Describe() => "item";
}

public sealed class ArrayItem
{
    public string Value { get; set; } = "";
}

// This type is another generic collaborator of the serialized collection target, but it is not a collection
// element. Traversal must not treat its data members as serialized collection-element members.
public sealed class UnrelatedCollaborator
{
    public string DeadValue { get; set; } = "";
}

public sealed class Startup
{
    public void ConfigureServices()
    {
        _ = JsonSerializer.Deserialize<SerializedItems>("[]");
        _ = JsonSerializer.Deserialize<ArrayItem[]>("[]");
    }
}
