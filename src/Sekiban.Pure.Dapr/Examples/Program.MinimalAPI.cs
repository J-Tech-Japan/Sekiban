// NOTE: このファイルはサンプルコードです。実際のプロジェクトでは適切な設定を行ってください。

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Sekiban.Pure.Dapr.Extensions;

var builder = WebApplication.CreateBuilder(args);

// NOTE: 実際のプロジェクトでは適切にSekibanとDaprを設定してください
// builder.Services.AddSekibanWithDapr(...);

var app = builder.Build();

// === 使用例 1: 基本的な有効化 ===
app.MapSekibanEventRelay();

// === 使用例 2: カスタム設定 ===
app.MapSekibanEventRelay(new SekibanPubSubRelayOptions
{
    PubSubName = "my-pubsub",
    TopicName = "domain.events",
    EndpointPath = "/api/internal/events",
    ConsumerGroup = "my-consumer-group",
    MaxConcurrency = 10,
    ContinueOnProjectorFailure = false
});

// === 使用例 3: 設定ファイルベース ===
app.MapSekibanEventRelayIfEnabled(options =>
{
    var config = app.Configuration.GetSection("Sekiban:PubSub");
    options.Enabled = config.GetValue<bool>("Enabled");
    options.PubSubName = config.GetValue<string>("ComponentName") ?? "sekiban-pubsub";
    options.TopicName = config.GetValue<string>("TopicName") ?? "events.all";
    options.EndpointPath = config.GetValue<string>("EndpointPath") ?? "/internal/pubsub/events";
    options.ConsumerGroup = config.GetValue<string>("ConsumerGroup");
    options.MaxConcurrency = config.GetValue<int>("MaxConcurrency", 10);
    options.ContinueOnProjectorFailure = config.GetValue<bool>("ContinueOnProjectorFailure", true);
});

// === 使用例 4: 開発環境でのみ有効化 ===
app.MapSekibanEventRelayForDevelopment(
    app.Environment.IsDevelopment(),
    new SekibanPubSubRelayOptions
    {
        EndpointPath = "/dev/pubsub/events",
        ConsumerGroup = "dev-projectors"
    });

// === 使用例 5: 複数トピック ===
app.MapSekibanEventRelayMultiTopic(
    new SekibanPubSubRelayOptions
    {
        PubSubName = "sekiban-pubsub",
        TopicName = "events.customer",
        EndpointPath = "/pubsub/customer-events",
        ConsumerGroup = "customer-projectors"
    },
    new SekibanPubSubRelayOptions
    {
        PubSubName = "sekiban-pubsub",
        TopicName = "events.order",
        EndpointPath = "/pubsub/order-events",
        ConsumerGroup = "order-projectors"
    }
);

// === 使用例 6: 高可用性設定 ===
app.MapSekibanEventRelay(new SekibanPubSubRelayOptions
{
    PubSubName = "sekiban-pubsub-prod",
    TopicName = "events.all",
    EndpointPath = "/internal/pubsub/events",
    ConsumerGroup = "sekiban-projectors-prod",
    MaxConcurrency = 50,
    ContinueOnProjectorFailure = false, // 厳密なエラー処理
    EnableDeadLetterQueue = true,
    DeadLetterTopic = "events.failed",
    MaxRetryCount = 5
});

// === 使用例 7: 条件付き有効化 ===
var enablePubSub = app.Configuration.GetValue<bool>("Features:PubSub");
if (enablePubSub)
{
    app.MapSekibanEventRelay(new SekibanPubSubRelayOptions
    {
        ConsumerGroup = app.Configuration.GetValue<string>("Environment") + "-projectors"
    });
}

// === 使用例 8: セキュリティ設定付き ===
app.MapSekibanEventRelay(new SekibanPubSubRelayOptions
{
    EndpointPath = "/internal/pubsub/events"
})
.RequireHost("localhost", "internal.company.com") // 特定のホストからのみアクセス可能
.WithMetadata("ExcludeFromDescription", true); // API文書から除外

app.Run();
