/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export const $BranchContentsCreateBranchAggregateCommandExecutorResponse = {
    properties: {
        aggregateDto: {
            type: 'BranchContentsAggregateDto',
        },
        command: {
            type: 'CreateBranchAggregateCommandDocument',
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