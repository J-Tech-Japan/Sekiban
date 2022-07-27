/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AddLoyaltyPoint } from '../models/AddLoyaltyPoint';
import type { AddRecentActivity } from '../models/AddRecentActivity';
import type { AddRecentInMemoryActivity } from '../models/AddRecentInMemoryActivity';
import type { ChangeClientName } from '../models/ChangeClientName';
import type { ClientContentsChangeClientNameAggregateCommandExecutorResponse } from '../models/ClientContentsChangeClientNameAggregateCommandExecutorResponse';
import type { ClientContentsDeleteClientAggregateCommandExecutorResponse } from '../models/ClientContentsDeleteClientAggregateCommandExecutorResponse';
import type { DeleteClient } from '../models/DeleteClient';
import type { DeleteLoyaltyPoint } from '../models/DeleteLoyaltyPoint';
import type { LoyaltyPointContentsAddLoyaltyPointAggregateCommandExecutorResponse } from '../models/LoyaltyPointContentsAddLoyaltyPointAggregateCommandExecutorResponse';
import type { LoyaltyPointContentsDeleteLoyaltyPointAggregateCommandExecutorResponse } from '../models/LoyaltyPointContentsDeleteLoyaltyPointAggregateCommandExecutorResponse';
import type { LoyaltyPointContentsUseLoyaltyPointAggregateCommandExecutorResponse } from '../models/LoyaltyPointContentsUseLoyaltyPointAggregateCommandExecutorResponse';
import type { RecentActivityContentsAddRecentActivityAggregateCommandExecutorResponse } from '../models/RecentActivityContentsAddRecentActivityAggregateCommandExecutorResponse';
import type { RecentInMemoryActivityContentsAddRecentInMemoryActivityAggregateCommandExecutorResponse } from '../models/RecentInMemoryActivityContentsAddRecentInMemoryActivityAggregateCommandExecutorResponse';
import type { UseLoyaltyPoint } from '../models/UseLoyaltyPoint';
import { request as __request } from '../core/request';

export class BaseChangeCommandController3Service {

    /**
     * @returns ClientContentsChangeClientNameAggregateCommandExecutorResponse Success
     * @throws ApiError
     */
    public static async patchBaseChangeCommandController3Service({
        requestBody,
    }: {
        requestBody?: ChangeClientName,
    }): Promise<ClientContentsChangeClientNameAggregateCommandExecutorResponse> {
        const result = await __request({
            method: 'PATCH',
            path: `/api/command/client/changeclientname`,
            body: requestBody,
        });
        return result.body;
    }

    /**
     * @returns ClientContentsDeleteClientAggregateCommandExecutorResponse Success
     * @throws ApiError
     */
    public static async patchBaseChangeCommandController3Service1({
        requestBody,
    }: {
        requestBody?: DeleteClient,
    }): Promise<ClientContentsDeleteClientAggregateCommandExecutorResponse> {
        const result = await __request({
            method: 'PATCH',
            path: `/api/command/client/deleteclient`,
            body: requestBody,
        });
        return result.body;
    }

    /**
     * @returns LoyaltyPointContentsAddLoyaltyPointAggregateCommandExecutorResponse Success
     * @throws ApiError
     */
    public static async patchBaseChangeCommandController3Service2({
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
    public static async patchBaseChangeCommandController3Service3({
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
    public static async patchBaseChangeCommandController3Service4({
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
     * @returns RecentActivityContentsAddRecentActivityAggregateCommandExecutorResponse Success
     * @throws ApiError
     */
    public static async patchBaseChangeCommandController3Service5({
        requestBody,
    }: {
        requestBody?: AddRecentActivity,
    }): Promise<RecentActivityContentsAddRecentActivityAggregateCommandExecutorResponse> {
        const result = await __request({
            method: 'PATCH',
            path: `/api/command/recentactivity/addrecentactivity`,
            body: requestBody,
        });
        return result.body;
    }

    /**
     * @returns RecentInMemoryActivityContentsAddRecentInMemoryActivityAggregateCommandExecutorResponse Success
     * @throws ApiError
     */
    public static async patchBaseChangeCommandController3Service6({
        requestBody,
    }: {
        requestBody?: AddRecentInMemoryActivity,
    }): Promise<RecentInMemoryActivityContentsAddRecentInMemoryActivityAggregateCommandExecutorResponse> {
        const result = await __request({
            method: 'PATCH',
            path: `/api/command/recentinmemoryactivity/addrecentinmemoryactivity`,
            body: requestBody,
        });
        return result.body;
    }

}