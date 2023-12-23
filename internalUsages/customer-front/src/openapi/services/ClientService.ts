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

import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';

export class ClientService {

    /**
     * @returns ClientContentsCreateClientAggregateCommandExecutorResponse Success
     * @throws ApiError
     */
    public static createClient({
        requestBody,
    }: {
        requestBody?: CreateClient,
    }): CancelablePromise<ClientContentsCreateClientAggregateCommandExecutorResponse> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/command/client/createclient',
            body: requestBody,
            mediaType: 'application/json',
        });
    }

    /**
     * @returns ClientContentsChangeClientNameAggregateCommandExecutorResponse Success
     * @throws ApiError
     */
    public static changeClientName({
        requestBody,
    }: {
        requestBody?: ChangeClientName,
    }): CancelablePromise<ClientContentsChangeClientNameAggregateCommandExecutorResponse> {
        return __request(OpenAPI, {
            method: 'PATCH',
            url: '/api/command/client/changeclientname',
            body: requestBody,
            mediaType: 'application/json',
        });
    }

    /**
     * @returns ClientContentsDeleteClientAggregateCommandExecutorResponse Success
     * @throws ApiError
     */
    public static deleteClient({
        requestBody,
    }: {
        requestBody?: DeleteClient,
    }): CancelablePromise<ClientContentsDeleteClientAggregateCommandExecutorResponse> {
        return __request(OpenAPI, {
            method: 'PATCH',
            url: '/api/command/client/deleteclient',
            body: requestBody,
            mediaType: 'application/json',
        });
    }

    /**
     * @returns ClientContentsAggregateDto Success
     * @throws ApiError
     */
    public static getApiQueryClientGet({
        id,
        toVersion,
    }: {
        id: string,
        toVersion?: number,
    }): CancelablePromise<ClientContentsAggregateDto> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/query/client/get/{id}',
            path: {
                'id': id,
            },
            query: {
                'toVersion': toVersion,
            },
        });
    }

    /**
     * @returns ClientContentsAggregateDto Success
     * @throws ApiError
     */
    public static getApiQueryClientGetids({
        ids,
    }: {
        ids?: Array<string>,
    }): CancelablePromise<Array<ClientContentsAggregateDto>> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/query/client/getids',
            query: {
                'ids': ids,
            },
        });
    }

    /**
     * @returns ClientContentsAggregateDto Success
     * @throws ApiError
     */
    public static getApiQueryClientList(): CancelablePromise<Array<ClientContentsAggregateDto>> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/query/client/list',
        });
    }

    /**
     * @returns ClientNameHistoryProjection Success
     * @throws ApiError
     */
    public static clientClientNameHistoryProjection({
        id,
        toVersion,
    }: {
        id?: string,
        toVersion?: number,
    }): CancelablePromise<ClientNameHistoryProjection> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/query/client/clientnamehistoryprojection',
            query: {
                'id': id,
                'toVersion': toVersion,
            },
        });
    }

}
