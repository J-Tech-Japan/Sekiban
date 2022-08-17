/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { AddLoyaltyPointAggregateCommandDocument } from './AddLoyaltyPointAggregateCommandDocument';
import type { IAggregateEvent } from './IAggregateEvent';
import type { LoyaltyPointContentsAggregateDto } from './LoyaltyPointContentsAggregateDto';

export type LoyaltyPointContentsAddLoyaltyPointAggregateCommandExecutorResponse = {
    aggregateDto?: LoyaltyPointContentsAggregateDto;
    command?: AddLoyaltyPointAggregateCommandDocument;
    events?: Array<IAggregateEvent> | null;
};

