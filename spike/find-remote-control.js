// Spike: hoe zet de app Remote Control aan? Zoek commandopalet-items, sneltoetsen,
// dispatch-functies en link-parameters rond Remote Control.
const fs = require("fs");
const path = require("path");
const txt = fs.readFileSync(path.join(process.argv[2], "resources", "app.asar")).toString("latin1");

function toon(label, re, venster, max) {
  console.log("\n=== " + label + " ===");
  const gezien = new Set();
  let m, n = 0;
  while ((m = re.exec(txt)) !== null && n < max) {
    const s = txt.substr(Math.max(0, m.index - venster), venster * 2 + m[0].length)
      .replace(/[^\x20-\x7e]/g, " ").replace(/\s+/g, " ");
    const k = m[0] + "|" + s.slice(venster - 30, venster + 30);
    if (gezien.has(k)) continue;
    gezien.add(k);
    console.log("- [" + m[0] + "] …" + s + "…");
    n++;
  }
  if (!n) console.log("(niets)");
}

toon("dispatch-functies met RemoteControl", /dispatch[A-Za-z]*RemoteControl[A-Za-z]*/g, 120, 8);
toon("setters van remoteControlUserEnabled", /remoteControlUserEnabled\s*[:=]/g, 140, 8);
toon("labels 'Remote Control' met accelerator of palette", /defaultMessage:"[^"]{0,40}Remote Control[^"]{0,40}"/g, 160, 14);
toon("link-parameters rond remote", /searchParams\.get\("(remote|rc|remoteControl|bridge)[A-Za-z]*"\)/g, 140, 8);
