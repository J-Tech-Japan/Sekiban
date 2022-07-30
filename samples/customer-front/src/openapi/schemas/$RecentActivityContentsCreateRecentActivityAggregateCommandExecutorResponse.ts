/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export const $RecentActivityContentsCreateRecentActivityAggregateCommandExecutorResponse = {
    properties: {
        aggregateDto: {
            type: 'RecentActivityContentsAggregateDto',
        },
        command: {
            type: 'CreateRecentActivityAggregateCommandDocument',
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