using Sekiban.EventSourcing.Validations;
using System.ComponentModel.DataAnnotations;
namespace Sekiban.EventSourcing.TestHelpers;

public class AggregateTestCommandExecutor
{
    private readonly List<AggregateBase> _aggregates = new();
    private readonly IServiceProvider _serviceProvider;
    public AggregateTestCommandExecutor(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public (IEnumerable<IAggregateEvent>, Guid) ExecuteCreateCommand<TAggregate>(ICreateAggregateCommand<TAggregate> command)
        where TAggregate : AggregateBase, new()
    {
        var validationResults = command.TryValidateProperties().ToList();
        if (validationResults.Any())
        {
            throw new ValidationException("Validation failed " + validationResults);
        }

        var baseType = typeof(ICreateAggregateCommandHandler<,>);
        var genericType = baseType.MakeGenericType(typeof(TAggregate), command.GetType());
        var handler = _serviceProvider.GetService(genericType);
        if (handler is null)
        {
            throw new SekibanAggregateCommandNotRegisteredException(command.GetType().Name);
        }
        var generateAggregateIdMethod = genericType.GetMethods().First(m => m.Name == "GenerateAggregateId");
        var aggregateId = generateAggregateIdMethod.Invoke(handler, new object?[] { command }) as Guid?;

        if (aggregateId is null) { throw new Exception("Create Aggregate Id failed"); }

        var aggregateCommandDocumentBaseType = typeof(AggregateCommandDocument<>);
        var aggregateCommandDocumentType = aggregateCommandDocumentBaseType.MakeGenericType(command.GetType());
        var commandDocument = Activator.CreateInstance(aggregateCommandDocumentType, aggregateId, command, typeof(TAggregate), null);
        var aggregate = new TAggregate { AggregateId = aggregateId.Value };
        var handlerType = handler.GetType().GetMethods();
        var handleAsyncMethod = handler.GetType().GetMethods().First(m => m.Name == "HandleAsync");
        var result = ((dynamic)handleAsyncMethod.Invoke(handler, new[] { commandDocument, aggregate })!)?.Result;
        if (result is null) { throw new Exception("Failed to execute create command"); }
        var aggregateResult = result.Aggregate;
        var latestEvents = (IReadOnlyCollection<IAggregateEvent>)aggregateResult.Events;
        if (latestEvents.Count == 0)
        {
            throw new SekibanCreateHasToMakeEventException();
        }
        if (latestEvents.Any(
            ev => (ev == latestEvents.First() && !ev.IsAggregateInitialEvent) || (ev != latestEvents.First() && ev.IsAggregateInitialEvent)))
        {
            throw new SekibanCreateCommandShouldSaveCreateEventFirstException();
        }
        _aggregates.Add(aggregateResult);
        return (latestEvents, aggregateId.Value);
    }

    public IEnumerable<IAggregateEvent> ExecuteChangeCommand<TAggregate>(ChangeAggregateCommandBase<TAggregate> command)
        where TAggregate : AggregateBase, new()
    {
        var validationResults = command.TryValidateProperties().ToList();
        if (validationResults.Any())
        {
            throw new ValidationException("Validation failed " + validationResults);
        }

        var baseType = typeof(IChangeAggregateCommandHandler<,>);
        var genericType = baseType.MakeGenericType(typeof(TAggregate), command.GetType());
        var handler = _serviceProvider.GetService(genericType);
        if (handler is null)
        {
            throw new SekibanAggregateCommandNotRegisteredException(command.GetType().Name);
        }
        var aggregateId = command.GetAggregateId();
        var aggregate = _aggregates.FirstOrDefault(a => a.GetType().FullName == typeof(TAggregate).FullName && a.AggregateId == aggregateId);

        var aggregateCommandDocumentBaseType = typeof(AggregateCommandDocument<>);
        var aggregateCommandDocumentType = aggregateCommandDocumentBaseType.MakeGenericType(command.GetType());
        var commandDocument = Activator.CreateInstance(aggregateCommandDocumentType, command, typeof(TAggregate));

        var handleAsyncMethod = handler.GetType().GetMethods().First(m => m.Name == "HandleAsync");
        var result = ((dynamic)handleAsyncMethod.Invoke(handler, new[] { commandDocument, aggregate })!)?.Result;
        if (result is null) { throw new Exception("Failed to execute change command"); }
        var aggregateResult = result.Aggregate;
        var latestEvents = (IList<IAggregateEvent>)aggregateResult.Events.ToList();
        if (latestEvents.Any(ev => ev.IsAggregateInitialEvent))
        {
            throw new SekibanChangeCommandShouldNotSaveCreateEventException();
        }
        return latestEvents;
    }
}
