namespace CatB.B6;

// B6 (project X of 2): declares CatB.B6.Duplicate.Collide() — IDENTICAL namespace + type + signature
// to the copy in project Y. Doc-comment IDs carry no assembly, so both copies share ONE graph node.
// Here Collide is REACHED from a rooted entry point in THIS project.
public sealed class Duplicate
{
    public void Collide() { }
}

public sealed class Startup
{
    // Rooted by the Startup ConfigureServices convention. Reaches THIS project's Duplicate.Collide,
    public void ConfigureServices()
    {
        new Duplicate().Collide();
    }
}
