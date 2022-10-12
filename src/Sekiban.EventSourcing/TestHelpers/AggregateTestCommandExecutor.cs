using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.Queries.SingleAggregates;
using Sekiban.EventSourcing.Validations;
using System.ComponentModel.DataAnnotations;
namespace Sekiban.EventSourcing.TestHelpers;

public class AggregateTestCommandExecutor
{
    private readonly IServiceProvider _serviceProvider;
    public IReadOnlyCollection<IAggregateEvent> LatestEvents { get; set; } = new List<IAggregateEvent>();
    public AggregateTestCommandExecutor(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public (IEnumerable<IAggregateEvent>, Guid) ExecuteCreateCommand<TAggregate>(
        ICreateAggregateCommand<TAggregate> command,
        Guid? injectingAggregateId = null) where TAggregate : AggregateBase, new()
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
        var aggregateId = injectingAggregateId ?? generateAggregateIdMethod.Invoke(handler, new object?[] { command }) as Guid?;

        if (aggregateId is null) { throw new Exception("Create Aggregate Id failed"); }

        var aggregateCommandDocumentBaseType = typeof(AggregateCommandDocument<>);
        var aggregateCommandDocumentType = aggregateCommandDocumentBaseType.MakeGenericType(command.GetType());
        var commandDocument = Activator.CreateInstance(aggregateCommandDocumentType, aggregateId, command, typeof(TAggregate), null);
        var aggregate = new TAggregate { AggregateId = aggregateId.Value };
        var handlerType = handler.GetType().GetMethods();
        var handleAsyncMethod = handler.GetType().GetMethods().First(m => m.Name == "HandleAsync");
        var result = ((dynamic)handleAsyncMethod.Invoke(handler, new[] { commandDocument, aggregate })!)?.Result;
        if (result is null) { throw new Exception("Failed to execute create command"); }
        var latestEvents = (IReadOnlyCollection<IAggregateEvent>)result.Events;
        if (latestEvents.Count == 0)
        {
            throw new SekibanCreateHasToMakeEventException();
        }
        if (latestEvents.Any(
            ev => (ev == latestEvents.First() && !ev.IsAggregateInitialEvent) || (ev != latestEvents.First() && ev.IsAggregateInitialEvent)))
        {
            throw new SekibanCreateCommandShouldSaveCreateEventFirstException();
        }
        var documentWriter = _serviceProvider.GetRequiredService(typeof(IDocumentWriter)) as IDocumentWriter;
        if (documentWriter is null) { throw new Exception("Failed to get document writer"); }
        foreach (var e in latestEvents)
        {
            documentWriter.SaveAsync(e, typeof(TAggregate)).Wait();
        }
        LatestEvents = latestEvents;
        return (latestEvents, aggregateId.Value);
    }

    private TAggregate GetAggregate<TAggregate>(Guid aggregateId) where TAggregate : AggregateBase, new()
    {

        var singleAggregateService = _serviceProvider.GetRequiredService(typeof(ISingleAggregateService)) as ISingleAggregateService;
        if (singleAggregateService is null) { throw new Exception("Failed to get Aggregate Service"); }
        var method = singleAggregateService.GetType().GetMethods().FirstOrDefault(m => m.Name == "GetAggregateAsync");
        if (method is null) { throw new Exception("Failed to get Aggregate Service"); }
        var aggregateBaseType = typeof(TAggregate).BaseType;
        if (aggregateBaseType?.DeclaringType != typeof(TransferableAggregateBase<>)) { throw new Exception("Failed to get Aggregate Service"); }
        var contentsType = aggregateBaseType.GetGenericArguments()[0];
        var genericMethod = method.MakeGenericMethod(typeof(TAggregate), contentsType);
        var aggregate = genericMethod.Invoke(singleAggregateService, new object?[] { aggregateId }) as TAggregate;
        return aggregate ?? throw new SekibanAggregateNotExistsException(aggregateId, typeof(TAggregate).Name);
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

        if (command is not IOnlyPublishingCommand)
        {
            var aggregate = GetAggregate<TAggregate>(aggregateId);
            if (aggregate is null)
            {
                throw new SekibanAggregateNotExistsException(aggregateId, typeof(TAggregate).Name);
            }
            aggregate.ResetEventsAndSnapshots();
            var aggregateCommandDocumentBaseType = typeof(AggregateCommandDocument<>);
            var aggregateCommandDocumentType = aggregateCommandDocumentBaseType.MakeGenericType(command.GetType());
            var commandToSend = command with { ReferenceVersion = aggregate?.Version ?? 0 };
            var commandDocument = Activator.CreateInstance(aggregateCommandDocumentType, aggregateId, commandToSend, typeof(TAggregate), null);

            var handleAsyncMethod = handler.GetType().GetMethods().First(m => m.Name == "HandleAsync");
            var result = ((dynamic)handleAsyncMethod.Invoke(handler, new[] { commandDocument, aggregate })!)?.Result;
            if (result is null) { throw new Exception("Failed to execute change command"); }
            LatestEvents = (ReadOnlyCollection<IAggregateEvent>)result.Events;
        } else
        {
            var aggregate = GetAggregate<TAggregate>(aggregateId);
            if (aggregate is null)
            {
                throw new SekibanAggregateNotExistsException(aggregateId, typeof(TAggregate).Name);
            }
            aggregate.ResetEventsAndSnapshots();
            var aggregateCommandDocumentBaseType = typeof(AggregateCommandDocument<>);
            var aggregateCommandDocumentType = aggregateCommandDocumentBaseType.MakeGenericType(command.GetType());
            var commandToSend = command with { ReferenceVersion = aggregate?.Version ?? 0 };
            var commandDocument = Activator.CreateInstance(aggregateCommandDocumentType, aggregateId, commandToSend, typeof(TAggregate), null);

            var handleAsyncMethod = handler.GetType().GetMethods().First(m => m.Name == "HandleForOnlyPublishingCommandAsync");
            var result = ((dynamic)handleAsyncMethod.Invoke(handler, new[] { commandDocument, aggregate!.AggregateId })!)?.Result;
            if (result is null) { throw new Exception("Failed to execute change command"); }
            LatestEvents = (ReadOnlyCollection<IAggregateEvent>)result.Events;
        }
        if (LatestEvents.Any(ev => ev.IsAggregateInitialEvent))
        {
            throw new SekibanChangeCommandShouldNotSaveCreateEventException();
        }
        var documentWriter = _serviceProvider.GetRequiredService(typeof(IDocumentWriter)) as IDocumentWriter;
        if (documentWriter is null) { throw new Exception("Failed to get document writer"); }
        foreach (var e in LatestEvents)
        {
            documentWriter.SaveAsync(e, typeof(TAggregate)).Wait();
        }
        return LatestEvents;
    }
}
