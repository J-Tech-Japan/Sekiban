namespace Sekiban.Infrastructure.IndexedDb.Databases;

public record DbBlob
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Payload { get; init; } = string.Empty;
    public bool IsGzipped { get; init; }

    public static DbBlob FromStream(Stream stream, string blobName, bool useGzip)
    {
        using var ms = new MemoryStream();

        if (useGzip)
        {
            Gzip(stream, ms);
        }
        else
        {
            stream.CopyTo(ms);
        }

        ms.Seek(0, SeekOrigin.Begin);
        var payload = Convert.ToBase64String(ms.ToArray());

        return new()
        {
            Id = Guid.NewGuid().ToString(),
            Name = blobName,
            Payload = payload,
            IsGzipped = useGzip,
        };
    }

    public Stream ToStream()
    {
        using var payload = new MemoryStream(Convert.FromBase64String(Payload));
        var result = new MemoryStream();

        if (IsGzipped)
        {
            Gunzip(payload, result);
        }
        else
        {
            payload.CopyTo(result);
        }

        result.Seek(0, SeekOrigin.Begin);
        return result;
    }

    private static void Gzip(Stream plain, Stream compressed)
    {
        using var workStream = new MemoryStream();
        using var gzip = new System.IO.Compression.GZipStream(workStream, System.IO.Compression.CompressionMode.Compress);

        plain.CopyTo(gzip);
        gzip.Close();

        workStream.Seek(0, SeekOrigin.Begin);
        workStream.CopyTo(compressed);
    }

    private static void Gunzip(Stream plain, Stream decompressed)
    {
        using var workStream = new MemoryStream();
        using var gzip = new System.IO.Compression.GZipStream(workStream, System.IO.Compression.CompressionMode.Compress);

        plain.CopyTo(gzip);
        gzip.Close();

        workStream.Seek(0, SeekOrigin.Begin);
        workStream.CopyTo(decompressed);
    }
}
