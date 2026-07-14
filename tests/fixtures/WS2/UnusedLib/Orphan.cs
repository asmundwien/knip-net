namespace WS2.UnusedLib;

// Referenced by Consumer's .csproj but NEVER touched by any Consumer symbol. There is no
// cross-assembly edge Consumer -> WS2.UnusedLib, so the <ProjectReference> is flagged as unused.
// (This public type is itself dead, but the project-reference finding is what WS2 asserts.)
public static class Orphan
{
    public static int Value() => 42;
}
