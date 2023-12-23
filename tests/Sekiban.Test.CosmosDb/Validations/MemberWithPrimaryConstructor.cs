using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
namespace Sekiban.Test.CosmosDb.Validations;

public record MemberWithPrimaryConstructor(
    [property: Required]
    [property: MaxLength(20)]
    string Name,
    [property: Required]
    [property: Range(18, 75)]
    int Age,
    [property: Phone]
    [property: MaxLength(15)]
    [property: MinLength(10)]
    string? Tel,
    [property: EmailAddress]
    [property: MaxLength(254)]
    [property: MinLength(8)]
    string? Email,
    MemberWithPrimaryConstructor? Partner,
    ImmutableList<MemberWithPrimaryConstructor> Friends);
