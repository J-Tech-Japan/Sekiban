using Dcb.Domain.WithoutResult.ClassRoom;
using System.Text.Json;

namespace SekibanDcbOrleans.Web;

public class ClassRoomApiClient(HttpClient httpClient)
{
    private class ClassRoomCreateResponse
    {
        public Guid classRoomId { get; set; }
        public Guid eventId { get; set; }
        public string? sortableUniqueId { get; set; }
        public string? message { get; set; }
    }
    
    private class ErrorResponse
    {
        public string? error { get; set; }
    }
    public async Task<ClassRoomItem[]> GetClassRoomsAsync(
        int? pageNumber = null,
        int? pageSize = null,
        string? waitForSortableUniqueId = null,
        CancellationToken cancellationToken = default)
    {
        var queryParams = new List<string>();

        if (!string.IsNullOrEmpty(waitForSortableUniqueId))
            queryParams.Add($"waitForSortableUniqueId={Uri.EscapeDataString(waitForSortableUniqueId)}");

        if (pageNumber.HasValue)
            queryParams.Add($"pageNumber={pageNumber.Value}");

        if (pageSize.HasValue)
            queryParams.Add($"pageSize={pageSize.Value}");

        var requestUri = queryParams.Count > 0
            ? $"/api/classrooms?{string.Join("&", queryParams)}"
            : "/api/classrooms";

        var classrooms = await httpClient.GetFromJsonAsync<List<ClassRoomItem>>(requestUri, cancellationToken);

        return classrooms?.ToArray() ?? [];
    }

    public async Task<dynamic?> GetClassRoomAsync(
        Guid classRoomId,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetFromJsonAsync<dynamic>($"/api/classrooms/{classRoomId}", cancellationToken);
        return response;
    }

    public async Task<CommandResponse> CreateClassRoomAsync(
        CreateClassRoom command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await httpClient.PostAsJsonAsync("/api/classrooms", command, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ClassRoomCreateResponse>(cancellationToken);
                if (result != null)
                {
                    return new CommandResponse(
                        true, 
                        result.eventId, 
                        result.classRoomId, 
                        null, 
                        result.sortableUniqueId);
                }
            }
            else
            {
                var errorResult = await response.Content.ReadFromJsonAsync<ErrorResponse>(cancellationToken);
                return new CommandResponse(false, null, null, errorResult?.error ?? "Unknown error", null);
            }
        }
        catch (Exception ex)
        {
            return new CommandResponse(false, null, null, ex.Message, null);
        }
        
        return new CommandResponse(false, null, null, "Failed to create classroom", null);
    }
}
