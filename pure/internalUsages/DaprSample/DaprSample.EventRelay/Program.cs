using Sekiban.Pure.Dapr.Extensions;
using SharedDomain;
using SharedDomain.Generated;
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add memory cache for CachedDaprSerializationService
builder.Services.AddMemoryCache();

// Generate domain types
var domainTypes = SharedDomainDomainTypes.Generate(SharedDomainEventsJsonContext.Default.Options);

// Configure Sekiban with Dapr for EventRelay (no actors registration)
builder.Services.AddSekibanWithDaprForEventRelay(
    domainTypes,
    options =>
    {
        options.StateStoreName = "sekiban-eventstore";
        options.PubSubName = "sekiban-pubsub";
        options.EventTopicName = "events.all";
    });

// Add Dapr services
builder.Services.AddControllers().AddDapr();

// Configure consumer group and concurrency
var consumerGroup = builder.Configuration["EventRelay:ConsumerGroup"] ?? "event-relay-group";
var maxConcurrency = builder.Configuration.GetValue("EventRelay:MaxConcurrency", 10);
var continueOnFailure = builder.Configuration.GetValue("EventRelay:ContinueOnProjectorFailure", false);

var app = builder.Build();

app.MapDefaultEndpoints();

// Subscribe to Dapr pub/sub
app.UseCloudEvents();
app.MapSubscribeHandler();

// Map only the Sekiban Event Relay endpoint
app.MapSekibanEventRelay(
    new SekibanPubSubRelayOptions
    {
        PubSubName = "sekiban-pubsub",
        TopicName = "events.all",
        EndpointPath = "/internal/pubsub/events",
        ConsumerGroup = consumerGroup,
        MaxConcurrency = maxConcurrency,
        ContinueOnProjectorFailure = continueOnFailure,
        EnableDeadLetterQueue = !app.Environment.IsDevelopment(),
        DeadLetterTopic = "events.dead-letter",
        MaxRetryCount = app.Environment.IsDevelopment() ? 1 : 3
    });

app.Run();
