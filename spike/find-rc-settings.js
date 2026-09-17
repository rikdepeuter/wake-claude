// Spike: waar in de app zitten de Remote Control-instellingen voor mappen?
// De vertaalteksten hebben een "description" die de plek in de interface beschrijft.
const fs = require("fs");
const path = require("path");
const txt = fs.readFileSync(path.join(process.argv[2], "resources", "app.asar")).toString("latin1");

function toon(label, re, max) {
  console.log("\n=== " + label + " ===");
  const gezien = new Set();
  let m, n = 0;
  while ((m = re.exec(txt)) !== null && n < max) {
    const regel = m[0].replace(/\s+/g, " ");
    if (gezien.has(regel)) continue;
    gezien.add(regel);
    console.log("- " + regel);
    n++;
  }
  if (!n) console.log("(niets)");
}

// defaultMessage + description-paren waarvan de beschrijving over Remote Control-instellingen gaat
toon("teksten in de Remote Control-instellingen",
  /defaultMessage:"[^"]{1,90}",(?:id:"[^"]{6,16}",)?description:"[^"]{0,60}Remote Control (settings|section|page|pane|tab)[^"]{0,120}"/g, 30);

toon("instellingenroutes met remote",
  /["'`]\/settings\/[a-z\-\/]*remote[a-z\-\/]*["'`]/g, 10);

toon("namen van instellingentabs",
  /defaultMessage:"[^"]{1,40}",(?:id:"[^"]{6,16}",)?description:"[^"]{0,40}[Ss]ettings (tab|sidebar|nav|navigation|section title)[^"]{0,80}"/g, 30);
