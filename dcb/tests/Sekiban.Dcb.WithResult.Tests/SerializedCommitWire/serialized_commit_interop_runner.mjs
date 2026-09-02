#!/usr/bin/env node
// SEK-G52 dependency-free Node witness. It deliberately imports no sekiban-dcb-ts code: the frozen fixtures define the
// runtime V1 / client-adapter boundary that both ecosystems can execute independently.
import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const runnerDirectory = dirname(fileURLToPath(import.meta.url));
const goldensDirectory = join(runnerDirectory, 'goldens');
const expectedOutcomes = new Map([
  ['interop_official_v1_populated.json', 'r1-byte-identical'],
  ['interop_legacy_populated.json', 'legacy-compatible'],
  ['interop_legacy_explicit_empty.json', 'legacy-empty-compatible'],
  ['interop_ts_client_model.json', 'r1-r2-paired-positive'],
  ['interop_r2_canonical_positive.json', 'r2-byte-exact-positive'],
  ['interop_r2_canonical_positive_v1.json', 'r2-byte-exact-expected-v1'],
  ['interop_r2_integer_like_key.json', 'r2-key-order-loss'],
  ['interop_r2_numeric_lexical_loss.json', 'r2-numeric-lexical-loss'],
  ['interop_r2_duplicate_key.json', 'r2-duplicate-key-error'],
  ['interop_r3_bom_payload.json', 'r3-bom-payload-error'],
  ['interop_r3_non_json_payload.json', 'r3-non-json-payload-error'],
  ['interop_r3_invalid_utf8_payload.json', 'r3-invalid-utf8-payload-error'],
  ['interop_client_empty_tag.json', 'r2-empty-tag-error'],
  ['interop_client_duplicate_consistency.json', 'r2-duplicate-consistency-error'],
  ['interop_response_member_vocabulary.json', 'response-vocabulary']
]);

class ContractError extends Error {
  constructor(code) {
    super(code);
    this.code = code;
  }
}

function sha256(bytes) {
  return createHash('sha256').update(bytes).digest('hex');
}

function readFixture(file) {
  return readFileSync(join(goldensDirectory, file));
}

function verifyDigest(bytes, expectedLength, expectedSha) {
  if (bytes.length !== expectedLength) {
    throw new Error(`Frozen fixture length mismatch: expected ${expectedLength}, got ${bytes.length}.`);
  }
  const actual = sha256(bytes);
  if (actual !== expectedSha) {
    throw new Error(`Frozen fixture SHA-256 mismatch: expected ${expectedSha}, got ${actual}.`);
  }
}

function loadManifestAndVerifyProvenance() {
  const manifest = JSON.parse(readFixture('interop_manifest.json').toString('utf8'));
  const provenance = readFileSync(join(goldensDirectory, 'PROVENANCE.md'), 'utf8');
  if (manifest.sourceCommit !== 'f53ffdc69e225433b266cc1f92875d6b2b11aa93' ||
      manifest.contractVersion !== 'runtime-v1' ||
      !manifest.excludedMembers.includes('eventId')) {
    throw new Error('Frozen interop manifest does not declare the shared runtime V1 contract.');
  }
  if (manifest.fixtures.length !== expectedOutcomes.size) {
    throw new Error('Frozen interop manifest does not contain the complete expected-outcome catalogue.');
  }

  for (const fixture of manifest.fixtures) {
    if (expectedOutcomes.get(fixture.file) !== fixture.expectedOutcome) {
      throw new Error(`Frozen fixture ${fixture.file} does not declare its expected outcome.`);
    }
    const bytes = readFixture(fixture.file);
    verifyDigest(bytes, fixture.byteLength, fixture.sha256);
    const provenanceRow = `| \`${fixture.file}\` | ${fixture.byteLength} | \`${fixture.sha256}\` |`;
    if (!provenance.includes(provenanceRow)) {
      throw new Error(`PROVENANCE.md is missing the pinned row for ${fixture.file}.`);
    }
  }
  return manifest;
}

function ensureNoBom(bytes, code) {
  if (bytes.length >= 3 && bytes[0] === 0xef && bytes[1] === 0xbb && bytes[2] === 0xbf) {
    throw new ContractError(code);
  }
}

function decodeStrictUtf8(bytes, code) {
  try {
    return new TextDecoder('utf-8', { fatal: true, ignoreBOM: true }).decode(bytes);
  } catch {
    throw new ContractError(code);
  }
}

function parseJson(text, code) {
  try {
    return JSON.parse(text);
  } catch {
    throw new ContractError(code);
  }
}

function hasForbiddenPayloadMember(value) {
  if (Array.isArray(value)) return value.some(hasForbiddenPayloadMember);
  if (value && typeof value === 'object') {
    return Object.entries(value).some(([key, nested]) =>
      key === 'eventType' || key === 'eventName' || key === 'eventPayloadName' || hasForbiddenPayloadMember(nested));
  }
  return false;
}

function verifyR1RuntimePayloadRoundTrip(officialV1Bytes) {
  const envelope = parseJson(decodeStrictUtf8(officialV1Bytes, 'r1-official-envelope-invalid'), 'r1-official-envelope-invalid');
  if (envelope?.version !== 1 || !Array.isArray(envelope.eventCandidates)) {
    throw new ContractError('r1-official-envelope-invalid');
  }

  for (const candidate of envelope.eventCandidates) {
    if (typeof candidate.payload !== 'string') throw new ContractError('r1-payload-invalid');
    const payload = Buffer.from(candidate.payload, 'base64');
    if (candidate.payload.length % 4 !== 0 || candidate.payload.includes('-') || candidate.payload.includes('_') ||
        payload.toString('base64') !== candidate.payload) {
      throw new ContractError('r1-base64-not-standard-padded');
    }
    ensureNoBom(payload, 'bom-prefixed-payload');
    const decoded = decodeStrictUtf8(payload, 'invalid-utf8-payload');
    const clientPayload = parseJson(decoded, 'non-json-payload');
    if (hasForbiddenPayloadMember(clientPayload)) throw new ContractError('r1-payload-member-conflict');
    if (!Buffer.from(decoded, 'utf8').equals(payload)) throw new ContractError('r1-payload-not-byte-identical');
  }
}

// JSON.parse cannot expose duplicate keys, so scan the literal first. The input is separately parsed by JSON.parse before
// this result is used; this scanner only supplies the profile's duplicate-key discriminator.
function hasDuplicateKeys(text) {
  let index = 0;
  const whitespace = () => {
    while (index < text.length && /\s/.test(text[index])) index++;
  };
  const readString = () => {
    if (text[index] !== '"') throw new Error('Invalid JSON string.');
    const start = index++;
    while (index < text.length) {
      const character = text[index++];
      if (character === '\\') {
        index++;
      } else if (character === '"') {
        return JSON.parse(text.slice(start, index));
      }
    }
    throw new Error('Unterminated JSON string.');
  };
  const readValue = () => {
    whitespace();
    const start = text[index];
    if (start === '{') {
      index++;
      const names = new Set();
      whitespace();
      if (text[index] === '}') {
        index++;
        return false;
      }
      while (true) {
        whitespace();
        const name = readString();
        if (names.has(name)) return true;
        names.add(name);
        whitespace();
        if (text[index++] !== ':') throw new Error('Expected JSON colon.');
        if (readValue()) return true;
        whitespace();
        if (text[index] === '}') {
          index++;
          return false;
        }
        if (text[index++] !== ',') throw new Error('Expected JSON object separator.');
      }
    }
    if (start === '[') {
      index++;
      whitespace();
      if (text[index] === ']') {
        index++;
        return false;
      }
      while (true) {
        if (readValue()) return true;
        whitespace();
        if (text[index] === ']') {
          index++;
          return false;
        }
        if (text[index++] !== ',') throw new Error('Expected JSON array separator.');
      }
    }
    if (start === '"') {
      readString();
      return false;
    }
    const primitiveStart = index;
    while (index < text.length && !/[\s,}\]]/.test(text[index])) index++;
    JSON.parse(text.slice(primitiveStart, index));
    return false;
  };

  const duplicate = readValue();
  if (duplicate) return true;
  whitespace();
  if (index !== text.length) throw new Error('Trailing JSON content.');
  return false;
}

function convertClientModelToCanonicalV1(clientBytes) {
  ensureNoBom(clientBytes, 'client-document-bom');
  const clientText = decodeStrictUtf8(clientBytes, 'client-json-invalid');
  // Validate syntax before returning a duplicate result that intentionally stops scanning early.
  const root = parseJson(clientText, 'client-json-invalid');
  if (hasDuplicateKeys(clientText)) throw new ContractError('duplicate-json-key');
  if (!root || !Array.isArray(root.candidates) || !Array.isArray(root.consistency)) {
    throw new ContractError('client-shape-invalid');
  }

  const eventCandidates = root.candidates.map((candidate) => {
    if (!candidate || typeof candidate !== 'object' || typeof candidate.eventId !== 'string' ||
        typeof candidate.eventPayloadName !== 'string' || !Object.hasOwn(candidate, 'payload') ||
        !Array.isArray(candidate.tags)) {
      throw new ContractError('client-candidate-invalid');
    }
    if (candidate.tags.some((tag) => typeof tag !== 'string' || tag.length === 0)) {
      throw new ContractError('empty-tag');
    }
    return {
      payload: Buffer.from(JSON.stringify(candidate.payload), 'utf8').toString('base64'),
      eventPayloadName: candidate.eventPayloadName,
      tags: candidate.tags
    };
  });

  const seenConsistency = new Set();
  const consistencyTags = root.consistency.map((entry) => {
    if (!entry || typeof entry !== 'object' || typeof entry.tag !== 'string' ||
        typeof entry.lastSortableUniqueId !== 'string') {
      throw new ContractError('client-consistency-invalid');
    }
    if (entry.tag.length === 0) throw new ContractError('empty-tag');
    if (seenConsistency.has(entry.tag)) throw new ContractError('duplicate-consistency');
    seenConsistency.add(entry.tag);
    return { tag: entry.tag, lastSortableUniqueId: entry.lastSortableUniqueId };
  });

  // eventId is intentionally absent: it is client-only and excluded from every C# V1 comparison.
  return Buffer.from(JSON.stringify({ version: 1, eventCandidates, consistencyTags }), 'utf8');
}

function firstCanonicalPayloadText(canonicalV1Bytes) {
  const envelope = JSON.parse(canonicalV1Bytes.toString('utf8'));
  return Buffer.from(envelope.eventCandidates[0].payload, 'base64').toString('utf8');
}

function expectCode(callback, expected) {
  try {
    callback();
  } catch (error) {
    if (error instanceof ContractError && error.code === expected) return;
    throw error;
  }
  throw new Error(`Expected contract error ${expected}.`);
}

function runContract() {
  const manifest = loadManifestAndVerifyProvenance();
  verifyR1RuntimePayloadRoundTrip(readFixture('interop_official_v1_populated.json'));

  if (!convertClientModelToCanonicalV1(readFixture('interop_ts_client_model.json')).equals(
    readFixture('interop_official_v1_populated.json'))) {
    throw new Error('The paired TypeScript client fixture did not produce the frozen official V1 bytes.');
  }
  if (!convertClientModelToCanonicalV1(readFixture('interop_r2_canonical_positive.json')).equals(
    readFixture('interop_r2_canonical_positive_v1.json'))) {
    throw new Error('The R2 canonical positive did not produce its frozen V1 bytes.');
  }
  if (firstCanonicalPayloadText(convertClientModelToCanonicalV1(readFixture('interop_r2_integer_like_key.json'))) !==
      '{"2":2,"z":1,"a":3}') {
    throw new Error('The integer-like-key witness did not preserve observed JavaScript key ordering.');
  }
  if (firstCanonicalPayloadText(convertClientModelToCanonicalV1(readFixture('interop_r2_numeric_lexical_loss.json'))) !==
      '{"one":1,"exp":100,"negativeZero":0,"large":9007199254740992}') {
    throw new Error('The numeric witness did not preserve observed JavaScript numeric normalization.');
  }

  expectCode(() => convertClientModelToCanonicalV1(readFixture('interop_r2_duplicate_key.json')), 'duplicate-json-key');
  expectCode(() => convertClientModelToCanonicalV1(readFixture('interop_client_empty_tag.json')), 'empty-tag');
  expectCode(() => convertClientModelToCanonicalV1(readFixture('interop_client_duplicate_consistency.json')), 'duplicate-consistency');
  expectCode(() => verifyR1RuntimePayloadRoundTrip(readFixture('interop_r3_bom_payload.json')), 'bom-prefixed-payload');
  expectCode(() => verifyR1RuntimePayloadRoundTrip(readFixture('interop_r3_non_json_payload.json')), 'non-json-payload');
  expectCode(() => verifyR1RuntimePayloadRoundTrip(readFixture('interop_r3_invalid_utf8_payload.json')), 'invalid-utf8-payload');

  console.log(`SEK-G52 Node interop runner passed ${manifest.fixtures.length} frozen fixtures.`);
}

function main() {
  const args = process.argv.slice(2);
  const verifyFile = args.indexOf('--verify-file');
  if (verifyFile >= 0) {
    const expected = args.indexOf('--expected-sha');
    if (verifyFile + 1 >= args.length || expected < 0 || expected + 1 >= args.length) {
      throw new Error('--verify-file requires --expected-sha.');
    }
    const bytes = readFileSync(args[verifyFile + 1]);
    verifyDigest(bytes, bytes.length, args[expected + 1]);
    console.log('SEK-G52 Node digest verification passed.');
    return;
  }
  runContract();
}

try {
  main();
} catch (error) {
  console.error(`SEK-G52 Node interop runner failed: ${error.message}`);
  process.exitCode = 1;
}
