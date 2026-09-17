// Spike: volg de melding die "de Remote Control-instellingen opent" tot zijn klikactie,
// en zoek alle beschrijvingen die over Remote Control-mappen gaan.
const fs = require("fs");
const path = require("path");
const txt = fs.readFileSync(path.join(process.argv[2], "resources", "app.asar")).toString("latin1");
const schoon = s => s.replace(/[^\x20-\x7e]/g, " ").replace(/\s+/g, " ");

console.log("=== rond 'opens the Remote Control settings' ===");
let i = -1, n = 0;
while ((i = txt.indexOf("opens the Remote Control settings", i + 1)) >= 0 && n < 3) {
  console.log("- …" + schoon(txt.substr(Math.max(0, i - 200), 1400)) + "…\n");
  n++;
}

console.log("=== routes of paden met 'remote' in de buurt van settings ===");
const re = /(settings|preferences)[^"'`]{0,20}["'`][^"'`]{0,40}remote[^"'`]{0,40}["'`]/gi;
let m, k = 0; const gezien = new Set();
while ((m = re.exec(txt)) !== null && k < 12) {
  const s = schoon(m[0]); if (gezien.has(s)) continue; gezien.add(s);
  console.log("- " + s); k++;
}
if (!k) console.log("(niets)");

console.log("\n=== beschrijvingen die 'folder' en 'Remote Control' samen noemen ===");
const re2 = /description:"[^"]{0,80}Remote Control[^"]{0,80}folder[^"]{0,120}"/g;
k = 0; gezien.clear();
while ((m = re2.exec(txt)) !== null && k < 20) {
  const s = schoon(m[0]); if (gezien.has(s)) continue; gezien.add(s);
  console.log("- " + s); k++;
}
if (!k) console.log("(niets)");
