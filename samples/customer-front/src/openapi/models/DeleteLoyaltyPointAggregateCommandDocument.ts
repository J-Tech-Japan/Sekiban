/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { CallHistory } from './CallHistory';
import type { DeleteLoyaltyPoint } from './DeleteLoyaltyPoint';
import type { DocumentType } from './DocumentType';

export type DeleteLoyaltyPointAggregateCommandDocument = {
    id?: string;
    aggregateId?: string;
    partitionKey?: string | null;
    documentType?: DocumentType;
    documentTypeName?: string | null;
    timeStamp?: string;
    sortableUniqueId?: string | null;
    payload?: DeleteLoyaltyPoint;
    executedUser?: string | null;
    exception?: string | null;
    callHistories?: Array<CallHistory> | null;
}
