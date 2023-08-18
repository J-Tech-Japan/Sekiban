using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Documents;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Partition;
using Sekiban.Core.PubSub;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Validation;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
namespace Sekiban.Testing.Command;

public class TestCommandExecutor
{
    private readonly IServiceProvider _serviceProvider;

    public ImmutableList<IEvent> LatestEvents { get; set; } = ImmutableList<IEvent>.Empty;

    public TestCommandExecutor(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

    public Guid ExecuteCommand<TAggregatePayload>(ICommand<TAggregatePayload> command, Guid? injectingAggregateId = null)
        where TAggregatePayload : IAggregatePayloadCommon =>
        ExecuteCommand(command, injectingAggregateId, false);

    public Guid ExecuteCommandWithPublish<TAggregatePayload>(ICommand<TAggregatePayload> command, Guid? injectingAggregateId = null)
        where TAggregatePayload : IAggregatePayloadCommon =>
        ExecuteCommand(command, injectingAggregateId, true);

    public Guid ExecuteCommandWithPublishAndBlockingSubscriptions<TAggregatePayload>(
        ICommand<TAggregatePayload> command,
        Guid? injectingAggregateId = null) where TAggregatePayload : IAggregatePayloadCommon
    {
        var nonBlockingStatus = _serviceProvider.GetService<EventNonBlockingStatus>();
        if (nonBlockingStatus is null) { throw new Exception("EventNonBlockingStatus could not be found."); }
        return nonBlockingStatus.RunBlockingFunc(() => ExecuteCommand(command, injectingAggregateId, true));
    }

    private Guid ExecuteCommand<TAggregatePayload>(ICommand<TAggregatePayload> command, Guid? injectingAggregateId, bool withPublish)
        where TAggregatePayload : IAggregatePayloadCommon
    {
        var rootPartitionKey = command.GetRootPartitionKey();
        var validationResults = command.ValidateProperties().ToList();
        if (validationResults.Any())
        {
            throw new ValidationException($"{command.GetType().Name} command validation failed :" + validationResults);
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
                rootPartitionKey,
                null);

            var aggregateLoader = _serviceProvider.GetRequiredService(typeof(IAggregateLoader)) as IAggregateLoader;
            if (aggregateLoader is null)
            {
                throw new Exception("Failed to get AddAggregate Service");
            }

            var baseClass = typeof(CommandHandlerAdapter<,>);
            var adapterClass = baseClass.MakeGenericType(typeof(TAggregatePayload), command.GetType());
            var adapter = Activator.CreateInstance(adapterClass, aggregateLoader, false) ?? throw new Exception("Adapter not found");

            var method = adapterClass.GetMethod(nameof(ICommandHandlerAdapterCommon.HandleCommandAsync)) ??
                throw new Exception("HandleCommandAsync not found");

            var commandResponse
                = (CommandResponse)((dynamic?)method.Invoke(adapter, new[] { commandDocument, handler, aggregateId, rootPartitionKey }) ??
                    throw new SekibanCommandHandlerNotMatchException("Command failed to execute " + command.GetType().Name)).Result;

            LatestEvents = commandResponse.Events;
        } else
        {
            var commandDocumentBaseType = typeof(CommandDocument<>);
            var commandDocumentType = commandDocumentBaseType.MakeGenericType(command.GetType());
            var commandDocument = Activator.CreateInstance(commandDocumentType, aggregateId, command, typeof(TAggregatePayload), null);

            var baseClass = typeof(OnlyPublishingCommandHandlerAdapter<,>);
            var adapterClass = baseClass.MakeGenericType(typeof(TAggregatePayload), command.GetType());
            var adapter = Activator.CreateInstance(adapterClass) ?? throw new Exception("Method not found");
            var method = adapterClass.GetMethod(nameof(ICommandHandlerAdapterCommon.HandleCommandAsync));
            var commandResponse
                = (CommandResponse)((dynamic?)method?.Invoke(adapter, new[] { commandDocument, handler, aggregateId, rootPartitionKey }) ??
                    throw new SekibanCommandHandlerNotMatchException("Command failed to execute " + command.GetType().Name)).Result;
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
                documentWriter.SaveAndPublishEvents(new List<IEvent> { e }, typeof(TAggregatePayload)).Wait();
            } else
            {
                documentWriter.SaveAsync(e, typeof(TAggregatePayload)).Wait();
            }
        }
        return aggregateId;
    }

    public IReadOnlyCollection<IEvent> GetAllAggregateEvents<TAggregatePayload>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey) where TAggregatePayload : IAggregatePayloadCommon
    {
        var toReturn = new List<IEvent>();
        var documentRepository = _serviceProvider.GetRequiredService(typeof(IDocumentRepository)) as IDocumentRepository ??
            throw new Exception("Failed to get document repository");
        documentRepository.GetAllEventsForAggregateIdAsync(
                aggregateId,
                typeof(TAggregatePayload),
                PartitionKeyGenerator.ForEvent(aggregateId, typeof(TAggregatePayload), rootPartitionKey),
                null,
                rootPartitionKey,
                eventObjects => { toReturn.AddRange(eventObjects); })
            .Wait();
        return toReturn;
    }
}
