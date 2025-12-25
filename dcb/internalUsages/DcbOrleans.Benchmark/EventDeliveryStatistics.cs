public class EventDeliveryStatistics
{
    public int totalUniqueEvents { get; set; }
    public long totalDeliveries { get; set; }
    public long duplicateDeliveries { get; set; }
    public int eventsWithMultipleDeliveries { get; set; }
    public int maxDeliveryCount { get; set; }
    public double averageDeliveryCount { get; set; }
    public int? streamUniqueEvents { get; set; }
    public long? streamDeliveries { get; set; }
    public int? catchUpUniqueEvents { get; set; }
    public long? catchUpDeliveries { get; set; }
}