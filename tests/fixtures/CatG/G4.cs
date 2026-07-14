using System.Collections.Generic;
using System.Threading.Tasks;

namespace CatG.G4;

// G4: async methods and iterator (yield) methods are ordinary members. The used ones stay alive; the
// unused ones are flagged just like any other method. (The state machines they compile into are
// compiler-generated and never reported.)
public sealed class Sample
{
    // Root: awaits the async member and enumerates the iterator member.
    public async Task<int> ConfigureServices()
    {
        var sum = 0;
        foreach (var n in UsedIterator()) sum += n;
        return sum + await UsedAsync();
    }

    // ALIVE: awaited from the root.
    private async Task<int> UsedAsync()
    {
        await Task.Yield();
        return 1;
    }

    // ALIVE: enumerated from the root.
    private IEnumerable<int> UsedIterator()
    {
        yield return 1;
        yield return 2;
    }

    // DEAD SIBLING: unused async method -> flagged.
    private async Task<int> UnusedAsync()
    {
        await Task.Yield();
        return 0;
    }

    // DEAD SIBLING: unused iterator method -> flagged.
    private IEnumerable<int> UnusedIterator()
    {
        yield return 9;
    }
}
