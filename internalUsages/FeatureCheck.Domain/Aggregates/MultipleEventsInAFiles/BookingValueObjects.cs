namespace FeatureCheck.Domain.Aggregates.MultipleEventsInAFiles;

public static class BookingValueObjects
{
    public record RoomNumber(int Value);
    public record GuestName(string FirstName, string LastName, string? MiddleName = null);
    public record DateOnly(DateTime Value)
    {
        public static DateOnly CreateFromDateTime(DateTime dateTime) => new(dateTime.Date);
    }
    public record Currency(string Value)
    {
        public static Currency USD = new("USD");
        public static Currency EUR = new("EUR");
        public static Currency JPY = new("JPY");
    }
    public record Money(decimal Value, Currency Currency)
    {
        public static Money ZeroMoney = new(0, Currency.USD);
        public static Money USDMoney(decimal value) => new(value, Currency.USD);
        public static Money EurMoney(decimal value) => new(value, Currency.EUR);
        public static Money JPYMoney(decimal value) => new(value, Currency.JPY);

        public bool CanAdd(Money other) => Value == 0 || Currency == other.Currency;
        public Money Add(Money other) =>
            Currency == other.Currency ? new Money(Value + other.Value, Currency) :
            Value == 0 ? other : throw new InvalidOperationException("Cannot add money with different currencies");
        public Money AddIfPossible(Money other) => CanAdd(other) ? Add(other) : this;
        public bool IsEqualOrGreaterThan(Money other) => Currency == other.Currency && Value >= other.Value;
        public bool IsGreaterThan(Money other) => Currency == other.Currency && Value > other.Value;
    }
}
