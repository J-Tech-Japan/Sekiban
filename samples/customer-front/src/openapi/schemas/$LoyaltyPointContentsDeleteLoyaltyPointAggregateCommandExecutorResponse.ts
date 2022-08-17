/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export const $LoyaltyPointContentsDeleteLoyaltyPointAggregateCommandExecutorResponse = {
    properties: {
        aggregateDto: {
            type: 'LoyaltyPointContentsAggregateDto',
        },
        command: {
            type: 'DeleteLoyaltyPointAggregateCommandDocument',
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
