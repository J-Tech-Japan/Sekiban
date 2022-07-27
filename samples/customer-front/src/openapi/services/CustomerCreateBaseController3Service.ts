/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { BranchContentsCreateBranchAggregateCommandExecutorResponse } from '../models/BranchContentsCreateBranchAggregateCommandExecutorResponse';
import type { ClientContentsCreateClientAggregateCommandExecutorResponse } from '../models/ClientContentsCreateClientAggregateCommandExecutorResponse';
import type { CreateBranch } from '../models/CreateBranch';
import type { CreateClient } from '../models/CreateClient';
import type { CreateLoyaltyPoint } from '../models/CreateLoyaltyPoint';
import type { CreateRecentActivity } from '../models/CreateRecentActivity';
import type { CreateRecentInMemoryActivity } from '../models/CreateRecentInMemoryActivity';
import type { LoyaltyPointContentsCreateLoyaltyPointAggregateCommandExecutorResponse } from '../models/LoyaltyPointContentsCreateLoyaltyPointAggregateCommandExecutorResponse';
import type { RecentActivityContentsCreateRecentActivityAggregateCommandExecutorResponse } from '../models/RecentActivityContentsCreateRecentActivityAggregateCommandExecutorResponse';
import type { RecentInMemoryActivityContentsCreateRecentInMemoryActivityAggregateCommandExecutorResponse } from '../models/RecentInMemoryActivityContentsCreateRecentInMemoryActivityAggregateCommandExecutorResponse';
import { request as __request } from '../core/request';

export class CustomerCreateBaseController3Service {

    /**
     * @returns BranchContentsCreateBranchAggregateCommandExecutorResponse Success
     * @throws ApiError
     */
    public static async postCustomerCreateBaseController3Service({
        requestBody,
    }: {
        requestBody?: CreateBranch,
    }): Promise<BranchContentsCreateBranchAggregateCommandExecutorResponse> {
        const result = await __request({
            method: 'POST',
            path: `/api/command/branch/createbranch`,
            body: requestBody,
        });
        return result.body;
    }

    /**
     * @returns ClientContentsCreateClientAggregateCommandExecutorResponse Success
     * @throws ApiError
     */
    public static async postCustomerCreateBaseController3Service1({
        requestBody,
    }: {
        requestBody?: CreateClient,
    }): Promise<ClientContentsCreateClientAggregateCommandExecutorResponse> {
        const result = await __request({
            method: 'POST',
            path: `/api/command/client/createclient`,
            body: requestBody,
        });
        return result.body;
    }

    /**
     * @returns LoyaltyPointContentsCreateLoyaltyPointAggregateCommandExecutorResponse Success
     * @throws ApiError
     */
    public static async postCustomerCreateBaseController3Service2({
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
     * @returns RecentActivityContentsCreateRecentActivityAggregateCommandExecutorResponse Success
     * @throws ApiError
     */
    public static async postCustomerCreateBaseController3Service3({
        requestBody,
    }: {
        requestBody?: CreateRecentActivity,
    }): Promise<RecentActivityContentsCreateRecentActivityAggregateCommandExecutorResponse> {
        const result = await __request({
            method: 'POST',
            path: `/api/command/recentactivity/createrecentactivity`,
            body: requestBody,
        });
        return result.body;
    }

    /**
     * @returns RecentInMemoryActivityContentsCreateRecentInMemoryActivityAggregateCommandExecutorResponse Success
     * @throws ApiError
     */
    public static async postCustomerCreateBaseController3Service4({
        requestBody,
    }: {
        requestBody?: CreateRecentInMemoryActivity,
    }): Promise<RecentInMemoryActivityContentsCreateRecentInMemoryActivityAggregateCommandExecutorResponse> {
        const result = await __request({
            method: 'POST',
            path: `/api/command/recentinmemoryactivity/createrecentinmemoryactivity`,
            body: requestBody,
        });
        return result.body;
    }

}