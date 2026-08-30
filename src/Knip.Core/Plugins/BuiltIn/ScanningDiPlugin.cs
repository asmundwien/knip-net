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
/// Recognizes resolved framework identities. Built-in matches require both namespace and defining assembly;
/// <c>plugins.scanningDi.aliases</c> can map a canonical type to namespace-qualified local stand-ins or
/// application-specific scanning markers without weakening the defaults:
///   • MediatR handlers  — IRequestHandler / INotificationHandler / IStreamRequestHandler.
///   • MassTransit       — IConsumer / IConsumer&lt;T&gt;.
///   • AutoMapper        — Profile subclasses.
///   • Microsoft DI      — Add/TryAdd Singleton, Scoped, and Transient registrations whose resolved
///                         declaring type proves that the container constructs the implementation.
///
/// These marker interfaces/bases exist specifically for framework discovery. A broad Scrutor
/// <c>AddClasses().AsImplementedInterfaces()</c> scan is deliberately not modeled because rooting every
/// implementer would hide dead code. A rooted type keeps its interface-method implementations alive through
/// normal polymorphism edges; the plugin also roots the recognized marker interface.
/// </summary>
internal sealed class ScanningDiPlugin : IKnipPlugin
{
    public string Id => "scanningDi";

    private static readonly string[] HandlerInterfaces =
    [
        "MediatR.Contracts::MediatR.IRequestHandler",
        "MediatR::MediatR.IRequestHandler",
        "MediatR.Contracts::MediatR.INotificationHandler",
        "MediatR::MediatR.INotificationHandler",
        "MediatR.Contracts::MediatR.IStreamRequestHandler",
        "MediatR::MediatR.IStreamRequestHandler",
        "MassTransit.Abstractions::MassTransit.IConsumer",
        "MassTransit::MassTransit.IConsumer",
    ];

    private const string AutoMapperProfile = "AutoMapper::AutoMapper.Profile";

    public void Contribute(PluginContext ctx, CancellationToken ct)
    {
        var compilation = ctx.Compilation;
        var sink = ctx.Sink;
        var matcher = new FrameworkTypeMatcher(ctx.Settings);

        foreach (var tree in compilation.SyntaxTrees)
        {
            ct.ThrowIfCancellationRequested();
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot(ct);
            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (model.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol method) continue;
                if (DependencyInjectionRegistration.TryResolve(
                        model, invocation, method, matcher, ct, out var registration)
                    && registration.ActivatesConstructors)
                    RuntimeActivation.AddRoots(registration.ImplementationType, sink);
            }

            foreach (var typeDecl in root.DescendantNodes()
                         .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(typeDecl, ct) is not INamedTypeSymbol type) continue;
                // Only concrete, instantiable classes are what a scanner registers as implementations.
                if (type.IsAbstract || type.TypeKind != TypeKind.Class) continue;

                if (IsScanRegistered(type, matcher))
                    RootScannedType(type, matcher, sink);
            }
        }
    }

    /// <summary>True if this concrete type has a resolved shape an assembly scanner registers.</summary>
    private static bool IsScanRegistered(INamedTypeSymbol type, FrameworkTypeMatcher matcher)
    {
        foreach (var iface in type.AllInterfaces)
            if (HandlerInterfaces.Any(identity => matcher.Matches(iface, identity)))
                return true;

        for (var b = type.BaseType; b is not null; b = b.BaseType)
            if (matcher.Matches(b, AutoMapperProfile))
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
    private static void RootScannedType(
        INamedTypeSymbol type,
        FrameworkTypeMatcher matcher,
        IContributionSink sink)
    {
        RootTypeAndMembers(type, sink);

        foreach (var iface in type.AllInterfaces)
            if (HandlerInterfaces.Any(identity => matcher.Matches(iface, identity)))
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
