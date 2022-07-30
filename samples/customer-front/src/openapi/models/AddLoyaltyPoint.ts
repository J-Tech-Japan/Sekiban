/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */

import type { LoyaltyPointReceiveTypeKeys } from './LoyaltyPointReceiveTypeKeys';

export type AddLoyaltyPoint = {
    referenceVersion: number;
    clientId?: string;
    happenedDate?: string;
    reason?: LoyaltyPointReceiveTypeKeys;
    pointAmount?: number;
    note?: string | null;
}
