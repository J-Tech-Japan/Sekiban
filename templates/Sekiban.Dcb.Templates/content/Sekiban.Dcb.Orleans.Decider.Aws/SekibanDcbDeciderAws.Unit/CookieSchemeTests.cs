using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using SekibanDcbDeciderAws.ApiService.Auth;

namespace SekibanDcbDeciderAws.Unit;

public class CookieSchemeTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void MultiAuth_SelectsIdentityCookieOrBearer(bool bearer)
    {
        var expected = bearer ? JwtBearerDefaults.AuthenticationScheme : IdentityConstants.ApplicationScheme;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Jwt:SecretKey"] = new string('x', 32) }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthServices(configuration, "Host=localhost;Database=identity");
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<PolicySchemeOptions>>().Get("MultiAuth");
        var context = new DefaultHttpContext();
        if (bearer) context.Request.Headers.Authorization = "Bearer token";
        Assert.That(options.ForwardDefaultSelector!(context), Is.EqualTo(expected));
    }
}
