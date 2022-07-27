/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export const $RecentInMemoryActivityContentsCreateRecentInMemoryActivityAggregateCommandExecutorResponse = {
    properties: {
        aggregateDto: {
            type: 'RecentInMemoryActivityContentsAggregateDto',
        },
        command: {
            type: 'CreateRecentInMemoryActivityAggregateCommandDocument',
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