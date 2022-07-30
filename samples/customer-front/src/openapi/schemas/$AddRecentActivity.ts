/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export const $AddRecentActivity = {
    properties: {
        referenceVersion: {
            type: 'number',
            isRequired: true,
            format: 'int32',
        },
        recentActivityId: {
            type: 'string',
            format: 'uuid',
        },
        activity: {
            type: 'string',
            isNullable: true,
        },
    },
};