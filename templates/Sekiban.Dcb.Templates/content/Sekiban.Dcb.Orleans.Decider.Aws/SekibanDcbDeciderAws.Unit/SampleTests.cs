using Dcb.EventSource;
using Dcb.EventSource.ClassRoom;
using Dcb.EventSource.MeetingRoom.Room;
using Dcb.EventSource.MeetingRoom.User;
using Dcb.EventSource.Student;
using Dcb.EventSource.Weather;
using Dcb.ImmutableModels.States.ClassRoom;
using Dcb.ImmutableModels.States.Student;
using Dcb.ImmutableModels.States.Weather;
using Dcb.ImmutableModels.Tags;
using Dcb.MeetingRoomModels.States.Room;
using Dcb.MeetingRoomModels.States.UserDirectory;
using Dcb.MeetingRoomModels.Tags;
using NUnit.Framework;
using Sekiban.Dcb;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.Tags;

namespace SekibanDcbOrleans.Unit;

public class SampleTests
{
    [Test]
    public void SimplePass() => Assert.Pass();

    [Test]
    public void DomainType_GetDomainTypes_ReturnsNonNull()
    {
        var domainTypes = DomainType.GetDomainTypes();
        Assert.That(domainTypes, Is.Not.Null);
    }

    [Test]
    public async Task CreateStudent_ThenGetState_ReturnsStudentState()
    {
        ISekibanExecutor executor = new InMemoryDcbExecutor(DomainType.GetDomainTypes());
        var studentId = Guid.CreateVersion7();

        await executor.ExecuteAsync(new CreateStudent(studentId, "Alice", 3));

        TagState tagState = await executor
            .GetTagStateAsync<StudentProjector>(new StudentTag(studentId));
        Assert.That(tagState.Payload, Is.InstanceOf<StudentState>());
        var state = (StudentState)tagState.Payload;
        Assert.That(state.Name, Is.EqualTo("Alice"));
        Assert.That(state.MaxClassCount, Is.EqualTo(3));
    }

    [Test]
    public async Task CreateClassRoom_ThenGetState_ReturnsClassRoomState()
    {
        ISekibanExecutor executor = new InMemoryDcbExecutor(DomainType.GetDomainTypes());
        var classRoomId = Guid.CreateVersion7();

        await executor.ExecuteAsync(
            new CreateClassRoom(classRoomId, "Physics 101", 25),
            CreateClassRoomHandler.HandleAsync);

        TagState tagState = await executor
            .GetTagStateAsync<ClassRoomProjector>(new ClassRoomTag(classRoomId));
        var payload = tagState.Payload as AvailableClassRoomState;
        Assert.That(payload, Is.Not.Null);
        Assert.That(payload!.Name, Is.EqualTo("Physics 101"));
        Assert.That(payload.MaxStudents, Is.EqualTo(25));
    }

    [Test]
    public async Task CreateWeatherForecast_ThenGetState_ReturnsForecastState()
    {
        ISekibanExecutor executor = new InMemoryDcbExecutor(DomainType.GetDomainTypes());
        var forecastId = Guid.CreateVersion7();

        await executor.ExecuteAsync(new CreateWeatherForecast
        {
            ForecastId = forecastId,
            Location = "Tokyo",
            Date = new DateOnly(2026, 3, 20),
            TemperatureC = 22,
            Summary = "Clear"
        });

        TagState tagState = await executor
            .GetTagStateAsync<WeatherForecastProjector>(new WeatherForecastTag(forecastId));
        var payload = tagState.Payload as WeatherForecastState;
        Assert.That(payload, Is.Not.Null);
        Assert.That(payload!.Location, Is.EqualTo("Tokyo"));
        Assert.That(payload.TemperatureC, Is.EqualTo(22));
    }

    [Test]
    public async Task CreateRoom_ThenGetState_ReturnsRoomState()
    {
        ISekibanExecutor executor = new InMemoryDcbExecutor(DomainType.GetDomainTypes());
        var roomId = Guid.CreateVersion7();

        await executor.ExecuteAsync(new CreateRoom
        {
            RoomId = roomId,
            Name = "Board Room",
            Capacity = 12,
            Location = "Floor 3",
            Equipment = ["Projector", "Whiteboard"],
            RequiresApproval = true
        });

        TagState tagState = await executor
            .GetTagStateAsync<RoomProjector>(new RoomTag(roomId));
        var payload = tagState.Payload as RoomState;
        Assert.That(payload, Is.Not.Null);
        Assert.That(payload!.Name, Is.EqualTo("Board Room"));
        Assert.That(payload.RequiresApproval, Is.True);
    }

    [Test]
    public async Task RegisterUser_ThenGetState_ReturnsUserState()
    {
        ISekibanExecutor executor = new InMemoryDcbExecutor(DomainType.GetDomainTypes());
        var userId = Guid.CreateVersion7();

        await executor.ExecuteAsync(new RegisterUser
        {
            UserId = userId,
            DisplayName = "Bob",
            Email = "bob@example.com",
            Department = "HR"
        });

        TagState tagState = await executor
            .GetTagStateAsync<UserDirectoryProjector>(new UserTag(userId));
        var payload = tagState.Payload as UserDirectoryState.UserDirectoryActive;
        Assert.That(payload, Is.Not.Null);
        Assert.That(payload!.DisplayName, Is.EqualTo("Bob"));
        Assert.That(payload.Email, Is.EqualTo("bob@example.com"));
    }
}
