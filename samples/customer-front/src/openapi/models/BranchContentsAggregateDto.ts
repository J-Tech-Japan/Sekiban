/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { BranchContents } from './BranchContents';

export type BranchContentsAggregateDto = {
    isDeleted: boolean;
    aggregateId: string;
    version: number;
    lastEventId: string;
    appliedSnapshotVersion: number;
    lastSortableUniqueId: string;
    contents?: BranchContents;
}
