using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
namespace Sekiban.Pure.SourceGenerator;

[Generator]
public class MultiProjectorTypesGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Collect all class and record declarations
        var typeDeclarations = context
            .SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax || node is RecordDeclarationSyntax,
                static (ctx, _) => ctx.Node)
            .Where(static typeDecl => typeDecl is ClassDeclarationSyntax || typeDecl is RecordDeclarationSyntax);

        // Combine with compilation information
        var compilationAndTypes = context.CompilationProvider.Combine(typeDeclarations.Collect());

        // Generate source code
        context.RegisterSourceOutput(
            compilationAndTypes,
            (ctx, source) =>
            {
                var (compilation, types) = source;
                var multiProjectorTypes = ImmutableArray.CreateBuilder<MultiProjectorValue>();
                var aggregateProjectorTypes = ImmutableArray.CreateBuilder<AggregateProjectorValues>();

                multiProjectorTypes.AddRange(GetMultiProjectorValues(compilation, types));
                aggregateProjectorTypes.AddRange(GetAggregateProjectorValues(compilation, types));
                // Generate source code
                var rootNamespace = compilation.AssemblyName ?? throw new ApplicationException("AssemblyName is null");
                var sourceCode = GenerateSourceCode(
                    multiProjectorTypes.ToImmutable(),
                    aggregateProjectorTypes.ToImmutable(),
                    rootNamespace);
                ctx.AddSource("MultiProjectorTypes.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
            });
    }

    public ImmutableArray<MultiProjectorValue> GetMultiProjectorValues(
        Compilation compilation,
        ImmutableArray<SyntaxNode> types)
    {
        var iMultiProjectorCommonSymbol =
            compilation.GetTypeByMetadataName("Sekiban.Pure.Projectors.IMultiProjectorCommon");
        if (iMultiProjectorCommonSymbol == null)
            return new ImmutableArray<MultiProjectorValue>();
        var multiProjectorTypes = ImmutableArray.CreateBuilder<MultiProjectorValue>();
        foreach (var typeSyntax in types)
        {
            var model = compilation.GetSemanticModel(typeSyntax.SyntaxTree);
            var typeSymbol = model.GetDeclaredSymbol(typeSyntax) as INamedTypeSymbol ??
                throw new ApplicationException("TypeSymbol is null");
            var allInterfaces = typeSymbol.AllInterfaces.ToList();
            if (typeSymbol.AllInterfaces.Any(
                m =>
                    m.Equals(iMultiProjectorCommonSymbol, SymbolEqualityComparer.Default)))
            {
                var interfaceImplementation = typeSymbol.AllInterfaces.First(
                    m => m.Equals(iMultiProjectorCommonSymbol, SymbolEqualityComparer.Default));
                multiProjectorTypes.Add(
                    new MultiProjectorValue
                    {
                        TypeName = typeSymbol.ToDisplayString()
                    });
            }
        }

        return multiProjectorTypes.ToImmutable();
    }
    public ImmutableArray<AggregateProjectorValues> GetAggregateProjectorValues(
        Compilation compilation,
        ImmutableArray<SyntaxNode> types)
    {
        var iEventPayloadSymbol = compilation.GetTypeByMetadataName("Sekiban.Pure.Projectors.IAggregateProjector");
        if (iEventPayloadSymbol == null)
            return new ImmutableArray<AggregateProjectorValues>();
        var eventTypes = ImmutableArray.CreateBuilder<AggregateProjectorValues>();
        foreach (var typeSyntax in types)
        {
            var model = compilation.GetSemanticModel(typeSyntax.SyntaxTree);
            var typeSymbol = model.GetDeclaredSymbol(typeSyntax) as INamedTypeSymbol ??
                throw new ApplicationException("TypeSymbol is null");
            var allInterfaces = typeSymbol.AllInterfaces.ToList();
            if (typeSymbol.AllInterfaces.Any(m => m.Equals(iEventPayloadSymbol, SymbolEqualityComparer.Default)))
            {
                var interfaceImplementation = typeSymbol.AllInterfaces.First(
                    m => m.Equals(iEventPayloadSymbol, SymbolEqualityComparer.Default));
                eventTypes.Add(
                    new AggregateProjectorValues
                    {
                        InterfaceName = interfaceImplementation.Name,
                        RecordName = typeSymbol.ToDisplayString()
                    });
            }
        }
        return eventTypes.ToImmutable();
    }
    public class AggregateProjectorValues
    {
        public string InterfaceName { get; set; } = string.Empty;
        public string RecordName { get; set; } = string.Empty;
        public int TypeCount { get; set; }
    }

    private string GenerateSourceCode(
        ImmutableArray<MultiProjectorValue> multiProjectorTypes,
        ImmutableArray<AggregateProjectorValues> aggregateProjectorTypes,
        string rootNamespace)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// Auto-generated by IncrementalGenerator");
        sb.AppendLine("using System;");
        sb.AppendLine("using ResultBoxes;");
        sb.AppendLine("using Sekiban.Pure.Events;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using Sekiban.Pure.Projectors;");

        sb.AppendLine();
        sb.AppendLine($"namespace {rootNamespace}.Generated");
        sb.AppendLine("{");
        sb.AppendLine($"    public class {rootNamespace.Replace(".", "")}MultiProjectorTypes : IMultiProjectorTypes");
        sb.AppendLine("    {");
        sb.AppendLine(
            "        public ResultBox<IMultiProjectorCommon> Project(IMultiProjectorCommon multiProjector, IEvent ev)");
        sb.AppendLine("            => multiProjector switch");
        sb.AppendLine("            {");

        foreach (var type in multiProjectorTypes)
        {
            sb.AppendLine(
                $"                {type.TypeName} {type.TypeName.Split('.').Last().ToCamelCase()} => {type.TypeName.Split('.').Last().ToCamelCase()}.Project({type.TypeName.Split('.').Last().ToCamelCase()}, ev)");
            sb.AppendLine("                    .Remap(mp => (IMultiProjectorCommon)mp),");
        }
        foreach (var type in aggregateProjectorTypes)
        {
            var className = type.RecordName.Split('.').Last().ToCamelCase();
            sb.AppendLine(
                $"                AggregateListProjector<{type.RecordName}> {className} => {className}.Project({className}, ev)");
            sb.AppendLine("                    .Remap(mp => (IMultiProjectorCommon)mp),");
        }

        sb.AppendLine("                _ => new ApplicationException(multiProjector.GetType().Name)");
        sb.AppendLine("            };");
        sb.AppendLine();
        sb.AppendLine(
            "        public ResultBox<IMultiProjectorCommon> Project(IMultiProjectorCommon multiProjector, IReadOnlyList<IEvent> events) => ResultBox.FromValue(events.ToList())");
        sb.AppendLine("            .ReduceEach(multiProjector, (ev, common) => Project(common, ev));");
        sb.AppendLine();
        sb.AppendLine("        public IMultiProjectorStateCommon ToTypedState(MultiProjectionState state)");
        sb.AppendLine("            => state.ProjectorCommon switch");
        sb.AppendLine("            {");

        foreach (var type in multiProjectorTypes)
        {
            var className = type.TypeName.Split('.').Last();
            sb.AppendLine(
                $"                {type.TypeName} projector => new MultiProjectionState<{type.TypeName}>(projector, state.LastEventId, state.LastSortableUniqueId, state.Version, state.AppliedSnapshotVersion, state.RootPartitionKey),");
        }
        foreach (var type in aggregateProjectorTypes)
        {
            sb.AppendLine(
                $"                AggregateListProjector<{type.RecordName}> aggregator => new MultiProjectionState<AggregateListProjector<{type.RecordName}>>(aggregator, state.LastEventId, state.LastSortableUniqueId, state.Version, state.AppliedSnapshotVersion, state.RootPartitionKey),");
        }

        sb.AppendLine(
            "                _ => throw new ArgumentException($\"No state type found for projector type: {state.ProjectorCommon.GetType().Name}\")");
        sb.AppendLine("            };");
        sb.AppendLine();
        sb.AppendLine("        public IMultiProjectorCommon GetProjectorFromMultiProjectorName(string grainName)");
        sb.AppendLine("            => grainName switch");
        sb.AppendLine("            {");

        foreach (var type in multiProjectorTypes)
        {
            var className = type.TypeName.Split('.').Last();
            sb.AppendLine(
                $"                not null when {type.TypeName}.GetMultiProjectorName() == grainName => {type.TypeName}.GenerateInitialPayload(),");
        }
        foreach (var type in aggregateProjectorTypes)
        {
            sb.AppendLine(
                $"                not null when AggregateListProjector<{type.RecordName}>.GetMultiProjectorName() == grainName => AggregateListProjector<{type.RecordName}>.GenerateInitialPayload(),");
        }

        sb.AppendLine(
            "                _ => throw new ArgumentException($\"No projector found for grain name: {grainName}\")");
        sb.AppendLine("            };");
        sb.AppendLine("        public ResultBox<string> GetMultiProjectorNameFromMultiProjector(IMultiProjectorCommon multiProjector)");
        sb.AppendLine("            => multiProjector switch");
        sb.AppendLine("            {");
        foreach (var type in multiProjectorTypes)
        {
            var className = type.TypeName.Split('.').Last();
            sb.AppendLine($"                {type.TypeName} projector => ResultBox.FromValue({type.TypeName}.GetMultiProjectorName()),");
        }
        foreach (var type in aggregateProjectorTypes)
        {
            sb.AppendLine($"                AggregateListProjector<{type.RecordName}> aggregator => ResultBox.FromValue(AggregateListProjector<{type.RecordName}>.GetMultiProjectorName()),");
        }
        sb.AppendLine("                _ => ResultBox<string>.Error(new ApplicationException(multiProjector.GetType().Name))");
        sb.AppendLine("            };");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    public class MultiProjectorValue
    {
        public string TypeName { get; set; } = string.Empty;
    }
}
