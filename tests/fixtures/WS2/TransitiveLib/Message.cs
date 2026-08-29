namespace WS2.TransitiveLib;

// Used directly by UsedLib, but only transitively available to Consumer. Consumer has no declared
// <ProjectReference> deletion unit for this project and must never receive a removal finding for it.
public static class Message
{
    public const string Value = "hi";
}
