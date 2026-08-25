import { chromium } from "playwright";
import { createServer } from "node:http";
import { readFile, stat } from "node:fs/promises";
import { extname, resolve, sep } from "node:path";

const [publishedRoot, runtimePath] = process.argv.slice(2);

if (!publishedRoot || !runtimePath) {
	throw new Error("usage: browser-gate.mjs <published-wwwroot> <runtime-path>");
}

const root = resolve(publishedRoot);
const contentTypes = new Map([
	[".html", "text/html; charset=utf-8"],
	[".js", "text/javascript; charset=utf-8"],
	[".mjs", "text/javascript; charset=utf-8"],
	[".json", "application/json"],
	[".wasm", "application/wasm"],
	[".css", "text/css; charset=utf-8"],
	[".svg", "image/svg+xml"],
]);

const server = createServer(async (request, response) => {
	try {
		const rawPath = new URL(request.url ?? "/", "http://127.0.0.1").pathname;
		const relativePath = rawPath === "/" ? "index.html" : decodeURIComponent(rawPath).replace(/^\/+/, "");
		const candidate = resolve(root, relativePath);
		if (candidate !== root && !candidate.startsWith(`${root}${sep}`)) {
			response.writeHead(403).end();
			return;
		}
		const details = await stat(candidate);
		if (!details.isFile()) {
			response.writeHead(404).end();
			return;
		}
		response.writeHead(200, {
			"content-type": contentTypes.get(extname(candidate)) ?? "application/octet-stream",
			"content-length": details.size,
			"cache-control": "no-store",
		});
		response.end(await readFile(candidate));
	} catch {
		response.writeHead(404).end();
	}
});

await new Promise((resolveListen, rejectListen) => {
	server.once("error", rejectListen);
	server.listen(0, "127.0.0.1", resolveListen);
});
const address = server.address();
if (!address || typeof address === "string") {
	throw new Error("browser gate static server did not expose a TCP address");
}
const url = `http://127.0.0.1:${address.port}`;

const expected = {
	Id: "6de901b9-14eb-45f8-9c5c-9ab2a0f54ac5",
	Payload: '{"kind":"browser-gate","value":10}',
	Version: 7,
	CallHistories: "[]",
	AggregateId: "13facb6f-31db-46f4-9da0-bc1edc44ebd0",
	PartitionKey: "sek-g49-browser-partition",
	DocumentType: "Event",
	DocumentTypeName: "BrowserGateEvent",
	TimeStamp: "2026-08-25T00:00:00.0000000Z",
	SortableUniqueId: "202608250000000000000000000001",
	AggregateType: "Sekiban.Net10.BrowserGate",
	RootPartitionKey: "sek-g49-browser-root",
};

let browser;
try {
	browser = await chromium.launch({ headless: true });
	const page = await browser.newPage();
	const runtimeResponses = [];
	page.on("response", (response) => {
		if (new URL(response.url()).pathname.endsWith(runtimePath)) {
			runtimeResponses.push({ url: response.url(), status: response.status() });
		}
	});

	await page.goto(url, { waitUntil: "networkidle" });
	await page.waitForFunction(
		() => document.body.dataset.sekG49BrowserGate !== "booting",
		undefined,
		{ timeout: 30_000 },
	);

	const state = await page.evaluate(() => ({
		state: document.body.dataset.sekG49BrowserGate,
		error: document.body.dataset.sekG49BrowserError,
		record: document.body.dataset.sekG49BrowserRecord,
	}));
	if (state.state !== "passed") {
		throw new Error(`browser gate reported failure: ${state.error ?? "unknown failure"}`);
	}
	if (runtimeResponses.length !== 1 || runtimeResponses[0].status !== 200) {
		throw new Error(
			`packed sekiban-runtime.mjs was not imported exactly once with HTTP 200: ${JSON.stringify(runtimeResponses)}`,
		);
	}
	const record = JSON.parse(state.record);
	for (const [name, value] of Object.entries(expected)) {
		if (record[name] !== value) {
			throw new Error(`IndexedDB round trip changed ${name}: ${record[name]} !== ${value}`);
		}
	}

	const indexedDbRecord = await page.evaluate(async (eventId) =>
		await new Promise((resolve, reject) => {
			const request = indexedDB.open("sek-g49-indexeddb-browser-gate");
			request.onerror = () => reject(request.error);
			request.onsuccess = () => {
				const database = request.result;
				const transaction = database.transaction("events", "readonly");
				const get = transaction.objectStore("events").get(eventId);
				get.onerror = () => reject(get.error);
				get.onsuccess = () => {
					database.close();
					resolve(get.result);
				};
			};
		}),
		expected.Id,
	);
	if (!indexedDbRecord || indexedDbRecord.Id !== expected.Id) {
		throw new Error("browser IndexedDB does not contain the record written through BlazorJsRuntime");
	}

	console.log("IndexedDB browser gate passed: default BlazorJsRuntime imported packed runtime and round-tripped the record");
} finally {
	if (browser) {
		await browser.close();
	}
	await new Promise((resolveClose, rejectClose) =>
		server.close((error) => (error ? rejectClose(error) : resolveClose())),
	);
}
