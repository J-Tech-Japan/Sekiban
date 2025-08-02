namespace DcbLib.Tags;

/// <summary>
/// Represents the identifier for a TagStateActor
/// Format: "[tagGroupName]:[tagContentName]:[TagProjectorName]"
/// </summary>
public class TagStateId
{
    public string TagGroup { get; }
    public string TagContent { get; }
    public string TagProjectorName { get; }
    
    public TagStateId(ITag tag, ITagProjector tagProjector)
    {
        var fullTag = tag.GetTag();
        var parts = fullTag.Split(':');
        if (parts.Length != 2)
        {
            throw new ArgumentException($"Invalid tag format: {fullTag}. Expected format: 'TagGroup:TagContent'");
        }
        
        TagGroup = tag.GetTagGroup();
        TagContent = parts[1];
        TagProjectorName = tagProjector.GetTagProjectorName();
    }
    
    private TagStateId(string tagGroup, string tagContent, string tagProjectorName)
    {
        TagGroup = tagGroup;
        TagContent = tagContent;
        TagProjectorName = tagProjectorName;
    }
    
    /// <summary>
    /// Gets the string representation of the TagStateId
    /// </summary>
    public override string ToString() => $"{TagGroup}:{TagContent}:{TagProjectorName}";
    
    /// <summary>
    /// Parses a string into a TagStateId
    /// </summary>
    public static TagStateId Parse(string value)
    {
        var parts = value.Split(':');
        if (parts.Length != 3)
        {
            throw new ArgumentException($"Invalid TagStateId format: {value}. Expected format: 'TagGroup:TagContent:TagProjectorName'");
        }
        
        return new TagStateId(parts[0], parts[1], parts[2]);
    }
}