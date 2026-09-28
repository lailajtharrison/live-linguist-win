"""Generate verified French easy-language (FALC) training pairs with local teacher models.

Three resumable stages, each appending to a JSONL file in --out (rerunning skips finished work):
  1. utterances.jsonl  diverse things people say, written by the WRITER model
  2. rewrites.jsonl    a FALC rewrite of each utterance, by the WRITER model
  3. verified.jsonl    each pair checked by the CHECKER model (a different model family)
                       plus deterministic checks; only pairs that pass everything are used

Talks to two llama-server instances over their OpenAI-compatible API (stdlib only).
    python gen.py --writer http://127.0.0.1:8201 --checker http://127.0.0.1:8202 --out gen --target 12000
"""
import argparse
import concurrent.futures as cf
import itertools
import json
import os
import random
import re
import sys
import threading
import unicodedata
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)  # training/

DOMAINS = [
    "un échange virtuel entre étudiants américains et marocains (présentations, vie étudiante, culture)",
    "un cours de français langue étrangère en classe",
    "l'administration universitaire (inscription, bourse, logement, examens)",
    "la vie quotidienne et la famille",
    "les actualités et la météo",
    "la santé et un rendez-vous médical",
    "le travail et une réunion d'équipe",
    "la culture, l'histoire et la littérature",
    "la cuisine et les fêtes en France et au Maroc",
    "les voyages et les transports",
    "les sciences et l'environnement",
    "la technologie et l'informatique",
    "le sport et les loisirs",
    "l'économie, l'argent et la consommation",
    "les services publics et les démarches administratives",
    "la visite d'un musée ou d'un site touristique",
]
STYLES = [
    "de la parole spontanée avec des hésitations (euh, ben, du coup, en fait), des idées enchaînées sans ponctuation",
    "une question posée directement à quelqu'un",
    "une demande polie formulée comme une question (Pourriez-vous..., Auriez-vous..., Est-ce que vous pourriez...)",
    "une réponse ou une réaction très courte (1 à 6 mots)",
    "une phrase formelle et longue, au vocabulaire soutenu, avec des subordonnées, du subjonctif ou du conditionnel",
    "une annonce ou une consigne donnée à un groupe",
    "une explication précise avec des chiffres, des dates, des durées ou des unités",
    "un récit personnel au passé",
]

UTTERANCE_PROMPT = """Écris {n} phrases différentes et réalistes qu'une personne pourrait dire à voix haute, en français.
Thème : {domain}.
Style : {style}.
Varie les personnes, les situations, les longueurs et le vocabulaire. Chaque phrase est indépendante et se comprend seule.
N'écris pas de phrases déjà simplifiées. N'utilise pas de guillemets autour des phrases.
Réponds uniquement avec un objet JSON de la forme {{"phrases": ["...", "..."]}}."""

TEACHER_RULES = """
Additional rules for this task (you are producing reference rewrites; accuracy matters more than anything):
- Preserve the meaning exactly. Every number, date, duration, unit, amount, name and place in the input must appear, unchanged in value, in the output (numbers may be written as digits).
- Add nothing: no explanation, background, consequence, opinion or advice that the speaker did not say.
- If the input is a question, the output is a question (ending with "?"). Never answer it. A polite request phrased as a question may become a polite imperative with "s'il vous plaît".
- If the input is already short and simple, return it almost unchanged; do not pad it.
- Use correct French grammar and agreement. Remove fillers (euh, ben, du coup, en fait, voilà, hein).
- Output only the rewrite."""

CHECK_PROMPT = """Tu es un relecteur expert en FALC (Facile à Lire et à Comprendre) et en grammaire française.
On te donne une phrase ORIGINALE (parole transcrite) et sa RÉÉCRITURE en français facile.
Évalue la réécriture strictement :
- sens_preserve : la réécriture dit la même chose que l'originale, sans contresens ni information importante perdue (les hésitations peuvent disparaître).
- rien_invente : la réécriture n'ajoute aucune information absente de l'originale (fait, cause, conseil, conséquence, opinion).
- nombres_ok : tous les nombres, dates, durées, unités, montants, noms et lieux sont conservés avec la même valeur.
- question_ok : si l'originale est une vraie question, la réécriture reste une question et n'y répond pas ; si l'originale n'est pas une question, mets true.
- grammaire_ok : la réécriture est en français correct (accords, conjugaisons, orthographe).
- simple : phrases courtes, une idée par phrase, mots courants.

ORIGINALE : {src}
RÉÉCRITURE : {out}

Réponds uniquement avec un objet JSON : {{"sens_preserve": true/false, "rien_invente": true/false, "nombres_ok": true/false, "question_ok": true/false, "grammaire_ok": true/false, "simple": true/false, "probleme": "court texte ou vide"}}"""

POLITE_Q = re.compile(r"^\s*(pourriez|pourrais|auriez|aurais|voudriez|voudrais|serait-il|est-ce que vous pourriez|est-ce que tu pourrais|vous pouvez|tu peux|pouvez-vous|peux-tu)", re.I)
FILLERS = re.compile(r"\b(euh+|ben|bah|hein|du coup|en fait|voilà|genre)\b", re.I)


def norm(s):
    s = unicodedata.normalize("NFKC", s).lower()
    return re.sub(r"[^\w]+", " ", s).strip()


def words(s):
    return re.findall(r"[\w'’-]+", s)


def sentences(s):
    return [x for x in re.split(r"(?<=[.!?])\s+", s.strip()) if x]


def chat(base, messages, temperature, max_tokens, json_mode=False, seed=None):
    body = {"messages": messages, "temperature": temperature, "max_tokens": max_tokens}
    if json_mode:
        body["response_format"] = {"type": "json_object"}
    if seed is not None:
        body["seed"] = seed
    req = urllib.request.Request(f"{base}/v1/chat/completions", data=json.dumps(body).encode("utf-8"),
                                 headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=600) as r:
        return json.loads(r.read())["choices"][0]["message"]["content"].strip()


def load_jsonl(path):
    if not os.path.exists(path):
        return []
    out = []
    with open(path, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line:
                try:
                    out.append(json.loads(line))
                except json.JSONDecodeError:
                    pass  # a line cut off by an interrupted run
    return out


class Appender:
    def __init__(self, path):
        self.f = open(path, "a", encoding="utf-8")
        self.lock = threading.Lock()

    def write(self, obj):
        with self.lock:
            self.f.write(json.dumps(obj, ensure_ascii=False) + "\n")
            self.f.flush()


def held_out_inputs():
    """Inputs that must never be trained on: the upstream test split and our regression probes."""
    held = set()
    with open(os.path.join(ROOT, "data-src", "heldout.txt"), encoding="utf-8") as f:
        for line in f:
            if line.strip():
                held.add(norm(line))
    return held


def asr_style(text, rng):
    """Make a transcript look like live speech recognition output: no capitals or punctuation."""
    t = text.lower()
    t = re.sub(r"[,;:!?.«»\"()]", " ", t)
    t = re.sub(r"\s+", " ", t).strip()
    if rng.random() < 0.5 and not FILLERS.search(t):
        parts = t.split(" ")
        i = rng.randrange(len(parts)) if parts else 0
        parts.insert(i, rng.choice(["euh", "ben", "du coup", "en fait"]))
        t = " ".join(parts)
    return t


def stage_utterances(args, held):
    path = os.path.join(args.out, "utterances.jsonl")
    have = load_jsonl(path)
    seen = {norm(u["text"]) for u in have} | held
    need = args.target - len(have)
    print(f"[1/3] utterances: have {len(have)}, need {max(0, need)}", flush=True)
    if need <= 0:
        return have
    app = Appender(path)
    combos = list(itertools.product(DOMAINS, STYLES))
    lock = threading.Lock()
    count = [len(have)]

    def job(k):
        domain, style = combos[k % len(combos)]
        try:
            raw = chat(args.writer, [{"role": "user", "content": UTTERANCE_PROMPT.format(n=10, domain=domain, style=style)}],
                       temperature=1.0, max_tokens=1500, json_mode=True, seed=k)
            phrases = json.loads(raw).get("phrases", [])
        except Exception as e:  # malformed JSON or a transient server error: skip this batch
            print(f"  utterance batch {k} failed: {e}", file=sys.stderr, flush=True)
            return
        for p in phrases:
            if not isinstance(p, str) or not (2 <= len(words(p)) <= 70):
                continue
            n = norm(p)
            with lock:
                if n in seen or count[0] >= args.target:
                    continue
                seen.add(n)
                count[0] += 1
            app.write({"id": f"u{k}-{len(n)}-{abs(hash(n)) % 10**8}", "text": p.strip(), "domain": domain, "style": style})

    start = len(have) + 1000  # new seeds on every resume
    k = start
    with cf.ThreadPoolExecutor(args.workers) as ex:
        while count[0] < args.target:
            batch = [ex.submit(job, k + i) for i in range(args.workers * 4)]
            k += len(batch)
            cf.wait(batch)
            print(f"  utterances: {count[0]}/{args.target}", flush=True)
            if k - start > args.target * 2:  # generation has stalled on duplicates; stop anyway
                break
    return load_jsonl(path)


def few_shot():
    shots = []
    with open(os.path.join(ROOT, "data-src", "targeted.jsonl"), encoding="utf-8") as f:
        rows = [json.loads(l) for l in f if l.strip()]
    # one or two of each kind, so the teacher sees every rule demonstrated
    by_kind = {}
    for r in rows:
        by_kind.setdefault(r["why"], []).append(r)
    for kind, rs in by_kind.items():
        shots.extend(rs[:2])
    msgs = []
    for s in shots:
        msgs.append({"role": "user", "content": f"Original: {s['input']}\nRewritten:"})
        msgs.append({"role": "assistant", "content": s["output"]})
    return msgs


def stage_rewrites(args, utterances, system_prompt):
    path = os.path.join(args.out, "rewrites.jsonl")
    done = {r["id"] for r in load_jsonl(path)}
    todo = [u for u in utterances if u["id"] not in done]
    print(f"[2/3] rewrites: have {len(done)}, need {len(todo)}", flush=True)
    app = Appender(path)
    shots = few_shot()
    sys_msg = {"role": "system", "content": system_prompt + TEACHER_RULES}
    rng = random.Random(7)
    # A quarter of inputs are turned into speech-recognition style transcripts, like what Whisper gives the app.
    asr_ids = {u["id"] for u in utterances if rng.random() < 0.25}

    def job(u):
        src = asr_style(u["text"], random.Random(u["id"])) if u["id"] in asr_ids else u["text"]
        try:
            out = chat(args.writer, [sys_msg, *shots, {"role": "user", "content": f"Original: {src}\nRewritten:"}],
                       temperature=0.2, max_tokens=300)
        except Exception as e:
            print(f"  rewrite {u['id']} failed: {e}", file=sys.stderr, flush=True)
            return
        app.write({"id": u["id"], "input": src, "output": out, "asr": u["id"] in asr_ids})

    with cf.ThreadPoolExecutor(args.workers) as ex:
        for i, _ in enumerate(ex.map(job, todo), 1):
            if i % 500 == 0:
                print(f"  rewrites: {len(done) + i}/{len(utterances)}", flush=True)
    return load_jsonl(path)


def deterministic_problems(src, out):
    probs = []
    if not out or "<think>" in out or "Original:" in out or "Rewritten:" in out:
        probs.append("format")
    for d in re.findall(r"\d+", src):
        if d not in out:
            probs.append(f"lost number {d}")
    if src.rstrip().endswith("?") and not POLITE_Q.search(src) and "?" not in out:
        probs.append("question dropped")
    if any(len(words(s)) > 15 for s in sentences(out)):
        probs.append("sentence too long")
    if len(words(out)) > 1.6 * len(words(src)) + 6:
        probs.append("much longer than input")
    if FILLERS.search(out):
        probs.append("filler kept")
    return probs


def stage_verify(args, rewrites):
    path = os.path.join(args.out, "verified.jsonl")
    done = {r["id"] for r in load_jsonl(path)}
    todo = [r for r in rewrites if r["id"] not in done]
    print(f"[3/3] verify: have {len(done)}, need {len(todo)}", flush=True)
    app = Appender(path)
    keys = ["sens_preserve", "rien_invente", "nombres_ok", "question_ok", "grammaire_ok", "simple"]

    def job(r):
        probs = deterministic_problems(r["input"], r["output"])
        verdict = {}
        if not probs:
            try:
                raw = chat(args.checker, [{"role": "user", "content": CHECK_PROMPT.format(src=r["input"], out=r["output"])}],
                           temperature=0.0, max_tokens=200, json_mode=True)
                verdict = json.loads(raw)
                probs += [k for k in keys if verdict.get(k) is not True]
            except Exception as e:
                probs.append(f"checker error: {e}")
        app.write({**r, "ok": not probs, "problems": probs, "checker": verdict})

    with cf.ThreadPoolExecutor(args.workers) as ex:
        for i, _ in enumerate(ex.map(job, todo), 1):
            if i % 500 == 0:
                print(f"  verified: {len(done) + i}/{len(rewrites)}", flush=True)
    return load_jsonl(path)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--writer", required=True)
    ap.add_argument("--checker", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--target", type=int, default=12000)
    ap.add_argument("--workers", type=int, default=8)
    args = ap.parse_args()
    os.makedirs(args.out, exist_ok=True)

    prompts_cs = open(os.path.join(ROOT, "..", "winui", "Services", "Prompts.cs"), encoding="utf-8").read()
    system_prompt = re.search(r'FrenchFalc\s*=\s*"(.*?)";', prompts_cs, re.S).group(1).replace('\\"', '"')

    held = held_out_inputs()
    utterances = stage_utterances(args, held)
    rewrites = stage_rewrites(args, utterances, system_prompt)
    verified = stage_verify(args, rewrites)

    ok = [v for v in verified if v["ok"]]
    counts = {}
    for v in verified:
        for p in v["problems"]:
            key = p.split(" ")[0] if p.startswith("lost number") else p
            counts[key] = counts.get(key, 0) + 1
    summary = {"utterances": len(utterances), "rewrites": len(rewrites), "verified": len(verified),
               "accepted": len(ok), "rejections_by_reason": dict(sorted(counts.items(), key=lambda x: -x[1]))}
    with open(os.path.join(args.out, "summary.json"), "w", encoding="utf-8") as f:
        json.dump(summary, f, ensure_ascii=False, indent=2)
    print(json.dumps(summary, ensure_ascii=False, indent=2), flush=True)


if __name__ == "__main__":
    main()
