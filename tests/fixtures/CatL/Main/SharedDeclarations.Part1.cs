namespace CatL.Main;

public partial class SharedDeclarations
{
    // The second declarator is alive. Deleting the first field's containing declaration would delete it too.
    private int _deadField, _liveField;

    // Likewise for event declarators: only _liveEvent is referenced by reachable code.
    private event System.Action? _deadEvent, _liveEvent;

    public void UseSiblings()
    {
        _liveField++;
        _liveEvent += static () => { };
    }

    // The implementation is in a separate source file, so this symbol has no complete single-file span.
    private partial void DeadPartialMethod();

    // The counterpart is generated. This user-authored declaration must not become a deletion finding.
    private partial void GeneratedPartialMethod();
}
