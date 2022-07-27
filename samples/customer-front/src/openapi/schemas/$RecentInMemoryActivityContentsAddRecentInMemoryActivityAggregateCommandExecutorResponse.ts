/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export const $RecentInMemoryActivityContentsAddRecentInMemoryActivityAggregateCommandExecutorResponse = {
    properties: {
        aggregateDto: {
            type: 'RecentInMemoryActivityContentsAggregateDto',
        },
        command: {
            type: 'AddRecentInMemoryActivityAggregateCommandDocument',
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