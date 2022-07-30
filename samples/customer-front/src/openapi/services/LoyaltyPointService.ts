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
import { request as __request } from '../core/request';

export class LoyaltyPointService {

    /**
     * @returns LoyaltyPointContentsCreateLoyaltyPointAggregateCommandExecutorResponse Success
     * @throws ApiError
     */
    public static async createLoyaltyPoint({
        requestBody,
    }: {
        requestBody?: CreateLoyaltyPoint,
    }): Promise<LoyaltyPointContentsCreateLoyaltyPointAggregateCommandExecutorResponse> {
        const result = await __request({
            method: 'POST',
            path: `/api/command/loyaltypoint/createloyaltypoint`,
            body: requestBody,
        });
        return result.body;
    }

    /**
     * @returns LoyaltyPointContentsAddLoyaltyPointAggregateCommandExecutorResponse Success
     * @throws ApiError
     */
    public static async addLoyaltyPoint({
        requestBody,
    }: {
        requestBody?: AddLoyaltyPoint,
    }): Promise<LoyaltyPointContentsAddLoyaltyPointAggregateCommandExecutorResponse> {
        const result = await __request({
            method: 'PATCH',
            path: `/api/command/loyaltypoint/addloyaltypoint`,
            body: requestBody,
        });
        return result.body;
    }

    /**
     * @returns LoyaltyPointContentsUseLoyaltyPointAggregateCommandExecutorResponse Success
     * @throws ApiError
     */
    public static async useLoyaltyPoint({
        requestBody,
    }: {
        requestBody?: UseLoyaltyPoint,
    }): Promise<LoyaltyPointContentsUseLoyaltyPointAggregateCommandExecutorResponse> {
        const result = await __request({
            method: 'PATCH',
            path: `/api/command/loyaltypoint/useloyaltypoint`,
            body: requestBody,
        });
        return result.body;
    }

    /**
     * @returns LoyaltyPointContentsDeleteLoyaltyPointAggregateCommandExecutorResponse Success
     * @throws ApiError
     */
    public static async deleteLoyaltyPoint({
        requestBody,
    }: {
        requestBody?: DeleteLoyaltyPoint,
    }): Promise<LoyaltyPointContentsDeleteLoyaltyPointAggregateCommandExecutorResponse> {
        const result = await __request({
            method: 'PATCH',
            path: `/api/command/loyaltypoint/deleteloyaltypoint`,
            body: requestBody,
        });
        return result.body;
    }

    /**
     * @returns LoyaltyPointContentsAggregateDto Success
     * @throws ApiError
     */
    public static async loyaltyPointGet({
        id,
        toVersion,
    }: {
        id?: string,
        toVersion?: number,
    }): Promise<LoyaltyPointContentsAggregateDto> {
        const result = await __request({
            method: 'GET',
            path: `/api/query/loyaltypoint`,
            query: {
                'id': id,
                'toVersion': toVersion,
            },
        });
        return result.body;
    }

    /**
     * @returns LoyaltyPointContentsAggregateDto Success
     * @throws ApiError
     */
    public static async loyaltyPointList(): Promise<Array<LoyaltyPointContentsAggregateDto>> {
        const result = await __request({
            method: 'GET',
            path: `/api/query/loyaltypoint/list`,
        });
        return result.body;
    }

}