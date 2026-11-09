#!/usr/bin/env node
// CI check (SPEC §9): every key in locales/vi exists in locales/en and vice versa, and no value is empty.
import { readdirSync, readFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..', 'src', 'locales');
const langs = ['vi', 'en'];

function flatten(obj, prefix = '', out = {}) {
  for (const [k, v] of Object.entries(obj)) {
    const key = prefix ? `${prefix}.${k}` : k;
    if (v && typeof v === 'object' && !Array.isArray(v)) flatten(v, key, out);
    else out[key] = v;
  }
  return out;
}

const data = {};
for (const lang of langs) {
  data[lang] = {};
  for (const f of readdirSync(join(root, lang)).filter((f) => f.endsWith('.json'))) {
    const ns = f.replace(/\.json$/, '');
    data[lang][ns] = flatten(JSON.parse(readFileSync(join(root, lang, f), 'utf8')));
  }
}

let errors = 0;
const namespaces = new Set(langs.flatMap((l) => Object.keys(data[l])));
for (const ns of namespaces) {
  for (const lang of langs) {
    if (!data[lang][ns]) { console.error(`✗ namespace "${ns}" missing in ${lang}`); errors++; continue; }
    const other = langs.find((l) => l !== lang);
    for (const [key, value] of Object.entries(data[lang][ns])) {
      if (value === '' || value === null) { console.error(`✗ ${lang}/${ns}: empty value for "${key}"`); errors++; }
      if (data[other][ns] && !(key in data[other][ns])) { console.error(`✗ ${other}/${ns}: missing key "${key}" (present in ${lang})`); errors++; }
    }
  }
}
const total = Object.values(data.vi).reduce((n, ns) => n + Object.keys(ns).length, 0);
if (errors) { console.error(`\n${errors} i18n problem(s) found.`); process.exit(1); }
console.log(`✓ i18n OK — ${namespaces.size} namespaces, ${total} keys, vi/en in sync.`);
