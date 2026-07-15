namespace CatL.Degraded
{
    // NoSuchType is never declared or imported -> Roslyn binds a TypeKind.Error symbol ->
    // UnresolvedTypeReferences increments -> reliability.degraded == true (L3). Zero NuGet, offline.
    public sealed class Sample
    {
        public void Consume(NoSuchType value) { }
    }
}
