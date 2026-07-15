namespace CatI.I5.SolutionAdjacent;

// I5 (solution-relative discovery target): exactly ONE dead symbol (the method OnlyDead). The
// knip.json SITTING NEXT TO this solution ignores that symbol. Running the CLI from a DIFFERENT
// cwd (with no --config) must still discover this solution-adjacent knip.json — so OnlyDead is
// suppressed and the CLI exits 0. A rooted entry point keeps the TYPE alive so the single dead
// symbol is the METHOD the solution-adjacent ignore glob targets.
public sealed class OnlySample
{
    public void ConfigureServices() => Used();
    public void Used() { }

    public void OnlyDead() { }
}
