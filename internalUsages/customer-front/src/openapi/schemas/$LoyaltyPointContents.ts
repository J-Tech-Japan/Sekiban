/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export const $LoyaltyPointContents = {
    properties: {
        lastOccuredTime: {
            type: 'string',
            isNullable: true,
            format: 'date-time',
        },
        currentPoint: {
            type: 'number',
            format: 'int32',
        },
    },
} as const;
