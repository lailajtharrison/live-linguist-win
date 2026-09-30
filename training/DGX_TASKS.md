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

- Task 1: **DONE** (2026-09-29). Generated 12,000 utterances → rewrites → verified with the two
  teacher servers. **10,736 accepted (89%)**. Top rejection reasons: sentence too long 689,
  question_ok 288, sens_preserve 119, lost-number 72, rien_invente 70, grammaire 56. Merged to
  `data/train_v2.jsonl` = **12,627 pairs** (1,891 v1 + 10,736 new). Retrained 0.6B **wide2**
  (2 epochs, lr 1e-4): **eval_loss 0.8818, tok-acc 0.7919** — vs round-1 `wide` 0.9281 / 0.7895,
  i.e. lower loss on 6.6× the data. Spot-checks: questions kept as questions, numbers preserved,
  fillers stripped, no obvious invention getting through the checker.
- Task 2: **DONE**. Published pre-release **`weights-fr-0.6b-v2-20260929`** (prerelease, NOT Latest)
  with `ll-fr-0.6b-wide2-f16.gguf` (f16, 1.2 GB) + `gen-summary.json` + `gen-verified.jsonl` + logs.
  `v0.6-fr` remains the app's Latest. f16 only — Dell quantizes to Q4_K_M / Q5_K_M and tests.
- Task 3: not done (optional 1.7B full-SFT on fr_aug + round-2 accepted pairs). Left for a follow-up.
- Notes: Fixed a pre-existing bug in `train-v2.sh` — a stray apostrophe in a comment (`peft>=0.20's`)
  closed the single-quoted `docker ... bash -c '...'` block, leaking cmake + the python teacher
  downloads to the host (looked like "cmake pkgRedirects" / "python: command not found"). Fixed +
  pushed; also added a `BUILD_DIR` override and an up-front build-dir clean. **NEXT (Dell side):**
  download `ll-fr-0.6b-wide2-v2` into `C:\Users\lharr260\ll-eval\incoming\` and run the 216-sentence
  eval — ship only if it clears round-1's meaning errors (e.g. "20 min" → "20 sec", reversed Easterlin)
  while keeping the ~1.2 s/caption speed.
