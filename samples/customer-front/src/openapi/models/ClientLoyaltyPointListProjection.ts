/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { ClientLoyaltyPointListRecord } from './ClientLoyaltyPointListRecord';
import type { ProjectedBranchInternal } from './ProjectedBranchInternal';

export type ClientLoyaltyPointListProjection = {
    lastEventId?: string;
    lastSortableUniqueId?: string | null;
    appliedSnapshotVersion?: number;
    version?: number;
    records?: Array<ClientLoyaltyPointListRecord> | null;
    branches?: Array<ProjectedBranchInternal> | null;
};

