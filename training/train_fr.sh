#!/usr/bin/env bash
# Live Linguist French 1.7B fine-tune — the shipped-quality recipe.
# Run this on the DGX Spark from the training/ folder:
#   ./train_fr.sh
# Full-SFT of Qwen3-1.7B on the question-augmented FALC data (data/fr_aug), inside
# NVIDIA's PyTorch container. Nothing is installed on the Spark itself. The trained
# model lands in runs/fr/merged and is converted to out/ll-fr-1.7b-f16.gguf; if a
# llama-quantize binary is found on the host it is also quantized to Q4_K_M (the
# format the Windows app ships). ~48 min on a GB10.
#
# Override the base or output via env, e.g. to fine-tune from a local base:
#   BASE_MODEL=/path/to/qwen3-1.7b-base ./train_fr.sh
set -euo pipefail

IMAGE="${PYTORCH_IMAGE:-nvcr.io/nvidia/pytorch:25.09-py3}"
HERE="$(cd "$(dirname "$0")" && pwd)"
mkdir -p "$HERE/out" "$HERE/.hf-cache"

echo "== Using container $IMAGE"
docker run --rm --gpus all --ipc=host \
  -v "$HERE":/work -w /work \
  -v "$HERE/.hf-cache":/root/.cache/huggingface \
  -e BASE_MODEL="${BASE_MODEL:-Qwen/Qwen3-1.7B-Base}" \
  "$IMAGE" bash -euo pipefail -c '
    echo "== GPU check"
    python -c "import torch; print(torch.__version__, torch.cuda.is_available(), torch.cuda.get_device_name(0))"

    echo "== Installing training libraries (inside the container only)"
    pip install --quiet "trl>=0.20" "datasets>=3.0" "accelerate>=1.0"
    pip install --quiet liger-kernel || echo "== liger-kernel unavailable; training will run without it"
    # The 25.09 container ships torchao 0.13, which trips peft>=0.20 if trl imports
    # it. Remove it defensively (full-SFT does not need it).
    pip uninstall -y torchao >/dev/null 2>&1 || true

    echo "== Training French 1.7B (full-SFT, question-augmented)"
    python train_fr.py 2>&1 | tee "out/train-fr.log"

    echo "== Converting to GGUF (f16)"
    if [ ! -d llama.cpp ]; then git clone --depth 1 https://github.com/ggml-org/llama.cpp; fi
    pip install --quiet ./llama.cpp/gguf-py sentencepiece
    python llama.cpp/convert_hf_to_gguf.py runs/fr/merged \
      --outtype f16 --outfile out/ll-fr-1.7b-f16.gguf
    cp runs/fr/metrics.json out/metrics-fr.json
  '

# Q4_K_M quantization (the shippable format). Done on the host with an existing
# prebuilt llama-quantize if present; skipped cleanly otherwise (f16 is portable).
QUANT="${LLAMA_QUANTIZE:-/home/lharr260/spark-transfer/llama-cpp-build/build/bin/llama-quantize}"
if [ -x "$QUANT" ]; then
  echo "== Quantizing to Q4_K_M via $QUANT"
  LD_LIBRARY_PATH="$(dirname "$QUANT"):${LD_LIBRARY_PATH:-}" "$QUANT" \
    "$HERE/out/ll-fr-1.7b-f16.gguf" "$HERE/out/ll-fr-1.7b-Q4_K_M.gguf" Q4_K_M
else
  echo "== No llama-quantize at $QUANT — leaving f16 only (quantize separately for the app)"
fi

echo
echo "== Done. Outputs:"
ls -lh "$HERE/out"/ll-fr-1.7b-*.gguf 2>/dev/null || true
