"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.init = void 0;
const wrapio = (func) => async (input) => {
    const args = JSON.parse(input);
    const result = await func(args);
    const output = JSON.stringify(result);
    return output;
};
const init = async (contextName) => {
    // TODO: use IndexedDB
    const events = new Array();
    const writeEventAsync = async (event) => {
        events.push(structuredClone(event));
    };
    const getEventsAsync = async (query) => events
        .filter(x => query.RootPartitionKey === null ||
        x.RootPartitionKey === query.RootPartitionKey)
        .filter(x => query.PartitionKey === null || x.PartitionKey === query.PartitionKey)
        .filter(x => query.AggregateTypes === null ||
        query.AggregateTypes.includes(x.AggregateType))
        .filter(x => query.SortableIdStart === null ||
        query.SortableIdStart <= x.SortableUniqueId)
        .filter(x => query.SortableIdEnd === null ||
        x.SortableUniqueId <= query.SortableIdEnd);
    const removeAllEventsAsync = async () => {
        events.splice(0);
    };
    return {
        writeEventAsync: wrapio(writeEventAsync),
        getEventsAsync: wrapio(getEventsAsync),
        removeAllEventsAsync: wrapio(removeAllEventsAsync),
    };
};
exports.init = init;
//# sourceMappingURL=index.js.map