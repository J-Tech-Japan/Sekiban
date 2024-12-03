export declare const init: (contextName: string) => Promise<{
    writeEventAsync: (input: string) => Promise<string>;
    getEventsAsync: (input: string) => Promise<string>;
}>;
