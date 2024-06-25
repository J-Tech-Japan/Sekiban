using System.ComponentModel.DataAnnotations;
namespace BookBorrowing.Domain.ValueObjects;

public record PhoneNumber(
    [property: Phone]
    string Value);
