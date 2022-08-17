/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export const $DeleteLoyaltyPoint = {
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
    },
} as const;
