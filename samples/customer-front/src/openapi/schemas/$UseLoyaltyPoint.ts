/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export const $UseLoyaltyPoint = {
    properties: {
        referenceVersion: {
            type: 'number',
            isRequired: true,
            format: 'int32',
        },
        clientId: {
            type: 'string',
            format: 'uuid',
        },
        happenedDate: {
            type: 'string',
            format: 'date-time',
        },
        reason: {
            type: 'LoyaltyPointUsageTypeKeys',
        },
        pointAmount: {
            type: 'number',
            format: 'int32',
        },
        note: {
            type: 'string',
            isNullable: true,
        },
    },
};