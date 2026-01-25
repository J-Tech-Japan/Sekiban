using Dcb.EventSource.Enrollment;
using System.Text.Json;

namespace SekibanDcbOrleans.Web;

public class EnrollmentApiClient(HttpClient httpClient)
{
    private class EnrollmentResponse
    {
        public Guid studentId { get; set; }
        public Guid classRoomId { get; set; }
        public Guid eventId { get; set; }
        public string? sortableUniqueId { get; set; }
        public string? message { get; set; }
    }
    
    private class ErrorResponse
    {
        public string? error { get; set; }
    }

    public class EnrollmentView
    {
        public Guid StudentId { get; set; }
        public string StudentName { get; set; } = string.Empty;
        public int Grade { get; set; }
        public Guid ClassRoomId { get; set; }
        public string ClassName { get; set; } = string.Empty;
        public DateTime EnrollmentDate { get; set; }
    }

    public async Task<EnrollmentView[]> GetEnrollmentsAsync(
        string? waitForSortableUniqueId = null,
        CancellationToken cancellationToken = default)
    {
        var queryParams = new List<string>();

        if (!string.IsNullOrEmpty(waitForSortableUniqueId))
            queryParams.Add($"waitForSortableUniqueId={Uri.EscapeDataString(waitForSortableUniqueId)}");

        var requestUri = queryParams.Count > 0
            ? $"/api/enrollments?{string.Join("&", queryParams)}"
            : "/api/enrollments";

        var enrollments = await httpClient.GetFromJsonAsync<List<EnrollmentView>>(requestUri, cancellationToken);

        return enrollments?.ToArray() ?? [];
    }

    public async Task<CommandResponse> EnrollStudentAsync(
        EnrollStudentInClassRoom command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await httpClient.PostAsJsonAsync("/api/enrollments/add", command, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<EnrollmentResponse>(cancellationToken);
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
        
        return new CommandResponse(false, null, null, "Failed to enroll student", null);
    }

    public async Task<CommandResponse> DropStudentAsync(
        DropStudentFromClassRoom command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await httpClient.PostAsJsonAsync("/api/enrollments/drop", command, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<EnrollmentResponse>(cancellationToken);
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
        
        return new CommandResponse(false, null, null, "Failed to drop student", null);
    }
}
