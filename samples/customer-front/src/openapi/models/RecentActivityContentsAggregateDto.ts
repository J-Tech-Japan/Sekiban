/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { RecentActivityContents } from './RecentActivityContents';

export type RecentActivityContentsAggregateDto = {
    isDeleted: boolean;
    aggregateId: string;
    version: number;
    lastEventId: string;
    appliedSnapshotVersion: number;
    lastSortableUniqueId: string;
    contents?: RecentActivityContents;
}
