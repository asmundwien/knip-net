namespace CatJ.WithFindings;

// A second file so the finding set spans multiple files: lets J4 assert project->file->line
// ordering. This whole type is a dead island (no root reaches it) -> flagged (outermost only).
public sealed class Bravo
{
    private void Whatever() { }
}
