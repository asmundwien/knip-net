namespace WS2.UsedLib;

// Referenced AND used: Consumer.Main calls Greeter.Hello(), producing a cross-assembly symbol edge
// Consumer -> WS2.UsedLib. The <ProjectReference> to this project must therefore NOT be flagged.
public static class Greeter
{
    public static string Hello() => WS2.TransitiveLib.Message.Value;
}
