// Lists the Dutch UI texts in the code (Loc.T / Loc.F / {c:T ...}) that have no English
// translation in Core/Loc.En.cs yet. Run from the repository root: node scripts/strings.js
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..', 'HomeyBar');
const files = [];
(function walk(dir) {
  for (const name of fs.readdirSync(dir)) {
    if (name === 'bin' || name === 'obj') continue;
    const p = path.join(dir, name);
    if (fs.statSync(p).isDirectory()) walk(p);
    else if (/\.(cs|xaml)$/.test(name) && !name.startsWith('Loc.En')) files.push(p);
  }
})(root);

const found = new Set();
const quoted = /"((?:[^"\\]|\\.)*)"/g;
for (const file of files) {
  const text = fs.readFileSync(file, 'utf8');
  // Every string literal inside a Loc.T(...) or Loc.F(...) call, including both arms of a ?:
  for (const call of text.matchAll(/Loc\.[TF]\(((?:[^()]|\([^()]*\))*)\)/g)) {
    for (const q of call[1].matchAll(quoted)) found.add(q[1]);
  }
  // Loc.T(x switch { ... => "text" }) spreads over several lines
  for (const block of text.matchAll(/Loc\.T\([\w.]+ switch\s*\{([\s\S]*?)\}\)/g)) {
    for (const q of block[1].matchAll(/=>\s*"((?:[^"\\]|\\.)*)"/g)) found.add(q[1]);
  }
  for (const m of text.matchAll(/\{c:T '([^']*)'\}/g)) found.add(m[1]);
  for (const m of text.matchAll(/\{c:T ([^'}][^}]*)\}/g)) found.add(m[1].trim());
}

const en = fs.readFileSync(path.join(root, 'Core', 'Loc.En.cs'), 'utf8');
const known = new Set([...en.matchAll(/\["((?:[^"\\]|\\.)*)"\]\s*=/g)].map(m => m[1]));
const missing = [...found].filter(s => !known.has(s) && /[a-zA-Z]/.test(s)).sort();
console.log(missing.join('\n'));
console.error(`${found.size} texts, ${missing.length} without English`);
