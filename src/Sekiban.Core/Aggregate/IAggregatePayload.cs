namespace Sekiban.Core.Aggregate;

public interface IAggregatePayload
{
    public string GetPayloadVersionIdentifier() => "initial";
}
