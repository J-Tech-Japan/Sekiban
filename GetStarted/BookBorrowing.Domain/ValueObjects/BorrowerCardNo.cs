using System.ComponentModel.DataAnnotations;
namespace BookBorrowing.Domain.ValueObjects;

public record BorrowerCardNo(
    [property: Range(100000, 999999)]
    int Value);
