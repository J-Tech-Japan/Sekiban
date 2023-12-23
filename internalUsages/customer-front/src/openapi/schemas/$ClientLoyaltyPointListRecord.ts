/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export const $ClientLoyaltyPointListRecord = {
    properties: {
        branchId: {
            type: 'string',
            format: 'uuid',
        },
        branchName: {
            type: 'string',
            isNullable: true,
        },
        clientId: {
            type: 'string',
            format: 'uuid',
        },
        clientName: {
            type: 'string',
            isNullable: true,
        },
        point: {
            type: 'number',
            format: 'int32',
        },
    },
} as const;
