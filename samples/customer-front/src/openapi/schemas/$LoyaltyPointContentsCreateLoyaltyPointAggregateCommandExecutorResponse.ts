/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export const $LoyaltyPointContentsCreateLoyaltyPointAggregateCommandExecutorResponse = {
    properties: {
        aggregateDto: {
            type: 'LoyaltyPointContentsAggregateDto',
        },
        command: {
            type: 'CreateLoyaltyPointAggregateCommandDocument',
        },
        events: {
            type: 'array',
            contains: {
                type: 'IAggregateEvent',
            },
            isNullable: true,
        },
    },
};