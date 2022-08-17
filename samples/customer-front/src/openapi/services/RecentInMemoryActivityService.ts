/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AddRecentInMemoryActivity } from '../models/AddRecentInMemoryActivity';
import type { CreateRecentInMemoryActivity } from '../models/CreateRecentInMemoryActivity';
import type { RecentInMemoryActivityContentsAddRecentInMemoryActivityAggregateCommandExecutorResponse } from '../models/RecentInMemoryActivityContentsAddRecentInMemoryActivityAggregateCommandExecutorResponse';
import type { RecentInMemoryActivityContentsAggregateDto } from '../models/RecentInMemoryActivityContentsAggregateDto';
import type { RecentInMemoryActivityContentsCreateRecentInMemoryActivityAggregateCommandExecutorResponse } from '../models/RecentInMemoryActivityContentsCreateRecentInMemoryActivityAggregateCommandExecutorResponse';

import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';

export class RecentInMemoryActivityService {

    /**
     * @returns RecentInMemoryActivityContentsCreateRecentInMemoryActivityAggregateCommandExecutorResponse Success
     * @throws ApiError
     */
    public static createRecentInMemoryActivity({
        requestBody,
    }: {
        requestBody?: CreateRecentInMemoryActivity,
    }): CancelablePromise<RecentInMemoryActivityContentsCreateRecentInMemoryActivityAggregateCommandExecutorResponse> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/command/recentinmemoryactivity/createrecentinmemoryactivity',
            body: requestBody,
            mediaType: 'application/json',
        });
    }

    /**
     * @returns RecentInMemoryActivityContentsAddRecentInMemoryActivityAggregateCommandExecutorResponse Success
     * @throws ApiError
     */
    public static addRecentInMemoryActivity({
        requestBody,
    }: {
        requestBody?: AddRecentInMemoryActivity,
    }): CancelablePromise<RecentInMemoryActivityContentsAddRecentInMemoryActivityAggregateCommandExecutorResponse> {
        return __request(OpenAPI, {
            method: 'PATCH',
            url: '/api/command/recentinmemoryactivity/addrecentinmemoryactivity',
            body: requestBody,
            mediaType: 'application/json',
        });
    }

    /**
     * @returns RecentInMemoryActivityContentsAggregateDto Success
     * @throws ApiError
     */
    public static getApiQueryRecentinmemoryactivityGet({
        id,
        toVersion,
    }: {
        id: string,
        toVersion?: number,
    }): CancelablePromise<RecentInMemoryActivityContentsAggregateDto> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/query/recentinmemoryactivity/get/{id}',
            path: {
                'id': id,
            },
            query: {
                'toVersion': toVersion,
            },
        });
    }

    /**
     * @returns RecentInMemoryActivityContentsAggregateDto Success
     * @throws ApiError
     */
    public static getApiQueryRecentinmemoryactivityGetids({
        ids,
    }: {
        ids?: Array<string>,
    }): CancelablePromise<Array<RecentInMemoryActivityContentsAggregateDto>> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/query/recentinmemoryactivity/getids',
            query: {
                'ids': ids,
            },
        });
    }

    /**
     * @returns RecentInMemoryActivityContentsAggregateDto Success
     * @throws ApiError
     */
    public static getApiQueryRecentinmemoryactivityList(): CancelablePromise<Array<RecentInMemoryActivityContentsAggregateDto>> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/query/recentinmemoryactivity/list',
        });
    }

}
