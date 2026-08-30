namespace CatF.F13;

// F13: an entry type selected by an explicit `*Controller` glob is runtime-activated. A ctor-injected
// field assigned only inside the ctor and a helper called only from the ctor must stay alive. Before
// runtime activation was modeled, the type and public members were rooted but ctor-only state cascaded dead.
public sealed class WidgetController
{
    // ALIVE: assigned in the ctor from the injected dependency (used only during construction).
    private readonly WidgetStore _store;

    // DEAD SIBLING (anti-vacuous): a private field never assigned/read -> flagged.
    private readonly object _unusedField;

    public WidgetController(WidgetStore store)
    {
        _store = store;
        Configure();
    }

    // ALIVE: called only from the ctor.
    private void Configure() { }

    // ALIVE (root): public member of the explicitly configured entry type.
    public void Index() { }
}

// ALIVE: named as the ctor parameter type -> its type node stays alive via the ctor signature edge.
// (Its own members are not exercised here; the class itself being referenced is enough to keep the
// TYPE reachable, which is all F13 asserts.)
public sealed class WidgetStore { }
