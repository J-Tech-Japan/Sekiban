/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AddLoyaltyPoint } from '../models/AddLoyaltyPoint';
import type { CreateLoyaltyPoint } from '../models/CreateLoyaltyPoint';
import type { DeleteLoyaltyPoint } from '../models/DeleteLoyaltyPoint';
import type { LoyaltyPointContentsAddLoyaltyPointAggregateCommandExecutorResponse } from '../models/LoyaltyPointContentsAddLoyaltyPointAggregateCommandExecutorResponse';
import type { LoyaltyPointContentsAggregateDto } from '../models/LoyaltyPointContentsAggregateDto';
import type { LoyaltyPointContentsCreateLoyaltyPointAggregateCommandExecutorResponse } from '../models/LoyaltyPointContentsCreateLoyaltyPointAggregateCommandExecutorResponse';
import type { LoyaltyPointContentsDeleteLoyaltyPointAggregateCommandExecutorResponse } from '../models/LoyaltyPointContentsDeleteLoyaltyPointAggregateCommandExecutorResponse';
import type { LoyaltyPointContentsUseLoyaltyPointAggregateCommandExecutorResponse } from '../models/LoyaltyPointContentsUseLoyaltyPointAggregateCommandExecutorResponse';
import type { UseLoyaltyPoint } from '../models/UseLoyaltyPoint';

import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';

export class LoyaltyPointService {

    /**
     * @returns LoyaltyPointContentsCreateLoyaltyPointAggregateCommandExecutorResponse Success
     * @throws ApiError
     */
    public static createLoyaltyPoint({
        requestBody,
    }: {
        requestBody?: CreateLoyaltyPoint,
    }): CancelablePromise<LoyaltyPointContentsCreateLoyaltyPointAggregateCommandExecutorResponse> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/command/loyaltypoint/createloyaltypoint',
            body: requestBody,
            mediaType: 'application/json',
        });
    }

    /**
     * @returns LoyaltyPointContentsAddLoyaltyPointAggregateCommandExecutorResponse Success
     * @throws ApiError
     */
    public static addLoyaltyPoint({
        requestBody,
    }: {
        requestBody?: AddLoyaltyPoint,
    }): CancelablePromise<LoyaltyPointContentsAddLoyaltyPointAggregateCommandExecutorResponse> {
        return __request(OpenAPI, {
            method: 'PATCH',
            url: '/api/command/loyaltypoint/addloyaltypoint',
            body: requestBody,
            mediaType: 'application/json',
        });
    }

    /**
     * @returns LoyaltyPointContentsUseLoyaltyPointAggregateCommandExecutorResponse Success
     * @throws ApiError
     */
    public static useLoyaltyPoint({
        requestBody,
    }: {
        requestBody?: UseLoyaltyPoint,
    }): CancelablePromise<LoyaltyPointContentsUseLoyaltyPointAggregateCommandExecutorResponse> {
        return __request(OpenAPI, {
            method: 'PATCH',
            url: '/api/command/loyaltypoint/useloyaltypoint',
            body: requestBody,
            mediaType: 'application/json',
        });
    }

    /**
     * @returns LoyaltyPointContentsDeleteLoyaltyPointAggregateCommandExecutorResponse Success
     * @throws ApiError
     */
    public static deleteLoyaltyPoint({
        requestBody,
    }: {
        requestBody?: DeleteLoyaltyPoint,
    }): CancelablePromise<LoyaltyPointContentsDeleteLoyaltyPointAggregateCommandExecutorResponse> {
        return __request(OpenAPI, {
            method: 'PATCH',
            url: '/api/command/loyaltypoint/deleteloyaltypoint',
            body: requestBody,
            mediaType: 'application/json',
        });
    }

    /**
     * @returns LoyaltyPointContentsAggregateDto Success
     * @throws ApiError
     */
    public static getApiQueryLoyaltypointGet({
        id,
        toVersion,
    }: {
        id: string,
        toVersion?: number,
    }): CancelablePromise<LoyaltyPointContentsAggregateDto> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/query/loyaltypoint/get/{id}',
            path: {
                'id': id,
            },
            query: {
                'toVersion': toVersion,
            },
        });
    }

    /**
     * @returns LoyaltyPointContentsAggregateDto Success
     * @throws ApiError
     */
    public static getApiQueryLoyaltypointGetids({
        ids,
    }: {
        ids?: Array<string>,
    }): CancelablePromise<Array<LoyaltyPointContentsAggregateDto>> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/query/loyaltypoint/getids',
            query: {
                'ids': ids,
            },
        });
    }

    /**
     * @returns LoyaltyPointContentsAggregateDto Success
     * @throws ApiError
     */
    public static getApiQueryLoyaltypointList(): CancelablePromise<Array<LoyaltyPointContentsAggregateDto>> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/query/loyaltypoint/list',
        });
    }

}
