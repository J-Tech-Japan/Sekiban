using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Documents;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Partition;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Validation;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
namespace Sekiban.Testing.Command;

public class TestCommandExecutor
{
    private readonly IServiceProvider _serviceProvider;

    public TestCommandExecutor(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

    public ImmutableList<IEvent> LatestEvents { get; set; } = ImmutableList<IEvent>.Empty;

    //public (IEnumerable<IEvent>, Guid) ExecuteCreateCommand<TAggregatePayload>(
    //    ICommand<TAggregatePayload> command,
    //    Guid? injectingAggregateId = null) where TAggregatePayload : IAggregatePayload, new() =>
    //    ExecuteCreateCommand(command, injectingAggregateId, false);
    //public (IEnumerable<IEvent>, Guid) ExecuteCreateCommandWithPublish<TAggregatePayload>(
    //    ICommand<TAggregatePayload> command,
    //    Guid? injectingAggregateId = null) where TAggregatePayload : IAggregatePayload, new() =>
    //    ExecuteCreateCommand(command, injectingAggregateId, true);
    //private (IEnumerable<IEvent>, Guid) ExecuteCreateCommand<TAggregatePayload>(
    //    ICommand<TAggregatePayload> command,
    //    Guid? injectingAggregateId,
    //    bool withPublish) where TAggregatePayload : IAggregatePayload, new()
    //{
    //    var validationResults = command.ValidateProperties().ToList();
    //    if (validationResults.Any())
    //    {
    //        throw new ValidationException("Validation failed " + validationResults);
    //    }

    //    var baseType = typeof(ICommandHandler<,>);
    //    var genericType = baseType.MakeGenericType(typeof(TAggregatePayload), command.GetType());
    //    var handler = _serviceProvider.GetService(genericType);
    //    if (handler is null)
    //    {
    //        throw new SekibanCommandNotRegisteredException(command.GetType().Name);
    //    }
    //    var aggregateId = injectingAggregateId ?? command.GetAggregateId();

    //    var commandDocumentBaseType = typeof(CommandDocument<>);
    //    var commandDocumentType = commandDocumentBaseType.MakeGenericType(command.GetType());
    //    var commandDocument = Activator.CreateInstance(commandDocumentType, aggregateId, command, typeof(TAggregatePayload), null);
    //    var aggregate = new Aggregate<TAggregatePayload> { AggregateId = aggregateId };
    //    var handlerType = handler.GetType().GetMethods();
    //    var handleAsyncMethod = handler.GetType().GetMethods().First(m => m.Name == "HandleAsync");
    //    var result = ((dynamic)handleAsyncMethod.Invoke(handler, new[] { commandDocument, aggregate })!)?.Result;
    //    if (result is null) { throw new Exception("Failed to execute create command"); }
    //    var latestEvents = (ImmutableList<IEvent>)result.Events;
    //    if (latestEvents.Count == 0)
    //    {
    //        throw new SekibanCreateHasToMakeEventException();
    //    }
    //    var documentWriter = _serviceProvider.GetRequiredService(typeof(IDocumentWriter)) as IDocumentWriter;
    //    if (documentWriter is null) { throw new Exception("Failed to get document writer"); }
    //    foreach (var e in latestEvents)
    //    {
    //        if (withPublish)
    //        {
    //            documentWriter.SaveAndPublishEvent(e, typeof(TAggregatePayload)).Wait();
    //        }
    //        else
    //        {
    //            documentWriter.SaveAsync(e, typeof(TAggregatePayload)).Wait();
    //        }
    //    }
    //    LatestEvents = latestEvents;
    //    return (latestEvents, aggregateId);
    //}

    private Aggregate<TAggregatePayload>? GetAggregate<TAggregatePayload>(Guid aggregateId)
        where TAggregatePayload : IAggregatePayload, new()
    {
        var singleProjectionService = _serviceProvider.GetRequiredService(typeof(IAggregateLoader)) as IAggregateLoader;
        if (singleProjectionService is null)
        {
            throw new Exception("Failed to get AddAggregate Service");
        }
        var method = singleProjectionService.GetType().GetMethods().FirstOrDefault(m => m.Name == "AsAggregateAsync");
        if (method is null)
        {
            throw new Exception("Failed to get AddAggregate Service");
        }
        var genericMethod = method.MakeGenericMethod(typeof(TAggregatePayload));
        var aggregateTask =
            genericMethod.Invoke(singleProjectionService, new object?[] { aggregateId, null }) as dynamic;
        if (aggregateTask is null)
        {
            throw new Exception("Failed to get AddAggregate Service");
        }
        var aggregate = aggregateTask.Result;
        return aggregate;
    }

    public Guid ExecuteCommand<TAggregatePayload>(
        ICommand<TAggregatePayload> command,
        Guid? injectingAggregateId = null)
        where TAggregatePayload : IAggregatePayloadCommon => ExecuteCommand(command, injectingAggregateId, false);

    public Guid ExecuteCommandWithPublish<TAggregatePayload>(
        ICommand<TAggregatePayload> command,
        Guid? injectingAggregateId = null)
        where TAggregatePayload : IAggregatePayloadCommon => ExecuteCommand(command, injectingAggregateId, true);

    private Guid ExecuteCommand<TAggregatePayload>(
        ICommand<TAggregatePayload> command,
        Guid? injectingAggregateId,
        bool withPublish)
        where TAggregatePayload : IAggregatePayloadCommon
    {
        var validationResults = command.ValidateProperties().ToList();
        if (validationResults.Any())
        {
            throw new ValidationException("Validation failed " + validationResults);
        }

        var baseType = typeof(ICommandHandlerCommon<,>);
        var genericType = baseType.MakeGenericType(typeof(TAggregatePayload), command.GetType());
        var handler = _serviceProvider.GetService(genericType);
        if (handler is null)
        {
            throw new SekibanCommandNotRegisteredException(command.GetType().Name);
        }
        var aggregateId = injectingAggregateId ?? command.GetAggregateId();

        if (command is not IOnlyPublishingCommandCommon)
        {
            var commandDocumentBaseType = typeof(CommandDocument<>);
            var commandDocumentType = commandDocumentBaseType.MakeGenericType(command.GetType());
            var commandDocument = Activator.CreateInstance(
                commandDocumentType,
                aggregateId,
                command,
                typeof(TAggregatePayload),
                null);

            var aggregateLoader = _serviceProvider.GetRequiredService(typeof(IAggregateLoader)) as IAggregateLoader;
            if (aggregateLoader is null)
            {
                throw new Exception("Failed to get AddAggregate Service");
            }

            var baseClass = typeof(CommandHandlerAdapter<,>);
            var adapterClass = baseClass.MakeGenericType(typeof(TAggregatePayload), command.GetType());
            var adapter = Activator.CreateInstance(adapterClass, aggregateLoader, false) ??
                throw new Exception("Adapter not found");

            var method = adapterClass.GetMethod("HandleCommandAsync") ??
                throw new Exception("HandleCommandAsync not found");

            var commandResponse =
                (CommandResponse)((dynamic?)method.Invoke(adapter, new[] { commandDocument, handler, aggregateId }) ??
                    throw new SekibanCommandHandlerNotMatchException(
                        "Command failed to execute " +
                        command.GetType().Name)).Result;

            LatestEvents = commandResponse.Events;
        }
        else
        {
            var commandDocumentBaseType = typeof(CommandDocument<>);
            var commandDocumentType = commandDocumentBaseType.MakeGenericType(command.GetType());
            var commandDocument = Activator.CreateInstance(
                commandDocumentType,
                aggregateId,
                command,
                typeof(TAggregatePayload),
                null);

            var baseClass = typeof(OnlyPublishingCommandHandlerAdapter<,>);
            var adapterClass = baseClass.MakeGenericType(typeof(TAggregatePayload), command.GetType());
            var adapter = Activator.CreateInstance(adapterClass) ?? throw new Exception("Method not found");
            var method = adapterClass.GetMethod("HandleCommandAsync") ??
                throw new Exception("HandleCommandAsync not found");
            var commandResponse =
                (CommandResponse)((dynamic?)method.Invoke(adapter, new[] { commandDocument, handler, aggregateId }) ??
                    throw new SekibanCommandHandlerNotMatchException(
                        "Command failed to execute " +
                        command.GetType().Name)).Result;
            LatestEvents = commandResponse.Events;
        }

        var documentWriter = _serviceProvider.GetRequiredService(typeof(IDocumentWriter)) as IDocumentWriter;
        if (documentWriter is null)
        {
            throw new Exception("Failed to get document writer");
        }
        foreach (var e in LatestEvents)
        {
            if (withPublish)
            {
                documentWriter.SaveAndPublishEvent(e, typeof(TAggregatePayload)).Wait();
            }
            else
            {
                documentWriter.SaveAsync(e, typeof(TAggregatePayload)).Wait();
            }
        }
        return aggregateId;
    }

    public IReadOnlyCollection<IEvent> GetAllAggregateEvents<TAggregatePayload>(Guid aggregateId)
        where TAggregatePayload : IAggregatePayload, new()
    {
        var toReturn = new List<IEvent>();
        var documentRepository =
            _serviceProvider.GetRequiredService(typeof(IDocumentRepository)) as IDocumentRepository ??
            throw new Exception("Failed to get document repository");
        documentRepository.GetAllEventsForAggregateIdAsync(
                aggregateId,
                typeof(TAggregatePayload),
                PartitionKeyGenerator.ForEvent(aggregateId, typeof(TAggregatePayload)),
                null,
                eventObjects => { toReturn.AddRange(eventObjects); })
            .Wait();
        return toReturn;
    }
}
