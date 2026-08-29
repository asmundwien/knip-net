using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Knip.Core.Analysis;

internal readonly record struct DiRegistration(INamedTypeSymbol ImplementationType, bool ActivatesConstructors);

internal static class DependencyInjectionRegistration
{
    private static readonly HashSet<string> RegistrationMethodNames = new(StringComparer.Ordinal)
    {
        "AddSingleton",
        "AddScoped",
        "AddTransient",
        "TryAddSingleton",
        "TryAddScoped",
        "TryAddTransient",
    };

    public static bool TryResolve(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        CancellationToken ct,
        out DiRegistration registration)
    {
        registration = default;
        if (!RegistrationMethodNames.Contains(method.Name))
            return false;

        if (model.GetOperation(invocation, ct) is not IInvocationOperation operation)
            return false;

        if (method.TypeArguments.Length > 0 && ConcreteClass(method.TypeArguments[^1]) is { } genericType)
        {
            registration = new DiRegistration(
                genericType,
                ActivatesConstructors: !HasFactoryOrInstanceArgument(operation));
            return true;
        }

        INamedTypeSymbol? serviceType = null;
        foreach (var argument in operation.Arguments)
        {
            if (argument.Value is not ITypeOfOperation typeOf || ConcreteClass(typeOf.TypeOperand) is not { } type)
                continue;

            if (argument.Parameter is { Name: "implementationType" } implementationParameter
                && implementationParameter.Type.ToDisplayString() == "System.Type")
            {
                registration = new DiRegistration(type, ActivatesConstructors: true);
                return true;
            }

            if (argument.Parameter is { } serviceParameter && IsServiceTypeParameter(serviceParameter))
                serviceType = type;
        }

        if (serviceType is null)
            return false;

        registration = new DiRegistration(
            serviceType,
            ActivatesConstructors: !HasFactoryOrInstanceArgument(operation));
        return true;
    }

    private static bool IsServiceTypeParameter(IParameterSymbol parameter) =>
        parameter.Type.ToDisplayString() == "System.Type"
        && parameter.Name is "serviceType" or "service";

    private static bool HasFactoryOrInstanceArgument(IInvocationOperation operation)
    {
        foreach (var parameter in operation.TargetMethod.Parameters)
            if (parameter.Type.TypeKind == TypeKind.Delegate
                || parameter.Name.EndsWith("Factory", StringComparison.OrdinalIgnoreCase)
                || parameter.Name.EndsWith("Instance", StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }

    private static INamedTypeSymbol? ConcreteClass(ITypeSymbol type) =>
        type is INamedTypeSymbol { TypeKind: TypeKind.Class, IsAbstract: false } implementation
            ? implementation
            : null;
}
