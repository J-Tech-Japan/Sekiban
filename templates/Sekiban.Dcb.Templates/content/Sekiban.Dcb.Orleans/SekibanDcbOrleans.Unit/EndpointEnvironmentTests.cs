using NUnit.Framework;
using SekibanDcbOrleans.ApiService;

namespace SekibanDcbOrleans.Unit;

public class EndpointEnvironmentTests
{
    [TestCase("Development", true)]
    [TestCase("Staging", false)]
    [TestCase("Production", false)]
    public void OperatorEndpoints_AreDevelopmentOnly(string environment, bool expected) =>
        Assert.That(TemplateEnvironment.IsDevelopment(environment), Is.EqualTo(expected));
}
