namespace CatB.B1B2.ConsumerApp;

// B1: the CONSUMER's rooted entry point (ConfigureServices is a default entry-point symbol name)
// is the ONLY use site of CatB.B1B2.Widget.UsedFromConsumer. This is the cross-project edge that
// pins invariant #1: doc-comment-ID identity must let the metadata symbol resolve to the source node.
public sealed class Startup
{
    public void ConfigureServices()
    {
        var widget = new global::CatB.B1B2.Widget();
        widget.UsedFromConsumer();
    }
}
