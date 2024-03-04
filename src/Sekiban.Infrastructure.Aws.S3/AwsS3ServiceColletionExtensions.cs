using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Setting;
using Sekiban.Infrastructure.Aws.S3.Blobs;
namespace Sekiban.Infrastructure.Aws.S3;

public static class AwsS3ServiceColletionExtensions
{
    /// <summary>
    ///     Add AWS S3 services for Sekiban
    /// </summary>
    /// <param name="builder"></param>
    /// <returns></returns>
    public static SekibanAwsS3OptionsServiceCollection AddSekibanAwsS3(this WebApplicationBuilder builder) =>
        AddSekibanAwsS3(builder.Services, builder.Configuration);
    /// <summary>
    ///     Add AWS S3 services for Sekiban
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configuration"></param>
    /// <returns></returns>
    public static SekibanAwsS3OptionsServiceCollection AddSekibanAwsS3(this IServiceCollection services, IConfiguration configuration)
    {
        var options = SekibanAwsS3Options.FromConfiguration(configuration);
        return AddSekibanAwsS3(services, options);
    }
    /// <summary>
    ///     Add AWS S3 services for Sekiban
    /// </summary>
    /// <param name="services"></param>
    /// <param name="s3Options"></param>
    /// <returns></returns>
    public static SekibanAwsS3OptionsServiceCollection AddSekibanAwsS3(this IServiceCollection services, SekibanAwsS3Options s3Options)
    {
        services.AddSingleton(s3Options);
        services.AddTransient<IBlobAccessor, S3BlobAccessor>();
        return new SekibanAwsS3OptionsServiceCollection(s3Options, services);
    }
}
