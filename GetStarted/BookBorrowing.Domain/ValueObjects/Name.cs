using System.ComponentModel.DataAnnotations;
namespace BookBorrowing.Domain.ValueObjects;

public record Name(
    [property: Required]
    [property: MaxLength(50)]
    string FirstName,
    [property: Required]
    [property: MaxLength(50)]
    string LastName,
    [property: MaxLength(50)]
    string? MiddleName = null);
