/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { CallHistory } from './CallHistory';
import type { CreateRecentActivity } from './CreateRecentActivity';
import type { DocumentType } from './DocumentType';

export type CreateRecentActivityAggregateCommandDocument = {
    id?: string;
    aggregateId?: string;
    partitionKey?: string | null;
    documentType?: DocumentType;
    documentTypeName?: string | null;
    timeStamp?: string;
    sortableUniqueId?: string | null;
    payload?: CreateRecentActivity;
    executedUser?: string | null;
    exception?: string | null;
    callHistories?: Array<CallHistory> | null;
};

