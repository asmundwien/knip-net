namespace CatF.F6;

// F6 (file 2 of 2): the DEAD SIBLING for the top-level-statements scenario. This type is in namespace
// CatF.F6 and is never referenced by the top-level program, so it is flagged (outermost). Its presence
// proves the run analyzed the compilation while the synthesized Program/Main stayed rooted (asserted
// via the whole-finding-set exclusion in the test).
public sealed class Orphan
{
    public void NeverCalled() { }
}
