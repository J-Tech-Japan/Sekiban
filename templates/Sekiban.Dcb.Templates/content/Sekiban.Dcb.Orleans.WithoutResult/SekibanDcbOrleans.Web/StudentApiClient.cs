using Dcb.Domain.WithoutResult.Student;
using System.Text.Json;

namespace SekibanDcbOrleans.Web;

public class StudentApiClient(HttpClient httpClient)
{
    private class StudentCreateResponse
    {
        public Guid studentId { get; set; }
        public Guid eventId { get; set; }
        public string? sortableUniqueId { get; set; }
        public string? message { get; set; }
    }
    
    private class ErrorResponse
    {
        public string? error { get; set; }
    }
    public async Task<StudentState[]> GetStudentsAsync(
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
            ? $"/api/students?{string.Join("&", queryParams)}"
            : "/api/students";

        var students = await httpClient.GetFromJsonAsync<List<StudentState>>(requestUri, cancellationToken);

        return students?.ToArray() ?? [];
    }

    public async Task<StudentState?> GetStudentAsync(
        Guid studentId,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetFromJsonAsync<dynamic>($"/api/students/{studentId}", cancellationToken);
        // The API returns an object with payload property
        if (response?.payload != null)
        {
            var json = response.payload.ToString();
            return System.Text.Json.JsonSerializer.Deserialize<StudentState>(json);
        }
        return null;
    }

    public async Task<CommandResponse> CreateStudentAsync(
        CreateStudent command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await httpClient.PostAsJsonAsync("/api/students", command, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<StudentCreateResponse>(cancellationToken);
                if (result != null)
                {
                    return new CommandResponse(
                        true, 
                        result.eventId, 
                        result.studentId, 
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
        
        return new CommandResponse(false, null, null, "Failed to create student", null);
    }
}
