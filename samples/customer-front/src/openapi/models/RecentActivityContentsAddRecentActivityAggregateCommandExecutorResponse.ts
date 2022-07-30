/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { AddRecentActivityAggregateCommandDocument } from './AddRecentActivityAggregateCommandDocument';
import type { IAggregateEvent } from './IAggregateEvent';
import type { RecentActivityContentsAggregateDto } from './RecentActivityContentsAggregateDto';

export type RecentActivityContentsAddRecentActivityAggregateCommandExecutorResponse = {
    aggregateDto?: RecentActivityContentsAggregateDto;
    command?: AddRecentActivityAggregateCommandDocument;
    events?: Array<IAggregateEvent> | null;
}
