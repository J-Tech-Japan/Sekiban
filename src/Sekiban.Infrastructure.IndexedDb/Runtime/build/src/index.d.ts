import { DbEvent, DbEventQuery } from './models';
export declare const init: (contextName: string) => Promise<{
    writeEventAsync: (event: DbEvent) => Promise<void>;
    getEventsAsync: (query: DbEventQuery) => Promise<DbEvent[]>;
}>;
