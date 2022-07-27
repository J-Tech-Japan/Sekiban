/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { CreateRecentInMemoryActivityAggregateCommandDocument } from './CreateRecentInMemoryActivityAggregateCommandDocument';
import type { IAggregateEvent } from './IAggregateEvent';
import type { RecentInMemoryActivityContentsAggregateDto } from './RecentInMemoryActivityContentsAggregateDto';

export type RecentInMemoryActivityContentsCreateRecentInMemoryActivityAggregateCommandExecutorResponse = {
    aggregateDto?: RecentInMemoryActivityContentsAggregateDto;
    command?: CreateRecentInMemoryActivityAggregateCommandDocument;
    events?: Array<IAggregateEvent> | null;
}
