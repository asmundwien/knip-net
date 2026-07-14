namespace CatA.A6;

// A6: Target is referenced ONLY from Dead, which is itself unreachable. Dead code confers no life,
// so BOTH Dead and Target are flagged.
// LiveTarget is the dead sibling's control: same shape as Target but referenced from a rooted method,
// so it stays alive -- proving Target's deadness is about the caller's deadness, not the callee.
public sealed class Sample
{
    public void ConfigureServices() => LiveTarget();

    private void LiveTarget() { }

    private void Dead() => Target();
    private void Target() { }
}
