/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export const $ClientContentsChangeClientNameAggregateCommandExecutorResponse = {
    properties: {
        aggregateDto: {
            type: 'ClientContentsAggregateDto',
        },
        command: {
            type: 'ChangeClientNameAggregateCommandDocument',
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
