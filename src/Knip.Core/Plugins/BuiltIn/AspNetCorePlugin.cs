using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Knip.Core.Plugins.BuiltIn;

/// <summary>
/// Keeps alive ASP.NET Core convention-invoked members the framework dispatches by REFLECTION —
/// entry points no source ever names, so the core walker sees "no incoming references" and flags them
/// dead. Deleting them compiles fine and breaks at runtime: the §3.8-sacred reflection/convention class.
///
/// The bug this kills (dogfound on a live 11-project solution): <c>app.UseMiddleware&lt;AuditLoggingMiddleware&gt;()</c>
/// keeps the TYPE alive (a generic-arg edge) but the middleware's <c>Invoke</c>/<c>InvokeAsync(HttpContext)</c>
/// is invoked by ASP.NET reflectively — never named in source — so the entry method is flagged, and its
/// ctor/fields (<c>_next</c>, <c>_logger</c>) + private helpers (<c>LeggTilRequestMetadata</c>) CASCADE to
/// dead. Same for MVC filters: an <c>IAsyncActionFilter.OnActionExecutingAsync</c> is framework-dispatched,
/// so it + the private helpers it calls cascade dead. Rooting the entry members makes them reachable, so
/// their fields/ctors/helpers get liveness via the normal edges the core walker already recorded.
///
/// Conservative (§3.8): roots the framework-convention ENTRY members ONLY, and ONLY for types that ARE
/// middleware / filters / startup-filters — never a blanket "root the type's world". An unrelated dead
/// method on a middleware (one <c>Invoke</c> never calls) STAYS flagged (the over-rooting guard). Everything
/// goes through the additive sink, so worst case is a false negative.
///
/// Recognizes, matched by simple framework-type NAME (offline — no NuGet reference; fixtures use local
/// stand-ins so the plugin ships with ZERO framework dependencies and is version-agnostic, invariant #9):
///   • Convention middleware — for each <c>UseMiddleware&lt;T&gt;()</c> invocation (or the non-generic
///     <c>UseMiddleware(typeof(T))</c>), root T's <c>Invoke</c> AND <c>InvokeAsync</c> methods (whichever
///     exist) plus T's instance constructors.
///   • Factory middleware — types implementing <c>IMiddleware</c> → root their <c>InvokeAsync</c>.
///   • MVC / Razor Pages filters — types implementing any of IActionFilter / IAsyncActionFilter /
///     IResultFilter / IAsyncResultFilter / IExceptionFilter / IAsyncExceptionFilter / IAuthorizationFilter /
///     IAsyncAuthorizationFilter / IPageFilter / IAsyncPageFilter → root the type's implementations of those
///     interface methods (so they, and the helpers they call, are reachable).
///   • Startup filters — types implementing <c>IStartupFilter</c> → root their <c>Configure</c> method.
///   • Authorization handlers — types deriving from a base named <c>AuthorizationHandler</c>
///     (<c>AuthorizationHandler&lt;TRequirement&gt;</c> / <c>&lt;TRequirement,TResource&gt;</c>) OR implementing
///     <c>IAuthorizationHandler</c> → root <c>HandleRequirementAsync</c> and/or <c>HandleAsync</c> (whichever
///     exist) plus instance constructors. Policy evaluation dispatches the handler's entry method reflectively,
///     so its ctor/fields (<c>_logger</c>, <c>_authenticationStateProvider</c>) + helpers cascade dead without it.
///   • Blazor components — types deriving from a base named <c>ComponentBase</c> → root the lifecycle methods
///     when present (<c>OnInitialized</c>/<c>OnInitializedAsync</c>, <c>OnParametersSet</c>/<c>OnParametersSetAsync</c>,
///     <c>OnAfterRender</c>/<c>OnAfterRenderAsync</c>, <c>SetParametersAsync</c>, <c>BuildRenderTree</c>,
///     <c>Dispose</c>/<c>DisposeAsync</c>). The Blazor renderer invokes these by convention — never named in
///     source — so the helpers they call cascade dead without rooting. (<c>[Parameter]</c> props are the separate
///     <c>blazorParameter</c> plugin's job — this handles the lifecycle METHODS only.)
///   • Application Insights telemetry — types implementing <c>ITelemetryProcessor</c> → root <c>Process</c>;
///     types implementing <c>ITelemetryInitializer</c> → root <c>Initialize</c>; plus instance ctors in both
///     cases. The telemetry pipeline is DI-registered by generic arg (so the TYPE is alive), but the interface
///     entry method is invoked by the pipeline — never named in source — so the ctor-assigned <c>_next</c> and
///     the private helpers the entry calls cascade dead without rooting.
///   • Health checks — types implementing <c>IHealthCheck</c> → root <c>CheckHealthAsync</c> + instance ctors.
///     The health-check middleware dispatches <c>CheckHealthAsync</c>, so the helpers it calls cascade dead.
///   • Authorization policy providers — types implementing <c>IAuthorizationPolicyProvider</c> OR deriving from
///     a base named <c>DefaultAuthorizationPolicyProvider</c> → root <c>GetPolicyAsync</c>/<c>GetDefaultPolicyAsync</c>/
///     <c>GetFallbackPolicyAsync</c> + instance ctors. The authorization middleware dispatches these, so the
///     provider's ctor + policy-building helpers cascade dead without rooting.
///
/// OFF by default (opt-in via <c>plugins.aspnetcore.enabled: true</c>): a project not using ASP.NET Core
/// should not pay for these name matches, and the recognized names are common enough that rooting them is a
/// deliberate opt-in. When on, over-rooting is a false negative at worst, scoped to a middleware/filter's own
/// convention entry members (never its unrelated methods or its collaborators).
/// </summary>
internal sealed class AspNetCorePlugin : IKnipPlugin
{
    public string Id => "aspnetcore";

    // Convention-middleware entry method names invoked reflectively by the RequestDelegate factory.
    private static readonly HashSet<string> MiddlewareEntryMethodNames = new(StringComparer.Ordinal)
    {
        "Invoke",
        "InvokeAsync",
    };

    // MVC / Razor Pages filter marker interfaces (simple name). A type wearing one has its implementations
    // of that interface's methods dispatched by the framework filter pipeline.
    private static readonly HashSet<string> FilterInterfaceNames = new(StringComparer.Ordinal)
    {
        "IActionFilter",
        "IAsyncActionFilter",
        "IResultFilter",
        "IAsyncResultFilter",
        "IExceptionFilter",
        "IAsyncExceptionFilter",
        "IAuthorizationFilter",
        "IAsyncAuthorizationFilter",
        "IPageFilter",
        "IAsyncPageFilter",
    };

    // Authorization-handler entry method names — policy evaluation dispatches these reflectively. A handler
    // overrides one (AuthorizationHandler<T>.HandleRequirementAsync) or implements IAuthorizationHandler.HandleAsync.
    private static readonly HashSet<string> AuthorizationHandlerEntryMethodNames = new(StringComparer.Ordinal)
    {
        "HandleRequirementAsync",
        "HandleAsync",
    };

    // Blazor ComponentBase lifecycle method names — invoked by the renderer by convention, never named in source.
    private static readonly HashSet<string> BlazorLifecycleMethodNames = new(StringComparer.Ordinal)
    {
        "OnInitialized",
        "OnInitializedAsync",
        "OnParametersSet",
        "OnParametersSetAsync",
        "OnAfterRender",
        "OnAfterRenderAsync",
        "SetParametersAsync",
        "BuildRenderTree",
        "Dispose",
        "DisposeAsync",
    };

    // Application Insights telemetry-pipeline entry methods — the DI-registered processor/initializer has this
    // dispatched by the pipeline, never named in source.
    private static readonly HashSet<string> TelemetryProcessorEntryMethodNames = new(StringComparer.Ordinal)
    {
        "Process",
    };

    private static readonly HashSet<string> TelemetryInitializerEntryMethodNames = new(StringComparer.Ordinal)
    {
        "Initialize",
    };

    // Health-check entry method — the health-check middleware dispatches this, never named in source.
    private static readonly HashSet<string> HealthCheckEntryMethodNames = new(StringComparer.Ordinal)
    {
        "CheckHealthAsync",
    };

    // Authorization policy-provider entry methods — the authorization middleware dispatches these.
    private static readonly HashSet<string> PolicyProviderEntryMethodNames = new(StringComparer.Ordinal)
    {
        "GetPolicyAsync",
        "GetDefaultPolicyAsync",
        "GetFallbackPolicyAsync",
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

            // (1) UseMiddleware<T>() / UseMiddleware(typeof(T)) — root T's convention entry methods + ctors.
            foreach (var inv in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (model.GetSymbolInfo(inv, ct).Symbol is not IMethodSymbol method) continue;
                if (method.Name != "UseMiddleware") continue;

                if (MiddlewareTypeArg(model, inv, method, ct) is { } middleware)
                    RootConventionMiddleware(middleware, sink);
            }

            // (2) Type-shaped conventions matched by implemented interface / base — factory middleware,
            // MVC filters, startup filters.
            foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(typeDecl, ct) is not INamedTypeSymbol type) continue;
                // Only concrete, instantiable classes are what the framework activates.
                if (type.IsAbstract || type.TypeKind != TypeKind.Class) continue;

                RootFrameworkDispatchedMembers(type, sink);
            }
        }
    }

    /// <summary>
    /// The middleware type argument of a <c>UseMiddleware</c> call: its explicit type argument
    /// (<c>UseMiddleware&lt;T&gt;()</c>) if present, else a <c>typeof(T)</c> first argument
    /// (<c>UseMiddleware(typeof(T))</c>). Conservative: null if neither resolves.
    /// </summary>
    private static INamedTypeSymbol? MiddlewareTypeArg(
        SemanticModel model, InvocationExpressionSyntax inv, IMethodSymbol method, CancellationToken ct)
    {
        // UseMiddleware<T>() — the generic type argument is the middleware.
        if (method.TypeArguments.Length == 1 && method.TypeArguments[0] is INamedTypeSymbol { TypeKind: not TypeKind.Error } arg)
            return arg;

        // UseMiddleware(typeof(T), ...) — the middleware is the typeof'd first argument.
        if (inv.ArgumentList.Arguments.Count > 0
            && inv.ArgumentList.Arguments[0].Expression is TypeOfExpressionSyntax typeOf
            && model.GetTypeInfo(typeOf.Type, ct).Type is INamedTypeSymbol { TypeKind: not TypeKind.Error } typeofArg)
            return typeofArg;

        return null;
    }

    /// <summary>
    /// Root a convention middleware's reflectively-invoked entry methods (<c>Invoke</c>/<c>InvokeAsync</c>)
    /// and its instance constructors. The framework activates the type and calls the entry method per
    /// request; rooting the entry method makes it reachable so its fields/helpers gain liveness via the
    /// core walker's edges. Over-rooting is scoped to THIS type's convention members (a false negative at
    /// worst, §3.8) — never its unrelated methods.
    /// </summary>
    private static void RootConventionMiddleware(INamedTypeSymbol type, IContributionSink sink)
    {
        foreach (var member in type.GetMembers())
        {
            switch (member)
            {
                case IMethodSymbol { MethodKind: MethodKind.Ordinary } m when MiddlewareEntryMethodNames.Contains(m.Name):
                    sink.AddRoot(m);
                    break;
                // The RequestDelegate factory news the middleware up, injecting the next delegate (+ DI):
                // its instance ctors are entry points too, keeping the fields they assign (_next/_logger) alive.
                case IMethodSymbol { MethodKind: MethodKind.Constructor, IsStatic: false } ctor:
                    sink.AddRoot(ctor);
                    break;
            }
        }
    }

    /// <summary>
    /// Root the framework-dispatched entry members of a type by its implemented interfaces: a factory
    /// middleware's <c>InvokeAsync</c> (IMiddleware), an MVC/Razor filter's implementations of the filter
    /// interface methods, and a startup filter's <c>Configure</c> (IStartupFilter). Only the specific
    /// interface-method implementations are rooted — never the whole type — so an unrelated dead method
    /// stays flagged (the over-rooting guard).
    /// </summary>
    private static void RootFrameworkDispatchedMembers(INamedTypeSymbol type, IContributionSink sink)
    {
        // IAuthorizationHandler declares no requirement-typed HandleRequirementAsync (that lives on the
        // AuthorizationHandler<T> base), and its HandleAsync may be provided by the base too — so root the
        // handler's entry methods by NAME on the type itself rather than only via interface-member impls.
        var isAuthorizationHandler = false;
        var isTelemetryProcessor = false;
        var isTelemetryInitializer = false;
        var isHealthCheck = false;
        var isPolicyProvider = false;

        foreach (var iface in type.AllInterfaces)
        {
            var name = iface.Name;

            if (name == "IMiddleware")
            {
                // Factory-activated middleware: the framework resolves it from DI and calls InvokeAsync.
                RootInterfaceMethodImplementations(type, iface, sink);
            }
            else if (name == "IStartupFilter")
            {
                // The startup pipeline invokes Configure to wrap the app builder.
                RootInterfaceMethodImplementations(type, iface, sink);
            }
            else if (name == "IAuthorizationHandler")
            {
                isAuthorizationHandler = true;
            }
            else if (name == "ITelemetryProcessor")
            {
                isTelemetryProcessor = true;
            }
            else if (name == "ITelemetryInitializer")
            {
                isTelemetryInitializer = true;
            }
            else if (name == "IHealthCheck")
            {
                isHealthCheck = true;
            }
            else if (name == "IAuthorizationPolicyProvider")
            {
                isPolicyProvider = true;
            }
            else if (FilterInterfaceNames.Contains(name))
            {
                // The MVC / Razor Pages filter pipeline invokes the filter interface methods reflectively.
                RootInterfaceMethodImplementations(type, iface, sink);
            }
        }

        // Base-class-shaped conventions: authorization handlers (AuthorizationHandler<T>), Blazor components
        // (ComponentBase), and authorization policy providers (DefaultAuthorizationPolicyProvider). Matched by
        // simple base NAME up the chain (offline, version-agnostic).
        for (var b = type.BaseType; b is not null; b = b.BaseType)
        {
            if (b.Name == "AuthorizationHandler")
                isAuthorizationHandler = true;
            else if (b.Name == "ComponentBase")
                RootMethodsByName(type, BlazorLifecycleMethodNames, sink, includeConstructors: false);
            else if (b.Name == "DefaultAuthorizationPolicyProvider")
                isPolicyProvider = true;
        }

        if (isAuthorizationHandler)
        {
            // Policy evaluation activates the handler and dispatches its HandleRequirementAsync/HandleAsync
            // reflectively; root those entry methods + instance ctors so fields (_logger,
            // _authenticationStateProvider) and helpers gain liveness via the walker's edges.
            RootMethodsByName(type, AuthorizationHandlerEntryMethodNames, sink, includeConstructors: true);
        }

        if (isTelemetryProcessor)
        {
            // The telemetry pipeline dispatches Process(ITelemetry); root it + instance ctors so the
            // ctor-assigned _next and the private helpers Process calls gain liveness via the walker's edges.
            RootMethodsByName(type, TelemetryProcessorEntryMethodNames, sink, includeConstructors: true);
        }

        if (isTelemetryInitializer)
        {
            // The telemetry pipeline dispatches Initialize(ITelemetry); root it + instance ctors.
            RootMethodsByName(type, TelemetryInitializerEntryMethodNames, sink, includeConstructors: true);
        }

        if (isHealthCheck)
        {
            // The health-check middleware dispatches CheckHealthAsync; root it + instance ctors so the
            // helpers it calls gain liveness via the walker's edges.
            RootMethodsByName(type, HealthCheckEntryMethodNames, sink, includeConstructors: true);
        }

        if (isPolicyProvider)
        {
            // The authorization middleware dispatches GetPolicyAsync/GetDefaultPolicyAsync/GetFallbackPolicyAsync;
            // root them + instance ctors so the provider's policy-building helpers gain liveness.
            RootMethodsByName(type, PolicyProviderEntryMethodNames, sink, includeConstructors: true);
        }
    }

    /// <summary>
    /// Root <paramref name="type"/>'s own ordinary methods whose simple name is in <paramref name="names"/>,
    /// and optionally its instance constructors. Only these convention entry members are rooted — never the
    /// whole type — so an unrelated dead method stays flagged (the over-rooting guard).
    /// </summary>
    private static void RootMethodsByName(
        INamedTypeSymbol type, HashSet<string> names, IContributionSink sink, bool includeConstructors)
    {
        foreach (var member in type.GetMembers())
        {
            switch (member)
            {
                case IMethodSymbol { MethodKind: MethodKind.Ordinary } m when names.Contains(m.Name):
                    sink.AddRoot(m);
                    break;
                case IMethodSymbol { MethodKind: MethodKind.Constructor, IsStatic: false } ctor when includeConstructors:
                    sink.AddRoot(ctor);
                    break;
            }
        }
    }

    /// <summary>
    /// Root <paramref name="type"/>'s implementation of each method declared on <paramref name="iface"/>.
    /// Uses <see cref="ITypeSymbol.FindImplementationForInterfaceMember"/> so both explicit and implicit
    /// implementations are found, and only that concrete member — not the whole type — is rooted.
    /// </summary>
    private static void RootInterfaceMethodImplementations(INamedTypeSymbol type, INamedTypeSymbol iface, IContributionSink sink)
    {
        foreach (var member in iface.GetMembers())
        {
            if (member is not IMethodSymbol) continue;
            if (type.FindImplementationForInterfaceMember(member) is { } impl)
                sink.AddRoot(impl);
        }
    }
}
