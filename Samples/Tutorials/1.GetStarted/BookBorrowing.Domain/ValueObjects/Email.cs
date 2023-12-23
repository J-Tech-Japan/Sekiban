using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
namespace BookBorrowing.Domain.ValueObjects;

public record Email([property: EmailAddress]string Value);
