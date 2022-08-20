namespace Sekiban.EventSourcing.Addon.Tenant.ValueObjects.Bases
{
    public abstract record class SingleValueObjectClassBase<T> : ISingleValueObject<T>
        where T : class
    {
        protected readonly T _value;

        public SingleValueObjectClassBase(T value)
        {
            _value = value;
            Validate();
        }
        public T Value
        {
            get => _value;
        }

        protected abstract void Validate();
    }
}