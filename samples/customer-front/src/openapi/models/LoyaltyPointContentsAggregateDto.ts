/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { LoyaltyPointContents } from './LoyaltyPointContents';

export type LoyaltyPointContentsAggregateDto = {
    contents?: LoyaltyPointContents;
    isDeleted: boolean;
    aggregateId: string;
    version: number;
    lastEventId: string;
    appliedSnapshotVersion: number;
    lastSortableUniqueId: string;
}
