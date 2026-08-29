using Microsoft.CodeAnalysis;

namespace Knip.Core.Analysis;

/// <summary>
/// Enumerates the serializer-visible data types represented by a declared target. Collection serializers
/// reflect over each element's data members, not only members declared by the collection target itself.
/// Traversal follows only array elements and <see cref="IEnumerable{T}"/> contracts. Other generic
/// collaborators are not part of the serialized payload.
/// </summary>
internal static class SerializedTypeTraversal
{
    public static IEnumerable<ITypeSymbol> SelfAndCollectionElements(ITypeSymbol? target)
    {
        if (target is null) yield break;

        var pending = new Stack<ITypeSymbol>();
        var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        pending.Push(target);

        while (pending.Count > 0)
        {
            var type = pending.Pop();
            if (!seen.Add(type)) continue;

            yield return type;

            if (type is IArrayTypeSymbol array)
            {
                pending.Push(array.ElementType);
                continue;
            }

            if (type is not INamedTypeSymbol named) continue;

            if (CollectionElement(named) is { } element)
                pending.Push(element);
        }
    }

    private static ITypeSymbol? CollectionElement(INamedTypeSymbol type)
    {
        if (IsGenericEnumerable(type)) return type.TypeArguments[0];

        foreach (var contract in type.AllInterfaces)
            if (IsGenericEnumerable(contract))
                return contract.TypeArguments[0];

        return null;
    }

    private static bool IsGenericEnumerable(INamedTypeSymbol type) =>
        type.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T;
}
