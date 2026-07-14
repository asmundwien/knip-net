using System.Runtime.CompilerServices;

namespace CatE.E06;

// E6: await on a custom awaitable. GetAwaiter / IsCompleted / GetResult (+ OnCompleted for
// INotifyCompletion) are invoked by the await lowering with no IdentifierName at the use site.
// CORRECT behavior: GetAwaiter, IsCompleted, GetResult ALIVE.
public sealed class Delayed
{
    // ALIVE (hypothesis): bound by await pattern below.
    public Awaiter GetAwaiter() => new();

    public sealed class Awaiter : INotifyCompletion
    {
        public bool IsCompleted => true;                 // ALIVE (hypothesis)
        public int GetResult() => 42;                    // ALIVE (hypothesis)
        public void OnCompleted(System.Action continuation) => continuation(); // INotifyCompletion impl
    }
}

public sealed class Root
{
    public async System.Threading.Tasks.Task<int> ConfigureServices()
    {
        return await new Delayed();
    }
}
