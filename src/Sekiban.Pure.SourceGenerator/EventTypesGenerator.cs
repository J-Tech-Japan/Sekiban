using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
namespace Sekiban.Pure.SourceGenerator;

[Generator]
public class EventTypesGenerator : IIncrementalGenerator
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
                var commandTypes = ImmutableArray.CreateBuilder<CommandWithHandlerValues>();

                commandTypes.AddRange(GetEventValues(compilation, types));

                // Generate source code
                var rootNamespace = compilation.AssemblyName;
                var sourceCode = GenerateSourceCode(commandTypes.ToImmutable(), rootNamespace);
                ctx.AddSource("EventTypes.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
            });

    }
    public ImmutableArray<CommandWithHandlerValues> GetEventValues(
        Compilation compilation,
        ImmutableArray<SyntaxNode> types)
    {
        var iEventPayloadSymbol = compilation.GetTypeByMetadataName("Sekiban.Pure.IEventPayload");
        if (iEventPayloadSymbol == null)
            return new ImmutableArray<CommandWithHandlerValues>();
        var eventTypes = ImmutableArray.CreateBuilder<CommandWithHandlerValues>();
        foreach (var typeSyntax in types)
        {
            var model = compilation.GetSemanticModel(typeSyntax.SyntaxTree);
            var typeSymbol = model.GetDeclaredSymbol(typeSyntax) as INamedTypeSymbol;
            var allInterfaces = typeSymbol.AllInterfaces.ToList();
            if (typeSymbol != null && typeSymbol.AllInterfaces.Any(m => m == iEventPayloadSymbol))
            {
                var interfaceImplementation = typeSymbol.AllInterfaces.First(m => m == iEventPayloadSymbol);
                eventTypes.Add(
                    new CommandWithHandlerValues
                    {
                        InterfaceName = interfaceImplementation.Name,
                        RecordName = typeSymbol.ToDisplayString()
                    });
            }
        }
        return eventTypes.ToImmutable();
    }

    private string GenerateSourceCode(ImmutableArray<CommandWithHandlerValues> eventTypes, string rootNamespace)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// Auto-generated by IncrementalGenerator");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using ResultBoxes;");
        sb.AppendLine("using Sekiban.Pure;");
        sb.AppendLine("using Sekiban.Pure.Exception;");

        sb.AppendLine();
        sb.AppendLine($"namespace {rootNamespace}.Generated");
        sb.AppendLine("{");
        sb.AppendLine($"    public class {rootNamespace.Replace(".", "")}EventTypes : IEventTypes");
        sb.AppendLine("    {");
        sb.AppendLine("        public ResultBox<IEvent> GenerateTypedEvent(");
        sb.AppendLine("            IEventPayload payload,");
        sb.AppendLine("            PartitionKeys partitionKeys,");
        sb.AppendLine("            string sortableUniqueId,");
        sb.AppendLine("            int version) => payload switch");
        sb.AppendLine("        {");

        foreach (var type in eventTypes)
        {
            switch (type.InterfaceName, type.TypeCount)
            {
                case ("IEventPayload", 0):
                    sb.AppendLine(
                        $"            {type.RecordName} {type.RecordName.Split('.').Last().ToLower()} => new Event<{type.RecordName}>(");
                    sb.AppendLine($"                {type.RecordName.Split('.').Last().ToLower()},");
                    sb.AppendLine("                partitionKeys,");
                    sb.AppendLine("                sortableUniqueId,");
                    sb.AppendLine("                version),");
                    break;
            }
        }

        sb.AppendLine("            _ => ResultBox<IEvent>.FromException(");
        sb.AppendLine(
            "                new SekibanEventTypeNotFoundException($\"Event Type {payload.GetType().Name} Not Found\"))");
        sb.AppendLine("        };");
        sb.AppendLine("    };");
        sb.AppendLine("}");

        return sb.ToString();
    }
    public class CommandWithHandlerValues
    {
        public string InterfaceName { get; set; }
        public string RecordName { get; set; }
        public int TypeCount { get; set; }
    }
}