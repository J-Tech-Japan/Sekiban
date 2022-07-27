/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { BranchContentsAggregateDto } from '../models/BranchContentsAggregateDto';
import type { ClientContentsAggregateDto } from '../models/ClientContentsAggregateDto';
import type { LoyaltyPointContentsAggregateDto } from '../models/LoyaltyPointContentsAggregateDto';
import type { RecentActivityContentsAggregateDto } from '../models/RecentActivityContentsAggregateDto';
import type { RecentInMemoryActivityContentsAggregateDto } from '../models/RecentInMemoryActivityContentsAggregateDto';
import { request as __request } from '../core/request';

export class BaseQueryController2Service {

    /**
     * @returns BranchContentsAggregateDto Success
     * @throws ApiError
     */
    public static async getBaseQueryController2Service({
        id,
        toVersion,
    }: {
        id: string,
        toVersion?: number,
    }): Promise<BranchContentsAggregateDto> {
        const result = await __request({
            method: 'GET',
            path: `/api/query/branch/${id}`,
            query: {
                'toVersion': toVersion,
            },
        });
        return result.body;
    }

    /**
     * @returns BranchContentsAggregateDto Success
     * @throws ApiError
     */
    public static async getBaseQueryController2Service1(): Promise<Array<BranchContentsAggregateDto>> {
        const result = await __request({
            method: 'GET',
            path: `/api/query/branch/list`,
        });
        return result.body;
    }

    /**
     * @returns ClientContentsAggregateDto Success
     * @throws ApiError
     */
    public static async getBaseQueryController2Service2({
        id,
        toVersion,
    }: {
        id: string,
        toVersion?: number,
    }): Promise<ClientContentsAggregateDto> {
        const result = await __request({
            method: 'GET',
            path: `/api/query/client/${id}`,
            query: {
                'toVersion': toVersion,
            },
        });
        return result.body;
    }

    /**
     * @returns ClientContentsAggregateDto Success
     * @throws ApiError
     */
    public static async getBaseQueryController2Service3(): Promise<Array<ClientContentsAggregateDto>> {
        const result = await __request({
            method: 'GET',
            path: `/api/query/client/list`,
        });
        return result.body;
    }

    /**
     * @returns LoyaltyPointContentsAggregateDto Success
     * @throws ApiError
     */
    public static async getBaseQueryController2Service4({
        id,
        toVersion,
    }: {
        id: string,
        toVersion?: number,
    }): Promise<LoyaltyPointContentsAggregateDto> {
        const result = await __request({
            method: 'GET',
            path: `/api/query/loyaltypoint/${id}`,
            query: {
                'toVersion': toVersion,
            },
        });
        return result.body;
    }

    /**
     * @returns LoyaltyPointContentsAggregateDto Success
     * @throws ApiError
     */
    public static async getBaseQueryController2Service5(): Promise<Array<LoyaltyPointContentsAggregateDto>> {
        const result = await __request({
            method: 'GET',
            path: `/api/query/loyaltypoint/list`,
        });
        return result.body;
    }

    /**
     * @returns RecentActivityContentsAggregateDto Success
     * @throws ApiError
     */
    public static async getBaseQueryController2Service6({
        id,
        toVersion,
    }: {
        id: string,
        toVersion?: number,
    }): Promise<RecentActivityContentsAggregateDto> {
        const result = await __request({
            method: 'GET',
            path: `/api/query/recentactivity/${id}`,
            query: {
                'toVersion': toVersion,
            },
        });
        return result.body;
    }

    /**
     * @returns RecentActivityContentsAggregateDto Success
     * @throws ApiError
     */
    public static async getBaseQueryController2Service7(): Promise<Array<RecentActivityContentsAggregateDto>> {
        const result = await __request({
            method: 'GET',
            path: `/api/query/recentactivity/list`,
        });
        return result.body;
    }

    /**
     * @returns RecentInMemoryActivityContentsAggregateDto Success
     * @throws ApiError
     */
    public static async getBaseQueryController2Service8({
        id,
        toVersion,
    }: {
        id: string,
        toVersion?: number,
    }): Promise<RecentInMemoryActivityContentsAggregateDto> {
        const result = await __request({
            method: 'GET',
            path: `/api/query/recentinmemoryactivity/${id}`,
            query: {
                'toVersion': toVersion,
            },
        });
        return result.body;
    }

    /**
     * @returns RecentInMemoryActivityContentsAggregateDto Success
     * @throws ApiError
     */
    public static async getBaseQueryController2Service9(): Promise<Array<RecentInMemoryActivityContentsAggregateDto>> {
        const result = await __request({
            method: 'GET',
            path: `/api/query/recentinmemoryactivity/list`,
        });
        return result.body;
    }

}