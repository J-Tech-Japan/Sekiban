using NUnit.Framework;
using SekibanDcbDeciderAws.ApiService;
using SekibanDcbDeciderAws.ApiService.Auth;

namespace SekibanDcbDeciderAws.Unit;

public class AuthenticationConfigurationTests
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("short-key")]
    public void JwtSecretKey_InvalidFailsStartup(string? key) =>
        Assert.That(() => JwtSettings.ValidateSecretKey(key), Throws.InvalidOperationException.With.Message.Contains("Jwt:SecretKey"));

    [Test]
    public void JwtSecretKey_ValidAccepted() =>
        Assert.That(() => JwtSettings.ValidateSecretKey(new string('x', 32)), Throws.Nothing);

    [TestCase("Development", true)]
    [TestCase("Staging", false)]
    [TestCase("Production", false)]
    public void SampleUsers_AreDevelopmentOnly(string environment, bool expected) =>
        Assert.That(TemplateEnvironment.IsDevelopment(environment), Is.EqualTo(expected));
    [TestCase("Production", null, null, "Jwt:SecretKey")]
    [TestCase("Production", "", null, "Jwt:SecretKey")]
    [TestCase("Staging", "short", "Host=localhost", "Jwt:SecretKey")]
    [TestCase("Production", "12345678901234567890123456789012", null, "Identity")]
    [TestCase("Production", "12345678901234567890123456789012", "  ", "Identity")]
    public void Startup_RejectsMissingConfigurationEvenWithoutIdentity(string environment, string? key, string? connection, string diagnostic) =>
        Assert.That(() => JwtSettings.ValidateStartup(environment, key, connection),
            Throws.InvalidOperationException.With.Message.Contains(diagnostic));

    [TestCase("Development", null, null)]
    [TestCase("Production", "12345678901234567890123456789012", "Host=localhost")]
    public void Startup_AcceptsDevelopmentOrCompleteProductionConfiguration(string environment, string? key, string? connection) =>
        Assert.That(() => JwtSettings.ValidateStartup(environment, key, connection), Throws.Nothing);
}
