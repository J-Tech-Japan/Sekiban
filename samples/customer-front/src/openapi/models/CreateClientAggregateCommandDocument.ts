/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { CallHistory } from './CallHistory';
import type { CreateClient } from './CreateClient';
import type { DocumentType } from './DocumentType';

export type CreateClientAggregateCommandDocument = {
    id?: string;
    aggregateId?: string;
    partitionKey?: string | null;
    documentType?: DocumentType;
    documentTypeName?: string | null;
    timeStamp?: string;
    sortableUniqueId?: string | null;
    payload?: CreateClient;
    executedUser?: string | null;
    exception?: string | null;
    callHistories?: Array<CallHistory> | null;
};

