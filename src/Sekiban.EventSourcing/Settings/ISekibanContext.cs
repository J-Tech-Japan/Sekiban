namespace Sekiban.EventSourcing.Settings
{
    public interface ISekibanContext
    {
        public string SettingGroupIdentifier { get; }
        public Task<T> SekibanActionAsync<T>(string sekibanIdentifier, Func<Task<T>> action);
        public Task SekibanActionAsync(string sekibanIdentifier, Func<Task> action);
    }
}
