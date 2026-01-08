using Dcb.EventSource.MeetingRoom.Reservation;
using Dcb.EventSource.MeetingRoom.Room;
using Dcb.Interactions.Workflows.Reservation;
using Microsoft.AspNetCore.Mvc;
using Sekiban.Dcb;
namespace SekibanDcbOrleans.ApiService.Endpoints;

public static class TestDataEndpoints
{
    public static void MapTestDataEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/test-data")
            .WithTags("Test Data")
            .RequireAuthorization();

        group.MapPost("/generate", GenerateTestDataAsync)
            .WithName("GenerateTestData");

        group.MapPost("/generate-rooms", GenerateRoomsAsync)
            .WithName("GenerateRooms");

        group.MapPost("/generate-reservations", GenerateReservationsAsync)
            .WithName("GenerateReservations");
    }

    private static async Task<IResult> GenerateTestDataAsync(
        [FromServices] ISekibanExecutor executor)
    {
        var result = new TestDataGenerationResult();

        // Generate rooms first
        var rooms = await GenerateRoomsInternalAsync(executor);
        result.RoomsCreated = rooms.Count;
        result.RoomIds = rooms;

        // Generate reservations using the created rooms
        if (rooms.Count > 0)
        {
            var reservations = await GenerateReservationsInternalAsync(executor, rooms);
            result.ReservationsCreated = reservations.Count;
            result.ReservationIds = reservations;
        }

        return Results.Ok(result);
    }

    private static async Task<IResult> GenerateRoomsAsync(
        [FromServices] ISekibanExecutor executor)
    {
        var rooms = await GenerateRoomsInternalAsync(executor);
        return Results.Ok(new { roomsCreated = rooms.Count, roomIds = rooms });
    }

    private static async Task<IResult> GenerateReservationsAsync(
        [FromQuery] Guid? roomId,
        [FromServices] ISekibanExecutor executor)
    {
        // If no roomId provided, we need at least one room
        List<Guid> roomIds;
        if (roomId.HasValue)
        {
            roomIds = [roomId.Value];
        }
        else
        {
            // Try to create rooms first
            roomIds = await GenerateRoomsInternalAsync(executor);
            if (roomIds.Count == 0)
            {
                return Results.BadRequest("No rooms available for reservations");
            }
        }

        var reservations = await GenerateReservationsInternalAsync(executor, roomIds);
        return Results.Ok(new { reservationsCreated = reservations.Count, reservationIds = reservations });
    }

    private static async Task<List<Guid>> GenerateRoomsInternalAsync(ISekibanExecutor executor)
    {
        var roomIds = new List<Guid>();
        var errors = new List<string>();

        var roomDefinitions = new[]
        {
            new { Name = "Conference Room A", Capacity = 20, Location = "Building 1, Floor 2", Equipment = new List<string> { "Projector", "Whiteboard", "Video Conference" } },
            new { Name = "Meeting Room B", Capacity = 8, Location = "Building 1, Floor 3", Equipment = new List<string> { "TV Screen", "Whiteboard" } },
            new { Name = "Executive Boardroom", Capacity = 16, Location = "Building 2, Floor 5", Equipment = new List<string> { "Projector", "Video Conference", "Sound System", "Recording" } },
            new { Name = "Huddle Space 1", Capacity = 4, Location = "Building 1, Floor 1", Equipment = new List<string> { "TV Screen" } },
            new { Name = "Training Room", Capacity = 30, Location = "Building 3, Floor 1", Equipment = new List<string> { "Projector", "Multiple Screens", "Recording", "Microphones" } },
            new { Name = "Small Meeting Room C", Capacity = 6, Location = "Building 1, Floor 2", Equipment = new List<string> { "Whiteboard" } },
        };

        foreach (var roomDef in roomDefinitions)
        {
            var roomId = Guid.CreateVersion7();
            var command = new CreateRoom
            {
                RoomId = roomId,
                Name = roomDef.Name,
                Capacity = roomDef.Capacity,
                Location = roomDef.Location,
                Equipment = roomDef.Equipment
            };

            var result = await executor.ExecuteAsync(command);
            roomIds.Add(roomId);
        }

        return roomIds;
    }

    private static async Task<List<Guid>> GenerateReservationsInternalAsync(
        ISekibanExecutor executor,
        List<Guid> roomIds)
    {
        var reservationIds = new List<Guid>();
        var organizerId = Guid.CreateVersion7();
        var baseDate = DateTime.UtcNow.Date.AddDays(1); // Start from tomorrow

        var reservationDefinitions = new[]
        {
            new { RoomIndex = 0, DaysOffset = 0, StartHour = 9, EndHour = 10, Purpose = "Team Standup", Confirm = true },
            new { RoomIndex = 0, DaysOffset = 0, StartHour = 14, EndHour = 16, Purpose = "Sprint Planning", Confirm = true },
            new { RoomIndex = 1, DaysOffset = 1, StartHour = 10, EndHour = 11, Purpose = "1:1 Meeting", Confirm = false },
            new { RoomIndex = 2, DaysOffset = 1, StartHour = 13, EndHour = 15, Purpose = "Board Meeting", Confirm = true },
            new { RoomIndex = 0, DaysOffset = 2, StartHour = 9, EndHour = 10, Purpose = "Daily Standup", Confirm = true },
            new { RoomIndex = 3, DaysOffset = 2, StartHour = 15, EndHour = 16, Purpose = "Quick Sync", Confirm = false },
            new { RoomIndex = 4, DaysOffset = 3, StartHour = 9, EndHour = 17, Purpose = "All-hands Training", Confirm = true },
            new { RoomIndex = 1, DaysOffset = 4, StartHour = 11, EndHour = 12, Purpose = "Project Review", Confirm = true },
            new { RoomIndex = 5, DaysOffset = 4, StartHour = 14, EndHour = 15, Purpose = "Interview", Confirm = false },
        };

        var workflow = new QuickReservationWorkflow(executor);

        foreach (var resDef in reservationDefinitions)
        {
            try
            {
                // Skip if room index exceeds available rooms
                if (resDef.RoomIndex >= roomIds.Count) continue;

                var roomId = roomIds[resDef.RoomIndex];
                var startTime = baseDate.AddDays(resDef.DaysOffset).AddHours(resDef.StartHour);
                var endTime = baseDate.AddDays(resDef.DaysOffset).AddHours(resDef.EndHour);

                var result = await workflow.ExecuteAsync(
                    roomId,
                    organizerId,
                    startTime,
                    endTime,
                    resDef.Purpose);

                reservationIds.Add(result.ReservationId);
            }
            catch (Exception)
            {
                // Reservation might fail due to conflicts, continue with next
            }
        }

        return reservationIds;
    }
}

public record TestDataGenerationResult
{
    public int RoomsCreated { get; set; }
    public List<Guid> RoomIds { get; set; } = [];
    public int ReservationsCreated { get; set; }
    public List<Guid> ReservationIds { get; set; } = [];
}
