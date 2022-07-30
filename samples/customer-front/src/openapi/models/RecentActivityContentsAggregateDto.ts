/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { RecentActivityContents } from './RecentActivityContents';

export type RecentActivityContentsAggregateDto = {
    contents?: RecentActivityContents;
    isDeleted: boolean;
    aggregateId: string;
    version: number;
    lastEventId: string;
    appliedSnapshotVersion: number;
    lastSortableUniqueId: string;
}
