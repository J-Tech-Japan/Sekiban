/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { SekibanAggregateInfo } from '../models/SekibanAggregateInfo';
import type { UpdatedLocationType } from '../models/UpdatedLocationType';

import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';

export class SekibanInfoService {

    /**
     * @returns SekibanAggregateInfo Success
     * @throws ApiError
     */
    public static sekibanAggregates(): CancelablePromise<Array<SekibanAggregateInfo>> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/info/aggregates',
        });
    }

    /**
     * @returns any Success
     * @throws ApiError
     */
    public static sekibanEvents({
        aggregateName,
        id,
    }: {
        aggregateName: string,
        id: string,
    }): CancelablePromise<Array<any>> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/info/events/{aggregateName}/{id}',
            path: {
                'aggregateName': aggregateName,
                'id': id,
            },
        });
    }

    /**
     * @returns any Success
     * @throws ApiError
     */
    public static sekibanCommands({
        aggregateName,
        id,
    }: {
        aggregateName: string,
        id: string,
    }): CancelablePromise<Array<any>> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/info/commands/{aggregateName}/{id}',
            path: {
                'aggregateName': aggregateName,
                'id': id,
            },
        });
    }

    /**
     * @returns any Success
     * @throws ApiError
     */
    public static updateAggregateId({
        aggregateName,
        id,
        sortableUniqueId,
        locationType,
    }: {
        aggregateName: string,
        id: string,
        sortableUniqueId?: string,
        locationType?: UpdatedLocationType,
    }): CancelablePromise<any> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/info/updatemarker/{aggregateName}/{id}',
            path: {
                'aggregateName': aggregateName,
                'id': id,
            },
            query: {
                'sortableUniqueId': sortableUniqueId,
                'locationType': locationType,
            },
        });
    }

}
