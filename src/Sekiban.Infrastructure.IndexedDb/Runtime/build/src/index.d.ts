export declare const init: (contextName: string) => Promise<{
    writeEventAsync: (input: string | null) => Promise<string | null>;
    getEventsAsync: (input: string | null) => Promise<string | null>;
    removeAllEventsAsync: (input: string | null) => Promise<string | null>;
    writeDissolvableEventAsync: (input: string | null) => Promise<string | null>;
    getDissolvableEventsAsync: (input: string | null) => Promise<string | null>;
    removeAllDissolvableEventsAsync: (input: string | null) => Promise<string | null>;
    writeCommandAsync: (input: string | null) => Promise<string | null>;
    getCommandsAsync: (input: string | null) => Promise<string | null>;
    removeAllCommandsAsync: (input: string | null) => Promise<string | null>;
    removeAllSingleProjectionSnapshotsAsync: (input: string | null) => Promise<string | null>;
    removeAllMultiProjectionSnapshotsAsync: (input: string | null) => Promise<string | null>;
}>;
