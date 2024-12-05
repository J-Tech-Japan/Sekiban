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
    const singleProjectionSnapshots = [];
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
    const writeSingleProjectionSnapshotAsync = async (snapshot) => {
        singleProjectionSnapshots.push(structuredClone(snapshot));
    };
    const getSingleProjectionSnapshotsAsync = async (query) => {
        const items = singleProjectionSnapshots
            .filter(x => query.Id === null || x.Id === query.Id)
            .filter(x => query.AggregateContainerGroup === null ||
            x.AggregateContainerGroup === query.AggregateContainerGroup)
            .filter(x => query.PartitionKey === null || x.PartitionKey === query.PartitionKey)
            .filter(x => query.AggregateId === null || x.AggregateId === query.AggregateId)
            .filter(x => query.RootPartitionKey === null ||
            x.RootPartitionKey === query.RootPartitionKey)
            .filter(x => query.AggregateType === null ||
            x.AggregateType === query.AggregateType)
            .filter(x => query.PayloadVersionIdentifier === null ||
            x.PayloadVersionIdentifier === query.PayloadVersionIdentifier)
            .filter(x => query.SavedVersion === null || x.SavedVersion === query.SavedVersion)
            .map(x => structuredClone(x));
        if (query.IsLatestOnly) {
            return items.slice(0, 1);
        }
        else {
            return items;
        }
    };
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
        writeSingleProjectionSnapshotAsync: wrapio(writeSingleProjectionSnapshotAsync),
        getSingleProjectionSnapshotsAsync: wrapio(getSingleProjectionSnapshotsAsync),
        removeAllSingleProjectionSnapshotsAsync: wrapio(removeAllSingleProjectionSnapshotsAsync),
        removeAllMultiProjectionSnapshotsAsync: wrapio(removeAllMultiProjectionSnapshotsAsync),
    };
};
exports.init = init;
//# sourceMappingURL=index.js.map