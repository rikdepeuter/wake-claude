// Spike: lees de code-tak van claudeURLHandler integraal uit, rond /continue en /new.
const fs = require("fs");
const path = require("path");
const txt = fs.readFileSync(path.join(process.argv[2], "resources", "app.asar")).toString("latin1");

function blok(label, anker, voor, na) {
  const i = txt.indexOf(anker);
  console.log("\n=== " + label + " ===");
  if (i < 0) { console.log("(anker niet gevonden)"); return; }
  console.log(txt.substr(Math.max(0, i - voor), voor + na).replace(/[^\x20-\x7e]/g, " "));
}

blok("OS-snelkoppeling nieuwe Code-sessie", 'id:"NewCode"', 0, 420);
blok("code: /continue en /needs-input", 'a.pathname==="/continue"||a.pathname==="/needs-input"', 300, 900);
blok("code: /new", 'unrecognized code path', 200, 1100);
