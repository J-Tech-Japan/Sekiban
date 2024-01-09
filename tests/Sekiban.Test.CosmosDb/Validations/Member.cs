using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
namespace Sekiban.Test.CosmosDb.Validations;

public class Member
{
    [Required]
    [MaxLength(20)]
    public string Name { get; init; } = default!;

    [Required]
    [Range(18, 75)]
    public int Age { get; init; }

    public int? No { get; init; }

    [Phone]
    [MaxLength(15)]
    [MinLength(10)]
    public string? Tel { get; init; }

    [EmailAddress]
    [MaxLength(254)]
    [MinLength(8)]
    public string? Email { get; init; }

    public Member? Partner { get; init; }

    public List<Member> Friends { get; init; } = [];
}
