/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export const $AddRecentInMemoryActivity = {
    properties: {
        referenceVersion: {
            type: 'number',
            isRequired: true,
            format: 'int32',
        },
        recentInMemoryActivityId: {
            type: 'string',
            format: 'uuid',
        },
        activity: {
            type: 'string',
            isNullable: true,
        },
    },
} as const;
