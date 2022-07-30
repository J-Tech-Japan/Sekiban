/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { ClientContentsAggregateDto } from './ClientContentsAggregateDto';
import type { DeleteClientAggregateCommandDocument } from './DeleteClientAggregateCommandDocument';
import type { IAggregateEvent } from './IAggregateEvent';

export type ClientContentsDeleteClientAggregateCommandExecutorResponse = {
    aggregateDto?: ClientContentsAggregateDto;
    command?: DeleteClientAggregateCommandDocument;
    events?: Array<IAggregateEvent> | null;
}
