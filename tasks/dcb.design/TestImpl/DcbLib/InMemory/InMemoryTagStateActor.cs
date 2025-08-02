using System.Text;
using DcbLib.Actors;
using DcbLib.Tags;

namespace DcbLib.InMemory;

/// <summary>
/// In-memory implementation of ITagStateActorCommon for testing
/// Manages tag state and provides serializable state
/// </summary>
public class InMemoryTagStateActor : ITagStateActorCommon
{
    private readonly TagState _tagState;
    private readonly object _stateLock = new();
    
    public InMemoryTagStateActor(TagState tagState)
    {
        _tagState = tagState ?? throw new ArgumentNullException(nameof(tagState));
    }
    
    public SerializableTagState GetState()
    {
        lock (_stateLock)
        {
            // Convert payload to bytes (if it exists)
            byte[] payloadBytes;
            if (_tagState.Payload != null)
            {
                // For testing, use simple JSON serialization with proper options
                var options = new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                };
                var json = System.Text.Json.JsonSerializer.Serialize(_tagState.Payload, _tagState.Payload.GetType(), options);
                payloadBytes = Encoding.UTF8.GetBytes(json);
            }
            else
            {
                payloadBytes = Array.Empty<byte>();
            }
            
            return new SerializableTagState(
                payloadBytes,
                _tagState.Version,
                _tagState.LastSortedUniqueId,
                _tagState.TagGroup,
                _tagState.TagContent,
                _tagState.TagProjector,
                _tagState.Payload?.GetType().Name ?? "None"
            );
        }
    }
    
    public string GetTagStateActorId()
    {
        return $"{_tagState.TagGroup}:{_tagState.TagContent}:state";
    }
    
    /// <summary>
    /// Updates the internal state (for testing purposes)
    /// </summary>
    public void UpdateState(TagState newState)
    {
        lock (_stateLock)
        {
            // In a real implementation, this would be handled by the actor framework
            // For testing, we allow direct updates
            if (newState.TagGroup != _tagState.TagGroup || newState.TagContent != _tagState.TagContent)
            {
                throw new InvalidOperationException("Cannot change tag identity");
            }
        }
    }
    
    /// <summary>
    /// Gets the current tag state (for testing purposes)
    /// </summary>
    public TagState GetTagState()
    {
        lock (_stateLock)
        {
            return _tagState;
        }
    }
}