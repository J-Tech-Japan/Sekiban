/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export const $RecentActivityContents = {
    properties: {
        latestActivities: {
            type: 'array',
            contains: {
                type: 'RecentActivityRecord',
            },
            isNullable: true,
        },
    },
};