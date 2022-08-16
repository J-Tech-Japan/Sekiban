/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AddRecentActivity } from '../models/AddRecentActivity';
import type { CreateRecentActivity } from '../models/CreateRecentActivity';
import type { RecentActivityContentsAddRecentActivityAggregateCommandExecutorResponse } from '../models/RecentActivityContentsAddRecentActivityAggregateCommandExecutorResponse';
import type { RecentActivityContentsAggregateDto } from '../models/RecentActivityContentsAggregateDto';
import type { RecentActivityContentsCreateRecentActivityAggregateCommandExecutorResponse } from '../models/RecentActivityContentsCreateRecentActivityAggregateCommandExecutorResponse';
import { request as __request } from '../core/request';

export class RecentActivityService {

    /**
     * @returns RecentActivityContentsCreateRecentActivityAggregateCommandExecutorResponse Success
     * @throws ApiError
     */
    public static async createRecentActivity({
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
     * @returns RecentActivityContentsAddRecentActivityAggregateCommandExecutorResponse Success
     * @throws ApiError
     */
    public static async addRecentActivity({
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
     * @returns RecentActivityContentsAggregateDto Success
     * @throws ApiError
     */
    public static async recentActivityGet({
        id,
        toVersion,
    }: {
        id?: string,
        toVersion?: number,
    }): Promise<RecentActivityContentsAggregateDto> {
        const result = await __request({
            method: 'GET',
            path: `/api/query/recentactivity/get`,
            query: {
                'id': id,
                'toVersion': toVersion,
            },
        });
        return result.body;
    }

    /**
     * @returns RecentActivityContentsAggregateDto Success
     * @throws ApiError
     */
    public static async recentActivityList(): Promise<Array<RecentActivityContentsAggregateDto>> {
        const result = await __request({
            method: 'GET',
            path: `/api/query/recentactivity/list`,
        });
        return result.body;
    }

}