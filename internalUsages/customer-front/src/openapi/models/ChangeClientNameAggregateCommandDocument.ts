/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { CallHistory } from './CallHistory';
import type { ChangeClientName } from './ChangeClientName';
import type { DocumentType } from './DocumentType';

export type ChangeClientNameAggregateCommandDocument = {
    id?: string;
    aggregateId?: string;
    partitionKey?: string | null;
    documentType?: DocumentType;
    documentTypeName?: string | null;
    timeStamp?: string;
    sortableUniqueId?: string | null;
    payload?: ChangeClientName;
    executedUser?: string | null;
    exception?: string | null;
    callHistories?: Array<CallHistory> | null;
};

