using Knip.Core.Configuration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

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

    /// <summary>
    /// (WS7) Attribute names (with or without the "Attribute" suffix) that mark a root as TEST-ORIGIN,
    /// regardless of the declaring project. These are the test-FRAMEWORK attributes — the runner invokes
    /// the method, and it exists to exercise production code. NON-test entry-point attributes (ASP.NET
    /// routing like <c>HttpGet</c>, <c>IHostedService</c> conventions) are NOT here: they are genuine
    /// production entry points and must seed PRODUCTION roots. Kept in sync with the test-framework subset
    /// of <see cref="EntryPointConfig.Attributes"/>.
    /// </summary>
    internal static readonly HashSet<string> TestAttributeNames = new(StringComparer.Ordinal)
    {
        // xUnit
        "Fact", "Theory",
        // MSTest
        "TestMethod", "DataTestMethod",
        "TestInitialize", "TestCleanup", "ClassInitialize", "ClassCleanup",
        "AssemblyInitialize", "AssemblyCleanup",
        // NUnit
        "Test", "TestCase", "SetUp", "TearDown", "OneTimeSetUp", "OneTimeTearDown",
        // BenchmarkDotNet (a benchmark harness, not a production entry point)
        "Benchmark", "GlobalSetup",
    };

    private readonly SemanticModel _model;
    private readonly KnipConfig _config;
    private readonly bool _publicApiProject;
    // (WS7) When true this walker's project is a TEST project: EVERY root it seeds is a test root
    // (feeds two-color reachability). In a production project only test-ATTRIBUTE-driven roots are test.
    private readonly bool _testProject;
    // ISet (not IReadOnlySet) is the common surface across the net10.0 and net472 BCLs — IReadOnlySet<T>
    // does not exist on net472. Only .Contains is used here, and it is never mutated after construction.
    private readonly ISet<string> _solutionAssemblies;
    private readonly GraphState _state;
    // When true this tree is BUILT-IN generated (H11): walk it for edges/roots exactly as normal, but
    // record every id it DECLARES as "never report" so its own dead code is not flagged to the user.
    private readonly bool _generatedTree;
    private readonly string? _ownAssembly;
    private readonly Stack<string> _context = new();

    // (WS8c) The reference-SITE location of the syntax node currently being recorded, so an edge can
    // carry "where the reference lives" for the --why report. Set around RecordReference-style calls;
    // only READ when GraphState.CaptureProvenance is on (default runs never touch it). Null falls back
    // to the target/source declaration location at render time.
    private Location? _currentSite;

    public ReferenceWalker(
        SemanticModel model,
        KnipConfig config,
        bool publicApiProject,
        ISet<string> solutionAssemblies,
        GraphState state,
        bool generatedTree = false,
        bool testProject = false)
        : base(SyntaxWalkerDepth.Node)
    {
        _model = model;
        _config = config;
        _publicApiProject = publicApiProject;
        _solutionAssemblies = solutionAssemblies;
        _state = state;
        _generatedTree = generatedTree;
        _testProject = testProject;
        // The assembly this tree belongs to — the source project of every edge this walker records.
        _ownAssembly = model.Compilation.Assembly.Name;
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
            // Program entry point: production origin (a test project's own Main would be test-origin).
            AddRoot(entryId, _testProject);
            if (entry.ContainingType is { } host && SymbolId.For(host) is { } hostId)
                AddRoot(hostId, _testProject);

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

    // Enum MEMBERS are named constants (IFieldSymbol) with their own graph node (G6/WS-enum). Declaring
    // them makes an unused member a first-class finding when the enum TYPE is alive; if the whole enum is
    // dead, the outermost-only rule (§3.7) reports the type and suppresses each member. `Enter` walks the
    // member's initializer (`= SomeConst | Other`) under the member's context so those edges land.
    public override void VisitEnumMemberDeclaration(EnumMemberDeclarationSyntax node) => Enter(node, base.VisitEnumMemberDeclaration);
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
        // Signature edges added below are not "reference sites" in a body — clear any stale --why site
        // so they fall back to the target declaration location rather than an unrelated prior node.
        _currentSite = null;
        // A declaration whose id first appears in a generated tree is "generated" for reporting: its own
        // dead code is not the user's to delete (H11). Marked here — where "declared in THIS tree" is
        // known — rather than post-hoc from Locations, which would also cover a partial's user-authored
        // part in another file.
        if (_generatedTree) _state.GeneratedDeclarations.Add(id);
        // (WS7) Symbols declared in a test project are test CODE — never OnlyUsedByTests production findings.
        if (_testProject) _state.TestDeclarations.Add(id);
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

    // User-defined binary operators (e.g. a + b, a == b, a != b) are invoked with no IdentifierName/
    // GenericName node at the use site. GetSymbolInfo on the binary expression resolves to the
    // user-defined operator method when one applies (null/BCL for built-in operands — AddEdge drops
    // non-solution targets, so recording unconditionally is safe).
    public override void VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        RecordReference(node);
        base.VisitBinaryExpression(node);
    }

    // Explicit casts ((T)x) carry a user-defined conversion operator with no IdentifierName node.
    public override void VisitCastExpression(CastExpressionSyntax node)
    {
        RecordReference(node);
        base.VisitCastExpression(node);
    }

    // Implicit user-defined conversions (e.g. `Celsius c = 21.5;`, passing an argument, `return x;`)
    // are invoked with no operator token or IdentifierName node. GetConversion on the source
    // expression exposes the conversion method when the compiler applied a user-defined conversion.
    public override void VisitEqualsValueClause(EqualsValueClauseSyntax node)
    {
        RecordConversion(node.Value);
        base.VisitEqualsValueClause(node);
    }

    public override void VisitArgument(ArgumentSyntax node)
    {
        RecordConversion(node.Expression);
        base.VisitArgument(node);
    }

    public override void VisitReturnStatement(ReturnStatementSyntax node)
    {
        if (node.Expression is { } expr) RecordConversion(expr);
        base.VisitReturnStatement(node);
    }

    public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        // Compound assignment (a += b) invokes a user-defined operator; simple assignment may apply
        // an implicit user-defined conversion on the right-hand side.
        if (node.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            RecordConversion(node.Right);
            // Tuple deconstruction (`var (a, b) = obj`, `(a, b) = obj`) invokes a user-defined
            // Deconstruct with no IdentifierName node at the use site.
            RecordDeconstruction(node);
        }
        else
        {
            RecordReference(node);
        }
        base.VisitAssignmentExpression(node);
    }

    // Collection initializers (`new C { 1, 2 }`) lower each element to an Add(...) call with no
    // IdentifierName node at the use site. GetCollectionInitializerSymbolInfo on each element
    // resolves to the invoked Add method (CandidateSymbols fallback when overload resolution fails).
    public override void VisitInitializerExpression(InitializerExpressionSyntax node)
    {
        if (node.IsKind(SyntaxKind.CollectionInitializerExpression) && _context.Count > 0)
        {
            var source = _context.Peek();
            SetSite(node);
            foreach (var element in node.Expressions)
            {
                var info = _model.GetCollectionInitializerSymbolInfo(element);
                if (info.Symbol is { } symbol)
                    AddEdge(source, symbol);
                else
                    foreach (var candidate in info.CandidateSymbols)
                        AddEdge(source, candidate);
            }
        }
        base.VisitInitializerExpression(node);
    }

    private void RecordDeconstruction(AssignmentExpressionSyntax node)
    {
        if (_context.Count == 0) return;
        SetSite(node);
        RecordDeconstructionInfo(_model.GetDeconstructionInfo(node), _context.Peek());
    }

    private void RecordDeconstructionInfo(DeconstructionInfo info, string source)
    {
        if (info.Method is { } method) AddEdge(source, method);
        foreach (var nested in info.Nested)
            RecordDeconstructionInfo(nested, source);
    }

    // Element access (obj[i], obj[^1], obj[1..]) invokes a member with no IdentifierName/GenericName
    // at the use site, so the walker would otherwise miss the edge and flag the member dead.
    //   - Custom indexer `obj[i]`: GetSymbolInfo resolves to the indexer property (IsIndexer).
    //     Arrays/BCL resolve to null/non-solution — AddEdge drops those, so recording is safe.
    //   - Index/Range over the Length/Slice pattern (`obj[^1]`, `obj[1..]`): GetSymbolInfo on the
    //     element access does NOT surface the pattern members. GetOperation yields an
    //     IImplicitIndexerReferenceOperation exposing the Length symbol plus the indexer/Slice.
    public override void VisitElementAccessExpression(ElementAccessExpressionSyntax node)
    {
        RecordReference(node);
        RecordImplicitIndexerMembers(node);
        base.VisitElementAccessExpression(node);
    }

    private void RecordImplicitIndexerMembers(SyntaxNode node)
    {
        if (_context.Count == 0) return;
        if (_model.GetOperation(node) is not IImplicitIndexerReferenceOperation implicitIndexer) return;

        var source = _context.Peek();
        SetSite(node);
        if (implicitIndexer.LengthSymbol is { } length) AddEdge(source, length);
        if (implicitIndexer.IndexerSymbol is { } indexer) AddEdge(source, indexer);
    }

    // `foreach` over a pattern-based (duck-typed) enumerable binds GetEnumerator/MoveNext/Current
    // (and DisposeMethod for a pattern enumerator implementing pattern dispose) with no IdentifierName
    // node at the use site. GetForEachStatementInfo surfaces each bound member; the enumerator TYPE
    // stays alive automatically once its members are edged.
    public override void VisitForEachStatement(ForEachStatementSyntax node)
    {
        RecordForEachMembers(node);
        base.VisitForEachStatement(node);
    }

    private void RecordForEachMembers(CommonForEachStatementSyntax node)
    {
        if (_context.Count == 0) return;
        var info = _model.GetForEachStatementInfo(node);
        var source = _context.Peek();
        SetSite(node);
        if (info.GetEnumeratorMethod is { } getEnumerator) AddEdge(source, getEnumerator);
        if (info.MoveNextMethod is { } moveNext) AddEdge(source, moveNext);
        if (info.CurrentProperty is { } current) AddEdge(source, current);
        if (info.DisposeMethod is { } dispose) AddEdge(source, dispose);
    }

    // `await` on a custom awaitable binds GetAwaiter/IsCompleted/GetResult with no IdentifierName node
    // at the use site. GetAwaitExpressionInfo surfaces each bound member; the awaiter TYPE stays alive
    // automatically once its members are edged.
    public override void VisitAwaitExpression(AwaitExpressionSyntax node)
    {
        RecordAwaitMembers(node);
        base.VisitAwaitExpression(node);
    }

    private void RecordAwaitMembers(AwaitExpressionSyntax node)
    {
        if (_context.Count == 0) return;
        var info = _model.GetAwaitExpressionInfo(node);
        var source = _context.Peek();
        SetSite(node);
        if (info.GetAwaiterMethod is { } getAwaiter) AddEdge(source, getAwaiter);
        if (info.IsCompletedProperty is { } isCompleted) AddEdge(source, isCompleted);
        if (info.GetResultMethod is { } getResult) AddEdge(source, getResult);
    }

    // LINQ query syntax (`from x in xs where ... select ...`) lowers each clause to a method call
    // (Where/Select/SelectMany/Join/GroupJoin/OrderBy/GroupBy/Cast/...) with NO IdentifierName node at
    // the use site, so the walker would otherwise miss the edge and flag the bound provider methods
    // dead. Each clause surfaces the method it binds:
    //   - GetSymbolInfo(whereClause/selectClause/groupClause/orderingClause) → its method (may be null
    //     for a degenerate/identity select; AddEdge no-ops on null).
    //   - GetQueryClauseInfo(fromClause/joinClause/letClause/continuation).OperationInfo (a SymbolInfo)
    //     → the SelectMany/Cast/Join/GroupJoin/Select bound for 2nd-and-later from, joins, let, into.
    // Only the methods the query actually binds are edged (siblings like an unused GroupBy stay dead).
    public override void VisitQueryExpression(QueryExpressionSyntax node)
    {
        if (_context.Count > 0)
        {
            var source = _context.Peek();
            SetSite(node);
            RecordQueryClauseInfo(_model.GetQueryClauseInfo(node.FromClause), source);
            RecordQueryBody(node.Body, source);
        }
        base.VisitQueryExpression(node);
    }

    private void RecordQueryBody(QueryBodySyntax body, string source)
    {
        foreach (var clause in body.Clauses)
        {
            switch (clause)
            {
                case OrderByClauseSyntax orderBy:
                    foreach (var ordering in orderBy.Orderings)
                        RecordSymbolInfo(_model.GetSymbolInfo(ordering), source);
                    break;
                // where, from (2nd+), join, let carry the invoked method on OperationInfo.
                case WhereClauseSyntax:
                case FromClauseSyntax:
                case JoinClauseSyntax:
                case LetClauseSyntax:
                    RecordQueryClauseInfo(_model.GetQueryClauseInfo(clause), source);
                    break;
            }
        }

        // select / group ... by ... — the terminal projection.
        RecordSymbolInfo(_model.GetSymbolInfo(body.SelectOrGroup), source);

        // `into` continuation reintroduces a range variable and recurses into a nested body.
        if (body.Continuation is { } continuation)
            RecordQueryBody(continuation.Body, source);
    }

    private void RecordQueryClauseInfo(QueryClauseInfo info, string source) =>
        RecordSymbolInfo(info.OperationInfo, source);

    private void RecordSymbolInfo(SymbolInfo info, string source)
    {
        if (info.Symbol is { } symbol)
            AddEdge(source, symbol);
        else
            foreach (var candidate in info.CandidateSymbols)
                AddEdge(source, candidate);
    }

    // Pattern-based `Dispose` on a ref struct in a `using` statement / local `using` declaration is
    // invoked by the using lowering with no IdentifierName node at the use site. No public
    // SemanticModel info API surfaces the bound pattern-dispose method (GetOperation on the
    // resource does not expose it), so we resolve it deterministically: from the resource's type,
    // find the accessible instance parameterless method named `Dispose` and edge to it. This mirrors
    // how the compiler binds pattern dispose; it is additive and solution-scoped via AddEdge.
    public override void VisitUsingStatement(UsingStatementSyntax node)
    {
        if (node.Declaration is { } declaration)
            foreach (var variable in declaration.Variables)
                RecordPatternDispose(declaration.Type, variable);
        else if (node.Expression is { } expression)
            RecordPatternDisposeForType(_model.GetTypeInfo(expression).Type);
        base.VisitUsingStatement(node);
    }

    public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
    {
        if (!node.UsingKeyword.IsKind(SyntaxKind.None))
            foreach (var variable in node.Declaration.Variables)
                RecordPatternDispose(node.Declaration.Type, variable);
        base.VisitLocalDeclarationStatement(node);
    }

    private void RecordPatternDispose(TypeSyntax declaredType, VariableDeclaratorSyntax variable)
    {
        // `using var x = ...` has an implicit type; take the initializer's type when the declared
        // type node doesn't resolve to a concrete type.
        var type = _model.GetTypeInfo(declaredType).Type;
        if (type is null or IErrorTypeSymbol && variable.Initializer is { } init)
            type = _model.GetTypeInfo(init.Value).Type;
        RecordPatternDisposeForType(type);
    }

    private void RecordPatternDisposeForType(ITypeSymbol? type)
    {
        if (_context.Count == 0 || type is null) return;
        var source = _context.Peek();
        // No single reference-site node here (pattern dispose is implicit); leave the site unset so
        // --why falls back to the target declaration location.
        _currentSite = null;
        foreach (var member in type.GetMembers("Dispose"))
            if (member is IMethodSymbol { Parameters.IsEmpty: true, IsStatic: false })
                AddEdge(source, member);
    }

    private void RecordConversion(ExpressionSyntax expression)
    {
        if (_context.Count == 0) return;
        var conversion = _model.GetConversion(expression);
        if (conversion.IsUserDefined && conversion.MethodSymbol is { } method)
        {
            _currentSite = _state.CaptureProvenance ? expression.GetLocation() : null;
            AddEdge(_context.Peek(), method);
        }
    }

    /// <summary>(WS8c) Set the reference-site for the edges a body-recording helper is about to add.</summary>
    private void SetSite(SyntaxNode node) => _currentSite = _state.CaptureProvenance ? node.GetLocation() : null;

    private void RecordReference(SyntaxNode node)
    {
        if (_context.Count == 0) return;
        var source = _context.Peek();
        // (WS8c) Remember the reference SITE so any edge recorded below carries its file:line for --why.
        _currentSite = _state.CaptureProvenance ? node.GetLocation() : null;
        var info = _model.GetSymbolInfo(node);
        if (info.Symbol is { } symbol)
        {
            AddEdge(source, symbol);
            // Extension syntax (`obj.Used()`) resolves to a REDUCED extension method: the declaring
            // static class name never appears in source, so nothing else edges its TYPE node and it
            // would be flagged dead (hiding the genuinely-live method under the outermost-only rule).
            // Edge the reduced method's ContainingType (the static class) so it stays reachable, and
            // normalize the method edge to the unreduced declaration (ReducedFrom) so the method-node
            // id matches the source declaration.
            if (symbol is IMethodSymbol { MethodKind: MethodKind.ReducedExtension, ReducedFrom: { } reduced })
            {
                AddEdge(source, reduced.ContainingType);
                AddEdge(source, reduced);
            }
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

        // EXTERNAL branch: the target lives in a NON-solution assembly (BCL / NuGet). We do NOT add it
        // as a graph node (invariant #5), but we DO record (ownAssembly → externalAssemblyName) — the
        // only place the walker sees which external assemblies a project's code actually touches. This
        // is the raw signal WS3 maps back to <PackageReference>s (a package none of whose delivered
        // assemblies appear here is never referenced). Skip the own assembly (self is not external).
        if (assembly is not null && !_solutionAssemblies.Contains(assembly))
        {
            if (_ownAssembly is not null && !string.Equals(assembly, _ownAssembly, StringComparison.Ordinal))
                _state.RecordExternalAssemblyUse(_ownAssembly, assembly);
            return;
        }

        if (assembly is null) return;

        // Cross-assembly edge: this project's code touches a symbol OWNED by another solution
        // assembly. Record (ownAssembly -> targetAssembly) so DeadCodeAnalyzer can tell which
        // <ProjectReference>s are actually exercised. Guard on ownAssembly being a distinct solution
        // assembly so intra-project references don't register as "uses" of a reference.
        if (_ownAssembly is not null
            && !string.Equals(assembly, _ownAssembly, StringComparison.Ordinal))
        {
            _state.RecordAssemblyUse(_ownAssembly, assembly);
        }

        var targetId = SymbolId.For(target);
        if (targetId is null || string.Equals(targetId, sourceId, StringComparison.Ordinal)) return;

        _state.AddEdge(sourceId, targetId);
        // (WS8c) Attach the reference-site location for --why (no-op unless provenance is on).
        _state.RecordEdgeSource(sourceId, targetId, _currentSite);
    }

    // ---- roots: framework entry points seeded from config ----------------------------------

    private void EvaluateRoots(ISymbol symbol, string id)
    {
        var ep = _config.EntryPoints;

        // A test-ATTRIBUTE-driven root is test-origin even in a production project (WS7 two-color).
        var hasTestAttribute =
            symbol.GetAttributes().Any(a => MatchesTestAttribute(a));

        var isRoot = ep.SymbolNames.Contains(symbol.Name)
            || symbol.GetAttributes().Any(a => MatchesAttribute(a, ep.Attributes))
            || ((_publicApiProject || _config.Roots.TreatAllPublicAsUsed) && IsExternallyVisible(symbol));

        if (isRoot)
        {
            // TEST origin when the whole project is a test project OR this specific root fired on a test
            // attribute. Everything else (Main, publicApi surface, ASP.NET routing) is a PRODUCTION root.
            var asTest = _testProject || hasTestAttribute;
            AddRoot(id, asTest);
            // A framework-invoked member implies its declaring type is instantiated/used. The type chain
            // inherits the SAME origin as the member (a test class kept alive only by its [Fact]s is test).
            for (var container = symbol.ContainingType; container is not null; container = container.ContainingType)
                if (SymbolId.For(container) is { } containerId) AddRoot(containerId, asTest);
        }

        if (symbol is INamedTypeSymbol type && IsEntryType(type, ep))
        {
            // Entry types (Controller, IHostedService, …) are production entry points; in a test project
            // they still take the project's test origin.
            AddRoot(id, _testProject);
            foreach (var member in type.GetMembers())
                if (!member.IsImplicitlyDeclared && IsExternallyVisible(member) && SymbolId.For(member) is { } memberId)
                    AddRoot(memberId, _testProject);
        }
    }

    /// <summary>
    /// Seed a root, recording its ORIGIN (WS7). A single id can be added from several sites with different
    /// origins; both origins are recorded and the FINAL classification (production wins) is resolved at
    /// traversal time by <see cref="GraphState.TestOnlyRoots"/>.
    /// </summary>
    private void AddRoot(string id, bool asTest)
    {
        _state.Roots.Add(id);
        if (asTest) _state.TestRoots.Add(id);
        else _state.ProductionRoots.Add(id);
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

    /// <summary>(WS7) True when the attribute is a test-FRAMEWORK attribute (see <see cref="TestAttributeNames"/>).</summary>
    private static bool MatchesTestAttribute(AttributeData attr)
    {
        var name = attr.AttributeClass?.Name;
        if (name is null) return false;
        var trimmed = name.EndsWith("Attribute", StringComparison.Ordinal)
            ? name[..^"Attribute".Length]
            : name;
        return TestAttributeNames.Contains(name) || TestAttributeNames.Contains(trimmed);
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
