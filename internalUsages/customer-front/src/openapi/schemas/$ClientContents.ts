/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export const $ClientContents = {
    properties: {
        branchId: {
            type: 'string',
            format: 'uuid',
        },
        clientName: {
            type: 'string',
            isNullable: true,
        },
        clientEmail: {
            type: 'string',
            isNullable: true,
        },
    },
} as const;
