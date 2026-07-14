namespace CatB.B3.ConsumerApp;

// B3: the friend consumer binds the lib's INTERNAL member (allowed by [InternalsVisibleTo]).
// Rooted via ConfigureServices; the only use site of Engine.InternalUsedByFriend.
public sealed class Startup
{
    public void ConfigureServices()
    {
        var engine = new global::CatB.B3.Engine();
        engine.InternalUsedByFriend();
    }
}
