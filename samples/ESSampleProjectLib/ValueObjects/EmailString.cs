using System.Text.RegularExpressions;

namespace ESSampleProjectLib.ValueObjects
{
    public class EmailString : IValueObject<string>
    {
        public string Value { get; } = null!;

        public EmailString(string email)
        {
            if (!Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase))
                throw new InvalidValueException("Eメールアドレスが正しくありません。");

            Value = email;
        }

        public static implicit operator string(EmailString vo) => vo.Value;
        public static implicit operator EmailString(string v) => new(v);
    }
}
