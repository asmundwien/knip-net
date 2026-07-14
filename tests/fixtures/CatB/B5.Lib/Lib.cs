namespace CatB.B5;

// B5: run under `treatAllPublicAsUsed: true`. Every externally visible symbol is a root, so public
// dead code is suppressed; only non-public dead code is still reportable.
public sealed class Surface
{
    // B5 NOT-FLAGGED: unused PUBLIC member. Rooted by treatAllPublicAsUsed -> must NOT be reported.
    public void UnusedPublic() { }

    // B5 DEAD SIBLING: PRIVATE unused member is not externally visible, so treatAllPublicAsUsed does
    // not root it. Unreferenced -> flagged. Proves the flag suppresses only the public surface.
    private void UnusedPrivate() { }
}
