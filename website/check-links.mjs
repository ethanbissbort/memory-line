#!/usr/bin/env node
/**
 * Verify the built site: every internal link points at a page or asset that
 * exists, and every in-site anchor resolves to a real element id.
 *
 *   node check-links.mjs [--dir _site]
 *
 * Exits non-zero when something is broken, so CI can gate on it.
 */

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const dirArg = process.argv.indexOf('--dir');
const siteDir = path.resolve(
  here,
  dirArg > -1 && process.argv[dirArg + 1] ? process.argv[dirArg + 1] : '_site'
);

if (!fs.existsSync(siteDir)) {
  console.error(`No build found at ${siteDir}. Run "npm run build" first.`);
  process.exit(1);
}

const htmlFiles = fs.readdirSync(siteDir).filter((f) => f.endsWith('.html'));
const documents = new Map();

for (const file of htmlFiles) {
  const html = fs.readFileSync(path.join(siteDir, file), 'utf8');
  documents.set(file, {
    html,
    ids: new Set([...html.matchAll(/\sid="([^"]+)"/g)].map((m) => m[1])),
  });
}

const problems = [];
let links = 0;
let anchors = 0;

for (const [file, { html }] of documents) {
  for (const match of html.matchAll(/href="([^"]+)"/g)) {
    const href = match[1];
    if (/^(?:https?:|mailto:|data:|javascript:)/i.test(href)) continue;

    const [target, fragment] = href.split('#');
    const page = target === '' ? file : target;

    if (target !== '') {
      links += 1;
      const onDisk = path.join(siteDir, target);
      if (!fs.existsSync(onDisk)) {
        problems.push(`${file} → ${href} (target does not exist)`);
        continue;
      }
    }

    if (fragment && documents.has(page)) {
      anchors += 1;
      if (!documents.get(page).ids.has(fragment)) {
        problems.push(`${file} → ${href} (anchor not found)`);
      }
    }
  }
}

if (problems.length) {
  console.error(`${problems.length} broken link(s):`);
  for (const problem of problems) console.error(`  ${problem}`);
  process.exit(1);
}

console.log(
  `OK — ${htmlFiles.length} pages, ${links} internal links and ${anchors} anchors all resolve.`
);
