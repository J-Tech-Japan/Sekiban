"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.init = void 0;
const wrapio = (func) => async (input) => {
    const args = input !== undefined && input !== null ? JSON.parse(input) : null;
    const result = await func(args);
    const output = result !== undefined && result !== null ? JSON.stringify(result) : null;
    return output;
};
const asc = (id) => (x, y) => id(x).localeCompare(id(y));
const desc = (id) => (x, y) => id(y).localeCompare(id(x));
const filterEvents = (events, query) => {
    const items = events
        .filter(x => query.RootPartitionKey === null ||
        x.RootPartitionKey === query.RootPartitionKey)
        .filter(x => query.PartitionKey === null || x.PartitionKey === query.PartitionKey)
        .filter(x => query.AggregateTypes === null ||
        query.AggregateTypes.includes(x.AggregateType))
        .filter(x => query.SortableIdStart === null ||
        query.SortableIdStart <= x.SortableUniqueId)
        .filter(x => query.SortableIdEnd === null ||
        x.SortableUniqueId <= query.SortableIdEnd)
        .toSorted(asc(x => x.SortableUniqueId))
        .map(x => structuredClone(x));
    if (query.MaxCount !== null) {
        return items.slice(0, query.MaxCount);
    }
    else {
        return items;
    }
};
const operations = (store) => {
    const writeEventAsync = async (event) => {
        store.events.push(structuredClone(event));
    };
    const getEventsAsync = async (query) => filterEvents(store.events, query);
    const removeAllEventsAsync = async () => {
        store.events.splice(0);
    };
    const writeDissolvableEventAsync = async (event) => {
        store.dissolvableEvents.push(structuredClone(event));
    };
    const getDissolvableEventsAsync = async (query) => filterEvents(store.dissolvableEvents, query);
    const removeAllDissolvableEventsAsync = async () => {
        store.dissolvableEvents.splice(0);
    };
    const writeCommandAsync = async (command) => {
        store.commands.push(structuredClone(command));
    };
    const getCommandsAsync = async (query) => store.commands
        .filter(x => query.SortableIdStart === null ||
        query.SortableIdStart <= x.SortableUniqueId)
        .filter(x => query.PartitionKey === null || x.PartitionKey === query.PartitionKey)
        .filter(x => query.AggregateContainerGroup === null ||
        x.AggregateContainerGroup === query.AggregateContainerGroup)
        .toSorted(asc(x => x.SortableUniqueId))
        .map(x => structuredClone(x));
    const removeAllCommandsAsync = async () => {
        store.commands.splice(0);
    };
    const writeSingleProjectionSnapshotAsync = async (snapshot) => {
        store.singleProjectionSnapshots.push(structuredClone(snapshot));
    };
    const getSingleProjectionSnapshotsAsync = async (query) => {
        const items = store.singleProjectionSnapshots
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
            .toSorted(desc(x => x.LastSortableUniqueId))
            .map(x => structuredClone(x));
        if (query.IsLatestOnly) {
            return items.slice(0, 1);
        }
        else {
            return items;
        }
    };
    const removeAllSingleProjectionSnapshotsAsync = async () => {
        store.singleProjectionSnapshots.splice(0);
    };
    const writeMultiProjectionSnapshotAsync = async (payload) => {
        store.multiProjectionSnapshots.push(structuredClone(payload));
    };
    const getMultiProjectionSnapshotsAsync = async (query) => {
        const items = store.multiProjectionSnapshots
            .filter(x => query.AggregateContainerGroup === null ||
            x.AggregateContainerGroup === query.AggregateContainerGroup)
            .filter(x => query.PartitionKey === null || x.PartitionKey === query.PartitionKey)
            .filter(x => query.PayloadVersionIdentifier === null ||
            x.PayloadVersionIdentifier === query.PayloadVersionIdentifier)
            .toSorted(desc(x => x.LastSortableUniqueId))
            .map(x => structuredClone(x));
        if (query.IsLatestOnly) {
            return items.slice(0, 1);
        }
        else {
            return items;
        }
    };
    const removeAllMultiProjectionSnapshotsAsync = async () => {
        store.multiProjectionSnapshots.splice(0);
    };
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
        writeMultiProjectionSnapshotAsync: wrapio(writeMultiProjectionSnapshotAsync),
        getMultiProjectionSnapshotsAsync: wrapio(getMultiProjectionSnapshotsAsync),
        removeAllMultiProjectionSnapshotsAsync: wrapio(removeAllMultiProjectionSnapshotsAsync),
    };
};
const stores = new Map();
const newStore = () => ({
    events: [],
    dissolvableEvents: [],
    commands: [],
    singleProjectionSnapshots: [],
    multiProjectionSnapshots: [],
});
const init = async (contextName) => {
    // TODO: use IndexedDB
    let store = stores.get(contextName);
    if (store === undefined) {
        store = newStore();
    }
    stores.set(contextName, store);
    return operations(store);
};
exports.init = init;
//# sourceMappingURL=index.js.map