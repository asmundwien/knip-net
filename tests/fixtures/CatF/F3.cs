namespace CatF.F3;

// F3: a subtype of a configured baseType is a root (and its public members), exactly like F2 but keyed
// on the base class instead of the name glob. BaseTypes match by FULLY-QUALIFIED display string, so a
// LOCAL base class works once its FQN "CatF.F3.ControllerBase" is passed in EntryPoints.BaseTypes.
// (The test passes a config whose BaseTypes = ["CatF.F3.ControllerBase"].)
public abstract class ControllerBase { }

public sealed class WidgetEndpoint : ControllerBase
{
    // ALIVE (root): public member of a configured-base-type subtype.
    public void Handle() { }

    // DEAD SIBLING: private, so not externally visible -> not auto-rooted, uncalled -> flagged.
    private void Internal() { }
}

// DEAD SIBLING: does NOT derive from ControllerBase -> not rooted, uncalled -> flagged (outermost).
public sealed class WidgetHelper
{
    public void Assist() { }
}
