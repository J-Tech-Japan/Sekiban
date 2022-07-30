/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { AddLoyaltyPoint } from './AddLoyaltyPoint';
import type { CallHistory } from './CallHistory';
import type { DocumentType } from './DocumentType';

export type AddLoyaltyPointAggregateCommandDocument = {
    id?: string;
    aggregateId?: string;
    partitionKey?: string | null;
    documentType?: DocumentType;
    documentTypeName?: string | null;
    timeStamp?: string;
    sortableUniqueId?: string | null;
    payload?: AddLoyaltyPoint;
    executedUser?: string | null;
    exception?: string | null;
    callHistories?: Array<CallHistory> | null;
}
