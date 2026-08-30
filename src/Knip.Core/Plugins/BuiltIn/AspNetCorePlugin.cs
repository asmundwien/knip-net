using Knip.Core.Analysis;
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
/// Recognizes resolved framework type identities. Built-in matches require the expected namespace and
/// defining assembly. <c>plugins.aspnetcore.aliases</c> can map a canonical framework type to explicit
/// namespace-qualified stand-ins or compatible user extensions without making simple names global.
/// Supported conventions are middleware, MVC/Razor filters, startup filters, authorization handlers and
/// policy providers, Blazor component lifecycle methods, Application Insights processors/initializers,
/// and health checks. Only the framework-dispatched entry members and runtime activation closure are rooted;
/// unrelated members remain reportable.
///
/// ON by default because field validation found these conventions produce dangerous high-confidence false
/// positives. Contributions remain additive, so an imprecise configured alias can only hide a finding.
/// </summary>
internal sealed class AspNetCorePlugin : IKnipPlugin
{
    public string Id => "aspnetcore";

    private const string UseMiddlewareExtensions =
        "Microsoft.AspNetCore.Http.Abstractions::Microsoft.AspNetCore.Builder.UseMiddlewareExtensions";
    private const string MiddlewareInterface =
        "Microsoft.AspNetCore.Http.Abstractions::Microsoft.AspNetCore.Http.IMiddleware";
    private const string StartupFilterInterface =
        "Microsoft.AspNetCore.Hosting.Abstractions::Microsoft.AspNetCore.Hosting.IStartupFilter";
    private const string AuthorizationHandlerInterface =
        "Microsoft.AspNetCore.Authorization::Microsoft.AspNetCore.Authorization.IAuthorizationHandler";
    private const string AuthorizationHandlerBase =
        "Microsoft.AspNetCore.Authorization::Microsoft.AspNetCore.Authorization.AuthorizationHandler";
    private const string ComponentBase =
        "Microsoft.AspNetCore.Components::Microsoft.AspNetCore.Components.ComponentBase";
    private const string PolicyProviderInterface =
        "Microsoft.AspNetCore.Authorization.Policy::Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider";
    private const string DefaultPolicyProviderBase =
        "Microsoft.AspNetCore.Authorization.Policy::Microsoft.AspNetCore.Authorization.DefaultAuthorizationPolicyProvider";
    private const string TelemetryProcessorInterface =
        "Microsoft.ApplicationInsights::Microsoft.ApplicationInsights.Extensibility.ITelemetryProcessor";
    private const string TelemetryInitializerInterface =
        "Microsoft.ApplicationInsights::Microsoft.ApplicationInsights.Extensibility.ITelemetryInitializer";
    private const string HealthCheckInterface =
        "Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions::Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck";

    private static readonly string[] FilterInterfaces =
    [
        "Microsoft.AspNetCore.Mvc.Abstractions::Microsoft.AspNetCore.Mvc.Filters.IActionFilter",
        "Microsoft.AspNetCore.Mvc.Abstractions::Microsoft.AspNetCore.Mvc.Filters.IAsyncActionFilter",
        "Microsoft.AspNetCore.Mvc.Abstractions::Microsoft.AspNetCore.Mvc.Filters.IResultFilter",
        "Microsoft.AspNetCore.Mvc.Abstractions::Microsoft.AspNetCore.Mvc.Filters.IAsyncResultFilter",
        "Microsoft.AspNetCore.Mvc.Abstractions::Microsoft.AspNetCore.Mvc.Filters.IExceptionFilter",
        "Microsoft.AspNetCore.Mvc.Abstractions::Microsoft.AspNetCore.Mvc.Filters.IAsyncExceptionFilter",
        "Microsoft.AspNetCore.Mvc.Abstractions::Microsoft.AspNetCore.Mvc.Filters.IAuthorizationFilter",
        "Microsoft.AspNetCore.Mvc.Abstractions::Microsoft.AspNetCore.Mvc.Filters.IAsyncAuthorizationFilter",
        "Microsoft.AspNetCore.Mvc.RazorPages::Microsoft.AspNetCore.Mvc.Filters.IPageFilter",
        "Microsoft.AspNetCore.Mvc.RazorPages::Microsoft.AspNetCore.Mvc.Filters.IAsyncPageFilter",
    ];

    // Convention-middleware entry method names invoked reflectively by the RequestDelegate factory.
    private static readonly HashSet<string> MiddlewareEntryMethodNames = new(StringComparer.Ordinal)
    {
        "Invoke",
        "InvokeAsync",
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
        var matcher = new FrameworkTypeMatcher(ctx.Settings);

        foreach (var tree in compilation.SyntaxTrees)
        {
            ct.ThrowIfCancellationRequested();
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot(ct);

            // (1) UseMiddleware<T>() / UseMiddleware(typeof(T)) — root T's convention entry methods + ctors.
            foreach (var inv in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (model.GetSymbolInfo(inv, ct).Symbol is not IMethodSymbol method) continue;
                if (method.Name != "UseMiddleware"
                    || !matcher.Matches((method.ReducedFrom ?? method).ContainingType, UseMiddlewareExtensions))
                    continue;

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

                RootFrameworkDispatchedMembers(type, matcher, sink);
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
    /// and runtime-activation entry points. The framework activates the type and calls the entry method per
    /// request; unrelated methods remain unrooted.
    /// </summary>
    private static void RootConventionMiddleware(INamedTypeSymbol type, IContributionSink sink)
    {
        foreach (var member in type.GetMembers())
            if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary } method
                && MiddlewareEntryMethodNames.Contains(method.Name))
                sink.AddRoot(method);

        RuntimeActivation.AddRoots(type, sink);
    }

    /// <summary>
    /// Root the framework-dispatched entry members of a type by its implemented interfaces: a factory
    /// middleware's <c>InvokeAsync</c> (IMiddleware), an MVC/Razor filter's implementations of the filter
    /// interface methods, and a startup filter's <c>Configure</c> (IStartupFilter). Only the specific
    /// interface-method implementations are rooted — never the whole type — so an unrelated dead method
    /// stays flagged (the over-rooting guard).
    /// </summary>
    private static void RootFrameworkDispatchedMembers(
        INamedTypeSymbol type,
        FrameworkTypeMatcher matcher,
        IContributionSink sink)
    {
        // IAuthorizationHandler declares no requirement-typed HandleRequirementAsync (that lives on the
        // AuthorizationHandler<T> base), and its HandleAsync may be provided by the base too — so root the
        // handler's entry methods by NAME on the type itself rather than only via interface-member impls.
        var isAuthorizationHandler = false;
        var isTelemetryProcessor = false;
        var isTelemetryInitializer = false;
        var isHealthCheck = false;
        var isPolicyProvider = false;
        var isFrameworkActivated = false;

        foreach (var iface in type.AllInterfaces)
        {
            if (matcher.Matches(iface, MiddlewareInterface))
            {
                isFrameworkActivated = true;
                RootInterfaceMethodImplementations(type, iface, sink);
            }
            else if (matcher.Matches(iface, StartupFilterInterface))
            {
                isFrameworkActivated = true;
                RootInterfaceMethodImplementations(type, iface, sink);
            }
            else if (matcher.Matches(iface, AuthorizationHandlerInterface))
            {
                isAuthorizationHandler = true;
                isFrameworkActivated = true;
            }
            else if (matcher.Matches(iface, TelemetryProcessorInterface))
            {
                isTelemetryProcessor = true;
                isFrameworkActivated = true;
            }
            else if (matcher.Matches(iface, TelemetryInitializerInterface))
            {
                isTelemetryInitializer = true;
                isFrameworkActivated = true;
            }
            else if (matcher.Matches(iface, HealthCheckInterface))
            {
                isHealthCheck = true;
                isFrameworkActivated = true;
            }
            else if (matcher.Matches(iface, PolicyProviderInterface))
            {
                isPolicyProvider = true;
                isFrameworkActivated = true;
            }
            else if (FilterInterfaces.Any(identity => matcher.Matches(iface, identity)))
            {
                isFrameworkActivated = true;
                RootInterfaceMethodImplementations(type, iface, sink);
            }
        }

        // Base-class conventions retain their role through explicit canonical-to-alias mappings.
        for (var b = type.BaseType; b is not null; b = b.BaseType)
        {
            if (matcher.Matches(b, AuthorizationHandlerBase))
            {
                isAuthorizationHandler = true;
                isFrameworkActivated = true;
            }
            else if (matcher.Matches(b, ComponentBase))
            {
                RootMethodsByName(type, BlazorLifecycleMethodNames, sink);
                isFrameworkActivated = true;
            }
            else if (matcher.Matches(b, DefaultPolicyProviderBase))
            {
                isPolicyProvider = true;
                isFrameworkActivated = true;
            }
        }

        if (isAuthorizationHandler)
        {
            // Policy evaluation activates the handler and dispatches its HandleRequirementAsync/HandleAsync
            // reflectively; root those entry methods + instance ctors so fields (_logger,
            // _authenticationStateProvider) and helpers gain liveness via the walker's edges.
            RootMethodsByName(type, AuthorizationHandlerEntryMethodNames, sink);
        }

        if (isTelemetryProcessor)
        {
            // The telemetry pipeline dispatches Process(ITelemetry); root it + instance ctors so the
            // ctor-assigned _next and the private helpers Process calls gain liveness via the walker's edges.
            RootMethodsByName(type, TelemetryProcessorEntryMethodNames, sink);
        }

        if (isTelemetryInitializer)
        {
            // The telemetry pipeline dispatches Initialize(ITelemetry); root it + instance ctors.
            RootMethodsByName(type, TelemetryInitializerEntryMethodNames, sink);
        }

        if (isHealthCheck)
        {
            // The health-check middleware dispatches CheckHealthAsync; root it + instance ctors so the
            // helpers it calls gain liveness via the walker's edges.
            RootMethodsByName(type, HealthCheckEntryMethodNames, sink);
        }

        if (isPolicyProvider)
        {
            // The authorization middleware dispatches GetPolicyAsync/GetDefaultPolicyAsync/GetFallbackPolicyAsync;
            // root them + instance ctors so the provider's policy-building helpers gain liveness.
            RootMethodsByName(type, PolicyProviderEntryMethodNames, sink);
        }

        if (isFrameworkActivated)
            RuntimeActivation.AddRoots(type, sink);
    }

    /// <summary>
    /// Root <paramref name="type"/>'s own ordinary methods whose simple name is in <paramref name="names"/>.
    /// Only these convention entry members are rooted — never the whole type — so an unrelated dead method
    /// stays flagged (the over-rooting guard).
    /// </summary>
    private static void RootMethodsByName(
        INamedTypeSymbol type, HashSet<string> names, IContributionSink sink)
    {
        foreach (var member in type.GetMembers())
            if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary } method && names.Contains(method.Name))
                sink.AddRoot(method);
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
