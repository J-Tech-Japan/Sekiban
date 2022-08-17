/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export const $CallHistory = {
    properties: {
        id: {
            type: 'string',
            format: 'uuid',
        },
        typeName: {
            type: 'string',
            isNullable: true,
        },
        executedUser: {
            type: 'string',
            isNullable: true,
        },
    },
} as const;
