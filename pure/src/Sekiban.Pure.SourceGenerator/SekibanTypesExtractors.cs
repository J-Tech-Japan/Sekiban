using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
namespace Sekiban.Pure.SourceGenerator;

public static class SekibanTypesExtractors
{
    public static ImmutableArray<EventTypeValues> GetEventValues(
        Compilation compilation,
        ImmutableArray<SyntaxNode> types)
    {
        var iEventPayloadSymbol = compilation.GetTypeByMetadataName("Sekiban.Pure.Events.IEventPayload");
        if (iEventPayloadSymbol == null)
            return new ImmutableArray<EventTypeValues>();
        var eventTypes = ImmutableArray.CreateBuilder<EventTypeValues>();
        foreach (var typeSyntax in types)
        {
            var model = compilation.GetSemanticModel(typeSyntax.SyntaxTree);
            var typeSymbol = model.GetDeclaredSymbol(typeSyntax) as INamedTypeSymbol ??
                throw new ApplicationException("TypeSymbol is null");
            var allInterfaces = typeSymbol.AllInterfaces.ToList();
            if (typeSymbol.AllInterfaces.Any(m => m.Equals(iEventPayloadSymbol, SymbolEqualityComparer.Default)))
            {
                var interfaceImplementation = typeSymbol.AllInterfaces.First(m => m.Equals(
                    iEventPayloadSymbol,
                    SymbolEqualityComparer.Default));
                eventTypes.Add(
                    new EventTypeValues
                    {
                        InterfaceName = interfaceImplementation.Name,
                        RecordName = typeSymbol.ToDisplayString()
                    });
            }
        }
        return eventTypes.ToImmutable();
    }

    public static ImmutableArray<AggregateTypesValues> GetAggregateTypeValues(
        Compilation compilation,
        ImmutableArray<SyntaxNode> types)
    {
        var iEventPayloadSymbol = compilation.GetTypeByMetadataName("Sekiban.Pure.Aggregates.IAggregatePayload");
        if (iEventPayloadSymbol == null)
            return new ImmutableArray<AggregateTypesValues>();
        var eventTypes = ImmutableArray.CreateBuilder<AggregateTypesValues>();
        foreach (var typeSyntax in types)
        {
            var model = compilation.GetSemanticModel(typeSyntax.SyntaxTree);
            var typeSymbol = model.GetDeclaredSymbol(typeSyntax) as INamedTypeSymbol ??
                throw new ApplicationException("TypeSymbol is null");
            var allInterfaces = typeSymbol.AllInterfaces.ToList();
            if (typeSymbol.AllInterfaces.Any(m => m.Equals(iEventPayloadSymbol, SymbolEqualityComparer.Default)))
            {
                var interfaceImplementation = typeSymbol.AllInterfaces.First(m => m.Equals(
                    iEventPayloadSymbol,
                    SymbolEqualityComparer.Default));
                eventTypes.Add(
                    new AggregateTypesValues
                    {
                        InterfaceName = interfaceImplementation.Name,
                        RecordName = typeSymbol.ToDisplayString()
                    });
            }
        }
        return eventTypes.ToImmutable();
    }
    public static ImmutableArray<MultiProjectorValue> GetMultiProjectorValues(
        Compilation compilation,
        ImmutableArray<SyntaxNode> types)
    {
        var iMultiProjectorCommonSymbol
            = compilation.GetTypeByMetadataName("Sekiban.Pure.Projectors.IMultiProjectorCommon");
        if (iMultiProjectorCommonSymbol == null)
            return new ImmutableArray<MultiProjectorValue>();
        var multiProjectorTypes = ImmutableArray.CreateBuilder<MultiProjectorValue>();
        foreach (var typeSyntax in types)
        {
            var model = compilation.GetSemanticModel(typeSyntax.SyntaxTree);
            var typeSymbol = model.GetDeclaredSymbol(typeSyntax) as INamedTypeSymbol ??
                throw new ApplicationException("TypeSymbol is null");
            var allInterfaces = typeSymbol.AllInterfaces.ToList();
            if (typeSymbol.AllInterfaces.Any(m => m.Equals(
                iMultiProjectorCommonSymbol,
                SymbolEqualityComparer.Default)))
            {
                var interfaceImplementation = typeSymbol.AllInterfaces.First(m => m.Equals(
                    iMultiProjectorCommonSymbol,
                    SymbolEqualityComparer.Default));
                multiProjectorTypes.Add(
                    new MultiProjectorValue
                    {
                        TypeName = typeSymbol.ToDisplayString()
                    });
            }
        }

        return multiProjectorTypes.ToImmutable();
    }
    public class EventTypeValues
    {
        public string InterfaceName { get; set; } = string.Empty;
        public string RecordName { get; set; } = string.Empty;
        public int TypeCount { get; set; }
    }

    public class AggregateTypesValues
    {
        public string InterfaceName { get; set; } = string.Empty;
        public string RecordName { get; set; } = string.Empty;
        public int TypeCount { get; set; }
    }

    public class MultiProjectorValue
    {
        public string TypeName { get; set; } = string.Empty;
    }
}
