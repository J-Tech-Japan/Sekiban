/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export const $CreateRecentInMemoryActivityAggregateCommandDocument = {
    properties: {
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
        payload: {
            type: 'CreateRecentInMemoryActivity',
        },
        executedUser: {
            type: 'string',
            isNullable: true,
        },
        exception: {
            type: 'string',
            isNullable: true,
        },
        callHistories: {
            type: 'array',
            contains: {
                type: 'CallHistory',
            },
            isNullable: true,
        },
    },
};