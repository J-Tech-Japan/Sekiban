/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export const $RecentInMemoryActivityContents = {
    properties: {
        latestActivities: {
            type: 'array',
            contains: {
                type: 'RecentInMemoryActivityRecord',
            },
            isNullable: true,
        },
    },
};