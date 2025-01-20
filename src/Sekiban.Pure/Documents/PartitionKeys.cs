using System.ComponentModel.DataAnnotations;
using Orleans;
using ResultBoxes;
using Sekiban.Pure.Exception;
using Sekiban.Pure.Extensions;
using Sekiban.Pure.Projectors;
namespace Sekiban.Pure.Documents;
public record PartitionKeys(Guid AggregateId, [property:RegularExpression("^[a-z0-9-_]{1,36}$")]string Group, [property:RegularExpression("^[a-z0-9-_]{1,36}$")]string RootPartitionKey)
{
    public const string RootPartitionKeyRegexPattern = "^[a-z0-9-_]{1,36}$";
    private const string AggregateGroupRegexPattern = "^[a-zA-Z0-9-_]{1,36}$";

    public const string DefaultRootPartitionKey = "default";
    public const string DefaultAggregateGroupName = "default";
    public static PartitionKeys Generate(
        string group = DefaultAggregateGroupName,
        string rootPartitionKey = DefaultRootPartitionKey) =>
        new(GuidExtensions.CreateVersion7(), group, rootPartitionKey);
    public static PartitionKeys Generate<TAggregateProjector>(string rootPartitionKey = DefaultRootPartitionKey)
        where TAggregateProjector : IAggregateProjector =>
        new(GuidExtensions.CreateVersion7(), typeof(TAggregateProjector).Name, rootPartitionKey);
    public static PartitionKeys Existing(
        Guid aggregateId,
        string group = "default",
        string rootPartitionKey = "default") =>
        new(aggregateId, group, rootPartitionKey);
    public static PartitionKeys Existing<TAggregateProjector>(Guid aggregateId, string rootPartitionKey = "default")
        where TAggregateProjector : IAggregateProjector =>
        new(aggregateId, typeof(TAggregateProjector).Name, rootPartitionKey);

    public static ResultBox<PartitionKeys> FromPrimaryKeysString(string primaryKeyString)
    {
        var split = primaryKeyString.Split('@');
        if (split.Length != 3)
        {
            return ResultBox<PartitionKeys>.Error(new SekibanPartitionKeyInvalidException($"Invalid primary key string: {primaryKeyString}"));
        }
        if (Guid.TryParse(split[2], out var guid))
        {
            return new PartitionKeys(guid, split[1], split[0]);
        }
        return ResultBox<PartitionKeys>.Error(new SekibanPartitionKeyInvalidException($"Invalid primary key string Id can not be parsed: {primaryKeyString}"));
    }
    public string ToPrimaryKeysString() => $"{RootPartitionKey}@{Group}@{AggregateId}";
}
public static class PartitionKeys<TAggregateProjector> where TAggregateProjector : IAggregateProjector, new()
{
    public static PartitionKeys Generate(string rootPartitionKey = PartitionKeys.DefaultRootPartitionKey) =>
        PartitionKeys.Generate<TAggregateProjector>(rootPartitionKey);
    public static PartitionKeys Existing(Guid aggregateId, string group = PartitionKeys.DefaultAggregateGroupName) =>
        PartitionKeys.Existing<TAggregateProjector>(aggregateId, group);
}
