using Microsoft.Extensions.DependencyInjection;
namespace Sekiban.Infrastructure.Postgres;

public class SekibanPostgresDbOptionsServiceCollection(
    SekibanPostgresOptions sekibanPostgresDbOptions,
    IServiceCollection serviceCollection)
{
    public SekibanPostgresOptions SekibanPostgresDbOptions { get; init; } = sekibanPostgresDbOptions;
    public IServiceCollection ServiceCollection { get; init; } = serviceCollection;
}
