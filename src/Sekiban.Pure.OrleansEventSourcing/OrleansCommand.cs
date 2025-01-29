namespace Sekiban.Pure.OrleansEventSourcing;

[GenerateSerializer]
public record OrleansCommand([property:Id(0)]string payload);