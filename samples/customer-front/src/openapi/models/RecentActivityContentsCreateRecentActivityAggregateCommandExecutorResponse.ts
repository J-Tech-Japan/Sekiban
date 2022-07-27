/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { CreateRecentActivityAggregateCommandDocument } from './CreateRecentActivityAggregateCommandDocument';
import type { IAggregateEvent } from './IAggregateEvent';
import type { RecentActivityContentsAggregateDto } from './RecentActivityContentsAggregateDto';

export type RecentActivityContentsCreateRecentActivityAggregateCommandExecutorResponse = {
    aggregateDto?: RecentActivityContentsAggregateDto;
    command?: CreateRecentActivityAggregateCommandDocument;
    events?: Array<IAggregateEvent> | null;
}
