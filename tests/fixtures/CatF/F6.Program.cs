// F6 (file 1 of 2): top-level statements. The compiler synthesizes a Program host type with a Main
// entry point; ReferenceWalker.VisitCompilationUnit roots BOTH the synthesized entry point and its
// containing type. OutputType=Exe (see CatF.csproj) is required for the compiler to synthesize Main.
//
// There must be exactly ONE top-level-statement file per compilation, so this is the only one in CatF.
// Top-level statements live in the GLOBAL namespace (no "CatF.F6" prefix), so the CatF.F6-scoped
// assertion cannot see Program directly; the test instead asserts (a) the CatF.F6 dead sibling IS
// flagged and (b) nothing named "Program"/"<Main>$" appears in the WHOLE finding set.
System.Console.WriteLine("F6 top-level entry point");
