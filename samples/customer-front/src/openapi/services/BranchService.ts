/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { BranchContentsAggregateDto } from '../models/BranchContentsAggregateDto';
import type { BranchContentsCreateBranchAggregateCommandExecutorResponse } from '../models/BranchContentsCreateBranchAggregateCommandExecutorResponse';
import type { CreateBranch } from '../models/CreateBranch';
import { request as __request } from '../core/request';

export class BranchService {

    /**
     * @returns BranchContentsCreateBranchAggregateCommandExecutorResponse Success
     * @throws ApiError
     */
    public static async createBranch({
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
     * @returns BranchContentsAggregateDto Success
     * @throws ApiError
     */
    public static async branchGet({
        id,
        toVersion,
    }: {
        id?: string,
        toVersion?: number,
    }): Promise<BranchContentsAggregateDto> {
        const result = await __request({
            method: 'GET',
            path: `/api/query/branch/get`,
            query: {
                'id': id,
                'toVersion': toVersion,
            },
        });
        return result.body;
    }

    /**
     * @returns BranchContentsAggregateDto Success
     * @throws ApiError
     */
    public static async branchList(): Promise<Array<BranchContentsAggregateDto>> {
        const result = await __request({
            method: 'GET',
            path: `/api/query/branch/list`,
        });
        return result.body;
    }

}