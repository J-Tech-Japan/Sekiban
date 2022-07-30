/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { DeleteLoyaltyPointAggregateCommandDocument } from './DeleteLoyaltyPointAggregateCommandDocument';
import type { IAggregateEvent } from './IAggregateEvent';
import type { LoyaltyPointContentsAggregateDto } from './LoyaltyPointContentsAggregateDto';

export type LoyaltyPointContentsDeleteLoyaltyPointAggregateCommandExecutorResponse = {
    aggregateDto?: LoyaltyPointContentsAggregateDto;
    command?: DeleteLoyaltyPointAggregateCommandDocument;
    events?: Array<IAggregateEvent> | null;
}
