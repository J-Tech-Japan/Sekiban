namespace SekibanDcbOrleans.Domain;

public record SampleCreated(string Name);

public class SampleAggregate
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = string.Empty;

    public IEnumerable<object> Create(string name)
    {
        yield return new SampleCreated(name);
    }

    public void Apply(SampleCreated e)
    {
        Name = e.Name;
    }
}
