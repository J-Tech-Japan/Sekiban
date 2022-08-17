/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ClientLoyaltyPointMultipleProjection } from '../models/ClientLoyaltyPointMultipleProjection';

import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';

export class ClientLoyaltyPointMultipleProjectionService {

    /**
     * @returns ClientLoyaltyPointMultipleProjection Success
     * @throws ApiError
     */
    public static clientLoyaltyPointMultipleProjection(): CancelablePromise<ClientLoyaltyPointMultipleProjection> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/query/clientloyaltypointmultipleprojection/get',
        });
    }

}
