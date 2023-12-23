/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export const $CreateLoyaltyPoint = {
    properties: {
        clientId: {
            type: 'string',
            format: 'uuid',
        },
        initialPoint: {
            type: 'number',
            format: 'int32',
        },
    },
} as const;
