namespace Sekiban.EventSourcing.Addon.Tenant.Exceptions
{
    public class SekibanTenantNotExistsException : Exception
    {
        public SekibanTenantNotExistsException(Guid tenantId) : base($"Tenant {tenantId} not exists") { }
    }
}
