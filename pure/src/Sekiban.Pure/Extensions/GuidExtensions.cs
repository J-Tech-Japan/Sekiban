namespace Sekiban.Pure.Extensions;

public static class GuidExtensions
{
    public static Guid CreateVersion7()
    {
#if NET9_0
        return Guid.CreateVersion7();
#else
        return Guid.NewGuid();
#endif
    }
}
