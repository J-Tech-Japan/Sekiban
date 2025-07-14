using Dapr;
using Dapr.Actors;
using Dapr.Actors.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Pure.Dapr.Actors;
using Sekiban.Pure.Dapr.Serialization;
using Sekiban.Pure;

namespace Sekiban.Pure.Dapr.Extensions;

/// <summary>
/// MinimalAPI拡張メソッドでPubSubイベントリレーを提供
/// クライアント側で明示的に有効化する必要がある（opt-in方式）
/// </summary>
public static class SekibanEventRelayExtensions
{
    /// <summary>
    /// SekibanイベントリレーエンドポイントをMinimalAPIとして追加
    /// </summary>
    /// <param name="app">IEndpointRouteBuilder</param>
    /// <param name="options">PubSubリレーオプション</param>
    /// <returns>RouteHandlerBuilder</returns>
    public static RouteHandlerBuilder MapSekibanEventRelay(
        this IEndpointRouteBuilder app,
        SekibanPubSubRelayOptions? options = null)
    {
        options ??= new SekibanPubSubRelayOptions();

        var builder = app.MapPost(options.EndpointPath,
            async (
                DaprEventEnvelope envelope,
                [FromServices]IActorProxyFactory actorProxyFactory,
                [FromServices]SekibanDomainTypes domainTypes,
                [FromServices]ILogger<SekibanEventRelayHandler> logger) =>
            {
                return await HandleEventAsync(envelope, actorProxyFactory, domainTypes, logger, options);
            })
            .WithTopic(options.PubSubName, options.TopicName)
            .WithName("SekibanEventRelay")
            .WithMetadata("Tags", new[] { "Internal" })
            .WithMetadata("Summary", "Sekiban PubSub Event Relay")
            .WithMetadata("Description", "Internal endpoint for relaying Dapr PubSub events to MultiProjectorActors")
            .Produces<object>(200)
            .Produces<ProblemDetails>(500);

        // Consumer Group が指定されている場合は追加
        if (!string.IsNullOrEmpty(options.ConsumerGroup))
        {
            builder.WithMetadata("dapr.io/consumer-group", options.ConsumerGroup);
        }

        return builder;
    }

    /// <summary>
    /// 複数のトピックに対応したSekibanイベントリレーエンドポイントを追加
    /// </summary>
    /// <param name="app">IEndpointRouteBuilder</param>
    /// <param name="topicConfigs">トピック設定のリスト</param>
    /// <returns>RouteHandlerBuilderのリスト</returns>
    public static List<RouteHandlerBuilder> MapSekibanEventRelayMultiTopic(
        this IEndpointRouteBuilder app,
        params SekibanPubSubRelayOptions[] topicConfigs)
    {
        var builders = new List<RouteHandlerBuilder>();
        
        foreach (var config in topicConfigs)
        {
            var builder = app.MapSekibanEventRelay(config);
            builders.Add(builder);
        }

        return builders;
    }

    /// <summary>
    /// 設定ベースでSekibanイベントリレーを追加
    /// </summary>
    /// <param name="app">IEndpointRouteBuilder</param>
    /// <param name="configure">設定アクション</param>
    /// <returns>RouteHandlerBuilder or null</returns>
    public static RouteHandlerBuilder? MapSekibanEventRelayIfEnabled(
        this IEndpointRouteBuilder app,
        Action<SekibanPubSubRelayOptions>? configure = null)
    {
        var options = new SekibanPubSubRelayOptions();
        configure?.Invoke(options);

        // 設定で無効化されている場合はnullを返す
        if (!options.Enabled)
        {
            return null;
        }

        return app.MapSekibanEventRelay(options);
    }

    /// <summary>
    /// 開発環境でのみSekibanイベントリレーを追加
    /// </summary>
    /// <param name="app">IEndpointRouteBuilder</param>
    /// <param name="isDevelopment">開発環境かどうか</param>
    /// <param name="options">PubSubリレーオプション</param>
    /// <returns>RouteHandlerBuilder or null</returns>
    public static RouteHandlerBuilder? MapSekibanEventRelayForDevelopment(
        this IEndpointRouteBuilder app,
        bool isDevelopment,
        SekibanPubSubRelayOptions? options = null)
    {
        if (!isDevelopment)
        {
            return null;
        }

        return app.MapSekibanEventRelay(options);
    }

    /// <summary>
    /// イベント処理の実際のロジック
    /// </summary>
    private static async Task<IResult> HandleEventAsync(
        DaprEventEnvelope envelope,
        IActorProxyFactory actorProxyFactory,
        SekibanDomainTypes domainTypes,
        ILogger<SekibanEventRelayHandler> logger,
        SekibanPubSubRelayOptions options)
    {
        try
        {
            logger.LogDebug("Received event envelope: AggregateId={AggregateId}, Version={Version}, Endpoint={Endpoint}",
                envelope.AggregateId, envelope.Version, options.EndpointPath);

            // Get all multi-projector names that should process this event
            var projectorNames = domainTypes.MultiProjectorsType.GetAllProjectorNames();

            if (!projectorNames.Any())
            {
                logger.LogDebug("No projectors found to process event {EventId}", envelope.EventId);
                return Results.Ok(new { Message = "No projectors to process", EventId = envelope.EventId });
            }

            // Forward the event to each multi-projector actor
            var tasks = projectorNames.Select(async projectorName =>
            {
                try
                {
                    var actorId = new ActorId(projectorName);
                    var actor = actorProxyFactory.CreateActorProxy<IMultiProjectorActor>(
                        actorId,
                        nameof(MultiProjectorActor));

                    await actor.HandlePublishedEvent(envelope);
                    logger.LogDebug("Forwarded event to projector: {ProjectorName}", projectorName);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to forward event to projector: {ProjectorName}", projectorName);
                    
                    // プロジェクター個別の失敗は続行する
                    if (!options.ContinueOnProjectorFailure)
                    {
                        throw;
                    }
                }
            });

            await Task.WhenAll(tasks);

            logger.LogDebug("Successfully processed event {EventId} for {ProjectorCount} projectors", 
                envelope.EventId, projectorNames.Count());

            return Results.Ok(new { Message = "Event processed successfully", EventId = envelope.EventId });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling event from PubSub: EventId={EventId}", envelope.EventId);
            return Results.Problem(
                title: "Event processing failed",
                detail: ex.Message,
                statusCode: 500);
        }
    }
}

/// <summary>
/// Sekibanイベントリレーのオプション設定
/// </summary>
public class SekibanPubSubRelayOptions
{
    /// <summary>
    /// リレー機能を有効にするかどうか
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// PubSubコンポーネント名
    /// </summary>
    public string PubSubName { get; set; } = "sekiban-pubsub";

    /// <summary>
    /// 購読するトピック名
    /// </summary>
    public string TopicName { get; set; } = "events.all";

    /// <summary>
    /// エンドポイントのパス
    /// </summary>
    public string EndpointPath { get; set; } = "/internal/pubsub/events";

    /// <summary>
    /// 個別プロジェクターの失敗時に処理を続行するかどうか
    /// </summary>
    public bool ContinueOnProjectorFailure { get; set; } = true;

    /// <summary>
    /// Consumer Group名（Dapr 1.14+でサポート）
    /// 同じConsumer Groupのインスタンスは重複処理を避けるため、1つのインスタンスのみがイベントを受信する
    /// </summary>
    public string? ConsumerGroup { get; set; }

    /// <summary>
    /// このリレーが処理する最大並行数
    /// </summary>
    public int MaxConcurrency { get; set; } = 10;

    /// <summary>
    /// デッドレターキューを有効にするかどうか
    /// </summary>
    public bool EnableDeadLetterQueue { get; set; } = false;

    /// <summary>
    /// デッドレターキューのトピック名
    /// </summary>
    public string DeadLetterTopic { get; set; } = "events.dead-letter";

    /// <summary>
    /// リトライの最大回数
    /// </summary>
    public int MaxRetryCount { get; set; } = 3;
}

/// <summary>
/// ログ用のマーカークラス
/// </summary>
internal class SekibanEventRelayHandler
{
}
