/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { IAggregateEvent } from './IAggregateEvent';
import type { LoyaltyPointContentsAggregateDto } from './LoyaltyPointContentsAggregateDto';
import type { UseLoyaltyPointAggregateCommandDocument } from './UseLoyaltyPointAggregateCommandDocument';

export type LoyaltyPointContentsUseLoyaltyPointAggregateCommandExecutorResponse = {
    aggregateDto?: LoyaltyPointContentsAggregateDto;
    command?: UseLoyaltyPointAggregateCommandDocument;
    events?: Array<IAggregateEvent> | null;
};

