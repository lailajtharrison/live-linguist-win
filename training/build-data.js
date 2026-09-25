// Builds training/data/{train,valid}.jsonl in prompt/completion form from:
//   - ndgold/live-linguist-easylanguage-sft fr/train + fr/valid (CC BY-SA 4.0)
//   - data-src/question-fixes.json  (targets that answered a question, rewritten)
//   - data-src/targeted.jsonl       (hand-written cases for failures we measured)
// Usage: node build-data.js <dir containing fr-train.jsonl, fr-valid.jsonl, fr-test.jsonl>
const fs = require("fs");
const path = require("path");

const SRC = process.argv[2];
if (!SRC) { console.error("usage: node build-data.js <upstream data dir>"); process.exit(1); }
const HERE = __dirname;

// The app's system prompt, read from the source so training matches inference exactly.
const promptsCs = fs.readFileSync(path.join(HERE, "..", "winui", "Services", "Prompts.cs"), "utf8");
const SYS = promptsCs.match(/FrenchFalc\s*=\s*"([\s\S]*?)";/)[1].replace(/\\"/g, '"');

// Same rendering as LlamaSimplifier, minus worked examples: the fine-tune learns the register.
const prompt = (input) =>
  `<|im_start|>system\n${SYS}<|im_end|>\n` +
  `<|im_start|>user\nOriginal: ${input}\nRewritten:<|im_end|>\n` +
  `<|im_start|>assistant\n<think>\n\n</think>\n\n`;
const pair = (input, output) => ({ prompt: prompt(input), completion: `${output}<|im_end|>` });

const readUpstream = (file) =>
  fs.readFileSync(path.join(SRC, file), "utf8").split("\n").filter(Boolean).map((l) => {
    const m = JSON.parse(l).messages;
    return {
      input: m[1].content.replace(/^Original:\s*/, "").replace(/\s*Rewritten:\s*$/, ""),
      output: m[2].content,
    };
  });

const norm = (s) => s.toLowerCase().replace(/[^\p{L}\p{N}]+/gu, " ").trim();

// Held-out inputs that must never be trained on: upstream test split + our regression probes.
const heldOut = new Set(readUpstream("fr-test.jsonl").map((r) => norm(r.input)));
const evalJs = fs.readFileSync("C:\\Users\\lharr260\\ll-eval\\eval.js", "utf8");
for (const m of evalJs.matchAll(/\["[^"]+", "([^"]+)"\]/g)) heldOut.add(norm(m[1]));

const fixes = JSON.parse(fs.readFileSync(path.join(HERE, "data-src", "question-fixes.json"), "utf8"));
const targeted = fs.readFileSync(path.join(HERE, "data-src", "targeted.jsonl"), "utf8")
  .split("\n").filter(Boolean).map((l) => JSON.parse(l));

const train = [];
let fixed = 0, dropped = 0;
readUpstream("fr-train.jsonl").forEach((r, i) => {
  if (heldOut.has(norm(r.input))) { dropped++; return; }
  if (fixes[i] !== undefined) { r.output = fixes[i]; fixed++; }
  train.push(pair(r.input, r.output));
});
if (fixed !== Object.keys(fixes).filter((k) => !k.startsWith("_")).length) throw new Error("some fixes did not apply");

// The targeted set is small next to ~1.7k upstream pairs, so it is repeated 3x.
for (const t of targeted) {
  if (heldOut.has(norm(t.input))) throw new Error("targeted example duplicates a held-out input: " + t.input);
  for (let k = 0; k < 3; k++) train.push(pair(t.input, t.output));
}

const valid = readUpstream("fr-valid.jsonl").filter((r) => !heldOut.has(norm(r.input))).map((r) => pair(r.input, r.output));

// Deterministic shuffle so reruns produce identical files.
let seed = 42;
const rnd = () => ((seed = (seed * 1103515245 + 12345) % 2 ** 31) / 2 ** 31);
for (let i = train.length - 1; i > 0; i--) { const j = Math.floor(rnd() * (i + 1)); [train[i], train[j]] = [train[j], train[i]]; }

fs.mkdirSync(path.join(HERE, "data"), { recursive: true });
fs.writeFileSync(path.join(HERE, "data", "train.jsonl"), train.map((x) => JSON.stringify(x)).join("\n") + "\n");
fs.writeFileSync(path.join(HERE, "data", "valid.jsonl"), valid.map((x) => JSON.stringify(x)).join("\n") + "\n");
console.log(`train: ${train.length} (upstream ${train.length - 3 * targeted.length}, questions fixed ${fixed}, ` +
  `targeted ${targeted.length} x3, dropped as held-out ${dropped})  valid: ${valid.length}`);
