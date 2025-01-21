using System.IO.Compression;

namespace Sekiban.Infrastructure.IndexedDb.Databases;

public record DbBlob
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Payload { get; init; } = string.Empty;
    public bool IsGzipped { get; init; }

    public static DbBlob FromStream(Stream stream, string blobName, bool useGzip)
    {
        using var ms = useGzip ?
            Gzip(stream) :
            Noop(stream);

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

        var result = IsGzipped ?
            Gunzip(payload) :
            Noop(payload);

        return result;
    }

    private static MemoryStream Gzip(Stream plain)
    {
        using var workStream = new MemoryStream();
        using var gzipStream = new GZipStream(workStream, CompressionMode.Compress);

        plain.CopyTo(gzipStream);
        gzipStream.Close();

        var resultStream = new MemoryStream(workStream.ToArray());
        return resultStream;
    }

    private static MemoryStream Gunzip(Stream gzipped)
    {
        using var workStream = new MemoryStream();
        using var gzipStream = new GZipStream(gzipped, CompressionMode.Decompress);

        gzipStream.CopyTo(workStream);
        gzipStream.Close();

        var resultStream = new MemoryStream(workStream.ToArray());
        return resultStream;
    }

    private static MemoryStream Noop(Stream stream)
    {
        var resultStream = new MemoryStream();

        stream.CopyTo(resultStream);
        resultStream.Seek(0, SeekOrigin.Begin);

        return resultStream;
    }
}
