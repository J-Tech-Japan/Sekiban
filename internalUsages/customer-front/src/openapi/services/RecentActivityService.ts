/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AddRecentActivity } from '../models/AddRecentActivity';
import type { CreateRecentActivity } from '../models/CreateRecentActivity';
import type { RecentActivityContentsAddRecentActivityAggregateCommandExecutorResponse } from '../models/RecentActivityContentsAddRecentActivityAggregateCommandExecutorResponse';
import type { RecentActivityContentsAggregateDto } from '../models/RecentActivityContentsAggregateDto';
import type { RecentActivityContentsCreateRecentActivityAggregateCommandExecutorResponse } from '../models/RecentActivityContentsCreateRecentActivityAggregateCommandExecutorResponse';

import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';

export class RecentActivityService {

    /**
     * @returns RecentActivityContentsCreateRecentActivityAggregateCommandExecutorResponse Success
     * @throws ApiError
     */
    public static createRecentActivity({
        requestBody,
    }: {
        requestBody?: CreateRecentActivity,
    }): CancelablePromise<RecentActivityContentsCreateRecentActivityAggregateCommandExecutorResponse> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/command/recentactivity/createrecentactivity',
            body: requestBody,
            mediaType: 'application/json',
        });
    }

    /**
     * @returns RecentActivityContentsAddRecentActivityAggregateCommandExecutorResponse Success
     * @throws ApiError
     */
    public static addRecentActivity({
        requestBody,
    }: {
        requestBody?: AddRecentActivity,
    }): CancelablePromise<RecentActivityContentsAddRecentActivityAggregateCommandExecutorResponse> {
        return __request(OpenAPI, {
            method: 'PATCH',
            url: '/api/command/recentactivity/addrecentactivity',
            body: requestBody,
            mediaType: 'application/json',
        });
    }

    /**
     * @returns RecentActivityContentsAggregateDto Success
     * @throws ApiError
     */
    public static getApiQueryRecentactivityGet({
        id,
        toVersion,
    }: {
        id: string,
        toVersion?: number,
    }): CancelablePromise<RecentActivityContentsAggregateDto> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/query/recentactivity/get/{id}',
            path: {
                'id': id,
            },
            query: {
                'toVersion': toVersion,
            },
        });
    }

    /**
     * @returns RecentActivityContentsAggregateDto Success
     * @throws ApiError
     */
    public static getApiQueryRecentactivityGetids({
        ids,
    }: {
        ids?: Array<string>,
    }): CancelablePromise<Array<RecentActivityContentsAggregateDto>> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/query/recentactivity/getids',
            query: {
                'ids': ids,
            },
        });
    }

    /**
     * @returns RecentActivityContentsAggregateDto Success
     * @throws ApiError
     */
    public static getApiQueryRecentactivityList(): CancelablePromise<Array<RecentActivityContentsAggregateDto>> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/query/recentactivity/list',
        });
    }

}
