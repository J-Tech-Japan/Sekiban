using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using System.Runtime.Serialization;
namespace Sekiban.Pure.Orleans.Parts;

public static class SekibanSerializationTypesChecker
{
    public static void CheckDomainSerializability(SekibanDomainTypes domainTypes)
    {
        CheckEventsSerializability(domainTypes);
        CheckCommandsSerializability(domainTypes);
        CheckQuerySerializability(domainTypes);
    }

    private static Serializer GetSerializer()
    {
        var services = new ServiceCollection();
        services.AddSerializer();
        var serviceProvider = services.BuildServiceProvider();
        return serviceProvider.GetRequiredService<Serializer>();
    }

    public static void CheckEventsSerializability(SekibanDomainTypes domainTypes)
    {
        foreach (var type in domainTypes.EventTypes.GetEventTypes())
        {
            CheckTypeSerializability(type);
        }
    }

    public static void CheckCommandsSerializability(SekibanDomainTypes domainTypes)
    {
        foreach (var type in domainTypes.CommandTypes.GetCommandTypes())
        {
            CheckTypeSerializability(type);
        }
    }

    public static void CheckQuerySerializability(SekibanDomainTypes domainTypes)
    {
        foreach (var type in domainTypes.QueryTypes.GetQueryTypes())
        {
            CheckTypeSerializability(type);
        }
    }

    private static void CheckTypeSerializability(Type type)
    {
        var serializer = GetSerializer();
        if (!serializer.CanSerialize(type))
        {
            throw new SerializationException(
                $"{type.FullName} is not serializable with Orleans Default Serializer. Please consider adding [GenerateSerializer] attribute to the type.");
        }
    }
}
