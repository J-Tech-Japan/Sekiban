using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Command.UserInformation;

namespace Sekiban.Core.Dependency;

/// <summary>
///     Extension methods for <see cref="IServiceCollection" />
/// </summary>
public static class SekibanAspNetCoreServiceExtensions
{
    public enum HttpContextType
    {
        Local = 1, Azure = 2
    }

    public static IServiceCollection AddSekibanHTTPUser(
        this IServiceCollection services,
        HttpContextType contextType = HttpContextType.Local)
    {
        // Users Information
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        switch (contextType)
        {
            case HttpContextType.Local:
                services.AddTransient<IUserInformationFactory, HttpContextUserInformationFactory>();
                break;

            case HttpContextType.Azure:
                services.AddTransient<IUserInformationFactory, AzureAdUserInformationFactory>();
                break;
        }

        return services;
    }
}
