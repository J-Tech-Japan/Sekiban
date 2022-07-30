/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { RecentInMemoryActivityContents } from './RecentInMemoryActivityContents';

export type RecentInMemoryActivityContentsAggregateDto = {
    contents?: RecentInMemoryActivityContents;
    isDeleted: boolean;
    aggregateId: string;
    version: number;
    lastEventId: string;
    appliedSnapshotVersion: number;
    lastSortableUniqueId: string;
}
