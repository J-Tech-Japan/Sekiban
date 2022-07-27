/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { AddRecentActivity } from './AddRecentActivity';
import type { CallHistory } from './CallHistory';
import type { DocumentType } from './DocumentType';

export type AddRecentActivityAggregateCommandDocument = {
    id?: string;
    aggregateId?: string;
    partitionKey?: string | null;
    documentType?: DocumentType;
    documentTypeName?: string | null;
    timeStamp?: string;
    sortableUniqueId?: string | null;
    payload?: AddRecentActivity;
    executedUser?: string | null;
    exception?: string | null;
    callHistories?: Array<CallHistory> | null;
}
