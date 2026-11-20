using Dcb.Domain.WithoutResult.Enrollment;
using System.Text.Json;

namespace SekibanDcbOrleansAws.Web;

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
                return new CommandResponse(false, null, null, await ApiErrorReader.ReadAsync(response, cancellationToken), null);
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
                return new CommandResponse(false, null, null, await ApiErrorReader.ReadAsync(response, cancellationToken), null);
            }
        }
        catch (Exception ex)
        {
            return new CommandResponse(false, null, null, ex.Message, null);
        }
        
        return new CommandResponse(false, null, null, "Failed to drop student", null);
    }
}
