/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export const $IAggregateEvent = {
    properties: {
        aggregateType: {
            type: 'string',
            isReadOnly: true,
            isNullable: true,
        },
        isAggregateInitialEvent: {
            type: 'boolean',
            isReadOnly: true,
        },
        version: {
            type: 'number',
            isReadOnly: true,
            format: 'int32',
        },
        callHistories: {
            type: 'array',
            contains: {
                type: 'CallHistory',
            },
            isNullable: true,
        },
        id: {
            type: 'string',
            format: 'uuid',
        },
        aggregateId: {
            type: 'string',
            format: 'uuid',
        },
        partitionKey: {
            type: 'string',
            isNullable: true,
        },
        documentType: {
            type: 'DocumentType',
        },
        documentTypeName: {
            type: 'string',
            isNullable: true,
        },
        timeStamp: {
            type: 'string',
            format: 'date-time',
        },
        sortableUniqueId: {
            type: 'string',
            isNullable: true,
        },
    },
};