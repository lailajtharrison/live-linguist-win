# Live Linguist for Windows

Live Linguist listens to French as it is spoken and shows it, live, in easy-to-read French. The easy register is FALC, Facile à Lire et à Comprendre. It works during a Teams or Zoom call, with a YouTube video, or with someone speaking in the room. Everything runs on the computer. Nothing is sent online.

**Status:** Active, as of 2026-10-12. Version 0.8 is built and under review. The `v0.8` branch loads the faster 0.6B v2 model, `ll-fr-0.6b-v2-Q4_K_M.gguf`, and falls back to the 1.7B file. The `main` branch still carries the 1.7B only, so `main` is behind `v0.8`.
**Owner:** Laila Harrison, KSU Office of Research.

## Download

**[Download the installer](https://github.com/lailajtharrison/live-linguist-win/releases/download/v0.8-fr/LiveLinguist-fr-Setup.exe)**. The file is LiveLinguist-fr-Setup.exe, about 640 MB.

This is version 0.8, which is currently being reviewed. See
[what's new and how to install](https://github.com/lailajtharrison/live-linguist-win/releases/tag/v0.8-fr).

1. Click the link above. The installer downloads to your Downloads folder.
2. Open it. If Windows says "Windows protected your PC", click **More info**, then
   **Run anyway**. The installer is not code-signed, so Windows shows this warning.
3. Live Linguist opens with a short welcome screen that shows you how to start.

Needs Windows 10 or 11 on a 64-bit PC. No graphics card is needed.

---

## For developers

This is the Windows port of [Live Linguist](https://www.ndgold.com/live-linguist). It rewrites live speech into simpler text in the same language. It does not translate.

## What it does

- **Captions a meeting or video.** Captures what plays through the speakers and transcribes it with Whisper. This is the Teams, Zoom, and YouTube use case.
- **Captions a speaker in the room.** Uses the Windows built-in French speech recognizer on the microphone. Shows partial text as the person talks.
- **Simplifies each finished phrase.** A fine-tuned Qwen3 model rewrites the phrase into FALC French on the CPU. The original and the simple version show one above the other.
- **Type to test.** A text box lets anyone paste French and click Simplifier, with no microphone or speech pack.
- **Shows latency.** The status bar reports the measured seconds per caption for the last phrase.
- **Stays offline.** The app makes no network calls. Models load from disk.

## Quick start

Developer build on Windows 10 or 11, x64. You need Visual Studio 2022 with the Windows App SDK / WinUI workload, and a .NET 8 SDK. `global.json` pins SDK 8.0.100 with roll-forward to the latest 8.0 feature band.

```powershell
git clone https://github.com/lailajtharrison/live-linguist-win.git
cd live-linguist-win

# Build the app. Use Visual Studio MSBuild, not dotnet build. WinUI needs the VS PRI tasks.
msbuild winui\LiveLinguistWinUI.csproj -t:restore -p:Configuration=Release -p:Platform=x64
msbuild winui\LiveLinguistWinUI.csproj -t:build -p:Configuration=Release -p:Platform=x64

# Native libraries, copied next to LiveLinguistWinUI.exe
nuget install LLamaSharp.Backend.Cpu -Version 0.24.0 -OutputDirectory natives
nuget install Whisper.net.Runtime -Version 1.8.1 -OutputDirectory natives
# copy natives\...\runtimes\win-x64\native\avx2\*.dll and natives\Whisper.net.Runtime*\build\win-x64\*.dll
# into the folder that contains LiveLinguistWinUI.exe

# Models, next to the exe or in %LOCALAPPDATA%\LiveLinguist\
gh release download v0.1-fr --repo lailajtharrison/live-linguist-win --pattern "*.gguf"
gh release download v0.1-fr --repo lailajtharrison/live-linguist-win --pattern "ggml-small-q5_1.bin"

# Run the exe from under winui\bin
```

For the microphone source, add the French speech pack first. Settings, Time and language, Speech, add Français (France). Allow Microphone for desktop apps under Privacy. See [RUN_ON_WINDOWS.md](RUN_ON_WINDOWS.md) for the one-time setup walk-through and `scripts/setup.ps1` for the scripted version.

Training runs on the DGX Spark, not on Windows. See the training pipeline below and [training/README.md](training/README.md).

## Repository layout

| Path | What lives there |
|---|---|
| `winui/` | The WinUI 3 app. `MainWindow.xaml` is the UI, `MainWindow.xaml.cs` wires the pipeline, `Services/` holds the speech sources and the simplifier, `ViewModels/` holds the bound state. |
| `winui/Services/Prompts.cs` | The FALC system prompt and four worked examples. Training scripts read the prompt from this file so training matches inference. |
| `installer/installer.iss` | Inno Setup script. Per-user install, Start menu and desktop shortcut, bundles app, native DLLs, and both models. |
| `scripts/setup.ps1` | One-time setup helper shipped in the zip bundle. Copies the model into `%LOCALAPPDATA%\LiveLinguist` and checks for a French language pack. |
| `smoke/` | A tiny WPF program that renders the dual-transcript layout offscreen to a PNG. Proves CI can build and screenshot a Windows GUI app. |
| `training/` | Fine-tuning kit for the DGX Spark. Data, recipes, container scripts, round results. |
| `training/data/` | `train.jsonl` and `valid.jsonl` for the 0.6B LoRA runs. `fr_aug/` holds the question-augmented set for the 1.7B full fine-tune. |
| `training/data-src/` | Hand-written fixes and targeted examples, plus `heldout.txt`, the inputs that must never be trained on. |
| `training/v2/` | Round 2 data generation. `gen.py` writes and verifies pairs with two local teacher models. `merge.py` builds `train_v2.jsonl`. |
| `training/out/` | Metric logs from round 1. Model weights are gitignored and live in GitHub releases. |
| `.github/workflows/` | Four workflows: `smoke`, `winui`, `bundle`, `installer`. See Deploy / distribute. |
| `RUN_ON_WINDOWS.md` | Manual setup notes for a target PC. Copied into the zip bundle. |
| `global.json` | Pins the .NET SDK band. |

## How it works

The request path for one caption.

- **Audio in.** The user picks a source with two radio buttons. `Micro` uses `Services/SpeechSource.cs`, the Windows `SpeechRecognizer` for fr-FR in continuous mode. `Réunion / vidéo` uses `Services/LoopbackTranscriber.cs`, which captures system playback through WASAPI loopback. WASAPI is the Windows Audio Session API.
- **Loopback transcription.** The loopback path downmixes to mono, resamples to 16 kHz, and gates on energy in 30 ms frames. An utterance flushes after 700 ms of silence or at 12 s, and anything under 400 ms is dropped. Whisper.net runs the `ggml-small-q5_1.bin` model with the French language set. Two common Whisper hallucinations on silence are filtered out.
- **Phrase event.** Both sources implement `ISpeechSource` and raise `Hypothesis` for partial text and `Phrase` for a finished one. `MainWindow` shows the phrase in the "Votre texte" box right away.
- **Simplify.** `Services/LlamaSimplifier.cs` builds a ChatML prompt by hand: the FALC system prompt, four worked examples, the phrase, then an assistant turn pre-filled with an empty think block so Qwen3 does not reason out loud. It runs LLamaSharp `StatelessExecutor` with greedy sampling, 256 max tokens, context 2048, zero GPU layers. Output is stripped of any think tags and `<|im_end|>`.
- **Display.** The simplified text lands in the "Langage simple · FALC" box through `ViewModels/MainViewModel.cs`. The status bar shows the measured latency in French decimal format.
- **Fallbacks.** If the LLM file is missing, the badge reads "Modèle manquant". If the microphone recognizer or French pack is missing, the badge reads "Micro éteint" and the hint explains how to add the pack. If the Whisper model is missing, the badge reads "Audio indisponible". The type-to-test box works in every state that has the LLM.
- **Screenshot mode.** If `SCREENSHOT_PATH` is set, the app waits 1.2 s, renders the window to that PNG, and exits. CI uses this.

### Model files

| File | Role | Size | Where the app looks | Source |
|---|---|---|---|---|
| `qwen3-1.7b-easylang-fr-Q4_K_M.gguf` | FALC simplifier, Qwen3 1.7B, 4-bit | about 1.1 GB | Next to the exe, then `%LOCALAPPDATA%\LiveLinguist\` | Release `v0.1-fr` |
| `ggml-small-q5_1.bin` | Whisper small, French transcription for the loopback source | unknown | Same lookup as above | Release `v0.1-fr` |
| `ll-fr-0.6b-{dylan,wide}-f16.gguf` | Round 1 LoRA candidates, 0.6B, f16 | about 1.2 GB each | Not used by the app | Release `weights-fr-0.6b-20260925` |
| `ll-fr-0.6b-wide2-f16.gguf` | Round 2 candidate, 0.6B, f16 | about 1.2 GB | Not used by the app yet | Pre-release `weights-fr-0.6b-v2-20260929` |
| `ll-fr-1.7b-f16.gguf`, `ll-fr-1.7b-Q4_K_M.gguf` | Output of `train_fr.sh`, the 1.7B full fine-tune | unknown | Not used by the app directly | Produced on the Spark. No release named in this repo. |

Native libraries ship beside the exe: `llama.dll` and `ggml*.dll` from `LLamaSharp.Backend.Cpu` 0.24.0, x64 AVX2 variant, plus the Whisper.net.Runtime 1.8.1 DLLs. The Spark's ARM Linux builds do not work here.

### Training pipeline

All training runs inside the NVIDIA PyTorch container on the DGX Spark, so nothing is installed on the host. Weights never go into git. They go into GitHub releases marked pre-release, never Latest.

- **Data build.** `build-data.js` reads the French split of `ndgold/live-linguist-easylanguage-sft`, applies 29 question fixes from `data-src/question-fixes.json`, repeats the 70 hand-written `data-src/targeted.jsonl` examples three times, and drops every held-out input. Output is 1,891 train and 208 validation pairs in prompt/completion form with the app's exact system prompt.
- **Round 1, `train.sh`.** `train.py` runs two LoRA recipes on `Qwen/Qwen3-0.6B`. LoRA is low-rank adaptation. `dylan` reproduces the upstream model card: attention only, top 16 layers, 1 epoch. `wide` targets all linear layers, 3 epochs, lr 2e-4. Both are merged and converted to f16 GGUF with llama.cpp. `RESULTS.md` records eval loss 1.0130 for `dylan` and 0.9281 for `wide`. `wide` is the clear winner but overfits.
- **Round 1 verdict.** On the Dell, `wide` at Q5_K_M took 1.23 s per caption against 2.6 s for the shipped 1.7B, but made more meaning errors. Not shipped. `DGX_TASKS.md` names the cause: too little data.
- **Round 2, `train-v2.sh`.** Builds llama.cpp with CUDA, downloads two teacher models, and serves them locally. `v2/gen.py` has the writer, Qwen3-30B-A3B-Instruct-2507, generate 12,000 utterances across 16 domains and 8 styles and rewrite each one. The checker, Mistral-Small-3.2-24B, then judges each pair on six criteria. Deterministic checks catch lost numbers, dropped questions, long sentences, padding, and fillers. A quarter of inputs are turned into speech-recognition style first. `v2/merge.py` joins accepted pairs with the v1 set, then `train.py --recipe wide2` trains 2 epochs at lr 1e-4.
- **Round 2 result.** 10,736 of 12,000 pairs accepted, 89 percent. `train_v2.jsonl` has 12,627 pairs. `wide2` reached eval loss 0.8818 and token accuracy 0.7919, against 0.9281 and 0.7895 for round 1. Published as pre-release `weights-fr-0.6b-v2-20260929`, f16 only. The next step, per `DGX_TASKS.md`, is the 216-sentence evaluation on the Dell. The model ships only if it clears round 1's meaning errors at about 1.2 s per caption.
- **1.7B recipe, `train_fr.sh`.** `train_fr.py` does a full fine-tune of `Qwen3-1.7B-Base` on `data/fr_aug`: 4 epochs, lr 2e-5, batch 32, Liger kernels if available. About 48 minutes on a GB10. Quantizes to Q4_K_M if a `llama-quantize` binary is found on the host. The script calls this the shipped-quality recipe.

## Configuration

There is no config file. Everything is an env var, a fixed path, or a script flag.

| Name | Used by | Required | What it does |
|---|---|---|---|
| `SCREENSHOT_PATH` | App | No | When set, the app renders one PNG to this path and exits. CI only. |
| `%LOCALAPPDATA%\LiveLinguist\` | App, `setup.ps1` | No | Fallback folder for both model files when they are not next to the exe. |
| `PYTORCH_IMAGE` | `train.sh`, `train-v2.sh`, `train_fr.sh` | No | NGC container tag. Default `nvcr.io/nvidia/pytorch:25.09-py3`. |
| `TARGET` | `train-v2.sh` | No | Number of utterances to generate. Default 12000. |
| `BUILD_DIR` | `train-v2.sh` | No | llama.cpp CUDA build directory. Default `llama.cpp/build-cuda`. |
| `BASE_MODEL` | `train_fr.sh`, `train_fr.py` | No | Base model for the 1.7B run. Default `Qwen/Qwen3-1.7B-Base`. |
| `OUT_DIR`, `DATA_DIR` | `train_fr.py` | No | Output folder and data folder overrides. |
| `LLAMA_QUANTIZE` | `train_fr.sh` | No | Path to a host `llama-quantize` binary. Quantization is skipped if absent. |
| `GH_TOKEN` | `installer.yml` | Yes, in CI | GitHub token used to download model assets and publish the release. Provided by Actions. |

## Deploy / distribute

The app is distributed as a Windows installer built on GitHub Actions. There is no server.

| Workflow | Trigger | What it produces |
|---|---|---|
| `smoke.yml` | Push to `main`, manual | Builds `smoke/` with `dotnet`, renders a PNG offscreen, uploads it as artifact `app-screenshot`. |
| `winui.yml` | Push to `main` touching `winui/**` or `global.json`, manual | Builds the real WinUI app with Visual Studio MSBuild, launches it with `SCREENSHOT_PATH`, fails if the PNG is missing or under 2,000 bytes, uploads `winui-screenshot`. |
| `bundle.yml` | Push to `main` touching `winui/**` or `scripts/setup.ps1`, manual | Self-contained x64 build plus the AVX2 llama DLLs, `RUN_ON_WINDOWS.md`, and `setup.ps1`, zipped as artifact `LiveLinguist-fr-bundle`. No Whisper DLLs and no model. |
| `installer.yml` | Manual only | Full build, llama and Whisper DLLs, both models downloaded from release `v0.1-fr`, Inno Setup compile of `installer/installer.iss`, then deletes and recreates the app release with `LiveLinguist-fr-Setup.exe` attached. |

Release assets are used instead of Actions artifacts for the installer. They do not count against the artifact storage quota and they give a stable download link.

Release tags in use: `v0.1-fr` holds the model assets. `v0.6-fr`, `v0.7-fr`, and `v0.8-fr` are app releases. `weights-fr-0.6b-20260925` and `weights-fr-0.6b-v2-20260929` are training checkpoints and must stay pre-release so the app's Latest link never points at them.

Note that `installer.yml` and `installer.iss` on `main` still say 0.7 and publish to `v0.7-fr`. The download link above points at `v0.8-fr`, which the previous README says was built from a `v0.8` branch. See open decision 1 in the spec.

## Related

- Upstream project: [Live Linguist](https://www.ndgold.com/live-linguist)
- Upstream data: `ndgold/live-linguist-easylanguage-sft` on Hugging Face, CC BY-SA 4.0
- Releases: https://github.com/lailajtharrison/live-linguist-win/releases
- Training notes: [training/README.md](training/README.md), [training/DGX_TASKS.md](training/DGX_TASKS.md), [training/RESULTS.md](training/RESULTS.md)
- Target PC setup: [RUN_ON_WINDOWS.md](RUN_ON_WINDOWS.md)

## Spec

See [docs/SPEC.md](docs/SPEC.md) for requirements and how changes ship.
