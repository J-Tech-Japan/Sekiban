/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { CallHistory } from './CallHistory';
import type { DocumentType } from './DocumentType';

export type IAggregateEvent = {
    readonly aggregateType?: string | null;
    readonly isAggregateInitialEvent?: boolean;
    readonly version?: number;
    callHistories?: Array<CallHistory> | null;
    id?: string;
    aggregateId?: string;
    partitionKey?: string | null;
    documentType?: DocumentType;
    documentTypeName?: string | null;
    timeStamp?: string;
    sortableUniqueId?: string | null;
};

