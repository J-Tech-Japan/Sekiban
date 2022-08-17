/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { BranchContentsAggregateDto } from '../models/BranchContentsAggregateDto';
import type { BranchContentsCreateBranchAggregateCommandExecutorResponse } from '../models/BranchContentsCreateBranchAggregateCommandExecutorResponse';
import type { CreateBranch } from '../models/CreateBranch';

import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';

export class BranchService {

    /**
     * @returns BranchContentsCreateBranchAggregateCommandExecutorResponse Success
     * @throws ApiError
     */
    public static createBranch({
        requestBody,
    }: {
        requestBody?: CreateBranch,
    }): CancelablePromise<BranchContentsCreateBranchAggregateCommandExecutorResponse> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/command/branch/createbranch',
            body: requestBody,
            mediaType: 'application/json',
        });
    }

    /**
     * @returns BranchContentsAggregateDto Success
     * @throws ApiError
     */
    public static getApiQueryBranchGet({
        id,
        toVersion,
    }: {
        id: string,
        toVersion?: number,
    }): CancelablePromise<BranchContentsAggregateDto> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/query/branch/get/{id}',
            path: {
                'id': id,
            },
            query: {
                'toVersion': toVersion,
            },
        });
    }

    /**
     * @returns BranchContentsAggregateDto Success
     * @throws ApiError
     */
    public static getApiQueryBranchGetids({
        ids,
    }: {
        ids?: Array<string>,
    }): CancelablePromise<Array<BranchContentsAggregateDto>> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/query/branch/getids',
            query: {
                'ids': ids,
            },
        });
    }

    /**
     * @returns BranchContentsAggregateDto Success
     * @throws ApiError
     */
    public static getApiQueryBranchList(): CancelablePromise<Array<BranchContentsAggregateDto>> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/query/branch/list',
        });
    }

}
