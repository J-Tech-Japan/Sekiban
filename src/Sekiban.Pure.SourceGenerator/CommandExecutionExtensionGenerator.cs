using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
namespace Sekiban.Pure.SourceGenerator;

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
                var commandTypes = ImmutableArray.CreateBuilder<CommandWithHandlerValues>();

                commandTypes.AddRange(GetCommandWithHandlerValues(compilation, types));
                commandTypes.AddRange(GetICommandWithHandlerInjectionValues(compilation, types));
                commandTypes.AddRange(GetCommandValues(compilation, types, commandTypes.ToImmutable()));

                // Generate source code
                var rootNamespace = compilation.AssemblyName;
                var sourceCode = GenerateSourceCode(commandTypes.ToImmutable(), rootNamespace);
                ctx.AddSource("CommandExecutorExtension.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
            });

    }
    public ImmutableArray<CommandWithHandlerValues> GetCommandWithHandlerValues(
        Compilation compilation,
        ImmutableArray<SyntaxNode> types)
    {
        var iCommmandWithHandlerSymbol = compilation.GetTypeByMetadataName("Sekiban.Pure.ICommandWithHandler`2");
        if (iCommmandWithHandlerSymbol == null)
            return new ImmutableArray<CommandWithHandlerValues>();
        var eventTypes = ImmutableArray.CreateBuilder<CommandWithHandlerValues>();
        foreach (var typeSyntax in types)
        {
            var model = compilation.GetSemanticModel(typeSyntax.SyntaxTree);
            var typeSymbol = model.GetDeclaredSymbol(typeSyntax) as INamedTypeSymbol;
            var allInterfaces = typeSymbol.AllInterfaces.ToList();
            if (typeSymbol != null &&
                typeSymbol.AllInterfaces.Any(m => m.OriginalDefinition.Name == iCommmandWithHandlerSymbol.Name))
            {
                var interfaceImplementation = typeSymbol.AllInterfaces.First(
                    m => m.OriginalDefinition is not null &&
                        m.OriginalDefinition.Name == iCommmandWithHandlerSymbol.Name);
                eventTypes.Add(
                    new CommandWithHandlerValues
                    {
                        InterfaceName = interfaceImplementation.Name,
                        RecordName = typeSymbol.ToDisplayString(),
                        TypeCount = interfaceImplementation.TypeArguments.Length,
                        Type1Name = interfaceImplementation.TypeArguments[0].ToDisplayString(),
                        Type2Name = interfaceImplementation.TypeArguments[1].ToDisplayString(),
                        AggregatePayloadTypeName = interfaceImplementation.TypeArguments.Length > 2
                            ? interfaceImplementation.TypeArguments[2].ToDisplayString()
                            : string.Empty
                    });
            }
        }
        return eventTypes.ToImmutable();
    }



    public ImmutableArray<CommandWithHandlerValues> GetCommandValues(
        Compilation compilation,
        ImmutableArray<SyntaxNode> types,
        ImmutableArray<CommandWithHandlerValues> alreadyFoundCommands)
    {
        var iCommandSymbol = compilation.GetTypeByMetadataName("Sekiban.Pure.ICommand");
        var iCommandWithAggregateRestrictionSymbol
            = compilation.GetTypeByMetadataName("Sekiban.Pure.ICommandWithAggregateRestriction`1");
        if (iCommandSymbol == null || iCommandWithAggregateRestrictionSymbol == null)
            return new ImmutableArray<CommandWithHandlerValues>();
        var eventTypes = ImmutableArray.CreateBuilder<CommandWithHandlerValues>();
        foreach (var typeSyntax in types)
        {
            var model = compilation.GetSemanticModel(typeSyntax.SyntaxTree);
            var typeSymbol = model.GetDeclaredSymbol(typeSyntax) as INamedTypeSymbol;
            if (typeSymbol == null)
            {
                continue;
            }
            if (alreadyFoundCommands.Any(m => m.RecordName == typeSymbol.ToDisplayString()))
            {
                continue;
            }
            var allInterfaces = typeSymbol.AllInterfaces.ToList();
            if (typeSymbol != null &&
                typeSymbol.AllInterfaces.Any(m => m.OriginalDefinition.Name == iCommandSymbol.Name))
            {
                var interfaceImplementationAggregate = typeSymbol.AllInterfaces.FirstOrDefault(
                    m => m.OriginalDefinition.Name == iCommandWithAggregateRestrictionSymbol.Name);

                var interfaceImplementation = typeSymbol.AllInterfaces.First(
                    m => m.OriginalDefinition.Name == iCommandSymbol.Name);
                eventTypes.Add(
                    new CommandWithHandlerValues
                    {
                        InterfaceName = interfaceImplementation.Name,
                        RecordName = typeSymbol.ToDisplayString(),
                        TypeCount = interfaceImplementationAggregate?.TypeArguments.Length ?? 0,
                        AggregatePayloadTypeName
                            = interfaceImplementationAggregate?.TypeArguments[0].ToDisplayString() ?? string.Empty
                    });
            }
        }
        return eventTypes.ToImmutable();
    }



    public ImmutableArray<CommandWithHandlerValues> GetICommandWithHandlerInjectionValues(
        Compilation compilation,
        ImmutableArray<SyntaxNode> types)
    {
        var iCommandWithHandlerSymbol
            = compilation.GetTypeByMetadataName("Sekiban.Pure.ICommandWithHandlerInjection`3");
        if (iCommandWithHandlerSymbol == null)
            return new ImmutableArray<CommandWithHandlerValues>();
        var eventTypes = ImmutableArray.CreateBuilder<CommandWithHandlerValues>();
        foreach (var typeSyntax in types)
        {
            var model = compilation.GetSemanticModel(typeSyntax.SyntaxTree);
            var typeSymbol = model.GetDeclaredSymbol(typeSyntax) as INamedTypeSymbol;
            var allInterfaces = typeSymbol.AllInterfaces.ToList();
            if (typeSymbol != null &&
                typeSymbol.AllInterfaces.Any(m => m.OriginalDefinition.Name == iCommandWithHandlerSymbol.Name))
            {
                var interfaceImplementation = typeSymbol.AllInterfaces.First(
                    m => m.OriginalDefinition is not null &&
                        m.OriginalDefinition.Name == iCommandWithHandlerSymbol.Name);
                var toadd = new CommandWithHandlerValues
                {
                    InterfaceName = interfaceImplementation.Name,
                    RecordName = typeSymbol.ToDisplayString(),
                    TypeCount = interfaceImplementation.TypeArguments.Length,
                    Type1Name = interfaceImplementation.TypeArguments[0].ToDisplayString(),
                    Type2Name = interfaceImplementation.TypeArguments[1].ToDisplayString(),
                    InjectTypeName = interfaceImplementation.TypeArguments[2].ToDisplayString(),
                    AggregatePayloadTypeName = interfaceImplementation.TypeArguments.Length > 3
                        ? interfaceImplementation.TypeArguments[3].ToDisplayString()
                        : string.Empty
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
                    sb.AppendLine("            executor.ExecuteFunctionWithoutAggregateRestriction(");
                    sb.AppendLine("                command,");
                    sb.AppendLine("                (command as ICommandGetProjector).GetProjector(),");
                    sb.AppendLine("                command.SpecifyPartitionKeys,");
                    sb.AppendLine("                command.Handle);");
                    sb.AppendLine();
                    // add this too
                    sb.AppendLine(
                        $"        public static Task<ResultBox<CommandResponse>> ExecuteFunction(this CommandExecutor executor, {type.RecordName} command,");
                    sb.AppendLine("                IAggregateProjector projector,");
                    sb.AppendLine($"                Func<{type.RecordName}, PartitionKeys> specifyPartitionKeys,");
                    sb.AppendLine(
                        $"                Func<{type.RecordName}, ICommandContext<IAggregatePayload>, ResultBox<EventOrNone>> handler) =>");
                    sb.AppendLine("            executor.ExecuteFunctionWithoutAggregateRestriction(");
                    sb.AppendLine("                command,");
                    sb.AppendLine("                projector,");
                    sb.AppendLine("                specifyPartitionKeys,");
                    sb.AppendLine("                handler);");
                    sb.AppendLine();
                    break;
                case ("ICommandWithHandler", 3):
                    sb.AppendLine(
                        $"        public static Task<ResultBox<CommandResponse>> Execute(this CommandExecutor executor, {type.RecordName} command) =>");
                    sb.AppendLine(
                        "            executor.ExecuteFunctionWithAggregateRestriction<" +
                        $"{type.RecordName}, {type.AggregatePayloadTypeName}>(");
                    sb.AppendLine("                command,");
                    sb.AppendLine("                (command as ICommandGetProjector).GetProjector(),");
                    sb.AppendLine("                command.SpecifyPartitionKeys,");
                    sb.AppendLine("                command.Handle);");
                    sb.AppendLine();

                    // add this too
                    sb.AppendLine(
                        $"        public static Task<ResultBox<CommandResponse>> ExecuteFunction(this CommandExecutor executor, {type.RecordName} command,");
                    sb.AppendLine("                IAggregateProjector projector,");
                    sb.AppendLine($"                Func<{type.RecordName}, PartitionKeys> specifyPartitionKeys,");
                    sb.AppendLine(
                        $"                Func<{type.RecordName}, ICommandContext<{type.AggregatePayloadTypeName}>, ResultBox<EventOrNone>> handler) =>");
                    sb.AppendLine(
                        "            executor.ExecuteFunctionWithAggregateRestriction<" +
                        $"{type.RecordName}, {type.AggregatePayloadTypeName}>(");
                    sb.AppendLine("                command,");
                    sb.AppendLine("                projector,");
                    sb.AppendLine("                specifyPartitionKeys,");
                    sb.AppendLine("                handler);");
                    sb.AppendLine();



                    break;
                case ("ICommandWithHandlerInjection", 3):
                    sb.AppendLine(
                        $"        public static Task<ResultBox<CommandResponse>> Execute(this CommandExecutor executor, {type.RecordName} command, {type.InjectTypeName} injection) =>");
                    sb.AppendLine(
                        "            executor.ExecuteFunctionWithInjectionWithoutAggregateRestriction<" +
                        $"{type.RecordName}, {type.InjectTypeName}>(");
                    sb.AppendLine("                command,");
                    sb.AppendLine("                (command as ICommandGetProjector).GetProjector(),");
                    sb.AppendLine("                command.SpecifyPartitionKeys,");
                    sb.AppendLine("                injection,");
                    sb.AppendLine("                command.Handle);");
                    sb.AppendLine();

                    // add this too
                    sb.AppendLine(
                        $"        public static Task<ResultBox<CommandResponse>> ExecuteFunction(this CommandExecutor executor, {type.RecordName} command,");
                    sb.AppendLine("                IAggregateProjector projector,");
                    sb.AppendLine($"                Func<{type.RecordName}, PartitionKeys> specifyPartitionKeys,");
                    sb.AppendLine($"                {type.InjectTypeName} injection,");
                    sb.AppendLine(
                        $"                Func<{type.RecordName}, {type.InjectTypeName}, ICommandContext<IAggregatePayload>, ResultBox<EventOrNone>> handler) =>");
                    sb.AppendLine(
                        "            executor.ExecuteFunctionWithInjectionWithoutAggregateRestriction<" +
                        $"{type.RecordName}, {type.InjectTypeName}>(");
                    sb.AppendLine("                command,");
                    sb.AppendLine("                projector,");
                    sb.AppendLine("                specifyPartitionKeys,");
                    sb.AppendLine("                injection,");
                    sb.AppendLine("                handler);");
                    sb.AppendLine();
                    break;
                case ("ICommandWithHandlerInjection", 4):
                    sb.AppendLine(
                        $"        public static Task<ResultBox<CommandResponse>> Execute(this CommandExecutor executor, {type.RecordName} command, {type.InjectTypeName} injection) =>");
                    sb.AppendLine(
                        "            executor.ExecuteFunctionWithInjectionWithAggregateRestriction<" +
                        $"{type.RecordName}, {type.InjectTypeName}, {type.AggregatePayloadTypeName}>(");
                    sb.AppendLine("                command,");
                    sb.AppendLine("                (command as ICommandGetProjector).GetProjector(),");
                    sb.AppendLine("                command.SpecifyPartitionKeys,");
                    sb.AppendLine("                injection,");
                    sb.AppendLine("                command.Handle);");
                    sb.AppendLine();

                    // add this too
                    sb.AppendLine(
                        $"        public static Task<ResultBox<CommandResponse>> ExecuteFunction(this CommandExecutor executor, {type.RecordName} command,");
                    sb.AppendLine("                IAggregateProjector projector,");
                    sb.AppendLine($"                Func<{type.RecordName}, PartitionKeys> specifyPartitionKeys,");
                    sb.AppendLine($"                {type.InjectTypeName} injection,");
                    sb.AppendLine(
                        $"                Func<{type.RecordName}, {type.InjectTypeName}, ICommandContext<{type.AggregatePayloadTypeName}>, ResultBox<EventOrNone>> handler) =>");
                    sb.AppendLine(
                        $"            executor.ExecuteFunctionWithInjectionWithAggregateRestriction<{type.RecordName}, {type.InjectTypeName}, {type.AggregatePayloadTypeName}>(");
                    sb.AppendLine("                command,");
                    sb.AppendLine("                projector,");
                    sb.AppendLine("                specifyPartitionKeys,");
                    sb.AppendLine("                injection,");
                    sb.AppendLine("                handler);");
                    sb.AppendLine();
                    break;
                case ("ICommand", 0):
                    sb.AppendLine(
                        $"        public static Task<ResultBox<CommandResponse>> ExecuteFunction(this CommandExecutor executor, {type.RecordName} command,");
                    sb.AppendLine("                IAggregateProjector projector,");
                    sb.AppendLine($"                Func<{type.RecordName}, PartitionKeys> specifyPartitionKeys,");
                    sb.AppendLine(
                        $"                Func<{type.RecordName},  ICommandContext<IAggregatePayload>, ResultBox<EventOrNone>> handler) =>");
                    sb.AppendLine(
                        "            executor.ExecuteFunctionWithoutAggregateRestriction<" + $"{type.RecordName}>(");
                    sb.AppendLine("                command,");
                    sb.AppendLine("                projector,");
                    sb.AppendLine("                specifyPartitionKeys,");
                    sb.AppendLine("                handler);");
                    sb.AppendLine();

                    break;
                case ("ICommand", 1):
                    sb.AppendLine(
                        $"        public static Task<ResultBox<CommandResponse>> ExecuteFunction(this CommandExecutor executor, {type.RecordName} command,");
                    sb.AppendLine("                IAggregateProjector projector,");
                    sb.AppendLine($"                Func<{type.RecordName}, PartitionKeys> specifyPartitionKeys,");
                    sb.AppendLine(
                        $"                Func<{type.RecordName},  ICommandContext<{type.AggregatePayloadTypeName}>, ResultBox<EventOrNone>> handler) =>");
                    sb.AppendLine(
                        $"            executor.ExecuteFunctionWithAggregateRestriction<{type.RecordName}, {type.AggregatePayloadTypeName}>(");
                    sb.AppendLine("                command,");
                    sb.AppendLine("                projector,");
                    sb.AppendLine("                specifyPartitionKeys,");
                    sb.AppendLine("                handler);");
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
        public string InjectTypeName { get; set; }
        public string AggregatePayloadTypeName { get; set; }
    }
}