namespace CatB.B6;

// B6 (project Y of 2): an IDENTICAL declaration of CatB.B6.Duplicate.Collide(). This project has NO
// use site for Collide. In isolation this copy would be dead. Because the doc-comment-ID node is
// shared with project X (where it IS reached), the collision can only confer EXTRA liveness — a
// potential false NEGATIVE aligned with invariant #3.8. The test captures whatever the tool does.
public sealed class Duplicate
{
    public void Collide() { }
}
