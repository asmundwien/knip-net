namespace CatD.D2;

// D2: an override invoked through a BASE-class-typed reference keeps the OVERRIDE alive
// (polymorphism edge Base.Speak -> Derived.Speak) and, per invariant #7, the override is NEVER
// reported even though it is only reached via the base type.
//
// Mechanism: RED-FLIP for the override's aliveness (removing b.Speak() makes it unreachable but it
// is still unreported as an override). Mutation-check sibling = ordinary uncalled UnusedHelper.
public abstract class Animal
{
    // ALIVE: called via base ref b.Speak(); virtual.
    public abstract void Speak();
}

public sealed class Dog : Animal
{
    // ALIVE: override reached via Animal.Speak polymorphism edge; never reported anyway.
    public override void Speak() { }

    // DEAD SIBLING (mutation check): ordinary uncalled method -> flagged.
    public void UnusedHelper() { }
}

public sealed class Runner
{
    public void ConfigureServices()
    {
        Animal b = new Dog();
        b.Speak();
    }
}
