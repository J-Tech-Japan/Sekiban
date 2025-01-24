using Sekiban.Core.Setting;
using Sekiban.Infrastructure.IndexedDb.Databases;

namespace Sekiban.Infrastructure.IndexedDb.Documents;

public class IndexedDbBlobAccessor(IndexedDbFactory dbFactory) : IBlobAccessor
{
    public async Task<Stream?> GetBlobAsync(SekibanBlobContainer container, string blobName)
    {
        var dbBlob = (
            await dbFactory.DbActionAsync(
                async (dbContext) => container switch
                {
                    SekibanBlobContainer.SingleProjectionState => await dbContext.GetSingleProjectionStateBlobsAsync(DbBlobQuery.ForName(blobName)),
                    SekibanBlobContainer.MultiProjectionState => await dbContext.GetMultiProjectionStateBlobsAsync(DbBlobQuery.ForName(blobName)),
                    SekibanBlobContainer.MultiProjectionEvents => await dbContext.GetMultiProjectionEventsBlobsAsync(DbBlobQuery.ForName(blobName)),
                    _ => throw new NotImplementedException(),
                }
            )
        )
            .FirstOrDefault();

        if (dbBlob is null)
        {
            return null;
        }

        return dbBlob.ToStream();
    }

    public async Task<bool> SetBlobAsync(SekibanBlobContainer container, string blobName, Stream blob)
    {
        var dbBlob = DbBlob.FromStream(blob, blobName, useGzip: false);
        await SetBlobAsync(container, dbBlob);
        return true;
    }

    public async Task<Stream?> GetBlobWithGZipAsync(SekibanBlobContainer container, string blobName) =>
        await GetBlobAsync(container, blobName);

    public async Task<bool> SetBlobWithGZipAsync(SekibanBlobContainer container, string blobName, Stream blob)
    {
        var dbBlob = DbBlob.FromStream(blob, blobName, useGzip: true);
        await SetBlobAsync(container, dbBlob);
        return true;
    }

    public string BlobConnectionString() => string.Empty;

    private async Task SetBlobAsync(SekibanBlobContainer container, DbBlob blob) =>
        await dbFactory.DbActionAsync(
            async dbContext =>
            {
                switch (container)
                {
                    case SekibanBlobContainer.SingleProjectionState:
                        await dbContext.WriteSingleProjectionStateBlobAsync(blob);
                        break;

                    case SekibanBlobContainer.MultiProjectionState:
                        await dbContext.WriteMultiProjectionStateBlobAsync(blob);
                        break;

                    case SekibanBlobContainer.MultiProjectionEvents:
                        await dbContext.WriteMultiProjectionEventsBlobAsync(blob);
                        break;

                    default:
                        throw new NotImplementedException();
                }

            });
}
