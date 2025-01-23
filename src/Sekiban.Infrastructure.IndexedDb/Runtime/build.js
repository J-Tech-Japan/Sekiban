// @ts-check

import child_process from "node:child_process";
import fs from "node:fs/promises";
import process from "node:process";

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

const obtainLock = async () => {
	try {
		await fs.mkdir("build-lock");
		return true;
	} catch (e) {
		if (e.code === "EEXIST") {
			return false;
		}

		throw new Error(e.message, { cause: e });
	}
};

const releaseLock = async () => await fs.rmdir("build-lock");

const waitAnotherBuild = async () => {
	while (true) {
		await sleep(1000);

		try {
			await fs.stat("build-lock");
		} catch (e) {
			if (e.code === "ENOENT") {
				return;
			}

			throw new Error(e.message, { cause: e });
		}
	}
};

const main = async () => {
	if (!(await obtainLock())) {
		// An another build task is running concurrently.
		// Wait until it done.
		await waitAnotherBuild();
		process.exit(0);
	}

	try {
		const restore = child_process.spawnSync("npm", ["install"], {
			shell: true,
			stdio: "inherit",
		});
		if (restore.status !== 0) {
			process.exit(restore.status);
		}

		const build = child_process.spawnSync("npm", ["run", "build"], {
			shell: true,
			stdio: "inherit",
		});
		if (build.status !== 0) {
			process.exit(build.status);
		}
	} finally {
		await releaseLock();
	}
};

main();
