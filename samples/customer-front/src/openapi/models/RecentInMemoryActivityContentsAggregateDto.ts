/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { RecentInMemoryActivityContents } from './RecentInMemoryActivityContents';

export type RecentInMemoryActivityContentsAggregateDto = {
    isDeleted: boolean;
    aggregateId: string;
    version: number;
    lastEventId: string;
    appliedSnapshotVersion: number;
    lastSortableUniqueId: string;
    contents?: RecentInMemoryActivityContents;
}
