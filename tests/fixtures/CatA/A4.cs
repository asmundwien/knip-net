namespace CatA.A4;

// A4: a dead island. A and B call only each other; nothing on a root path reaches them -> both flagged.
// LiveProof is the dead sibling's counterpart: it is reached from the root, proving the fixture can
// keep a method alive at all (so the island's deadness is meaningful, not a fixture accident).
public sealed class Sample
{
    public void ConfigureServices() => LiveProof();

    private void LiveProof() { }

    private void A() => B();
    private void B() => A();
}
