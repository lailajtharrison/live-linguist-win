#!/usr/bin/env bash
# Live Linguist fine-tune: run this on the DGX Spark from the training/ folder.
#   ./train.sh
# Needs Docker (preinstalled on DGX OS) and internet access to nvcr.io, huggingface.co,
# pypi.org and github.com. Nothing is installed on the Spark itself; everything runs in
# NVIDIA's PyTorch container. Results land in training/out/.
#
# If the container tag below is not available, set a newer one from
# https://catalog.ngc.nvidia.com/orgs/nvidia/containers/pytorch, for example:
#   PYTORCH_IMAGE=nvcr.io/nvidia/pytorch:26.08-py3 ./train.sh
set -euo pipefail

IMAGE="${PYTORCH_IMAGE:-nvcr.io/nvidia/pytorch:25.09-py3}"
HERE="$(cd "$(dirname "$0")" && pwd)"
mkdir -p "$HERE/out" "$HERE/.hf-cache"

echo "== Using container $IMAGE"
docker run --rm --gpus all --ipc=host \
  -v "$HERE":/work -w /work \
  -v "$HERE/.hf-cache":/root/.cache/huggingface \
  "$IMAGE" bash -euo pipefail -c '
    echo "== GPU check"
    python -c "import torch; print(torch.__version__, torch.cuda.is_available(), torch.cuda.get_device_name(0))"

    echo "== Installing training libraries (inside the container only)"
    pip install --quiet "trl>=0.20" "peft>=0.15" "datasets>=3.0" "accelerate>=1.0"

    for RECIPE in dylan wide; do
      echo "== Training recipe: $RECIPE"
      python train.py --recipe "$RECIPE" --out "runs/$RECIPE" 2>&1 | tee "out/train-$RECIPE.log"
    done

    echo "== Converting to GGUF"
    if [ ! -d llama.cpp ]; then git clone --depth 1 https://github.com/ggml-org/llama.cpp; fi
    pip install --quiet ./llama.cpp/gguf-py sentencepiece
    for RECIPE in dylan wide; do
      python llama.cpp/convert_hf_to_gguf.py "runs/$RECIPE/merged" \
        --outtype f16 --outfile "out/ll-fr-0.6b-$RECIPE-f16.gguf"
      cp "runs/$RECIPE/metrics.json" "out/metrics-$RECIPE.json"
    done
  '

echo
echo "== Done. Copy everything in this folder back to the Dell:"
ls -lh "$HERE/out"
