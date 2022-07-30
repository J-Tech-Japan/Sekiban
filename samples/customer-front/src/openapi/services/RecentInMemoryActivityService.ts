/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AddRecentInMemoryActivity } from '../models/AddRecentInMemoryActivity';
import type { CreateRecentInMemoryActivity } from '../models/CreateRecentInMemoryActivity';
import type { RecentInMemoryActivityContentsAddRecentInMemoryActivityAggregateCommandExecutorResponse } from '../models/RecentInMemoryActivityContentsAddRecentInMemoryActivityAggregateCommandExecutorResponse';
import type { RecentInMemoryActivityContentsAggregateDto } from '../models/RecentInMemoryActivityContentsAggregateDto';
import type { RecentInMemoryActivityContentsCreateRecentInMemoryActivityAggregateCommandExecutorResponse } from '../models/RecentInMemoryActivityContentsCreateRecentInMemoryActivityAggregateCommandExecutorResponse';
import { request as __request } from '../core/request';

export class RecentInMemoryActivityService {

    /**
     * @returns RecentInMemoryActivityContentsCreateRecentInMemoryActivityAggregateCommandExecutorResponse Success
     * @throws ApiError
     */
    public static async createRecentInMemoryActivity({
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

    /**
     * @returns RecentInMemoryActivityContentsAddRecentInMemoryActivityAggregateCommandExecutorResponse Success
     * @throws ApiError
     */
    public static async addRecentInMemoryActivity({
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

    /**
     * @returns RecentInMemoryActivityContentsAggregateDto Success
     * @throws ApiError
     */
    public static async recentInMemoryActivityGet({
        id,
        toVersion,
    }: {
        id?: string,
        toVersion?: number,
    }): Promise<RecentInMemoryActivityContentsAggregateDto> {
        const result = await __request({
            method: 'GET',
            path: `/api/query/recentinmemoryactivity`,
            query: {
                'id': id,
                'toVersion': toVersion,
            },
        });
        return result.body;
    }

    /**
     * @returns RecentInMemoryActivityContentsAggregateDto Success
     * @throws ApiError
     */
    public static async recentInMemoryActivityList(): Promise<Array<RecentInMemoryActivityContentsAggregateDto>> {
        const result = await __request({
            method: 'GET',
            path: `/api/query/recentinmemoryactivity/list`,
        });
        return result.body;
    }

}