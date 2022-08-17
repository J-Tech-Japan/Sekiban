/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export const $ProjectedBranchInternal = {
    properties: {
        branchId: {
            type: 'string',
            format: 'uuid',
        },
        branchName: {
            type: 'string',
            isNullable: true,
        },
    },
} as const;
