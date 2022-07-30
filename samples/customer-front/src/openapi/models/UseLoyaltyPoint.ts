/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { LoyaltyPointUsageTypeKeys } from './LoyaltyPointUsageTypeKeys';

export type UseLoyaltyPoint = {
    referenceVersion: number;
    clientId?: string;
    happenedDate?: string;
    reason?: LoyaltyPointUsageTypeKeys;
    pointAmount?: number;
    note?: string | null;
}
