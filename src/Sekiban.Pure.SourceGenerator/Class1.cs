using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
namespace Sekiban.Pure.SourceGenerator;

public class Class1
{
}
[Generator]
public class CommandExecutionExtensionGenerator : IIncrementalGenerator
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
                var eventTypes = ImmutableArray.CreateBuilder<CommandWithHandlerValues>();

                eventTypes.AddRange(GetCommandWithHandlerValues(compilation, types));
                eventTypes.AddRange(GetICommandWithHandlerInjectionValues(compilation, types));
                // Generate source code
                var rootNamespace = compilation.AssemblyName;
                var sourceCode = GenerateSourceCode(eventTypes.ToImmutable(), rootNamespace);
                ctx.AddSource("CommandExecutorExtension.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
            });

    }
    public ImmutableArray<CommandWithHandlerValues> GetCommandWithHandlerValues(
        Compilation compilation,
        ImmutableArray<SyntaxNode> types)
    {
        var iEventSymbol = compilation.GetTypeByMetadataName("Sekiban.Pure.ICommandWithHandler`2");
        if (iEventSymbol == null)
            return new ImmutableArray<CommandWithHandlerValues>();
        var eventTypes = ImmutableArray.CreateBuilder<CommandWithHandlerValues>();
        foreach (var typeSyntax in types)
        {
            var model = compilation.GetSemanticModel(typeSyntax.SyntaxTree);
            var typeSymbol = model.GetDeclaredSymbol(typeSyntax) as INamedTypeSymbol;
            var allInterfaces = typeSymbol.AllInterfaces.ToList();
            if (typeSymbol != null && typeSymbol.AllInterfaces.Any(m => m.OriginalDefinition.Name == iEventSymbol.Name))
            {
                var interfaceImplementation = typeSymbol.AllInterfaces.First(
                    m => m.OriginalDefinition is not null && m.OriginalDefinition.Name == iEventSymbol.Name);
                eventTypes.Add(
                    new CommandWithHandlerValues
                    {
                        InterfaceName = interfaceImplementation.Name,
                        RecordName = typeSymbol.ToDisplayString(),
                        TypeCount = interfaceImplementation.TypeArguments.Length,
                        Type1Name = interfaceImplementation.TypeArguments[0].ToDisplayString(),
                        Type2Name = interfaceImplementation.TypeArguments[1].ToDisplayString(),
                        Type3Name = interfaceImplementation.TypeArguments.Length > 2
                            ? interfaceImplementation.TypeArguments[2].ToDisplayString()
                            : string.Empty
                    });
            }
        }
        return eventTypes.ToImmutable();
    }

    public ImmutableArray<CommandWithHandlerValues> GetICommandWithHandlerInjectionValues(
        Compilation compilation,
        ImmutableArray<SyntaxNode> types)
    {
        var iEventSymbol = compilation.GetTypeByMetadataName("Sekiban.Pure.ICommandWithHandlerInjection`3");
        if (iEventSymbol == null)
            return new ImmutableArray<CommandWithHandlerValues>();
        var eventTypes = ImmutableArray.CreateBuilder<CommandWithHandlerValues>();
        foreach (var typeSyntax in types)
        {
            var model = compilation.GetSemanticModel(typeSyntax.SyntaxTree);
            var typeSymbol = model.GetDeclaredSymbol(typeSyntax) as INamedTypeSymbol;
            var allInterfaces = typeSymbol.AllInterfaces.ToList();
            if (typeSymbol != null && typeSymbol.AllInterfaces.Any(m => m.OriginalDefinition == iEventSymbol))
            {
                var interfaceImplementation = typeSymbol.AllInterfaces.First(m => m.OriginalDefinition == iEventSymbol);
                var arg1 = interfaceImplementation.TypeArguments[0].ToDisplayString();
                var arg2 = interfaceImplementation.TypeArguments[1].ToDisplayString();
                var arg3 = interfaceImplementation.TypeArguments[2].ToDisplayString();
                var toadd = new CommandWithHandlerValues
                {
                    InterfaceName = interfaceImplementation.Name,
                    RecordName = typeSymbol.ToDisplayString(),
                    TypeCount = interfaceImplementation.TypeArguments.Length,
                    Type1Name = arg1,
                    Type2Name = arg2,
                    Type3Name = arg3
                };
                eventTypes.Add(toadd);
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
        sb.AppendLine("    public static class DomainExecutorExtensions");
        sb.AppendLine("    {");

        foreach (var type in eventTypes)
        {
            switch (type.InterfaceName, type.TypeCount)
            {
                case ("ICommandWithHandler", 2):
                    sb.AppendLine(
                        $"        public static Task<ResultBox<CommandResponse>> Execute(this CommandExecutor executor, {type.RecordName} command) =>");
                    sb.AppendLine("            executor.ExecuteWithFunction(");
                    sb.AppendLine("                command,");
                    sb.AppendLine("                (command as ICommandGetProjector).GetProjector(),");
                    sb.AppendLine("                command.SpecifyPartitionKeys,");
                    sb.AppendLine("                command.Handle);");
                    sb.AppendLine();
                    break;
                case ("ICommandWithHandler", 3):
                    sb.AppendLine(
                        $"        public static Task<ResultBox<CommandResponse>> Execute(this CommandExecutor executor, {type.RecordName} command) =>");
                    sb.AppendLine(
                        "            executor.ExecuteWithFunction<" + $"{type.RecordName}, {type.Type3Name}>(");
                    sb.AppendLine("                command,");
                    sb.AppendLine("                (command as ICommandGetProjector).GetProjector(),");
                    sb.AppendLine("                command.SpecifyPartitionKeys,");
                    sb.AppendLine("                command.Handle);");
                    sb.AppendLine();
                    break;
                case ("ICommandWithHandlerInjection", 3):
                    sb.AppendLine(
                        $"        public static Task<ResultBox<CommandResponse>> Execute(this CommandExecutor executor, {type.RecordName} command, {type.RecordName}.Injection injection) =>");
                    sb.AppendLine(
                        "            executor.ExecuteWithFunction<" +
                        $"{type.RecordName}, {type.Type3Name}, IAggregatePayload>(");
                    sb.AppendLine("                command,");
                    sb.AppendLine("                (command as ICommandGetProjector).GetProjector(),");
                    sb.AppendLine("                command.SpecifyPartitionKeys,");
                    sb.AppendLine("                injection,");
                    sb.AppendLine("                command.Handle);");
                    sb.AppendLine();
                    break;
            }
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }
    public class CommandWithHandlerValues
    {
        public string InterfaceName { get; set; }
        public string RecordName { get; set; }
        public int TypeCount { get; set; }
        public string Type1Name { get; set; }
        public string Type2Name { get; set; }
        public string Type3Name { get; set; }
    }
}
