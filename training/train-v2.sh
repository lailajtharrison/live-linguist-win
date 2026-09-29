#!/usr/bin/env bash
# Round 2: generate verified training data with two local teacher models, then retrain the 0.6B.
# Run on the DGX Spark from the training/ folder:
#   ./train-v2.sh
# Takes several hours (best overnight). Safe to stop and rerun: finished work is kept and skipped.
# Needs Docker, the GPU, and internet access to nvcr.io, huggingface.co, pypi.org and github.com.
# First run downloads about 70 GB (container + two teacher models).
set -euo pipefail

IMAGE="${PYTORCH_IMAGE:-nvcr.io/nvidia/pytorch:25.09-py3}"
TARGET="${TARGET:-12000}"   # how many new utterances to generate
HERE="$(cd "$(dirname "$0")" && pwd)"
TAG="weights-fr-0.6b-v2-$(date +%Y%m%d)"
mkdir -p "$HERE/out-v2" "$HERE/.hf-cache"

echo "== Using container $IMAGE"
docker run --rm --gpus all --ipc=host \
  -e TARGET="$TARGET" \
  -e BUILD_DIR="${BUILD_DIR:-llama.cpp/build-cuda}" \
  -v "$HERE/..":/repo -w /repo/training \
  -v "$HERE/.hf-cache":/root/.cache/huggingface \
  "$IMAGE" bash -euo pipefail -c '
    python -c "import torch; print(torch.__version__, torch.cuda.is_available(), torch.cuda.get_device_name(0))"
    pip install --quiet "trl>=0.20" "peft>=0.15" "datasets>=3.0" "accelerate>=1.0" "huggingface_hub>=0.25"
    # Same fix as train.sh: the 25.09 container ships torchao 0.13, which the peft LoRA
    # dispatch (peft 0.20+) rejects. Remove it so peft takes the normal LoRA path.
    pip uninstall -y torchao >/dev/null 2>&1 || true
    command -v cmake >/dev/null || pip install --quiet cmake

    echo "== Building llama.cpp with CUDA (first run only)"
    if [ ! -d llama.cpp ]; then git clone --depth 1 https://github.com/ggml-org/llama.cpp; fi
    # Build dir defaults to llama.cpp/build-cuda (on the mounted repo, so the compiled
    # binary is cached across resumable reruns). Set BUILD_DIR to a path off the mount
    # (e.g. /opt/llama-build) if CMake cannot create its pkgRedirects dir on the mounted
    # filesystem — as happens under a restricted/background sandbox.
    BUILD="${BUILD_DIR:-llama.cpp/build-cuda}"
    if [ ! -x "$BUILD/bin/llama-server" ]; then
      # Start from a clean build dir so CMake never trips over a stale re-configure.
      rm -rf "$BUILD"
      # "native" detects the GB10; if this nvcc cannot, fall back to its compute capability (12.1).
      cmake -S llama.cpp -B "$BUILD" -DGGML_CUDA=ON -DCMAKE_CUDA_ARCHITECTURES=native \
            -DLLAMA_CURL=OFF -DCMAKE_BUILD_TYPE=Release \
      || { rm -rf "$BUILD"; cmake -S llama.cpp -B "$BUILD" -DGGML_CUDA=ON \
            -DCMAKE_CUDA_ARCHITECTURES=121 -DLLAMA_CURL=OFF -DCMAKE_BUILD_TYPE=Release; }
      cmake --build "$BUILD" --target llama-server -j "$(nproc)"
    fi
    SERVER="$BUILD/bin/llama-server"

    echo "== Downloading teacher models (first run only)"
    WRITER=$(python -c "from huggingface_hub import hf_hub_download as d; print(d(\"unsloth/Qwen3-30B-A3B-Instruct-2507-GGUF\", \"Qwen3-30B-A3B-Instruct-2507-Q8_0.gguf\"))")
    CHECKER=$(python -c "from huggingface_hub import hf_hub_download as d; print(d(\"unsloth/Mistral-Small-3.2-24B-Instruct-2506-GGUF\", \"Mistral-Small-3.2-24B-Instruct-2506-Q5_K_M.gguf\"))")

    echo "== Starting teacher servers"
    $SERVER -m "$WRITER"  --port 8201 -ngl 999 -c 32768 -np 8 --jinja > out-v2/writer.log 2>&1 &
    WPID=$!
    $SERVER -m "$CHECKER" --port 8202 -ngl 999 -c 32768 -np 8 --jinja > out-v2/checker.log 2>&1 &
    CPID=$!
    trap "kill $WPID $CPID 2>/dev/null || true" EXIT
    for port in 8201 8202; do
      for i in $(seq 1 600); do
        if python -c "import urllib.request,sys; sys.exit(0 if b\"ok\" in urllib.request.urlopen(\"http://127.0.0.1:$port/health\").read() else 1)" 2>/dev/null; then break; fi
        sleep 2
        if [ "$i" = 600 ]; then echo "teacher on port $port did not start; see out-v2/*.log"; exit 1; fi
      done
    done

    echo "== Generating and verifying training pairs (target $TARGET)"
    python v2/gen.py --writer http://127.0.0.1:8201 --checker http://127.0.0.1:8202 --out v2/gen --target "$TARGET" 2>&1 | tee -a out-v2/gen.log
    kill $WPID $CPID 2>/dev/null || true
    sleep 5

    echo "== Building train_v2.jsonl"
    python v2/merge.py v2/gen | tee out-v2/merge.log

    echo "== Training recipe wide2"
    python train.py --recipe wide2 --train-file train_v2.jsonl --out runs/wide2 2>&1 | tee out-v2/train-wide2.log

    echo "== Converting to GGUF"
    pip install --quiet ./llama.cpp/gguf-py sentencepiece
    python llama.cpp/convert_hf_to_gguf.py runs/wide2/merged --outtype f16 --outfile out-v2/ll-fr-0.6b-wide2-f16.gguf
    cp runs/wide2/metrics.json out-v2/metrics-wide2.json
    cp v2/gen/summary.json out-v2/gen-summary.json
    cp v2/gen/verified.jsonl out-v2/gen-verified.jsonl
  '

echo
ls -lh "$HERE/out-v2"

# Upload to GitHub as a pre-release so it never replaces the app's "Latest" release.
if command -v gh >/dev/null && gh auth status >/dev/null 2>&1; then
  echo "== Uploading $TAG to GitHub"
  gh release create "$TAG" "$HERE"/out-v2/*.gguf "$HERE"/out-v2/*.json "$HERE"/out-v2/*.jsonl "$HERE"/out-v2/*.log \
    --prerelease --latest=false \
    --title "Training checkpoint - FR 0.6B wide2 (round 2)" \
    --notes "Round 2: trained on v1 data plus teacher-generated pairs that passed verification. For testing only; not an app release."
  echo "== Uploaded. Tell Claude on the Dell: the round 2 model is on GitHub as $TAG"
else
  echo "== gh is not set up here. Upload the out-v2 folder with:"
  echo "   gh release create $TAG out-v2/* --prerelease --latest=false --title 'FR 0.6B round 2'"
fi
