/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export const $ClientContentsAggregateDto = {
    properties: {
        contents: {
            type: 'ClientContents',
        },
        isDeleted: {
            type: 'boolean',
            isRequired: true,
        },
        aggregateId: {
            type: 'string',
            isRequired: true,
            format: 'uuid',
        },
        version: {
            type: 'number',
            isRequired: true,
            format: 'int32',
        },
        lastEventId: {
            type: 'string',
            isRequired: true,
            format: 'uuid',
        },
        appliedSnapshotVersion: {
            type: 'number',
            isRequired: true,
            format: 'int32',
        },
        lastSortableUniqueId: {
            type: 'string',
            isRequired: true,
        },
    },
} as const;