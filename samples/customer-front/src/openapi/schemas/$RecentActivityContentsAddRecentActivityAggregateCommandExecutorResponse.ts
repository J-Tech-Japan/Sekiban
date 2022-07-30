/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export const $RecentActivityContentsAddRecentActivityAggregateCommandExecutorResponse = {
    properties: {
        aggregateDto: {
            type: 'RecentActivityContentsAggregateDto',
        },
        command: {
            type: 'AddRecentActivityAggregateCommandDocument',
        },
        events: {
            type: 'array',
            contains: {
                type: 'IAggregateEvent',
            },
            isNullable: true,
        },
    },
};