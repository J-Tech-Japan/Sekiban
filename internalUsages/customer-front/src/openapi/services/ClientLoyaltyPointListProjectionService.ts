/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ClientLoyaltyPointListProjection } from '../models/ClientLoyaltyPointListProjection';
import type { ClientLoyaltyPointListRecord } from '../models/ClientLoyaltyPointListRecord';

import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';

export class ClientLoyaltyPointListProjectionService {

    /**
     * @returns ClientLoyaltyPointListProjection Success
     * @throws ApiError
     */
    public static getApiQueryClientloyaltypointlistprojectionGet(): CancelablePromise<ClientLoyaltyPointListProjection> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/query/clientloyaltypointlistprojection/get',
        });
    }

    /**
     * @returns ClientLoyaltyPointListRecord Success
     * @throws ApiError
     */
    public static getApiQueryClientloyaltypointlistprojectionList(): CancelablePromise<Array<ClientLoyaltyPointListRecord>> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/query/clientloyaltypointlistprojection/list',
        });
    }

}
