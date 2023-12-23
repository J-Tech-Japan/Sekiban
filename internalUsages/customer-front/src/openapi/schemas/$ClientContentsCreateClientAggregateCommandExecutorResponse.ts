/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export const $ClientContentsCreateClientAggregateCommandExecutorResponse = {
    properties: {
        aggregateDto: {
            type: 'ClientContentsAggregateDto',
        },
        command: {
            type: 'CreateClientAggregateCommandDocument',
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
