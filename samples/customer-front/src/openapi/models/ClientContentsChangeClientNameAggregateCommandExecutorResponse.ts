/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { ChangeClientNameAggregateCommandDocument } from './ChangeClientNameAggregateCommandDocument';
import type { ClientContentsAggregateDto } from './ClientContentsAggregateDto';
import type { IAggregateEvent } from './IAggregateEvent';

export type ClientContentsChangeClientNameAggregateCommandExecutorResponse = {
    aggregateDto?: ClientContentsAggregateDto;
    command?: ChangeClientNameAggregateCommandDocument;
    events?: Array<IAggregateEvent> | null;
}
