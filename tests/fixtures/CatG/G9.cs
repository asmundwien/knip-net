namespace CatG.G9;

// G9: a method whose parameter type is a pointer to a solution type (Payload*) must keep Payload ALIVE
// via the pointer-unwrap edge in AddTypeReference. DEAD SIBLING: an unused struct is flagged, proving
// the pointer edge (not blanket rooting) is what keeps Payload alive. Requires AllowUnsafeBlocks.
public struct Payload
{
    public int Value;
}

// DEAD SIBLING: same-shaped struct, never referenced by any pointer or value -> flagged.
public struct UnusedPayload
{
    public int Value;
}

public sealed class Sample
{
    // Root: signature references Payload only through a pointer type.
    public unsafe int ConfigureServices(Payload* p) => p->Value;
}
