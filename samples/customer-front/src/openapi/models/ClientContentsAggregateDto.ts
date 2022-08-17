/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { ClientContents } from './ClientContents';

export type ClientContentsAggregateDto = {
    contents?: ClientContents;
    isDeleted: boolean;
    aggregateId: string;
    version: number;
    lastEventId: string;
    appliedSnapshotVersion: number;
    lastSortableUniqueId: string;
};

