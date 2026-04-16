namespace Sekiban.Dcb.MaterializedView;

public sealed class MvStorageInfoProvider : IMvStorageInfoProvider
{
    private readonly MvStorageInfo _storageInfo;

    public MvStorageInfoProvider(MvStorageInfo storageInfo)
    {
        _storageInfo = storageInfo;
    }

    public MvStorageInfo GetStorageInfo() => _storageInfo;
}
