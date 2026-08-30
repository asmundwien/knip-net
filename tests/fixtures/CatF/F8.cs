namespace CatF.F8;

// F8: a controller-like name without MVC framework evidence is dead by default. The paired test then
// configures the broad "*Controller" escape hatch explicitly and proves that custom rooting still works.
public sealed class EmptyProbeController
{
    public void Index() { }
}
