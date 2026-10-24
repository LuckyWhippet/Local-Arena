#!/usr/bin/env node
// Ed25519 signer for latest.json, mirroring scripts/sign-update.py.
//
// The Python signer needs PyNaCl, which is unavailable when pip is broken.
// Node ships Ed25519 in its crypto module, so this is a dependency-free
// equivalent that produces the identical Base64 signature format.
//
// Usage:
//   node scripts/sign-update.mjs <manifest> <signature-out> --public-key <pub.txt>
//
// The 32-byte Ed25519 seed is read from CSBIP_UPDATE_SIGNING_KEY (Base64), the
// same environment variable the Python signer uses.

import { createPrivateKey, createPublicKey, sign } from 'node:crypto';
import { readFileSync, writeFileSync } from 'node:fs';

function fail(message) {
  console.error(`sign-update: ${message}`);
  process.exit(1);
}

function parseArgs(argv) {
  const positional = [];
  let publicKeyPath = null;
  for (let i = 0; i < argv.length; i += 1) {
    if (argv[i] === '--public-key') {
      publicKeyPath = argv[i + 1];
      i += 1;
    } else {
      positional.push(argv[i]);
    }
  }
  if (positional.length !== 2 || !publicKeyPath) {
    fail('usage: sign-update.mjs <manifest> <signature-out> --public-key <public-key.txt>');
  }
  return { manifest: positional[0], signature: positional[1], publicKeyPath };
}

const { manifest, signature, publicKeyPath } = parseArgs(process.argv.slice(2));

const encodedSeed = (process.env.CSBIP_UPDATE_SIGNING_KEY ?? '').trim();
if (!encodedSeed) fail('CSBIP_UPDATE_SIGNING_KEY is required');

const seed = Buffer.from(encodedSeed, 'base64');
if (seed.length !== 32) fail('CSBIP_UPDATE_SIGNING_KEY must decode to 32 bytes');

// Wrap the raw seed in a PKCS#8 envelope so Node can import it.
const pkcs8Prefix = Buffer.from('302e020100300506032b657004220420', 'hex');
const privateKey = createPrivateKey({
  key: Buffer.concat([pkcs8Prefix, seed]),
  format: 'der',
  type: 'pkcs8',
});

// Refuse to sign with a key that the shipped binary would reject.
const spki = createPublicKey(privateKey).export({ type: 'spki', format: 'der' });
const actualPublic = spki.subarray(spki.length - 32).toString('base64');
const expectedPublic = readFileSync(publicKeyPath, 'ascii').trim();
if (actualPublic !== expectedPublic) {
  fail('signing secret does not match the embedded update public key');
}

const payload = readFileSync(manifest);
const raw = sign(null, payload, privateKey);
if (raw.length !== 64) fail(`unexpected signature length ${raw.length}`);

writeFileSync(signature, `${raw.toString('base64')}\n`, 'ascii');
console.log(`signed ${manifest} -> ${signature}`);
