/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { CallHistory } from './CallHistory';
import type { CreateBranch } from './CreateBranch';
import type { DocumentType } from './DocumentType';

export type CreateBranchAggregateCommandDocument = {
    id?: string;
    aggregateId?: string;
    partitionKey?: string | null;
    documentType?: DocumentType;
    documentTypeName?: string | null;
    timeStamp?: string;
    sortableUniqueId?: string | null;
    payload?: CreateBranch;
    executedUser?: string | null;
    exception?: string | null;
    callHistories?: Array<CallHistory> | null;
};

