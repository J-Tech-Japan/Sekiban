/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export const $LoyaltyPointContentsUseLoyaltyPointAggregateCommandExecutorResponse = {
    properties: {
        aggregateDto: {
            type: 'LoyaltyPointContentsAggregateDto',
        },
        command: {
            type: 'UseLoyaltyPointAggregateCommandDocument',
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
