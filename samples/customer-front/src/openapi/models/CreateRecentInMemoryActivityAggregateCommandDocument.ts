/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { CallHistory } from './CallHistory';
import type { CreateRecentInMemoryActivity } from './CreateRecentInMemoryActivity';
import type { DocumentType } from './DocumentType';

export type CreateRecentInMemoryActivityAggregateCommandDocument = {
    id?: string;
    aggregateId?: string;
    partitionKey?: string | null;
    documentType?: DocumentType;
    documentTypeName?: string | null;
    timeStamp?: string;
    sortableUniqueId?: string | null;
    payload?: CreateRecentInMemoryActivity;
    executedUser?: string | null;
    exception?: string | null;
    callHistories?: Array<CallHistory> | null;
}
