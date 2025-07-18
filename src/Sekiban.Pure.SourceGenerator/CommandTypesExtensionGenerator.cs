using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Text;
namespace Sekiban.Pure.SourceGenerator;

[Generator]
public class CommandTypesExtensionGenerator : IIncrementalGenerator
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
                commandTypes.AddRange(GetCommandWithHandlerAsyncValues(compilation, types));
                commandTypes.AddRange(GetCommandValues(compilation, types, commandTypes.ToImmutable()));

                // Generate source code
                var rootNamespace = compilation.AssemblyName ?? throw new Exception();
                var commandTypesSource = GenerateCommandTypesSourceCode(commandTypes.ToImmutable(), rootNamespace);
                ctx.AddSource("CommandTypes.g.cs", SourceText.From(commandTypesSource, Encoding.UTF8));
            });

    }
    public ImmutableArray<CommandWithHandlerValues> GetCommandWithHandlerValues(
        Compilation compilation,
        ImmutableArray<SyntaxNode> types)
    {
        var iCommmandWithHandlerSymbol
            = compilation.GetTypeByMetadataName("Sekiban.Pure.Command.Handlers.ICommandWithHandler`2");
        if (iCommmandWithHandlerSymbol == null)
            return new ImmutableArray<CommandWithHandlerValues>();
        var eventTypes = ImmutableArray.CreateBuilder<CommandWithHandlerValues>();
        foreach (var typeSyntax in types)
        {
            var model = compilation.GetSemanticModel(typeSyntax.SyntaxTree);
            var typeSymbol = model.GetDeclaredSymbol(typeSyntax) as INamedTypeSymbol ?? throw new Exception();
            var allInterfaces = typeSymbol.AllInterfaces.ToList();
            if (typeSymbol.AllInterfaces.Any(m => m.OriginalDefinition.Name == iCommmandWithHandlerSymbol.Name))
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

    public ImmutableArray<CommandWithHandlerValues> GetCommandWithHandlerAsyncValues(
        Compilation compilation,
        ImmutableArray<SyntaxNode> types)
    {
        var iCommmandWithHandlerSymbol
            = compilation.GetTypeByMetadataName("Sekiban.Pure.Command.Handlers.ICommandWithHandlerAsync`2");
        if (iCommmandWithHandlerSymbol == null)
            return new ImmutableArray<CommandWithHandlerValues>();
        var eventTypes = ImmutableArray.CreateBuilder<CommandWithHandlerValues>();
        foreach (var typeSyntax in types)
        {
            var model = compilation.GetSemanticModel(typeSyntax.SyntaxTree);
            var typeSymbol = model.GetDeclaredSymbol(typeSyntax) as INamedTypeSymbol ?? throw new Exception();
            var allInterfaces = typeSymbol.AllInterfaces.ToList();
            if (typeSymbol.AllInterfaces.Any(m => m.OriginalDefinition.Name == iCommmandWithHandlerSymbol.Name))
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
        var iCommandSymbol = compilation.GetTypeByMetadataName("Sekiban.Pure.Command.Handlers.ICommand");
        var iCommandWithAggregateRestrictionSymbol
            = compilation.GetTypeByMetadataName("Sekiban.Pure.Command.Handlers.ICommandWithAggregateRestriction`1");
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




    private string GenerateCommandTypesSourceCode(
        ImmutableArray<CommandWithHandlerValues> commandTypes,
        string rootNamespace)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// Auto-generated by IncrementalGenerator");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using ResultBoxes;");
        sb.AppendLine("using Sekiban.Pure.Aggregates;");
        sb.AppendLine("using Sekiban.Pure.Command;");
        sb.AppendLine("using Sekiban.Pure.Command.Executor;");
        sb.AppendLine("using Sekiban.Pure.Command.Handlers;");
        sb.AppendLine("using Sekiban.Pure.Documents;");
        sb.AppendLine("using Sekiban.Pure.Events;");
        sb.AppendLine("using Sekiban.Pure.Projectors;");
        sb.AppendLine();
        sb.AppendLine($"namespace {rootNamespace}.Generated");
        sb.AppendLine("{");
        sb.AppendLine($"    public class {rootNamespace.Replace(".", "")}CommandTypes : ICommandTypes");
        sb.AppendLine("    {");
        sb.AppendLine("        public Task<ResultBox<CommandResponse>> ExecuteGeneral(");
        sb.AppendLine("            CommandExecutor executor,");
        sb.AppendLine("            ICommandWithHandlerSerializable command,");
        sb.AppendLine("            PartitionKeys partitionKeys,");
        sb.AppendLine("            CommandMetadata commandMetadata,");
        sb.AppendLine("            Func<PartitionKeys, IAggregateProjector, Task<ResultBox<Aggregate>>> loader,");
        sb.AppendLine("            Func<string, List<IEvent>, Task<ResultBox<List<IEvent>>>> saver) =>");
        sb.AppendLine("            command switch");
        sb.AppendLine("            {");

        foreach (var type in commandTypes)
        {
            var shortCommandName = type.RecordName.Split('.').Last();
            switch (type.InterfaceName)
            {
                case "ICommandWithHandler" when type.TypeCount == 2:
                    sb.AppendLine($"                {type.RecordName} {shortCommandName} => ");
                    sb.AppendLine($"executor.ExecuteGeneralWithPartitionKeys<{type.RecordName}, IAggregatePayload>(");
                    sb.AppendLine($"                    {shortCommandName},");
                    sb.AppendLine("                    command.GetProjector(),");
                    sb.AppendLine("                    partitionKeys,");
                    sb.AppendLine($"                    {shortCommandName}.Handle,");
                    sb.AppendLine("                    commandMetadata,");
                    sb.AppendLine("                    loader,");
                    sb.AppendLine("                    saver),");
                    break;

                case "ICommandWithHandler" when type.TypeCount == 3:
                    sb.AppendLine($"                {type.RecordName} {shortCommandName} => ");
                    sb.AppendLine(
                        $"executor.ExecuteGeneralWithPartitionKeys<{type.RecordName}, {type.AggregatePayloadTypeName}>(");
                    sb.AppendLine($"                    {shortCommandName},");
                    sb.AppendLine("                    command.GetProjector(),");
                    sb.AppendLine("                    partitionKeys,");
                    sb.AppendLine($"                    {shortCommandName}.Handle,");
                    sb.AppendLine("                    commandMetadata,");
                    sb.AppendLine("                    loader,");
                    sb.AppendLine("                    saver),");
                    break;

                case "ICommandWithHandlerAsync" when type.TypeCount == 2:
                    sb.AppendLine($"                {type.RecordName} {shortCommandName} => ");
                    sb.AppendLine($"executor.ExecuteGeneralWithPartitionKeys<{type.RecordName}, IAggregatePayload>(");
                    sb.AppendLine($"                    {shortCommandName},");
                    sb.AppendLine("                    command.GetProjector(),");
                    sb.AppendLine("                    partitionKeys,");
                    sb.AppendLine($"                    {shortCommandName}.HandleAsync,");
                    sb.AppendLine("                    commandMetadata,");
                    sb.AppendLine("                    loader,");
                    sb.AppendLine("                    saver),");
                    break;

                case "ICommandWithHandlerAsync" when type.TypeCount == 3:
                    sb.AppendLine($"                {type.RecordName} {shortCommandName} => ");
                    sb.AppendLine(
                        $"executor.ExecuteGeneralWithPartitionKeys<{type.RecordName}, {type.AggregatePayloadTypeName}>(");
                    sb.AppendLine($"                    {shortCommandName},");
                    sb.AppendLine("                    command.GetProjector(),");
                    sb.AppendLine("                    partitionKeys,");
                    sb.AppendLine($"                    {shortCommandName}.HandleAsync,");
                    sb.AppendLine("                    commandMetadata,");
                    sb.AppendLine("                    loader,");
                    sb.AppendLine("                    saver),");
                    break;

            }
        }

        sb.AppendLine(
            "                _ => Task.FromResult(ResultBox<CommandResponse>.Error(new ApplicationException(\"Unknown command type\")))");
        sb.AppendLine("            };");

        sb.AppendLine("        public List<Type> GetCommandTypes()");
        sb.AppendLine("        {");
        sb.AppendLine("            return new List<Type>");
        sb.AppendLine("            {");
        foreach (var type in commandTypes)
        {
            sb.AppendLine($"                typeof({type.RecordName}),");
        }
        sb.AppendLine("            };");
        sb.AppendLine("        }");
        
        // Add GetCommandTypeByName implementation
        sb.AppendLine();
        sb.AppendLine("        public Type? GetCommandTypeByName(string commandTypeName)");
        sb.AppendLine("        {");
        sb.AppendLine("            return commandTypeName switch");
        sb.AppendLine("            {");
        
        // Generate case statements for each command type
        foreach (var type in commandTypes)
        {
            // Get the short name (without namespace)
            var shortName = type.RecordName.Split('.').Last();
            sb.AppendLine($"                \"{shortName}\" => typeof({type.RecordName}),");
        }
        
        sb.AppendLine("                _ => null");
        sb.AppendLine("            };");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }
    public class CommandWithHandlerValues
    {
        public string InterfaceName { get; set; } = string.Empty;
        public string RecordName { get; set; } = string.Empty;
        public int TypeCount { get; set; }
        public string Type1Name { get; set; } = string.Empty;
        public string Type2Name { get; set; } = string.Empty;
        public string AggregatePayloadTypeName { get; set; } = string.Empty;
    }
}
