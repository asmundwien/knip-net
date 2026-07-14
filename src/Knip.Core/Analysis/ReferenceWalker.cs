using Knip.Core.Configuration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Knip.Core.Analysis;

/// <summary>
/// Walks a single syntax tree, recording (1) every source-declared symbol, (2) "uses" edges from
/// the enclosing member to whatever it references — both in signatures and bodies — and (3) which
/// symbols are roots per the configured entry-point rules. All graph keys are <see cref="SymbolId"/>s.
/// </summary>
internal sealed class ReferenceWalker : CSharpSyntaxWalker
{
    internal static readonly SymbolDisplayFormat FqFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted);

    private readonly SemanticModel _model;
    private readonly KnipConfig _config;
    private readonly bool _publicApiProject;
    private readonly IReadOnlySet<string> _solutionAssemblies;
    private readonly GraphState _state;
    private readonly Stack<string> _context = new();

    public ReferenceWalker(
        SemanticModel model,
        KnipConfig config,
        bool publicApiProject,
        IReadOnlySet<string> solutionAssemblies,
        GraphState state)
        : base(SyntaxWalkerDepth.Node)
    {
        _model = model;
        _config = config;
        _publicApiProject = publicApiProject;
        _solutionAssemblies = solutionAssemblies;
        _state = state;
    }

    // ---- declarations: establish the "current member" context ------------------------------

    public override void VisitCompilationUnit(CompilationUnitSyntax node)
    {
        // Top-level statements belong to the synthesized program entry point, which is a root.
        if (node.Members.Any(m => m is GlobalStatementSyntax)
            && _model.Compilation.GetEntryPoint(CancellationToken.None) is { } entry
            && SymbolId.For(entry) is { } entryId)
        {
            _state.Declared[entryId] = entry;
            _state.Roots.Add(entryId);
            if (entry.ContainingType is { } host && SymbolId.For(host) is { } hostId)
                _state.Roots.Add(hostId);

            _context.Push(entryId);
            base.VisitCompilationUnit(node);
            _context.Pop();
            return;
        }
        base.VisitCompilationUnit(node);
    }

    public override void VisitClassDeclaration(ClassDeclarationSyntax node) => Enter(node, base.VisitClassDeclaration);
    public override void VisitStructDeclaration(StructDeclarationSyntax node) => Enter(node, base.VisitStructDeclaration);
    public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node) => Enter(node, base.VisitInterfaceDeclaration);
    public override void VisitRecordDeclaration(RecordDeclarationSyntax node) => Enter(node, base.VisitRecordDeclaration);
    public override void VisitEnumDeclaration(EnumDeclarationSyntax node) => Enter(node, base.VisitEnumDeclaration);
    public override void VisitDelegateDeclaration(DelegateDeclarationSyntax node) => Enter(node, base.VisitDelegateDeclaration);

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node) => Enter(node, base.VisitMethodDeclaration);
    public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node) => Enter(node, base.VisitConstructorDeclaration);
    public override void VisitDestructorDeclaration(DestructorDeclarationSyntax node) => Enter(node, base.VisitDestructorDeclaration);
    public override void VisitOperatorDeclaration(OperatorDeclarationSyntax node) => Enter(node, base.VisitOperatorDeclaration);
    public override void VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node) => Enter(node, base.VisitConversionOperatorDeclaration);
    public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node) => Enter(node, base.VisitPropertyDeclaration);
    public override void VisitIndexerDeclaration(IndexerDeclarationSyntax node) => Enter(node, base.VisitIndexerDeclaration);
    public override void VisitEventDeclaration(EventDeclarationSyntax node) => Enter(node, base.VisitEventDeclaration);

    public override void VisitFieldDeclaration(FieldDeclarationSyntax node) => EnterVariables(node.Declaration);
    public override void VisitEventFieldDeclaration(EventFieldDeclarationSyntax node) => EnterVariables(node.Declaration);

    private void Enter<T>(T node, Action<T> visitChildren) where T : SyntaxNode
    {
        var symbol = _model.GetDeclaredSymbol(node);
        if (symbol is null || symbol.IsImplicitlyDeclared)
        {
            visitChildren(node);
            return;
        }
        var id = Declare(symbol);
        if (id is null)
        {
            visitChildren(node);
            return;
        }
        _context.Push(id);
        visitChildren(node);
        _context.Pop();
    }

    private void EnterVariables(VariableDeclarationSyntax declaration)
    {
        foreach (var variable in declaration.Variables)
        {
            var symbol = _model.GetDeclaredSymbol(variable);
            if (symbol is null || symbol.IsImplicitlyDeclared) continue;
            var id = Declare(symbol);
            if (id is not null && variable.Initializer is not null)
            {
                _context.Push(id);
                Visit(variable.Initializer);
                _context.Pop();
            }
        }
    }

    private string? Declare(ISymbol symbol)
    {
        var id = SymbolId.For(symbol);
        if (id is null) return null;
        if (_state.Declared.TryAdd(id, symbol)) // partial types/methods appear once per file
        {
            EvaluateRoots(symbol, id);
            AddSignatureReferences(symbol, id);
        }
        return id;
    }

    // ---- references: edges from the current member to what it uses -------------------------

    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        RecordReference(node);
        base.VisitIdentifierName(node);
    }

    public override void VisitGenericName(GenericNameSyntax node)
    {
        RecordReference(node);
        base.VisitGenericName(node);
    }

    public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        RecordReference(node); // resolves to the constructor
        base.VisitObjectCreationExpression(node);
    }

    public override void VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
    {
        RecordReference(node);
        base.VisitImplicitObjectCreationExpression(node);
    }

    private void RecordReference(SyntaxNode node)
    {
        if (_context.Count == 0) return;
        var source = _context.Peek();
        var info = _model.GetSymbolInfo(node);
        if (info.Symbol is { } symbol)
        {
            AddEdge(source, symbol);
            return;
        }
        // Overload resolution failed (often because an argument type is unresolved): keep every
        // candidate alive rather than guessing, so we don't flag a real overload as dead.
        foreach (var candidate in info.CandidateSymbols)
            AddEdge(source, candidate);
    }

    /// <summary>Edges implied by a declaration's signature: base types, field/return/parameter types, attributes.</summary>
    private void AddSignatureReferences(ISymbol symbol, string id)
    {
        foreach (var attr in symbol.GetAttributes())
            if (attr.AttributeClass is not null) AddEdge(id, attr.AttributeClass);

        switch (symbol)
        {
            case IMethodSymbol method:
                AddTypeReference(id, method.ReturnType);
                foreach (var p in method.Parameters) AddTypeReference(id, p.Type);
                foreach (var tp in method.TypeParameters)
                    foreach (var c in tp.ConstraintTypes) AddTypeReference(id, c);
                break;
            case IPropertySymbol property:
                AddTypeReference(id, property.Type);
                foreach (var p in property.Parameters) AddTypeReference(id, p.Type);
                break;
            case IFieldSymbol field:
                AddTypeReference(id, field.Type);
                break;
            case IEventSymbol @event:
                AddTypeReference(id, @event.Type);
                break;
            case INamedTypeSymbol type:
                AddTypeReference(id, type.BaseType);
                foreach (var i in type.Interfaces) AddTypeReference(id, i);
                foreach (var tp in type.TypeParameters)
                    foreach (var c in tp.ConstraintTypes) AddTypeReference(id, c);
                break;
        }
    }

    private void AddTypeReference(string sourceId, ITypeSymbol? type)
    {
        switch (type)
        {
            case null:
                return;
            case IArrayTypeSymbol array:
                AddTypeReference(sourceId, array.ElementType);
                return;
            case IPointerTypeSymbol pointer:
                AddTypeReference(sourceId, pointer.PointedAtType);
                return;
            case INamedTypeSymbol named:
                if (named.TypeKind == TypeKind.Error) _state.UnresolvedTypeReferences++;
                AddEdge(sourceId, named);
                foreach (var arg in named.TypeArguments) AddTypeReference(sourceId, arg);
                return;
        }
    }

    private void AddEdge(string sourceId, ISymbol target)
    {
        // Only keep edges to symbols defined somewhere in the solution (source in any project).
        var assembly = target.OriginalDefinition.ContainingAssembly?.Name;
        if (assembly is null || !_solutionAssemblies.Contains(assembly)) return;

        var targetId = SymbolId.For(target);
        if (targetId is null || string.Equals(targetId, sourceId, StringComparison.Ordinal)) return;

        _state.AddEdge(sourceId, targetId);
    }

    // ---- roots: framework entry points seeded from config ----------------------------------

    private void EvaluateRoots(ISymbol symbol, string id)
    {
        var ep = _config.EntryPoints;

        var isRoot = ep.SymbolNames.Contains(symbol.Name)
            || symbol.GetAttributes().Any(a => MatchesAttribute(a, ep.Attributes))
            || ((_publicApiProject || _config.Roots.TreatAllPublicAsUsed) && IsExternallyVisible(symbol));

        if (isRoot)
        {
            _state.Roots.Add(id);
            // A framework-invoked member implies its declaring type is instantiated/used.
            for (var container = symbol.ContainingType; container is not null; container = container.ContainingType)
                if (SymbolId.For(container) is { } containerId) _state.Roots.Add(containerId);
        }

        if (symbol is INamedTypeSymbol type && IsEntryType(type, ep))
        {
            _state.Roots.Add(id);
            foreach (var member in type.GetMembers())
                if (!member.IsImplicitlyDeclared && IsExternallyVisible(member) && SymbolId.For(member) is { } memberId)
                    _state.Roots.Add(memberId);
        }
    }

    private static bool MatchesAttribute(AttributeData attr, List<string> names)
    {
        var name = attr.AttributeClass?.Name;
        if (name is null) return false;
        var trimmed = name.EndsWith("Attribute", StringComparison.Ordinal)
            ? name[..^"Attribute".Length]
            : name;
        return names.Contains(name) || names.Contains(trimmed);
    }

    private static bool IsEntryType(INamedTypeSymbol type, EntryPointConfig ep)
    {
        foreach (var pattern in ep.NamePatterns)
            if (Glob.IsMatch(type.Name, pattern)) return true;

        for (var b = type.BaseType; b is not null; b = b.BaseType)
        {
            var display = b.ToDisplayString(FqFormat);
            if (ep.BaseTypes.Contains(display) || ep.ImplementedInterfaces.Contains(display)) return true;
        }

        foreach (var iface in type.AllInterfaces)
            if (ep.ImplementedInterfaces.Contains(iface.ToDisplayString(FqFormat))) return true;

        return false;
    }

    private static bool IsExternallyVisible(ISymbol symbol) =>
        symbol.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal;
}
