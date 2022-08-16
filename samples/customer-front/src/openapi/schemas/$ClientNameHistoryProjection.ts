/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export const $ClientNameHistoryProjection = {
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
        isDeleted: {
            type: 'boolean',
        },
        aggregateId: {
            type: 'string',
            format: 'uuid',
        },
        branchId: {
            type: 'string',
            format: 'uuid',
        },
        clientNames: {
            type: 'array',
            contains: {
                type: 'ClientNameHistoryProjectionRecord',
            },
            isNullable: true,
        },
        clientEmail: {
            type: 'string',
            isNullable: true,
        },
    },
};