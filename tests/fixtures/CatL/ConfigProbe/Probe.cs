namespace CatL.ConfigProbe
{
    // A rooted entry point keeps Program alive; DeadHelper is a dead sibling (anti-vacuous) so the
    // fixture actually produces a finding when analyzed. L6/L7 exercise CONFIG behavior, not findings,
    // but a non-empty run keeps the CLI honest (exit 1 when analyzed without --no-fail).
    public sealed class Program
    {
        public static void Main() { }
    }

    public sealed class DeadHelper
    {
        public void NeverCalled() { }
    }
}
