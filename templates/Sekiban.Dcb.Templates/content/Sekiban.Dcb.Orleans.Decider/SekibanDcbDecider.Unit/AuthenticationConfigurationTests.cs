using NUnit.Framework;
using SekibanDcbDecider.ApiService;
using SekibanDcbDecider.ApiService.Auth;

namespace SekibanDcbDecider.Unit;

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
}
