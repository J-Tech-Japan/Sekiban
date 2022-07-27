/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import { request as __request } from '../core/request';

export class SekibanApiListController1Service {

    /**
     * @returns any Success
     * @throws ApiError
     */
    public static async getSekibanApiListController1Service(): Promise<any> {
        const result = await __request({
            method: 'GET',
            path: `/api/createCommands`,
        });
        return result.body;
    }

}