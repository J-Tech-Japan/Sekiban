/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { SekibanCommandInfo } from './SekibanCommandInfo';
import type { SekibanQueryInfo } from './SekibanQueryInfo';

export type SekibanAggregateInfo = {
    aggregateName?: string | null;
    queryInfo?: SekibanQueryInfo;
    commands?: Array<SekibanCommandInfo> | null;
};

