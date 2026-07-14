using System;
using System.Linq;

// G8: compiler-generated symbols — the top-level-statements entry point (<Main>$ / synthesized
// Program), lambdas, and anonymous types — must NEVER be reported. Top-level statements exist once per
// compilation, so this is the fixture's single top-level-statement file. It roots the program entry
// point; ShouldReport filters names beginning with '<' or '$'. DEAD SIBLING lives in CatG.G8 below.

var greeter = new CatG.G8.Greeter();

// Lambda: compiled into a compiler-generated method, never reported.
Func<int, int> square = x => x * x;

// Anonymous type: compiler-generated, never reported.
var anon = new { Name = "n", Value = square(3) };

var doubled = new[] { 1, 2, 3 }.Select(n => n * 2).Sum(); // more lambdas

Console.WriteLine(greeter.Used() + anon.Value + doubled);

namespace CatG.G8
{
    public sealed class Greeter
    {
        // ALIVE: called from the top-level statements above.
        public int Used() => 1;

        // DEAD SIBLING: ordinary unused method -> flagged. Proves the type isn't wholesale rooted and
        // that only compiler-generated symbols are being suppressed.
        public int Unused() => 2;
    }
}
