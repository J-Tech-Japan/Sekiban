/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export const $ChangeClientName = {
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
        clientName: {
            type: 'string',
            isNullable: true,
        },
    },
} as const;
