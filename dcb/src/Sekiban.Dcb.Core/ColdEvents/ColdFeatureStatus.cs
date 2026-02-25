namespace Sekiban.Dcb.ColdEvents;

public record ColdFeatureStatus(
    bool IsSupported,
    bool IsEnabled,
    string Reason);
