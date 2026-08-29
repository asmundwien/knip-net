using Knip.Core.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Knip.Core.Plugins.BuiltIn;

/// <summary>
/// Keeps alive runtime-activation dependency closures for explicit Microsoft DI registrations and types that
/// assembly-scanning DI registers reflectively. Registrations whose semantic overload proves container
/// activation preserve constructors and instance initializers; factory or instance overloads do not.
/// Scanning remains conservative (§3.8): root only types whose framework-marker shape the scan plausibly
/// registers, never every interface implementer.
///
/// Recognizes, matched by simple NAME (offline — no NuGet reference; fixtures use local stand-ins so
/// the plugin ships with ZERO framework dependencies and is version-agnostic, invariant #9):
///   • MediatR handlers  — types implementing IRequestHandler / INotificationHandler / IStreamRequestHandler
///                         (any generic arity), the interfaces MediatR's assembly scan discovers.
///   • MassTransit       — types implementing IConsumer / IConsumer&lt;T&gt; (H12), discovered by AddConsumers.
///   • AutoMapper        — types deriving from a base named Profile.
///   • Microsoft DI     — Add/TryAdd Singleton, Scoped, and Transient registrations whose overload proves
///                         that the container constructs the concrete implementation.
///
/// These marker interfaces/bases exist SPECIFICALLY for framework discovery, so a solution type wearing
/// one is exactly what an assembly scan registers — matching by that shape is the conservative rule (it
/// roots the handler, not its unrelated neighbours). A broad Scrutor "AddClasses().AsImplementedInterfaces()"
/// registers every interface-implementer, but rooting every implementer in the assembly IS blanket-rooting
/// (§3.8) and would leak liveness to unrelated interface implementers, so it is deliberately NOT done: a
/// scanned handler/consumer is caught by its marker interface anyway. A rooted type keeps its interface-
/// method implementations alive for free via AddPolymorphismEdges; the plugin additionally roots the marker
/// interface it satisfies (the registration is keyed on the interface — that is what the runtime resolves).
/// </summary>
internal sealed class ScanningDiPlugin : IKnipPlugin
{
    public string Id => "scanningDi";

    // Framework marker interfaces (simple name, any arity) whose implementers are scan-registered.
    private static readonly HashSet<string> HandlerInterfaceNames = new(StringComparer.Ordinal)
    {
        "IRequestHandler",       // MediatR
        "INotificationHandler",  // MediatR
        "IStreamRequestHandler", // MediatR
        "IConsumer",             // MassTransit (H12)
    };

    // Base-class names whose subclasses are scan-registered (AutoMapper profiles).
    private static readonly HashSet<string> ScannedBaseNames = new(StringComparer.Ordinal)
    {
        "Profile", // AutoMapper
    };

    public void Contribute(PluginContext ctx, CancellationToken ct)
    {
        var compilation = ctx.Compilation;
        var sink = ctx.Sink;

        foreach (var tree in compilation.SyntaxTrees)
        {
            ct.ThrowIfCancellationRequested();
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot(ct);
            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (model.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol method) continue;
                if (DependencyInjectionRegistration.TryResolve(model, invocation, method, ct, out var registration)
                    && registration.ActivatesConstructors)
                    RuntimeActivation.AddRoots(registration.ImplementationType, sink);
            }

            foreach (var typeDecl in root.DescendantNodes()
                         .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(typeDecl, ct) is not INamedTypeSymbol type) continue;
                // Only concrete, instantiable classes are what a scanner registers as implementations.
                if (type.IsAbstract || type.TypeKind != TypeKind.Class) continue;

                if (IsScanRegistered(type))
                    RootScannedType(type, sink);
            }
        }
    }

    /// <summary>True if this concrete type has the shape an assembly scanner would register.</summary>
    private static bool IsScanRegistered(INamedTypeSymbol type)
    {
        // Framework marker interfaces (MediatR handlers, MassTransit consumers) — matched by name,
        // any generic arity.
        foreach (var iface in type.AllInterfaces)
            if (HandlerInterfaceNames.Contains(iface.Name))
                return true;

        // Framework base classes (AutoMapper Profile) — matched by name up the base chain.
        for (var b = type.BaseType; b is not null; b = b.BaseType)
            if (ScannedBaseNames.Contains(b.Name))
                return true;

        return false;
    }

    /// <summary>
    /// Root the scan-registered type, its effectively externally-visible members, AND the marker interface(s) the
    /// scanner registered it under. The scanner wires <c>IHandler → Concrete</c> and the framework
    /// invokes the concrete through the INTERFACE (mediator.Send resolves IRequestHandler and calls
    /// Handle), so the interface members are the live entry point — rooting them keeps both the abstract
    /// contract and (via polymorphism edges) the concrete implementation alive. Over-rooting here is a
    /// false negative at worst (§3.8), scoped to THIS type and the framework interface it satisfies.
    /// </summary>
    private static void RootScannedType(INamedTypeSymbol type, IContributionSink sink)
    {
        RootTypeAndMembers(type, sink);

        // Root the recognized framework marker interface(s) this type implements (and their members):
        // the registration is keyed on the interface, so the interface is what the runtime resolves.
        foreach (var iface in type.AllInterfaces)
            if (HandlerInterfaceNames.Contains(iface.Name))
                RootTypeAndMembers(iface, sink);
    }

    private static void RootTypeAndMembers(INamedTypeSymbol type, IContributionSink sink)
    {
        sink.AddRoot(type);
        foreach (var member in type.GetMembers())
            if (!member.IsImplicitlyDeclared && SymbolVisibility.IsExternallyVisible(member))
                sink.AddRoot(member);
    }
}
