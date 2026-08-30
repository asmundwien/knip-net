using Knip.Core.Analysis;
using Microsoft.CodeAnalysis;

namespace Knip.Core.Plugins.BuiltIn;

/// <summary>
/// Keeps alive Blazor component members set from <c>.razor</c> markup / DI that the core walker cannot
/// see. A component property carrying <c>[Parameter]</c> / <c>[CascadingParameter]</c> is assigned by the
/// framework from markup (<c>&lt;MyComponent Title="…" /&gt;</c>), and a <c>[Inject]</c> property is set by
/// the DI container — neither is ever named in C# source, so the walker flags it dead. Promotes H6.
///
/// Conservative (§3.8): roots only an attribute-bearing member and its accessors. Built-in attributes
/// require their resolved Microsoft.AspNetCore.Components namespace and defining assembly;
/// <c>plugins.blazorParameter.aliases</c> provides explicit canonical-to-alias mappings for offline
/// fixtures and compatible user extensions.
/// </summary>
internal sealed class BlazorParameterPlugin : IKnipPlugin
{
    public string Id => "blazorParameter";

    private static readonly string[] RootingAttributes =
    [
        "Microsoft.AspNetCore.Components::Microsoft.AspNetCore.Components.ParameterAttribute",
        "Microsoft.AspNetCore.Components::Microsoft.AspNetCore.Components.CascadingParameterAttribute",
        "Microsoft.AspNetCore.Components::Microsoft.AspNetCore.Components.SupplyParameterFromQueryAttribute",
        "Microsoft.AspNetCore.Components::Microsoft.AspNetCore.Components.EditorRequiredAttribute",
        "Microsoft.AspNetCore.Components::Microsoft.AspNetCore.Components.InjectAttribute",
    ];

    public void Contribute(PluginContext ctx, CancellationToken ct)
    {
        var sink = ctx.Sink;
        var matcher = new FrameworkTypeMatcher(ctx.Settings);

        foreach (var tree in ctx.Compilation.SyntaxTrees)
        {
            ct.ThrowIfCancellationRequested();
            var model = ctx.Compilation.GetSemanticModel(tree);
            var root = tree.GetRoot(ct);

            foreach (var propDecl in root.DescendantNodes()
                         .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(propDecl, ct) is not IPropertySymbol property) continue;
                if (!WearsRootingAttribute(property, matcher)) continue;

                RootPropertyAndAccessors(property, sink);
            }
        }
    }

    private static bool WearsRootingAttribute(
        IPropertySymbol property,
        FrameworkTypeMatcher matcher)
    {
        foreach (var attr in property.GetAttributes())
            if (RootingAttributes.Any(identity => matcher.MatchesAttribute(attr.AttributeClass, identity)))
                return true;

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
