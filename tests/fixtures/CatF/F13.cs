namespace CatF.F13;

// F13 (FIX #4b): an ENTRY TYPE (`*Controller` glob) is DI-constructed by the framework to invoke its
// actions — so its instance ctor is USED. A ctor-injected field assigned ONLY inside the ctor and a
// helper called ONLY from the ctor must stay ALIVE. Before FIX #4 the controller type + its public
// members were rooted but NOT the ctor, so a ctor-only field/helper cascaded to dead.
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

    // ALIVE (root): public member of the entry type.
    public void Index() { }
}

// ALIVE: named as the ctor parameter type -> its type node stays alive via the ctor signature edge.
// (Its own members are not exercised here; the class itself being referenced is enough to keep the
// TYPE reachable, which is all F13 asserts.)
public sealed class WidgetStore { }
