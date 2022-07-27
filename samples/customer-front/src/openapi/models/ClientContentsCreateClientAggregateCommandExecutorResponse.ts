/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { ClientContentsAggregateDto } from './ClientContentsAggregateDto';
import type { CreateClientAggregateCommandDocument } from './CreateClientAggregateCommandDocument';
import type { IAggregateEvent } from './IAggregateEvent';

export type ClientContentsCreateClientAggregateCommandExecutorResponse = {
    aggregateDto?: ClientContentsAggregateDto;
    command?: CreateClientAggregateCommandDocument;
    events?: Array<IAggregateEvent> | null;
}
