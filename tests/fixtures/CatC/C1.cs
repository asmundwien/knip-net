namespace CatC.C1;

// C1: two overloads, one called -> the OTHER is flagged (flagship overload precision).
// Overload resolution succeeds at the call site (info.Symbol is the int overload), so only the
// uncalled string overload is unreachable.
public sealed class Sample
{
    // Root: ConfigureServices is a default entry-point symbol name.
    public void ConfigureServices() => Handle(1);

    // ALIVE: bound by Handle(1). Dead sibling is the string overload below.
    public void Handle(int value) { }

    // DEAD SIBLING: same name, never selected by any call site -> flagged.
    public void Handle(string value) { }
}
