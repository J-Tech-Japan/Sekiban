using System.Text.RegularExpressions;
namespace ESSampleProjectLib.ValueObjects;

public record EmailString : IValueObject<string>
{
    public EmailString(string email)
    {
        if (!Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250)))
        {
            throw new InvalidValueException("The email address is not correct.");
        }

        Value = email;
    }

    public string Value { get; } = null!;

    public static implicit operator string(EmailString vo) => vo.Value;

    public static implicit operator EmailString(string v) => new(v);
}
