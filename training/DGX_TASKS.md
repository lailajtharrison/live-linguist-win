# Tasks for the DGX Spark agent

Handoff from the Claude session on the Dell (Windows), which evaluates models. Work top to bottom.
When done, update the **Status** section at the bottom, commit and push, so the Dell side can see it.

## Background (read first)

- Live Linguist rewrites spoken French into FALC easy French for Abby's students on Windows laptops.
  The app runs the model on CPU through llama.cpp, so speed matters: the shipped 1.7B takes 2.6 s
  per caption on an i9; the target is under ~1.5 s.
- Round 1 (release `weights-fr-0.6b-20260925`): the 0.6B LoRA `wide` at Q5_K_M took 1.23 s per caption
  and fixed questions, padding and subjunctive, but made **more meaning errors** than the shipped
  model ("une vingtaine de minutes" became "20 secondes"; the Easterlin paradox came out reversed).
  Cause: too little data (1.9k pairs) and overfitting. Not shipped.
- Round 2 fixes the data: two local teacher models generate and cross-check about 12k new pairs.

## Task 1: run round 2

```
cd ~/live-linguist-win && git pull && cd training
chmod +x train-v2.sh
./train-v2.sh
```

- Runs several hours and is resumable (rerun after any stop; finished work in `v2/gen/` is kept).
- `train-v2.sh`, `v2/gen.py` and `v2/merge.py` were written on a machine without Python and have
  **never been executed**. Expect to fix small issues. Fix them in the repo (commit and push) rather
  than working around them locally, so the kit stays reproducible.
- Watch the first stage's output. After the first few hundred items, check `v2/gen/rewrites.jsonl`
  and `v2/gen/verified.jsonl` by eye. If the checker rejects more than about 60%, or accepts obvious
  mistakes, stop and report that in Status before spending hours on it.
- Keep the Windows app's prompt format: `v2/merge.py` renders pairs exactly like `build-data.js`
  (the app's system prompt from `winui/Services/Prompts.cs`, no worked examples, the pre-filled empty
  `<think>` block). Don't change that without noting it.
- **Never train on anything in `data-src/heldout.txt`** (the evaluation set). `gen.py` filters it;
  keep that true if you change generation.

## Task 2: publish the result

`train-v2.sh` uploads automatically if `gh` is logged in. If you publish manually, it **must** be a
pre-release that is **not** Latest. `v0.7-fr` has to stay Latest, because that is the app users download:

```
gh release create weights-fr-0.6b-v2-YYYYMMDD out-v2/* --prerelease --latest=false --title "FR 0.6B round 2"
```

Include `gen-summary.json` and `gen-verified.jsonl`; the Dell side reviews the generated data too.

## Task 3 (optional, only after Task 2): 1.7B with the round 2 data

If there is time, train the 1.7B full-SFT recipe (`train_fr.sh`) on `data/fr_aug` **plus** the
round 2 accepted pairs. That is a quality upgrade of the shipped model at the same speed, a fallback
in case the 0.6B still falls short. Publish it the same way (pre-release, not Latest), with a distinct
tag such as `weights-fr-1.7b-v2-YYYYMMDD`. Leave f16 as it is; the Dell side quantizes and tests
Q4_K_M and Q5_K_M (Dylan's model card reports the 1.7B degrading at Q4_K_M).

## Don't

- Don't change `winui/` (the app) or cut app releases; the Dell side ships only after evaluation
  and after Abby reviews.
- Don't mark any training release as Latest.
- Don't commit model weights or `v2/gen/` to git (both are gitignored; weights go in releases).

## Status (DGX agent: update this)

- Task 1: not started
- Task 2: not started
- Task 3: not started
- Notes:
