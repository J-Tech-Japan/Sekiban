/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export const $LoyaltyPointContentsAddLoyaltyPointAggregateCommandExecutorResponse = {
    properties: {
        aggregateDto: {
            type: 'LoyaltyPointContentsAggregateDto',
        },
        command: {
            type: 'AddLoyaltyPointAggregateCommandDocument',
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
