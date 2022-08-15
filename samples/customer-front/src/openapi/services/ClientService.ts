/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ChangeClientName } from '../models/ChangeClientName';
import type { ClientContentsAggregateDto } from '../models/ClientContentsAggregateDto';
import type { ClientContentsChangeClientNameAggregateCommandExecutorResponse } from '../models/ClientContentsChangeClientNameAggregateCommandExecutorResponse';
import type { ClientContentsCreateClientAggregateCommandExecutorResponse } from '../models/ClientContentsCreateClientAggregateCommandExecutorResponse';
import type { ClientContentsDeleteClientAggregateCommandExecutorResponse } from '../models/ClientContentsDeleteClientAggregateCommandExecutorResponse';
import type { ClientNameHistoryProjection } from '../models/ClientNameHistoryProjection';
import type { CreateClient } from '../models/CreateClient';
import type { DeleteClient } from '../models/DeleteClient';
import { request as __request } from '../core/request';

export class ClientService {

    /**
     * @returns ClientContentsCreateClientAggregateCommandExecutorResponse Success
     * @throws ApiError
     */
    public static async createClient({
        requestBody,
    }: {
        requestBody?: CreateClient,
    }): Promise<ClientContentsCreateClientAggregateCommandExecutorResponse> {
        const result = await __request({
            method: 'POST',
            path: `/api/command/client/createclient`,
            body: requestBody,
        });
        return result.body;
    }

    /**
     * @returns ClientContentsChangeClientNameAggregateCommandExecutorResponse Success
     * @throws ApiError
     */
    public static async changeClientName({
        requestBody,
    }: {
        requestBody?: ChangeClientName,
    }): Promise<ClientContentsChangeClientNameAggregateCommandExecutorResponse> {
        const result = await __request({
            method: 'PATCH',
            path: `/api/command/client/changeclientname`,
            body: requestBody,
        });
        return result.body;
    }

    /**
     * @returns ClientContentsDeleteClientAggregateCommandExecutorResponse Success
     * @throws ApiError
     */
    public static async deleteClient({
        requestBody,
    }: {
        requestBody?: DeleteClient,
    }): Promise<ClientContentsDeleteClientAggregateCommandExecutorResponse> {
        const result = await __request({
            method: 'PATCH',
            path: `/api/command/client/deleteclient`,
            body: requestBody,
        });
        return result.body;
    }

    /**
     * @returns ClientContentsAggregateDto Success
     * @throws ApiError
     */
    public static async clientGet({
        id,
        toVersion,
    }: {
        id?: string,
        toVersion?: number,
    }): Promise<ClientContentsAggregateDto> {
        const result = await __request({
            method: 'GET',
            path: `/api/query/client/get`,
            query: {
                'id': id,
                'toVersion': toVersion,
            },
        });
        return result.body;
    }

    /**
     * @returns ClientContentsAggregateDto Success
     * @throws ApiError
     */
    public static async clientList(): Promise<Array<ClientContentsAggregateDto>> {
        const result = await __request({
            method: 'GET',
            path: `/api/query/client/list`,
        });
        return result.body;
    }

    /**
     * @returns ClientNameHistoryProjection Success
     * @throws ApiError
     */
    public static async clientClientNameHistoryProjection({
        id,
        toVersion,
    }: {
        id?: string,
        toVersion?: number,
    }): Promise<ClientNameHistoryProjection> {
        const result = await __request({
            method: 'GET',
            path: `/api/query/client/clientnamehistoryprojection`,
            query: {
                'id': id,
                'toVersion': toVersion,
            },
        });
        return result.body;
    }

}