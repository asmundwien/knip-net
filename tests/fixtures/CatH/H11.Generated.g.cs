using System;

namespace CatH.H11;

// SOURCE-GENERATED code (name matches the default ignore glob "**/*.g.cs"). It references
// Handler.Invoke() — the sole caller of that member. Because this file is ignored, its edges are
// dropped and Invoke() looks dead. This file exists to reproduce the H11 decision scenario.
internal static class GeneratedWiring
{
    public static void Register(Handler handler)
    {
        handler.Invoke(); // the only reference to Handler.Invoke() in the whole fixture
    }
}
