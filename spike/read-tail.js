// Spike: lees enkel wat er vanaf een byte-offset aan een transcript is toegevoegd, en
// toon per regel het type, het tijdstip, de token-telling en een kort begin van de tekst.
const fs = require("fs");

const [bestand, offsetArg] = process.argv.slice(2);
const offset = parseInt(offsetArg, 10);
const fd = fs.openSync(bestand, "r");
const grootte = fs.fstatSync(fd).size;
const buf = Buffer.alloc(grootte - offset);
fs.readSync(fd, buf, 0, buf.length, offset);
fs.closeSync(fd);

const regels = buf.toString("utf8").split("\n").filter(Boolean);
console.log("toegevoegd: " + buf.length + " bytes, " + regels.length + " regels\n");

function kort(v) {
  if (v == null) return "";
  let t = typeof v === "string" ? v : Array.isArray(v) ? v.map(x => x.text || x.type || "").join(" ") : JSON.stringify(v);
  t = t.replace(/\s+/g, " ").trim();
  return t.length > 90 ? t.slice(0, 90) + "…" : t;
}

let invoer = 0, uitvoer = 0, cacheLees = 0, cacheSchrijf = 0;
for (const r of regels) {
  let o;
  try { o = JSON.parse(r); } catch { console.log("(geen json)"); continue; }
  const tijd = (o.timestamp || "").slice(11, 19);
  const u = o.message && o.message.usage;
  if (u) {
    invoer += u.input_tokens || 0; uitvoer += u.output_tokens || 0;
    cacheLees += u.cache_read_input_tokens || 0; cacheSchrijf += u.cache_creation_input_tokens || 0;
  }
  const inhoud = o.message ? kort(o.message.content) : kort(o.content || o.summary || o.operation);
  const extra = [o.isMeta ? "meta" : "", o.userType || "", o.entrypoint || "", o.origin || ""].filter(Boolean).join(",");
  console.log(`${tijd}  ${(o.type || "?").padEnd(10)} ${extra ? "[" + extra + "] " : ""}${inhoud}`);
}

console.log("\ntokens in deze toevoeging:");
console.log("  invoer (niet gecachet): " + invoer);
console.log("  cache gelezen         : " + cacheLees);
console.log("  cache geschreven      : " + cacheSchrijf);
console.log("  uitvoer               : " + uitvoer);
