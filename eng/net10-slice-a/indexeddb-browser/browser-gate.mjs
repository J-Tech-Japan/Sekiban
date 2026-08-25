import { chromium } from "playwright";
import { createServer } from "node:http";
import { mkdir, readFile, stat, writeFile } from "node:fs/promises";
import { extname, join, resolve, sep } from "node:path";

const [publishedRoot, runtimePath, artifactDirectory] = process.argv.slice(2);

if (!publishedRoot || !runtimePath || !artifactDirectory) {
	throw new Error("usage: browser-gate.mjs <published-wwwroot> <runtime-path> <artifact-directory>");
}

const root = resolve(publishedRoot);
const artifacts = resolve(artifactDirectory);
const diagnosticsPath = join(artifacts, "browser-gate-diagnostics.json");
const screenshotPath = join(artifacts, "browser-gate-screenshot.png");
const tracePath = join(artifacts, "browser-gate-trace.zip");
const diagnostics = {
	artifactDirectory: artifacts,
	phase: "starting",
	result: "running",
	artifacts: {
		diagnostics: diagnosticsPath,
		screenshot: screenshotPath,
		trace: tracePath,
	},
};

await mkdir(artifacts, { recursive: true });
const writeDiagnostics = async () =>
	await writeFile(diagnosticsPath, `${JSON.stringify(diagnostics, null, 2)}\n`, "utf8");
await writeDiagnostics();

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
diagnostics.url = url;
await writeDiagnostics();

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
let context;
let page;
let tracingStarted = false;
try {
	diagnostics.phase = "launching-browser";
	browser = await chromium.launch({ headless: true });
	context = await browser.newContext();
	await context.tracing.start({ screenshots: true, snapshots: true, sources: true });
	tracingStarted = true;
	page = await context.newPage();
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
	diagnostics.phase = "asserting-browser-state";
	diagnostics.state = state;
	diagnostics.runtimeResponses = runtimeResponses;
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
			const asError = (reason, action) =>
				reason instanceof Error
					? reason
					: new Error(`${action}: ${reason?.message ?? String(reason)}`);
			const request = indexedDB.open("sek-g49-indexeddb-browser-gate");
			request.onerror = () => reject(asError(request.error, "IndexedDB open failed"));
			request.onsuccess = () => {
				const database = request.result;
				const transaction = database.transaction("events", "readonly");
				const get = transaction.objectStore("events").get(eventId);
				get.onerror = () => reject(asError(get.error, "IndexedDB read failed"));
				get.onsuccess = () => {
					database.close();
					resolve(get.result);
				};
			};
		}),
		expected.Id,
	);
	if (indexedDbRecord?.Id !== expected.Id) {
		throw new Error("browser IndexedDB does not contain the record written through BlazorJsRuntime");
	}

	diagnostics.phase = "passed";
	diagnostics.result = "passed";
	console.log("IndexedDB browser gate passed: default BlazorJsRuntime imported packed runtime and round-tripped the record");

} catch (error) {
	diagnostics.phase = "failed";
	diagnostics.result = "failed";
	diagnostics.error = {
		message: error instanceof Error ? error.message : String(error),
		stack: error instanceof Error ? error.stack : undefined,
	};
	throw error;
} finally {
	if (page) {
		try {
			await page.screenshot({ path: screenshotPath, fullPage: true });
		} catch (error) {
			diagnostics.screenshotError = error instanceof Error ? error.message : String(error);
		}
	}
	if (tracingStarted && context) {
		try {
			await context.tracing.stop({ path: tracePath });
		} catch (error) {
			diagnostics.traceError = error instanceof Error ? error.message : String(error);
		}
	}
	if (browser) {
		await browser.close();
	}
	await writeDiagnostics();
	await new Promise((resolveClose, rejectClose) =>
		server.close((error) => (error ? rejectClose(error) : resolveClose())),
	);
}
