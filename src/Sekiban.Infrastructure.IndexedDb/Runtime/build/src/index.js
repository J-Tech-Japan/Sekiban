"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.init = void 0;
const wrapio = (func) => async (input) => {
    const args = input !== undefined && input !== null ? JSON.parse(input) : null;
    const result = await func(args);
    const output = result !== undefined && result !== null ? JSON.stringify(result) : null;
    return output;
};
const filterEvents = (events, query) => events
    .filter(x => query.RootPartitionKey === null ||
    x.RootPartitionKey === query.RootPartitionKey)
    .filter(x => query.PartitionKey === null || x.PartitionKey === query.PartitionKey)
    .filter(x => query.AggregateTypes === null ||
    query.AggregateTypes.includes(x.AggregateType))
    .filter(x => query.SortableIdStart === null ||
    query.SortableIdStart <= x.SortableUniqueId)
    .filter(x => query.SortableIdEnd === null ||
    x.SortableUniqueId <= query.SortableIdEnd)
    .map(x => structuredClone(x));
const init = async (contextName) => {
    // TODO: use IndexedDB
    const events = [];
    const dissolvableEvents = [];
    const commands = [];
    const writeEventAsync = async (event) => {
        events.push(structuredClone(event));
    };
    const getEventsAsync = async (query) => filterEvents(events, query);
    const removeAllEventsAsync = async () => {
        events.splice(0);
    };
    const writeDissolvableEventAsync = async (event) => {
        dissolvableEvents.push(structuredClone(event));
    };
    const getDissolvableEventsAsync = async (query) => filterEvents(dissolvableEvents, query);
    const removeAllDissolvableEventsAsync = async () => {
        dissolvableEvents.splice(0);
    };
    const writeCommandAsync = async (command) => {
        commands.push(structuredClone(command));
    };
    const getCommandsAsync = async (query) => commands
        .filter(x => query.SortableIdStart === null ||
        query.SortableIdStart <= x.SortableUniqueId)
        .filter(x => query.PartitionKey === null || x.PartitionKey === query.PartitionKey)
        .filter(x => query.AggregateContainerGroup === null ||
        x.AggregateContainerGroup === query.AggregateContainerGroup)
        .map(x => structuredClone(x));
    const removeAllCommandsAsync = async () => { };
    const removeAllSingleProjectionSnapshotsAsync = async () => { };
    const removeAllMultiProjectionSnapshotsAsync = async () => { };
    return {
        writeEventAsync: wrapio(writeEventAsync),
        getEventsAsync: wrapio(getEventsAsync),
        removeAllEventsAsync: wrapio(removeAllEventsAsync),
        writeDissolvableEventAsync: wrapio(writeDissolvableEventAsync),
        getDissolvableEventsAsync: wrapio(getDissolvableEventsAsync),
        removeAllDissolvableEventsAsync: wrapio(removeAllDissolvableEventsAsync),
        writeCommandAsync: wrapio(writeCommandAsync),
        getCommandsAsync: wrapio(getCommandsAsync),
        removeAllCommandsAsync: wrapio(removeAllCommandsAsync),
        removeAllSingleProjectionSnapshotsAsync: wrapio(removeAllSingleProjectionSnapshotsAsync),
        removeAllMultiProjectionSnapshotsAsync: wrapio(removeAllMultiProjectionSnapshotsAsync),
    };
};
exports.init = init;
//# sourceMappingURL=index.js.map