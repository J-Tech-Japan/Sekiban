export declare const init: (contextName: string) => Promise<{
    writeEventAsync: (input: string | null) => Promise<string | null>;
    getEventsAsync: (input: string | null) => Promise<string | null>;
    removeAllEventsAsync: (input: string | null) => Promise<string | null>;
}>;
