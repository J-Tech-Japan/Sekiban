/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { LoyaltyPointContents } from './LoyaltyPointContents';

export type LoyaltyPointContentsAggregateDto = {
    isDeleted: boolean;
    aggregateId: string;
    version: number;
    lastEventId: string;
    appliedSnapshotVersion: number;
    lastSortableUniqueId: string;
    contents?: LoyaltyPointContents;
}
