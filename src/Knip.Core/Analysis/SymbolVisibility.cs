using Microsoft.CodeAnalysis;

namespace Knip.Core.Analysis;

/// <summary>
/// Whether a symbol can be named by an ordinary external consumer or by a friend assembly. Every
/// containing type must expose the corresponding access path; a member cannot widen its container.
/// </summary>
internal static class SymbolVisibility
{
    public static bool IsExternallyVisible(ISymbol symbol)
    {
        if (!IsExternallyAccessible(symbol.DeclaredAccessibility))
            return false;

        for (var container = symbol.ContainingType; container is not null; container = container.ContainingType)
            if (!IsExternallyAccessible(container.DeclaredAccessibility))
                return false;

        return true;
    }

    public static bool IsVisibleToFriendAssembly(ISymbol symbol)
    {
        if (!IsAccessibleToFriendAssembly(symbol.DeclaredAccessibility))
            return false;

        for (var container = symbol.ContainingType; container is not null; container = container.ContainingType)
            if (!IsAccessibleToFriendAssembly(container.DeclaredAccessibility))
                return false;

        return true;
    }

    private static bool IsExternallyAccessible(Accessibility accessibility) => accessibility is
        Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal;

    // ProtectedAndInternal is private protected; IVT does not make a friend the declaring assembly.
    private static bool IsAccessibleToFriendAssembly(Accessibility accessibility) =>
        IsExternallyAccessible(accessibility) || accessibility == Accessibility.Internal;
}
