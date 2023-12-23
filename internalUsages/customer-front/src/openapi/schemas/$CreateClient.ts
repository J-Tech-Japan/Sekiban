/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export const $CreateClient = {
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
