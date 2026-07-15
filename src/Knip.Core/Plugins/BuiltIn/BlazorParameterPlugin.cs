using Microsoft.CodeAnalysis;

namespace Knip.Core.Plugins.BuiltIn;

/// <summary>
/// Keeps alive Blazor component members set from <c>.razor</c> markup / DI that the core walker cannot
/// see. A component property carrying <c>[Parameter]</c> / <c>[CascadingParameter]</c> is assigned by the
/// framework from markup (<c>&lt;MyComponent Title="…" /&gt;</c>), and a <c>[Inject]</c> property is set by
/// the DI container — neither is ever named in C# source, so the walker flags it dead. Promotes H6.
///
/// Conservative (§3.8): roots ONLY the attribute-bearing member (and its accessors), NEVER blanket-roots a
/// component type's members. Matched by attribute SIMPLE NAME (with or without the <c>Attribute</c> suffix),
/// offline — no NuGet reference; fixtures use local stand-in attributes so the plugin ships with ZERO
/// framework dependencies and is version-agnostic (invariant #9).
///
/// Recognizes:
///   • [Parameter]            — component property bound from markup.
///   • [CascadingParameter]   — component property supplied by a &lt;CascadingValue&gt; ancestor.
///   • [SupplyParameterFromQuery] — routable-component property bound from the query string.
///   • [EditorRequired]       — companion of [Parameter]; a member wearing it is a markup-bound parameter.
///   • [Inject]               — property injected by the DI container (never assigned in source).
///
/// OFF by default (opt-in via <c>plugins.blazorParameter.enabled: true</c>): the recognized attribute names
/// are common enough (a user's own <c>ParameterAttribute</c>) that rooting them everywhere is not safe as a
/// default. When on, over-rooting here is a false negative at worst, scoped to the attribute-bearing member.
/// </summary>
internal sealed class BlazorParameterPlugin : IKnipPlugin
{
    public string Id => "blazorParameter";

    // Attribute simple names (with or without the "Attribute" suffix) whose bearer is markup/DI-set.
    private static readonly HashSet<string> RootingAttributeNames = new(StringComparer.Ordinal)
    {
        "Parameter",                 // Microsoft.AspNetCore.Components.ParameterAttribute
        "CascadingParameter",        // Microsoft.AspNetCore.Components.CascadingParameterAttribute
        "SupplyParameterFromQuery",  // Microsoft.AspNetCore.Components.SupplyParameterFromQueryAttribute
        "EditorRequired",            // Microsoft.AspNetCore.Components.EditorRequiredAttribute (companion)
        "Inject",                    // Microsoft.AspNetCore.Components.InjectAttribute
    };

    public void Contribute(PluginContext ctx, CancellationToken ct)
    {
        var sink = ctx.Sink;

        foreach (var tree in ctx.Compilation.SyntaxTrees)
        {
            ct.ThrowIfCancellationRequested();
            var model = ctx.Compilation.GetSemanticModel(tree);
            var root = tree.GetRoot(ct);

            foreach (var propDecl in root.DescendantNodes()
                         .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(propDecl, ct) is not IPropertySymbol property) continue;
                if (!WearsRootingAttribute(property)) continue;

                RootPropertyAndAccessors(property, sink);
            }
        }
    }

    private static bool WearsRootingAttribute(IPropertySymbol property)
    {
        foreach (var attr in property.GetAttributes())
        {
            var name = attr.AttributeClass?.Name;
            if (name is null) continue;
            var trimmed = name.EndsWith("Attribute", StringComparison.Ordinal)
                ? name[..^"Attribute".Length]
                : name;
            if (RootingAttributeNames.Contains(trimmed)) return true;
        }
        return false;
    }

    /// <summary>
    /// Root the markup/DI-set property AND its get/set accessors: the framework reads and writes the
    /// property, so both accessors are live entry points. The property's signature edge keeps the marker
    /// attribute alive for free; over-rooting is scoped to THIS member (never the whole component).
    /// </summary>
    private static void RootPropertyAndAccessors(IPropertySymbol property, IContributionSink sink)
    {
        sink.AddRoot(property);
        if (property.GetMethod is { } getter) sink.AddRoot(getter);
        if (property.SetMethod is { } setter) sink.AddRoot(setter);
    }
}
