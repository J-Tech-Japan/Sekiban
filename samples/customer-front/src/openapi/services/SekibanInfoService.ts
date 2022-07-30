/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { SekibanAggregateInfo } from '../models/SekibanAggregateInfo';
import { request as __request } from '../core/request';

export class SekibanInfoService {

    /**
     * @returns SekibanAggregateInfo Success
     * @throws ApiError
     */
    public static async sekibanAggregates(): Promise<Array<SekibanAggregateInfo>> {
        const result = await __request({
            method: 'GET',
            path: `/api/info/aggregates`,
        });
        return result.body;
    }

    /**
     * @returns any Success
     * @throws ApiError
     */
    public static async sekibanEvents({
        aggregateName,
        id,
    }: {
        aggregateName: string,
        id: string,
    }): Promise<Array<any>> {
        const result = await __request({
            method: 'GET',
            path: `/api/info/events/${aggregateName}/${id}`,
        });
        return result.body;
    }

    /**
     * @returns any Success
     * @throws ApiError
     */
    public static async sekibanCommands({
        aggregateName,
        id,
    }: {
        aggregateName: string,
        id: string,
    }): Promise<Array<any>> {
        const result = await __request({
            method: 'GET',
            path: `/api/info/commands/${aggregateName}/${id}`,
        });
        return result.body;
    }

}