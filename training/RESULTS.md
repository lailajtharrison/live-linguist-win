# Training results — LiveLinguist FR simplifier (0.6B)

Two LoRA fine-tunes of the 0.6B base were run on the FR simplification data
(`data/train.jsonl` / `data/valid.jsonl`) on the DGX Spark. Both exported to
f16 GGUF (~1.2 GB each) in `training/out/` — **not committed** (gitignored, too
big for GitHub). Re-run from `train.py` / `train.sh` to regenerate, or ask for a
direct file transfer of the weights. The `metrics-*.json` files here are the
full logs.

## Two recipes

| | **dylan** (conservative) | **wide** (aggressive) |
|---|---|---|
| rank `r` / `alpha` | 16 / 320 | 16 / 32 |
| targets | attn only (q,k,v,o) | all linear (q,k,v,o + gate,up,down) |
| layers tuned | top 16 only | all |
| epochs | 1 | 3 |
| lr | 1e-5 | 2e-4 |
| **eval loss** | 1.0130 | **0.9281** |
| **eval token-acc** | 0.7588 | **0.7895** |
| eval entropy | 1.006 | 0.547 |
| final train loss | 1.034 | 0.373 |
| final train tok-acc | 0.757 | 0.902 |

## What we found

- **`wide` is the stronger checkpoint on every eval metric** and produces the
  more fluent simplification. Sample (same input):
  - wide: *"Cette somme va réparer les collèges ruraux. Beaucoup d'élus de
    l'opposition jugeaient cette somme trop peu."* — clean, natural.
  - dylan: *"Celle est pour la rénovation des collèges ruraux..."* — grammatically
    awkward ("Celle est"), clearly under-trained.
- **`dylan` is under-fit**: 1 epoch, attention-only, top-16 layers, train loss
  only reached ~1.03. It barely moved the base model.
- **`wide` shows an overfitting gap to watch**: train tok-acc 0.902 vs eval 0.790
  after 3 full epochs at lr 2e-4. Eval is still the best of the two, but the gap
  means more epochs would likely hurt generalization.

## Recommended next step

Start from the **wide** recipe (all-linear targets, all layers) but pull back to
**1–2 epochs** and/or a lower lr (~1e-4) to close the train/eval overfit gap.
`dylan`'s conservative footprint isn't worth pursuing — it under-trains.
