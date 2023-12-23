/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { CreateLoyaltyPointAggregateCommandDocument } from './CreateLoyaltyPointAggregateCommandDocument';
import type { IAggregateEvent } from './IAggregateEvent';
import type { LoyaltyPointContentsAggregateDto } from './LoyaltyPointContentsAggregateDto';

export type LoyaltyPointContentsCreateLoyaltyPointAggregateCommandExecutorResponse = {
    aggregateDto?: LoyaltyPointContentsAggregateDto;
    command?: CreateLoyaltyPointAggregateCommandDocument;
    events?: Array<IAggregateEvent> | null;
};

