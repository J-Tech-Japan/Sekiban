/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export const $SekibanAggregateInfo = {
    properties: {
        aggregateName: {
            type: 'string',
            isNullable: true,
        },
        queryInfo: {
            type: 'SekibanQueryInfo',
        },
        commands: {
            type: 'array',
            contains: {
                type: 'SekibanCommandInfo',
            },
            isNullable: true,
        },
    },
} as const;
