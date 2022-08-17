/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { AddRecentInMemoryActivityAggregateCommandDocument } from './AddRecentInMemoryActivityAggregateCommandDocument';
import type { IAggregateEvent } from './IAggregateEvent';
import type { RecentInMemoryActivityContentsAggregateDto } from './RecentInMemoryActivityContentsAggregateDto';

export type RecentInMemoryActivityContentsAddRecentInMemoryActivityAggregateCommandExecutorResponse = {
    aggregateDto?: RecentInMemoryActivityContentsAggregateDto;
    command?: AddRecentInMemoryActivityAggregateCommandDocument;
    events?: Array<IAggregateEvent> | null;
};

