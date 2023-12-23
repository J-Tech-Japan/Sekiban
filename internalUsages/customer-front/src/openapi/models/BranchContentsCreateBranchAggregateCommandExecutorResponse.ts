/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { BranchContentsAggregateDto } from './BranchContentsAggregateDto';
import type { CreateBranchAggregateCommandDocument } from './CreateBranchAggregateCommandDocument';
import type { IAggregateEvent } from './IAggregateEvent';

export type BranchContentsCreateBranchAggregateCommandExecutorResponse = {
    aggregateDto?: BranchContentsAggregateDto;
    command?: CreateBranchAggregateCommandDocument;
    events?: Array<IAggregateEvent> | null;
};

