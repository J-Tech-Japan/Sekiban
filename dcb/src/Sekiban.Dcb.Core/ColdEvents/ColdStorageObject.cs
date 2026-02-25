namespace Sekiban.Dcb.ColdEvents;

public record ColdStorageObject(
    byte[] Data,
    string ETag);
