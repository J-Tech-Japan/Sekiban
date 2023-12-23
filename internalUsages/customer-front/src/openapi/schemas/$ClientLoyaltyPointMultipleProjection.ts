/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export const $ClientLoyaltyPointMultipleProjection = {
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
        branches: {
            type: 'array',
            contains: {
                type: 'ProjectedBranch',
            },
            isNullable: true,
        },
        records: {
            type: 'array',
            contains: {
                type: 'ProjectedRecord',
            },
            isNullable: true,
        },
    },
} as const;
