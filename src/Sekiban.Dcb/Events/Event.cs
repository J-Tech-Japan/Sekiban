namespace Sekiban.Dcb.Events;

public record Event(
    IEventPayload Payload,
    string SortableUniqueIdValue,
    string EventType,
    Guid Id,
    EventMetadata EventMetadata,
    List<string> Tags)
{
    /// <summary>
    ///     Convert string tags to ITag list in the format "group:content".
    /// </summary>
    public System.Collections.Generic.List<Sekiban.Dcb.Tags.ITag> GetTags()
    {
        var result = new System.Collections.Generic.List<Sekiban.Dcb.Tags.ITag>();
        if (Tags is null || Tags.Count == 0)
        {
            return result;
        }

        foreach (var t in Tags)
        {
            if (string.IsNullOrEmpty(t)) continue;
            result.Add(new EventTag(t));
        }
        return result;
    }

    private sealed record EventTag(string TagValue) : Sekiban.Dcb.Tags.ITag
    {
        private bool _parsed;
        private string _group = string.Empty;
        private string _content = string.Empty;

        public bool IsConsistencyTag() => true;

        public string GetTagGroup()
        {
            EnsureParsed();
            return _group;
        }

        public string GetTagContent()
        {
            EnsureParsed();
            return _content;
        }

        private void EnsureParsed()
        {
            if (_parsed) return;
            var idx = TagValue.IndexOf(':');
            if (idx >= 0)
            {
                _group = TagValue[..idx];
                _content = TagValue[(idx + 1)..];
            }
            else
            {
                _group = TagValue;
                _content = string.Empty;
            }
            _parsed = true;
        }
    }
}
