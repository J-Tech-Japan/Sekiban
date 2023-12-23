/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export const $ClientContentsDeleteClientAggregateCommandExecutorResponse = {
    properties: {
        aggregateDto: {
            type: 'ClientContentsAggregateDto',
        },
        command: {
            type: 'DeleteClientAggregateCommandDocument',
        },
        events: {
            type: 'array',
            contains: {
                type: 'IAggregateEvent',
            },
            isNullable: true,
        },
    },
} as const;
