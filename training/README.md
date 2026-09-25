# Training the fast (0.6B) Live Linguist model on the DGX Spark

This folder trains a smaller, roughly 3× faster French model for Live Linguist. It runs
entirely inside NVIDIA's PyTorch container, so nothing is installed on the Spark.

## On the DGX Spark

1. Open a terminal on the Spark and get this repo:
   ```
   git clone https://github.com/lailajtharrison/live-linguist-win.git
   cd live-linguist-win
   git switch training-kit
   cd training
   ```
   (No internet for GitHub? Copy the `training` folder over on a USB stick instead.)
2. Run:
   ```
   chmod +x train.sh
   ./train.sh
   ```
   It trains two versions (`dylan` and `wide`) and takes about 15–45 minutes. The first
   run also downloads a large container, around 20 GB.
   - "permission denied" from Docker: run `sudo ./train.sh` instead.
   - "manifest unknown" / image not found: pick a current tag from
     https://catalog.ngc.nvidia.com/orgs/nvidia/containers/pytorch and run
     `PYTORCH_IMAGE=nvcr.io/nvidia/pytorch:<tag> ./train.sh`
3. When it finishes, copy the whole `training/out` folder back to the Dell (USB stick or
   OneDrive). It holds two `.gguf` files of about 1.2 GB each, plus logs.

## Back on the Dell

Put the `out` folder at `C:\Users\lharr260\ll-eval\incoming\` and ask Claude to evaluate it.
Each model is compressed to the app's size and run through the same 216-sentence test
the current model was measured on. A model only ships if it matches today's app quality
on the hard cases while staying fast.

## What is in here

| file | purpose |
|---|---|
| `data/train.jsonl`, `data/valid.jsonl` | 1,891 training / 208 validation pairs, built by `build-data.js` |
| `data-src/question-fixes.json` | 29 upstream examples that answered a question, rewritten so the question stays a question |
| `data-src/targeted.jsonl` | 70 hand-written examples for failures measured on 2026-09-24 (padding short input, answering questions, gender, "ça" referents, subjunctive, fillers) |
| `train.py` | LoRA fine-tune of `Qwen/Qwen3-0.6B` (trl + peft) |
| `train.sh` | runs both recipes in the container and exports GGUF |

Upstream data: `ndgold/live-linguist-easylanguage-sft` (CC BY-SA 4.0), French split. The
200-sentence test split and the regression probes are excluded from training.
