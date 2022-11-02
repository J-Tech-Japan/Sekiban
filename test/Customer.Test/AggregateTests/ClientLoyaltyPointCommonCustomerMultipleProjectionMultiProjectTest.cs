using Customer.Domain.Aggregates.Branches.Events;
using Customer.Domain.Projections.ClientLoyaltyPointMultiples;
using Customer.Domain.Shared;
using Sekiban.Core.Query.MultipleProjections;
using Sekiban.Testing.Projection;
using Sekiban.Testing.Queries;
using System;
using System.Collections.Immutable;
using Xunit;
namespace Customer.Test.AggregateTests;

public class ClientLoyaltyPointCommonCustomerMultipleProjectionMultiProjectTest : MultiProjectionMultiProjectTestBase<ClientLoyaltyPointMultipleProjection
    , ClientLoyaltyPointMultipleProjection.PayloadDefinition, CustomerDependency>
{
    private static readonly Guid branchId = Guid.Parse("b4a3c2e3-78ca-473b-8afb-f534e5d6d66b");
    private static readonly string branchName = "Test Branch";

    private readonly MultiProjectionQueryTest<ClientLoyaltyPointMultipleProjection, ClientLoyaltyPointMultipleProjection.PayloadDefinition,
        ClientLoyaltyPointMultipleMultiProjectionQuery, ClientLoyaltyPointMultipleMultiProjectionQuery.QueryParameter,
        ClientLoyaltyPointMultipleProjection.PayloadDefinition> multiProjectionQueryTest = new();

    [Fact]
    public void ProjectionTest()
    {
        GivenEvents((branchId, new BranchCreated(branchName)))
            .WhenProjection()
            .ThenNotThrowsAnException()
            .ThenStateIs(
                new MultiProjectionState<ClientLoyaltyPointMultipleProjection.PayloadDefinition>(
                    new ClientLoyaltyPointMultipleProjection.PayloadDefinition(
                        ImmutableList<ClientLoyaltyPointMultipleProjection.ProjectedBranch>.Empty.Add(
                            new ClientLoyaltyPointMultipleProjection.ProjectedBranch(branchId, branchName)),
                        ImmutableList<ClientLoyaltyPointMultipleProjection.ProjectedRecord>.Empty),
                    Guid.Empty,
                    string.Empty,
                    0,
                    0))
            .ThenGetQueryTest<ClientLoyaltyPointMultipleMultiProjectionQuery, ClientLoyaltyPointMultipleMultiProjectionQuery.QueryParameter,
                ClientLoyaltyPointMultipleProjection.PayloadDefinition>(
                test => test.WhenParam(
                        new ClientLoyaltyPointMultipleMultiProjectionQuery.QueryParameter(
                            null,
                            ClientLoyaltyPointMultipleMultiProjectionQuery.QuerySortKeys.ClientName))
                    .ThenResponseIs(
                        new ClientLoyaltyPointMultipleProjection.PayloadDefinition(
                            ImmutableList<ClientLoyaltyPointMultipleProjection.ProjectedBranch>.Empty.Add(
                                new ClientLoyaltyPointMultipleProjection.ProjectedBranch(branchId, branchName)),
                            ImmutableList<ClientLoyaltyPointMultipleProjection.ProjectedRecord>.Empty)));

    }

    [Fact]
    public void JsonEventsTest()
    {
        #region json
        GivenEventsFromJson(
                @"
[
    {
        ""Payload"": {
            ""Name"": ""JAPAN""
        },
        ""AggregateType"": ""Branch"",
        ""IsAggregateInitialEvent"": true,
        ""Version"": 1,
        ""CallHistories"": [
            {
                ""Id"": ""591d1220-21a5-4d4a-8b02-a17ecb151b08"",
                ""TypeName"": ""CreateBranch"",
                ""ExecutedUser"": ""Unauthenticated User from ::1""
            }
        ],
        ""id"": ""0b875347-3001-46c2-b71f-4443f0ce139c"",
        ""AggregateId"": ""b4a3c2e3-78ca-473b-8afb-f534e5d6d66b"",
        ""PartitionKey"": ""Branch_b4a3c2e3-78ca-473b-8afb-f534e5d6d66b"",
        ""DocumentType"": 2,
        ""DocumentTypeName"": ""BranchCreated"",
        ""TimeStamp"": ""2022-07-26T18:08:17.130777Z"",
        ""SortableUniqueId"": ""637944556971307770001844268543"",
        ""_rid"": ""f+UeAM+8kXwBAAAAAAAAAA=="",
        ""_self"": ""dbs/f+UeAA==/colls/f+UeAM+8kXw=/docs/f+UeAM+8kXwBAAAAAAAAAA==/"",
        ""_etag"": ""\""6201587e-0000-0700-0000-62e02d930000\"""",
        ""_attachments"": ""attachments/"",
        ""_ts"": 1658858899
    },
    {
        ""Payload"": {
            ""Name"": ""usa""
        },
        ""AggregateType"": ""Branch"",
        ""IsAggregateInitialEvent"": true,
        ""Version"": 1,
        ""CallHistories"": [
            {
                ""Id"": ""31511aa5-48bb-4d83-8497-1cd2a644937f"",
                ""TypeName"": ""CreateBranch"",
                ""ExecutedUser"": ""Unauthenticated User from 127.0.0.1""
            }
        ],
        ""id"": ""128600f4-1bc4-456d-a25f-852593e4db26"",
        ""AggregateId"": ""ae5e57f7-9690-4da2-a1f0-62869227f12e"",
        ""PartitionKey"": ""Branch_ae5e57f7-9690-4da2-a1f0-62869227f12e"",
        ""DocumentType"": 2,
        ""DocumentTypeName"": ""BranchCreated"",
        ""TimeStamp"": ""2022-07-26T20:01:41.224555Z"",
        ""SortableUniqueId"": ""637944625012245550001421189121"",
        ""_rid"": ""f+UeAM+8kXwCAAAAAAAAAA=="",
        ""_self"": ""dbs/f+UeAA==/colls/f+UeAM+8kXw=/docs/f+UeAM+8kXwCAAAAAAAAAA==/"",
        ""_etag"": ""\""64015e6b-0000-0700-0000-62e048270000\"""",
        ""_attachments"": ""attachments/"",
        ""_ts"": 1658865703
    },
    {
        ""Payload"": {
            ""Name"": ""usa""
        },
        ""AggregateType"": ""Branch"",
        ""IsAggregateInitialEvent"": true,
        ""Version"": 1,
        ""CallHistories"": [
            {
                ""Id"": ""094a2f05-d277-45b7-a1c4-7b8e32bd2a93"",
                ""TypeName"": ""CreateBranch"",
                ""ExecutedUser"": ""Unauthenticated User from 127.0.0.1""
            }
        ],
        ""id"": ""16a5827b-fb18-491e-8dd8-efc9e8344d42"",
        ""AggregateId"": ""bb365918-e3a2-4078-b86c-1eaabd9b2dea"",
        ""PartitionKey"": ""Branch_bb365918-e3a2-4078-b86c-1eaabd9b2dea"",
        ""DocumentType"": 2,
        ""DocumentTypeName"": ""BranchCreated"",
        ""TimeStamp"": ""2022-07-26T20:32:11.334098Z"",
        ""SortableUniqueId"": ""637944643313340980000736520954"",
        ""_rid"": ""f+UeAM+8kXwDAAAAAAAAAA=="",
        ""_self"": ""dbs/f+UeAA==/colls/f+UeAM+8kXw=/docs/f+UeAM+8kXwDAAAAAAAAAA==/"",
        ""_etag"": ""\""64016bbd-0000-0700-0000-62e04f4e0000\"""",
        ""_attachments"": ""attachments/"",
        ""_ts"": 1658867534
    },
    {
        ""Payload"": {
            ""Name"": ""string""
        },
        ""AggregateType"": ""Branch"",
        ""IsAggregateInitialEvent"": true,
        ""Version"": 1,
        ""CallHistories"": [
            {
                ""Id"": ""bae4e57c-d37d-4aba-a3d8-fa324867a00d"",
                ""TypeName"": ""CreateBranch"",
                ""ExecutedUser"": ""Unauthenticated User from ::1""
            }
        ],
        ""id"": ""7c0ab1c9-cdd0-4aae-8356-068f2541b273"",
        ""AggregateId"": ""6711917c-05f6-4294-a368-a451a3c90d1e"",
        ""PartitionKey"": ""Branch_6711917c-05f6-4294-a368-a451a3c90d1e"",
        ""DocumentType"": 2,
        ""DocumentTypeName"": ""BranchCreated"",
        ""TimeStamp"": ""2022-07-26T22:23:25.25571Z"",
        ""SortableUniqueId"": ""637944710052557100000904893505"",
        ""_rid"": ""f+UeAM+8kXwEAAAAAAAAAA=="",
        ""_self"": ""dbs/f+UeAA==/colls/f+UeAM+8kXw=/docs/f+UeAM+8kXwEAAAAAAAAAA==/"",
        ""_etag"": ""\""6501c3dd-0000-0700-0000-62e069610000\"""",
        ""_attachments"": ""attachments/"",
        ""_ts"": 1658874209
    },
    {
        ""Payload"": {
            ""Name"": ""string""
        },
        ""AggregateType"": ""Branch"",
        ""IsAggregateInitialEvent"": true,
        ""Version"": 1,
        ""CallHistories"": [
            {
                ""Id"": ""4aa03c5d-2f8f-4529-822f-5d950917f033"",
                ""TypeName"": ""CreateBranch"",
                ""ExecutedUser"": ""Unauthenticated User from ::1""
            }
        ],
        ""id"": ""b4cd4ed4-a9f7-4f47-9115-d54aaa85b68c"",
        ""AggregateId"": ""7d24462d-392c-40ff-9aef-62c0c79745fc"",
        ""PartitionKey"": ""Branch_7d24462d-392c-40ff-9aef-62c0c79745fc"",
        ""DocumentType"": 2,
        ""DocumentTypeName"": ""BranchCreated"",
        ""TimeStamp"": ""2022-07-26T22:25:40.363639Z"",
        ""SortableUniqueId"": ""637944711403636390001038710552"",
        ""_rid"": ""f+UeAM+8kXwFAAAAAAAAAA=="",
        ""_self"": ""dbs/f+UeAA==/colls/f+UeAM+8kXw=/docs/f+UeAM+8kXwFAAAAAAAAAA==/"",
        ""_etag"": ""\""6501dbe1-0000-0700-0000-62e069f20000\"""",
        ""_attachments"": ""attachments/"",
        ""_ts"": 1658874354
    },
    {
        ""Payload"": {
            ""Name"": ""string""
        },
        ""AggregateType"": ""Branch"",
        ""IsAggregateInitialEvent"": true,
        ""Version"": 1,
        ""CallHistories"": [
            {
                ""Id"": ""b5b840c7-e1b2-4e1c-9a89-cf93f8681ceb"",
                ""TypeName"": ""CreateBranch"",
                ""ExecutedUser"": ""Unauthenticated User from ::1""
            }
        ],
        ""id"": ""f5ed453f-d1d8-4aaa-81e7-ae0778cc8ad4"",
        ""AggregateId"": ""ea595694-e627-4770-a2b2-292c4f0f994e"",
        ""PartitionKey"": ""Branch_ea595694-e627-4770-a2b2-292c4f0f994e"",
        ""DocumentType"": 2,
        ""DocumentTypeName"": ""BranchCreated"",
        ""TimeStamp"": ""2022-07-26T22:27:11.29169Z"",
        ""SortableUniqueId"": ""637944712312916900001818476318"",
        ""_rid"": ""f+UeAM+8kXwGAAAAAAAAAA=="",
        ""_self"": ""dbs/f+UeAA==/colls/f+UeAM+8kXw=/docs/f+UeAM+8kXwGAAAAAAAAAA==/"",
        ""_etag"": ""\""6501d6e3-0000-0700-0000-62e06a420000\"""",
        ""_attachments"": ""attachments/"",
        ""_ts"": 1658874434
    },
    {
        ""Payload"": {
            ""Name"": ""string""
        },
        ""AggregateType"": ""Branch"",
        ""IsAggregateInitialEvent"": true,
        ""Version"": 1,
        ""CallHistories"": [
            {
                ""Id"": ""4ae64bfd-cd12-42d2-aa91-5878ca0302a7"",
                ""TypeName"": ""CreateBranch"",
                ""ExecutedUser"": ""Unauthenticated User from ::1""
            }
        ],
        ""id"": ""d2ff52e7-c8d6-4d89-a0b4-4b819aaa3104"",
        ""AggregateId"": ""049f54c7-20d8-4832-96e4-1b1504eddfef"",
        ""PartitionKey"": ""Branch_049f54c7-20d8-4832-96e4-1b1504eddfef"",
        ""DocumentType"": 2,
        ""DocumentTypeName"": ""BranchCreated"",
        ""TimeStamp"": ""2022-07-26T22:38:58.374663Z"",
        ""SortableUniqueId"": ""637944719383746630000437027851"",
        ""_rid"": ""f+UeAM+8kXwHAAAAAAAAAA=="",
        ""_self"": ""dbs/f+UeAA==/colls/f+UeAM+8kXw=/docs/f+UeAM+8kXwHAAAAAAAAAA==/"",
        ""_etag"": ""\""66012604-0000-0700-0000-62e06d050000\"""",
        ""_attachments"": ""attachments/"",
        ""_ts"": 1658875141
    },
    {
        ""Payload"": {
            ""BranchId"": ""b4a3c2e3-78ca-473b-8afb-f534e5d6d66b"",
            ""ClientName"": ""Tomo"",
            ""ClientEmail"": ""tomo@jtechs.com""
        },
        ""AggregateType"": ""Client"",
        ""IsAggregateInitialEvent"": true,
        ""Version"": 1,
        ""CallHistories"": [
            {
                ""Id"": ""11080ae8-aa23-4e4c-ac2e-86e5491443bf"",
                ""TypeName"": ""CreateClient"",
                ""ExecutedUser"": ""Unauthenticated User from ::1""
            }
        ],
        ""id"": ""ed611e3a-4ffb-4157-80c4-225e310b17e8"",
        ""AggregateId"": ""63ec28b1-d395-4632-9998-90d3f2df2940"",
        ""PartitionKey"": ""Client_63ec28b1-d395-4632-9998-90d3f2df2940"",
        ""DocumentType"": 2,
        ""DocumentTypeName"": ""ClientCreated"",
        ""TimeStamp"": ""2022-07-26T23:04:33.94134Z"",
        ""SortableUniqueId"": ""637944734739413400000436444784"",
        ""_rid"": ""f+UeAM+8kXwIAAAAAAAAAA=="",
        ""_self"": ""dbs/f+UeAA==/colls/f+UeAM+8kXw=/docs/f+UeAM+8kXwIAAAAAAAAAA==/"",
        ""_etag"": ""\""6601c83d-0000-0700-0000-62e073020000\"""",
        ""_attachments"": ""attachments/"",
        ""_ts"": 1658876674
    },
    {
        ""Payload"": {
            ""InitialPoint"": 0
        },
        ""AggregateType"": ""LoyaltyPoint"",
        ""IsAggregateInitialEvent"": true,
        ""Version"": 1,
        ""CallHistories"": [
            {
                ""Id"": ""11080ae8-aa23-4e4c-ac2e-86e5491443bf"",
                ""TypeName"": ""CreateClient"",
                ""ExecutedUser"": ""Unauthenticated User from ::1""
            },
            {
                ""Id"": ""ed611e3a-4ffb-4157-80c4-225e310b17e8"",
                ""TypeName"": ""Event`1"",
                ""ExecutedUser"": """"
            },
            {
                ""Id"": ""e8654fa4-7466-462b-b2dd-388042c92d8f"",
                ""TypeName"": ""CreateLoyaltyPoint"",
                ""ExecutedUser"": ""Unauthenticated User from ::1""
            }
        ],
        ""id"": ""720c6436-9e69-4b61-950f-7fc46a67f4cb"",
        ""AggregateId"": ""63ec28b1-d395-4632-9998-90d3f2df2940"",
        ""PartitionKey"": ""LoyaltyPoint_63ec28b1-d395-4632-9998-90d3f2df2940"",
        ""DocumentType"": 2,
        ""DocumentTypeName"": ""LoyaltyPointCreated"",
        ""TimeStamp"": ""2022-07-26T23:04:34.434348Z"",
        ""SortableUniqueId"": ""637944734744343480000921080480"",
        ""_rid"": ""f+UeAM+8kXwJAAAAAAAAAA=="",
        ""_self"": ""dbs/f+UeAA==/colls/f+UeAM+8kXw=/docs/f+UeAM+8kXwJAAAAAAAAAA==/"",
        ""_etag"": ""\""6601d93d-0000-0700-0000-62e073020000\"""",
        ""_attachments"": ""attachments/"",
        ""_ts"": 1658876674
    },
    {
        ""Payload"": {
            ""ClientName"": ""new name""
        },
        ""AggregateType"": ""Client"",
        ""IsAggregateInitialEvent"": false,
        ""Version"": 2,
        ""CallHistories"": [
            {
                ""Id"": ""4c4f1de2-ca64-4412-85f1-d69091ce535c"",
                ""TypeName"": ""ChangeClientName"",
                ""ExecutedUser"": ""Unauthenticated User from ::1""
            }
        ],
        ""id"": ""897d6fd5-6fa2-4a83-b03e-1a79fd7a1bfe"",
        ""AggregateId"": ""63ec28b1-d395-4632-9998-90d3f2df2940"",
        ""PartitionKey"": ""Client_63ec28b1-d395-4632-9998-90d3f2df2940"",
        ""DocumentType"": 2,
        ""DocumentTypeName"": ""ClientNameChanged"",
        ""TimeStamp"": ""2022-07-26T23:10:34.914223Z"",
        ""SortableUniqueId"": ""637944738349142230001157579834"",
        ""_rid"": ""f+UeAM+8kXwKAAAAAAAAAA=="",
        ""_self"": ""dbs/f+UeAA==/colls/f+UeAM+8kXw=/docs/f+UeAM+8kXwKAAAAAAAAAA==/"",
        ""_etag"": ""\""66015b49-0000-0700-0000-62e0746b0000\"""",
        ""_attachments"": ""attachments/"",
        ""_ts"": 1658877035
    },
    {
        ""Payload"": {
            ""ClientName"": ""V3""
        },
        ""AggregateType"": ""Client"",
        ""IsAggregateInitialEvent"": false,
        ""Version"": 3,
        ""CallHistories"": [
            {
                ""Id"": ""cd4a31e2-7796-4e81-9728-8376609db49a"",
                ""TypeName"": ""ChangeClientName"",
                ""ExecutedUser"": ""Unauthenticated User from ::1""
            }
        ],
        ""id"": ""bbe83532-df78-470f-94b1-0fd48262b1d1"",
        ""AggregateId"": ""63ec28b1-d395-4632-9998-90d3f2df2940"",
        ""PartitionKey"": ""Client_63ec28b1-d395-4632-9998-90d3f2df2940"",
        ""DocumentType"": 2,
        ""DocumentTypeName"": ""ClientNameChanged"",
        ""TimeStamp"": ""2022-07-27T18:23:09.747931Z"",
        ""SortableUniqueId"": ""637945429897479310000111593124"",
        ""_rid"": ""f+UeAM+8kXwLAAAAAAAAAA=="",
        ""_self"": ""dbs/f+UeAA==/colls/f+UeAM+8kXw=/docs/f+UeAM+8kXwLAAAAAAAAAA==/"",
        ""_etag"": ""\""7901501c-0000-0700-0000-62e1828e0000\"""",
        ""_attachments"": ""attachments/"",
        ""_ts"": 1658946190
    },
    {
        ""Payload"": {
            ""Name"": ""3333""
        },
        ""AggregateType"": ""Branch"",
        ""IsAggregateInitialEvent"": true,
        ""Version"": 1,
        ""CallHistories"": [
            {
                ""Id"": ""b4804d76-91e8-4435-962e-316a2131f3eb"",
                ""TypeName"": ""CreateBranch"",
                ""ExecutedUser"": ""Unauthenticated User from ::1""
            }
        ],
        ""id"": ""45bf2e76-f6c6-4e8b-86aa-48e0551767c5"",
        ""AggregateId"": ""13b2fbe5-58ab-4d7b-8246-318991231e93"",
        ""PartitionKey"": ""Branch_13b2fbe5-58ab-4d7b-8246-318991231e93"",
        ""DocumentType"": 2,
        ""DocumentTypeName"": ""BranchCreated"",
        ""TimeStamp"": ""2022-07-27T20:16:04.080445Z"",
        ""SortableUniqueId"": ""637945497640804450000773547363"",
        ""_rid"": ""f+UeAM+8kXwMAAAAAAAAAA=="",
        ""_self"": ""dbs/f+UeAA==/colls/f+UeAM+8kXw=/docs/f+UeAM+8kXwMAAAAAAAAAA==/"",
        ""_etag"": ""\""7a01c8cc-0000-0700-0000-62e19d060000\"""",
        ""_attachments"": ""attachments/"",
        ""_ts"": 1658952966
    },
    {
        ""Payload"": {
            ""Name"": ""string""
        },
        ""AggregateType"": ""Branch"",
        ""IsAggregateInitialEvent"": true,
        ""Version"": 1,
        ""CallHistories"": [
            {
                ""Id"": ""994a16a8-ad14-427f-a4e8-3b9752e855f5"",
                ""TypeName"": ""CreateBranch"",
                ""ExecutedUser"": ""Unauthenticated User from ::1""
            }
        ],
        ""id"": ""3b01374b-c805-4b60-9556-fe8a6ea27f7c"",
        ""AggregateId"": ""306aa36f-71ba-4b6d-8f18-5a70ddc8288b"",
        ""PartitionKey"": ""Branch_306aa36f-71ba-4b6d-8f18-5a70ddc8288b"",
        ""DocumentType"": 2,
        ""DocumentTypeName"": ""BranchCreated"",
        ""TimeStamp"": ""2022-07-28T19:20:54.160052Z"",
        ""SortableUniqueId"": ""637946328541600520002032137291"",
        ""_rid"": ""f+UeAM+8kXwNAAAAAAAAAA=="",
        ""_self"": ""dbs/f+UeAA==/colls/f+UeAM+8kXw=/docs/f+UeAM+8kXwNAAAAAAAAAA==/"",
        ""_etag"": ""\""9201f465-0000-0700-0000-62e2e1980000\"""",
        ""_attachments"": ""attachments/"",
        ""_ts"": 1659036056
    },
    {
        ""Payload"": {
            ""Name"": ""string""
        },
        ""AggregateType"": ""Branch"",
        ""IsAggregateInitialEvent"": true,
        ""Version"": 1,
        ""CallHistories"": [
            {
                ""Id"": ""ca383db8-25a4-485a-8c18-76922542acac"",
                ""TypeName"": ""CreateBranch"",
                ""ExecutedUser"": ""Unauthenticated User from ::1""
            }
        ],
        ""id"": ""18366bed-4a6d-448f-b1bf-8b3d20de7bcf"",
        ""AggregateId"": ""8613b78b-694a-485a-acee-1ba88edddf87"",
        ""PartitionKey"": ""Branch_8613b78b-694a-485a-acee-1ba88edddf87"",
        ""DocumentType"": 2,
        ""DocumentTypeName"": ""BranchCreated"",
        ""TimeStamp"": ""2022-07-28T19:37:32.050974Z"",
        ""SortableUniqueId"": ""637946338520509740001370931183"",
        ""_rid"": ""f+UeAM+8kXwOAAAAAAAAAA=="",
        ""_self"": ""dbs/f+UeAA==/colls/f+UeAM+8kXw=/docs/f+UeAM+8kXwOAAAAAAAAAA==/"",
        ""_etag"": ""\""92018fbd-0000-0700-0000-62e2e57e0000\"""",
        ""_attachments"": ""attachments/"",
        ""_ts"": 1659037054
    },
    {
        ""Payload"": {
            ""Name"": ""string""
        },
        ""AggregateType"": ""Branch"",
        ""IsAggregateInitialEvent"": true,
        ""Version"": 1,
        ""CallHistories"": [
            {
                ""Id"": ""07fc5e96-d877-4b93-a896-1049527a3a51"",
                ""TypeName"": ""CreateBranch"",
                ""ExecutedUser"": ""Unauthenticated User from ::1""
            }
        ],
        ""id"": ""33e8b890-9aa3-442e-9158-675f08f2b502"",
        ""AggregateId"": ""35399b62-87cc-408e-b6d0-71e52c8105db"",
        ""PartitionKey"": ""Branch_35399b62-87cc-408e-b6d0-71e52c8105db"",
        ""DocumentType"": 2,
        ""DocumentTypeName"": ""BranchCreated"",
        ""TimeStamp"": ""2022-07-28T20:08:18.902825Z"",
        ""SortableUniqueId"": ""637946356989028250000705988778"",
        ""_rid"": ""f+UeAM+8kXwPAAAAAAAAAA=="",
        ""_self"": ""dbs/f+UeAA==/colls/f+UeAM+8kXw=/docs/f+UeAM+8kXwPAAAAAAAAAA==/"",
        ""_etag"": ""\""93015b4d-0000-0700-0000-62e2ecb50000\"""",
        ""_attachments"": ""attachments/"",
        ""_ts"": 1659038901
    },
    {
        ""Payload"": {
            ""Name"": ""string""
        },
        ""AggregateType"": ""Branch"",
        ""IsAggregateInitialEvent"": true,
        ""Version"": 1,
        ""CallHistories"": [
            {
                ""Id"": ""a31f88bb-ca7c-44e3-90c0-f2658b2cc653"",
                ""TypeName"": ""CreateBranch"",
                ""ExecutedUser"": ""Unauthenticated User from ::1""
            }
        ],
        ""id"": ""96b3f994-493a-48f6-9ec1-29a0e177a514"",
        ""AggregateId"": ""dbb1b167-4ea1-4da4-9f1c-fb5566e829b4"",
        ""PartitionKey"": ""Branch_dbb1b167-4ea1-4da4-9f1c-fb5566e829b4"",
        ""DocumentType"": 2,
        ""DocumentTypeName"": ""BranchCreated"",
        ""TimeStamp"": ""2022-07-28T20:10:11.508338Z"",
        ""SortableUniqueId"": ""637946358115083380001791559377"",
        ""_rid"": ""f+UeAM+8kXwQAAAAAAAAAA=="",
        ""_self"": ""dbs/f+UeAA==/colls/f+UeAM+8kXw=/docs/f+UeAM+8kXwQAAAAAAAAAA==/"",
        ""_etag"": ""\""9301e855-0000-0700-0000-62e2ed250000\"""",
        ""_attachments"": ""attachments/"",
        ""_ts"": 1659039013
    },
    {
        ""Payload"": {
            ""Name"": ""string22""
        },
        ""AggregateType"": ""Branch"",
        ""IsAggregateInitialEvent"": true,
        ""Version"": 1,
        ""CallHistories"": [
            {
                ""Id"": ""03fc7bc4-4163-40b2-a025-2a9f36c614d7"",
                ""TypeName"": ""CreateBranch"",
                ""ExecutedUser"": ""Unauthenticated User from ::1""
            }
        ],
        ""id"": ""ad5b108d-8df3-41a5-99b1-d8a3110ff21d"",
        ""AggregateId"": ""795c5678-0b79-4e6a-a30a-3e23263cb50e"",
        ""PartitionKey"": ""Branch_795c5678-0b79-4e6a-a30a-3e23263cb50e"",
        ""DocumentType"": 2,
        ""DocumentTypeName"": ""BranchCreated"",
        ""TimeStamp"": ""2022-07-29T06:03:55.217729Z"",
        ""SortableUniqueId"": ""637946714352177290001389634550"",
        ""_rid"": ""f+UeAM+8kXwRAAAAAAAAAA=="",
        ""_self"": ""dbs/f+UeAA==/colls/f+UeAM+8kXw=/docs/f+UeAM+8kXwRAAAAAAAAAA==/"",
        ""_etag"": ""\""9b016d96-0000-0700-0000-62e3784d0000\"""",
        ""_attachments"": ""attachments/"",
        ""_ts"": 1659074637
    },
    {
        ""Payload"": {
            ""Name"": ""string""
        },
        ""AggregateType"": ""Branch"",
        ""IsAggregateInitialEvent"": true,
        ""Version"": 1,
        ""CallHistories"": [
            {
                ""Id"": ""d1c8edda-2724-4e36-80d7-9384b535365d"",
                ""TypeName"": ""CreateBranch"",
                ""ExecutedUser"": ""Unauthenticated User from ::1""
            }
        ],
        ""id"": ""39938608-8f62-4ef1-9543-20f2d2988c20"",
        ""AggregateId"": ""e2e336ec-113d-4a12-900d-a4705dd6cc78"",
        ""PartitionKey"": ""Branch_e2e336ec-113d-4a12-900d-a4705dd6cc78"",
        ""DocumentType"": 2,
        ""DocumentTypeName"": ""BranchCreated"",
        ""TimeStamp"": ""2022-07-29T07:54:33.120006Z"",
        ""SortableUniqueId"": ""637946780731200060001513172435"",
        ""_rid"": ""f+UeAM+8kXwSAAAAAAAAAA=="",
        ""_self"": ""dbs/f+UeAA==/colls/f+UeAM+8kXw=/docs/f+UeAM+8kXwSAAAAAAAAAA==/"",
        ""_etag"": ""\""9d01c6ca-0000-0700-0000-62e3923b0000\"""",
        ""_attachments"": ""attachments/"",
        ""_ts"": 1659081275
    },
    {
        ""Payload"": {
            ""ClientName"": ""v4 name""
        },
        ""AggregateType"": ""Client"",
        ""IsAggregateInitialEvent"": false,
        ""Version"": 4,
        ""CallHistories"": [
            {
                ""Id"": ""7871d0dd-5d50-459a-a126-a0854c23cc44"",
                ""TypeName"": ""ChangeClientName"",
                ""ExecutedUser"": ""Unauthenticated User from ::1""
            }
        ],
        ""id"": ""3d82a6b4-4f33-4283-ae0a-1a725bee9b6d"",
        ""AggregateId"": ""63ec28b1-d395-4632-9998-90d3f2df2940"",
        ""PartitionKey"": ""Client_63ec28b1-d395-4632-9998-90d3f2df2940"",
        ""DocumentType"": 2,
        ""DocumentTypeName"": ""ClientNameChanged"",
        ""TimeStamp"": ""2022-07-29T23:42:23.709438Z"",
        ""SortableUniqueId"": ""637947349437094380001619004786"",
        ""_rid"": ""f+UeAM+8kXwTAAAAAAAAAA=="",
        ""_self"": ""dbs/f+UeAA==/colls/f+UeAM+8kXw=/docs/f+UeAM+8kXwTAAAAAAAAAA==/"",
        ""_etag"": ""\""ac01b152-0000-0700-0000-62e4705f0000\"""",
        ""_attachments"": ""attachments/"",
        ""_ts"": 1659138143
    },
    {
        ""Payload"": {
            ""ClientName"": ""new name 5""
        },
        ""AggregateType"": ""Client"",
        ""IsAggregateInitialEvent"": false,
        ""Version"": 5,
        ""CallHistories"": [
            {
                ""Id"": ""363b5ae1-677a-47e2-990d-2707c82bbd73"",
                ""TypeName"": ""ChangeClientName"",
                ""ExecutedUser"": ""Unauthenticated User from ::1""
            }
        ],
        ""id"": ""f2988562-ad9f-47d7-895e-db1c58240e1e"",
        ""AggregateId"": ""63ec28b1-d395-4632-9998-90d3f2df2940"",
        ""PartitionKey"": ""Client_63ec28b1-d395-4632-9998-90d3f2df2940"",
        ""DocumentType"": 2,
        ""DocumentTypeName"": ""ClientNameChanged"",
        ""TimeStamp"": ""2022-08-01T06:56:25.685649Z"",
        ""SortableUniqueId"": ""637949337856856490001214623188"",
        ""_rid"": ""f+UeAM+8kXwUAAAAAAAAAA=="",
        ""_self"": ""dbs/f+UeAA==/colls/f+UeAM+8kXw=/docs/f+UeAM+8kXwUAAAAAAAAAA==/"",
        ""_etag"": ""\""c60173b9-0000-0700-0000-62e7791a0000\"""",
        ""_attachments"": ""attachments/"",
        ""_ts"": 1659336986
    }
]
")
            #endregion
            .WhenProjection()
            .ThenNotThrowsAnException();
    }

    [Fact]
    public void JsonFileEventsTest()
    {
        GivenQueryChecker(multiProjectionQueryTest)
            .GivenEventsFromFile("TestData1.json")
            .WhenProjection()
            .ThenNotThrowsAnException()
//        await ThenStateFileAsync("TestData1Result.json");
            .WriteProjectionToFile("TestData1ResultOut.json");

    }
    [Fact]
    public void QueryTest()
    {
        GivenScenario(JsonFileEventsTest);
        multiProjectionQueryTest.WhenParam(
                new ClientLoyaltyPointMultipleMultiProjectionQuery.QueryParameter(
                    branchId,
                    ClientLoyaltyPointMultipleMultiProjectionQuery.QuerySortKeys.ClientName))
            // .WriteResponseToFile("QueryResponseOut.json")
            .ThenResponseIsFromFile("ClientLoyaltyPointProjectionQueryResponse01.json");

    }
}
