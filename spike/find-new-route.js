// Spike: welke parameters accepteert de claude://claude.ai/new-route?
// Zoekt in app.asar naar de plek waar die URL ontleed wordt.
const fs = require("fs");
const path = require("path");

const txt = fs.readFileSync(path.join(process.argv[2], "resources", "app.asar")).toString("latin1");

function toon(label, re, venster, max) {
  console.log("\n=== " + label + " ===");
  const gezien = new Set();
  let m, n = 0;
  while ((m = re.exec(txt)) !== null && n < max) {
    const s = txt.substr(Math.max(0, m.index - venster), venster * 2).replace(/[^\x20-\x7e]/g, " ").replace(/\s+/g, " ");
    const k = s.slice(venster - 40, venster + 40);
    if (gezien.has(k)) continue;
    gezien.add(k);
    console.log("- …" + s + "…");
    n++;
  }
  if (!n) console.log("(niets)");
}

toon("surface-parameter", /surface=(code|chat|cowork|[a-z]+)/g, 160, 8);
toon("searchParams.get(...)", /searchParams\.get\("[a-zA-Z_]+"\)/g, 90, 30);
toon("pathname === / startsWith rond epitaxy of new", /(pathname|host)[^;]{0,40}("\/new"|"new"|"epitaxy")/g, 140, 10);
