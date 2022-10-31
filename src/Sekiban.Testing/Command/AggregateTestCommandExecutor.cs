using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Document;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Validation;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
namespace Sekiban.Testing.Command;

public class AggregateTestCommandExecutor
{
    private readonly IServiceProvider _serviceProvider;
    public AggregateTestCommandExecutor(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;
    public ImmutableList<IAggregateEvent> LatestEvents { get; set; } = ImmutableList<IAggregateEvent>.Empty;

    public (IEnumerable<IAggregateEvent>, Guid) ExecuteCreateCommand<TAggregate>(
        ICreateAggregateCommand<TAggregate> command,
        Guid? injectingAggregateId = null) where TAggregate : IAggregatePayload, new()
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
        var aggregateId = injectingAggregateId ?? command.GetAggregateId();

        var aggregateCommandDocumentBaseType = typeof(AggregateCommandDocument<>);
        var aggregateCommandDocumentType = aggregateCommandDocumentBaseType.MakeGenericType(command.GetType());
        var commandDocument = Activator.CreateInstance(aggregateCommandDocumentType, aggregateId, command, typeof(TAggregate), null);
        var aggregate = new Aggregate<TAggregate> { AggregateId = aggregateId };
        var handlerType = handler.GetType().GetMethods();
        var handleAsyncMethod = handler.GetType().GetMethods().First(m => m.Name == "HandleAsync");
        var result = ((dynamic)handleAsyncMethod.Invoke(handler, new[] { commandDocument, aggregate })!)?.Result;
        if (result is null) { throw new Exception("Failed to execute create command"); }
        var latestEvents = (ImmutableList<IAggregateEvent>)result.Events;
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
        return (latestEvents, aggregateId);
    }

    private Aggregate<TAggregate> GetAggregate<TAggregate>(Guid aggregateId) where TAggregate : IAggregatePayload, new()
    {

        var singleAggregateService = _serviceProvider.GetRequiredService(typeof(ISingleProjectionService)) as ISingleProjectionService;
        if (singleAggregateService is null) { throw new Exception("Failed to get Aggregate Service"); }
        var method = singleAggregateService.GetType().GetMethods().FirstOrDefault(m => m.Name == "GetAggregateAsync");
        if (method is null) { throw new Exception("Failed to get Aggregate Service"); }
        var genericMethod = method.MakeGenericMethod(typeof(TAggregate));
        var aggregateTask = genericMethod.Invoke(singleAggregateService, new object?[] { aggregateId, null }) as dynamic;
        if (aggregateTask is null) { throw new Exception("Failed to get Aggregate Service"); }
        var aggregate = aggregateTask.Result;
        return aggregate ?? throw new SekibanAggregateNotExistsException(aggregateId, typeof(TAggregate).Name);
    }

    public IEnumerable<IAggregateEvent> ExecuteChangeCommand<TAggregatePayload>(ChangeAggregateCommandBase<TAggregatePayload> command)
        where TAggregatePayload : IAggregatePayload, new()
    {
        var validationResults = command.TryValidateProperties().ToList();
        if (validationResults.Any())
        {
            throw new ValidationException("Validation failed " + validationResults);
        }

        var baseType = typeof(IChangeAggregateCommandHandler<,>);
        var genericType = baseType.MakeGenericType(typeof(TAggregatePayload), command.GetType());
        var handler = _serviceProvider.GetService(genericType);
        if (handler is null)
        {
            throw new SekibanAggregateCommandNotRegisteredException(command.GetType().Name);
        }
        var aggregateId = command.GetAggregateId();

        if (command is not IOnlyPublishingCommand)
        {
            var aggregate = GetAggregate<TAggregatePayload>(aggregateId);
            if (aggregate is null)
            {
                throw new SekibanAggregateNotExistsException(aggregateId, typeof(TAggregatePayload).Name);
            }
            var aggregateCommandDocumentBaseType = typeof(AggregateCommandDocument<>);
            var aggregateCommandDocumentType = aggregateCommandDocumentBaseType.MakeGenericType(command.GetType());
            var commandToSend = command with { ReferenceVersion = aggregate?.Version ?? 0 };
            var commandDocument = Activator.CreateInstance(aggregateCommandDocumentType, aggregateId, commandToSend, typeof(TAggregatePayload), null);

            var handleAsyncMethod = handler.GetType().GetMethods().First(m => m.Name == "HandleAsync");
            var result = ((dynamic)handleAsyncMethod.Invoke(handler, new[] { commandDocument, aggregate })!)?.Result;
            if (result is null) { throw new Exception("Failed to execute change command"); }
            LatestEvents = (ImmutableList<IAggregateEvent>)result.Events;
        }
        else
        {
            var aggregate = GetAggregate<TAggregatePayload>(aggregateId);
            if (aggregate is null)
            {
                throw new SekibanAggregateNotExistsException(aggregateId, typeof(TAggregatePayload).Name);
            }
            var aggregateCommandDocumentBaseType = typeof(AggregateCommandDocument<>);
            var aggregateCommandDocumentType = aggregateCommandDocumentBaseType.MakeGenericType(command.GetType());
            var commandToSend = command with { ReferenceVersion = aggregate?.Version ?? 0 };
            var commandDocument = Activator.CreateInstance(aggregateCommandDocumentType, aggregateId, commandToSend, typeof(TAggregatePayload), null);

            var handleAsyncMethod = handler.GetType().GetMethods().First(m => m.Name == "HandleForOnlyPublishingCommandAsync");
            var result = ((dynamic)handleAsyncMethod.Invoke(handler, new[] { commandDocument, aggregate!.AggregateId })!)?.Result;
            if (result is null) { throw new Exception("Failed to execute change command"); }
            LatestEvents = (ImmutableList<IAggregateEvent>)result.Events;
        }
        if (LatestEvents.Any(ev => ev.IsAggregateInitialEvent))
        {
            throw new SekibanChangeCommandShouldNotSaveCreateEventException();
        }
        var documentWriter = _serviceProvider.GetRequiredService(typeof(IDocumentWriter)) as IDocumentWriter;
        if (documentWriter is null) { throw new Exception("Failed to get document writer"); }
        foreach (var e in LatestEvents)
        {
            documentWriter.SaveAsync(e, typeof(TAggregatePayload)).Wait();
        }
        return LatestEvents;
    }
}
