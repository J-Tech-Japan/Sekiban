/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { ClientNameHistoryProjectionRecord } from './ClientNameHistoryProjectionRecord';

export type ClientNameHistoryProjection = {
    lastEventId?: string;
    lastSortableUniqueId?: string | null;
    appliedSnapshotVersion?: number;
    version?: number;
    isDeleted?: boolean;
    aggregateId?: string;
    branchId?: string;
    clientNames?: Array<ClientNameHistoryProjectionRecord> | null;
    clientEmail?: string | null;
}
