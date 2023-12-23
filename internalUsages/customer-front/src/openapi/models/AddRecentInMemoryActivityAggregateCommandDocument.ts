/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { AddRecentInMemoryActivity } from './AddRecentInMemoryActivity';
import type { CallHistory } from './CallHistory';
import type { DocumentType } from './DocumentType';

export type AddRecentInMemoryActivityAggregateCommandDocument = {
    id?: string;
    aggregateId?: string;
    partitionKey?: string | null;
    documentType?: DocumentType;
    documentTypeName?: string | null;
    timeStamp?: string;
    sortableUniqueId?: string | null;
    payload?: AddRecentInMemoryActivity;
    executedUser?: string | null;
    exception?: string | null;
    callHistories?: Array<CallHistory> | null;
};

