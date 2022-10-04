using Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanMembers;
using Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanMembers.Commands;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.Validations;
using System;
using System.Collections.Generic;
using Xunit;
namespace CustomerWithTenantAddonXTest.AggregateTests;

public class SekibanMemberSpec : AggregateTestBase<SekibanMember, SekibanMemberContents>
{
    public static Guid MemberId = Guid.NewGuid();
    public static string MemberName = "Sekiban Member";
    public static string MemberEmail = "sekiban@example.com";
    public static string UniqueId = "sekiban";
    [Fact]
    public void CreateMember()
    {
        // GivenEnvironmentDtoContents<SekibanTenant, SekibanTenantContents>(
        //         TenantSpec.TenantId,
        //         new SekibanTenantContents(TenantSpec.TenantName, TenantSpec.TenantCode))
        WhenCreate(new CreateSekibanMember(TenantSpec.TenantId, MemberId, MemberName, MemberEmail, UniqueId))
            .ThenNotThrowsAnException()
            .ThenState(member => new AggregateDto<SekibanMemberContents>(member, new SekibanMemberContents(MemberName, MemberEmail, UniqueId)));
    }
    [Fact]
    public void CreateMemberFailedWithGuid()
    {
        // GivenEnvironmentDtoContents<SekibanTenant, SekibanTenantContents>(
        //         TenantSpec.TenantId,
        //         new SekibanTenantContents(TenantSpec.TenantName, TenantSpec.TenantCode))
        WhenCreate(new CreateSekibanMember(TenantSpec.TenantId, Guid.Empty, MemberName, MemberEmail, UniqueId))
            .ThenHasValidationErrors(
                new List<SekibanValidationParameterError>
                {
                    new(nameof(CreateSekibanMember.SekibanMemberId), new List<string> { "SekibanMemberId is empty." })
                });
    }
    [Fact]
    public void CreateMemberFailedWithRegex()
    {
        // GivenEnvironmentDtoContents<SekibanTenant, SekibanTenantContents>(
        //         TenantSpec.TenantId,
        //         new SekibanTenantContents(TenantSpec.TenantName, TenantSpec.TenantCode))
        WhenCreate(new CreateSekibanMember(TenantSpec.TenantId, MemberId, MemberName, MemberEmail, "//''''"))
            .ThenHasValidationErrors(
                new List<SekibanValidationParameterError>
                {
                    new(
                        nameof(CreateSekibanMember.UniqueIdentifier),
                        new List<string> { "Only alphanumeric characters, dashes and underscores are allowed" })
                });
    }
}
