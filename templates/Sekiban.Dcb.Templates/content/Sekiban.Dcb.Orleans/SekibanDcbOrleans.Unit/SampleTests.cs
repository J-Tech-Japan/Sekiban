using Dcb.Domain;
using Dcb.Domain.Student;
using NUnit.Framework;
using Sekiban.Dcb;
using Sekiban.Dcb.Testing;

namespace SekibanDcbOrleans.Unit;

public class SampleTests
{
    [Test]
    public void DomainType_GetDomainTypes_ReturnsNonNull()
    {
        Assert.That(DomainType.GetDomainTypes(), Is.Not.Null);
    }

    [Test]
    public async Task CreateStudent_ThenReadItBack()
    {
        // The unit-test composition, and the only place it belongs: an in-process executor over a volatile store.
        // Both come from the Testing packages, and both say what they are — the executor reports TestingInProcess and
        // the store reports Volatile, so if this composition ever turned up in a Production host with
        // AddSekibanDcbProductionGuard() registered, that host would refuse to start. Which is the intent.
        var domainTypes = DomainType.GetDomainTypes();
        ISekibanExecutor executor = new InMemoryDcbExecutorForTesting(
            domainTypes,
            new InMemoryEventStore(domainTypes.EventTypes));

        var studentId = Guid.CreateVersion7();

        var executed = await executor.ExecuteAsync(new CreateStudent(studentId, "Alice", 3));
        Assert.That(executed.IsSuccess, Is.True);

        var state = await executor.GetTagStateAsync<StudentProjector>(new StudentTag(studentId));
        Assert.That(state.IsSuccess, Is.True);
        Assert.That(state.GetValue().Payload, Is.TypeOf<StudentState>());
    }
}
