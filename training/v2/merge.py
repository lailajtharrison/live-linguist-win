"""Builds data/train_v2.jsonl: the v1 training set plus every generated pair that passed verification.

Pairs are rendered exactly like build-data.js (the app's prompt, no worked examples).
    python merge.py gen
"""
import json
import os
import random
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)

prompts_cs = open(os.path.join(ROOT, "..", "winui", "Services", "Prompts.cs"), encoding="utf-8").read()
SYS = re.search(r'FrenchFalc\s*=\s*"(.*?)";', prompts_cs, re.S).group(1).replace('\\"', '"')


def render(inp, out):
    return {
        "prompt": f"<|im_start|>system\n{SYS}<|im_end|>\n"
                  f"<|im_start|>user\nOriginal: {inp}\nRewritten:<|im_end|>\n"
                  f"<|im_start|>assistant\n<think>\n\n</think>\n\n",
        "completion": f"{out}<|im_end|>",
    }


def main():
    gen_dir = sys.argv[1]
    with open(os.path.join(ROOT, "data", "train.jsonl"), encoding="utf-8") as f:
        v1 = [json.loads(l) for l in f if l.strip()]
    accepted = []
    with open(os.path.join(gen_dir, "verified.jsonl"), encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            try:
                r = json.loads(line)
            except json.JSONDecodeError:
                continue
            if r.get("ok"):
                accepted.append(render(r["input"], r["output"]))
    rows = v1 + accepted
    random.Random(42).shuffle(rows)
    with open(os.path.join(ROOT, "data", "train_v2.jsonl"), "w", encoding="utf-8") as f:
        for r in rows:
            f.write(json.dumps(r, ensure_ascii=False) + "\n")
    print(f"train_v2: {len(rows)} rows (v1 {len(v1)} + generated {len(accepted)})")


if __name__ == "__main__":
    main()
