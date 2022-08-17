/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { ProjectedBranch } from './ProjectedBranch';
import type { ProjectedRecord } from './ProjectedRecord';

export type ClientLoyaltyPointMultipleProjection = {
    lastEventId?: string;
    lastSortableUniqueId?: string | null;
    appliedSnapshotVersion?: number;
    version?: number;
    branches?: Array<ProjectedBranch> | null;
    records?: Array<ProjectedRecord> | null;
};

