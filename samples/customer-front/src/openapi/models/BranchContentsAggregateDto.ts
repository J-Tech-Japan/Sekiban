/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { BranchContents } from './BranchContents';

export type BranchContentsAggregateDto = {
    contents?: BranchContents;
    isDeleted: boolean;
    aggregateId: string;
    version: number;
    lastEventId: string;
    appliedSnapshotVersion: number;
    lastSortableUniqueId: string;
}
