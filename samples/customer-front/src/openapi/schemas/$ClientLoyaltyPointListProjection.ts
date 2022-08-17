/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export const $ClientLoyaltyPointListProjection = {
    properties: {
        lastEventId: {
            type: 'string',
            format: 'uuid',
        },
        lastSortableUniqueId: {
            type: 'string',
            isNullable: true,
        },
        appliedSnapshotVersion: {
            type: 'number',
            format: 'int32',
        },
        version: {
            type: 'number',
            format: 'int32',
        },
        records: {
            type: 'array',
            contains: {
                type: 'ClientLoyaltyPointListRecord',
            },
            isNullable: true,
        },
        branches: {
            type: 'array',
            contains: {
                type: 'ProjectedBranchInternal',
            },
            isNullable: true,
        },
    },
} as const;
