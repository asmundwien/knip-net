namespace CatA.A3;

// A3: transitive chain root -> A -> B keeps A and B alive; C is uncalled -> only C flagged.
public sealed class Sample
{
    public void ConfigureServices() => A();

    private void A() => B();
    private void B() { }

    // DEAD: not on the chain.
    private void C() { }
}
