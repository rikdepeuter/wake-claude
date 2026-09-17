// Spike: zoek in het app-pakket naar de claude://-routes en de sneltoetsen.
// Het app.asar-archief is binair, maar de JavaScript erin staat als leesbare tekst.
const fs = require("fs");
const path = require("path");

const appDir = process.argv[2];
const asar = path.join(appDir, "resources", "app.asar");
if (!fs.existsSync(asar)) { console.log("geen app.asar in " + asar); process.exit(1); }

const buf = fs.readFileSync(asar);
const txt = buf.toString("latin1");
console.log("app.asar: " + (buf.length / 1024 / 1024).toFixed(1) + " MB");

function zoek(label, re, venster, max) {
  console.log("\n=== " + label + " ===");
  const gezien = new Set();
  let m, n = 0;
  while ((m = re.exec(txt)) !== null && n < max) {
    const s = txt.substr(Math.max(0, m.index - venster), venster * 2 + m[0].length).replace(/[^\x20-\x7e]/g, " ").replace(/\s+/g, " ");
    const sleutel = m[0];
    if (gezien.has(s)) continue;
    gezien.add(s);
    console.log("- [" + sleutel + "] …" + s + "…");
    n++;
  }
  if (n === 0) console.log("(niets)");
}

zoek("epitaxy-routes", /epitaxy\/[a-zA-Z_:${}.-]{2,40}/g, 60, 12);
zoek("deep-link hosts/paden", /claude:\/\/claude\.ai\/[a-zA-Z_\-/${}.]{2,50}/g, 40, 12);
zoek("sneltoetsen rond nieuwe sessie", /(CmdOrCtrl|CommandOrControl)\+[A-Za-z0-9+]{1,12}/g, 70, 40);
