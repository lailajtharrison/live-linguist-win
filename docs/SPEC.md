# Live Linguist for Windows: specification

**Status:** v1.0, 2026-10-12. First written spec, reverse-engineered from the code as it stands.
**Owner:** Laila Harrison (KSU Office of Research). **Reviewer:** unassigned.
**Source of requirements:** the code in `winui/`, `smoke/`, `installer/`, `scripts/`, `training/`, and `.github/workflows/`, plus `README.md`, `RUN_ON_WINDOWS.md`, `training/README.md`, `training/DGX_TASKS.md`, and `training/RESULTS.md`.

This document is the contract for the code. Every change starts here: update the spec, then the code, then the tests, in one pull request. Each requirement has an ID and says how it is verified. **Auto** means a test checks it on every pull request. **Manual** means a reviewer checks it by hand before approving. **None** means nothing checks it today.

## 1. Purpose

Live Linguist for Windows shows live French speech as easy-to-read French, the FALC register, on an ordinary Windows laptop with no graphics card. The users are students in a French virtual exchange and the people who support them. It solves one problem: a partner on a Teams or Zoom call, a video, or a person in the room speaks French that is too fast or too complex, and the student needs the same meaning in short, plain sentences, right now, without the audio leaving the PC. Done means a student installs one file, picks the audio source, and sees a correct simple caption within about two seconds of each phrase.

## 2. Scope

**In scope:** a WinUI 3 desktop app for Windows 10 and 11 x64. Two audio sources, microphone and system playback. On-device transcription with the Windows speech recognizer or Whisper. On-device simplification with a fine-tuned Qwen3 model through LLamaSharp. A type-to-test box. An Inno Setup installer built on GitHub Actions. A training kit for the DGX Spark that fine-tunes the simplifier and publishes checkpoints as GitHub pre-releases.

**Out of scope:** translation to or from any other language. Any language other than French. Cloud speech or cloud language models. Saving or exporting transcripts. User accounts, settings screens, or model selection in the UI. Code signing. macOS or Linux builds. GPU inference in the app. Evaluation of trained models, which happens in a separate `ll-eval` folder on the Dell and is not in this repo.

## 3. Requirements

### A. App launch and state

| ID | Requirement | Verify |
|---|---|---|
| A1 | On launch the app looks for `qwen3-1.7b-easylang-fr-Q4_K_M.gguf` next to the exe, then in `%LOCALAPPDATA%\LiveLinguist\`, and loads the first one found. | Manual: delete the file from one location and confirm the other is used. |
| A2 | If no simplifier model is found, the badge reads "Modèle manquant", the hint tells the user to reinstall, and no audio source starts. | Manual: launch with no model present. |
| A3 | The microphone source is selected and started by default once the model loads. | Manual: launch and confirm the badge reads "En direct · Micro" with a French pack installed. |
| A4 | Switching the radio button restarts capture on the chosen source without restarting the app. | Manual: toggle between Micro and Réunion / vidéo during a session. |
| A5 | If `SCREENSHOT_PATH` is set, the app waits 1.2 s, writes a PNG of the window to that path, and exits with code 0. | Auto: `winui.yml` on push to `main`. |
| A6 | The window is a fixed 1280 by 720 dark layout with a verbatim box, a simplified box, a hint, a text box with a Simplifier button, and a status bar. | Manual: compare to the `winui-screenshot` artifact. |

### M. Microphone source

| ID | Requirement | Verify |
|---|---|---|
| M1 | `SpeechSource` uses the Windows `SpeechRecognizer` for `fr-FR` in continuous recognition mode. | Manual: read `Services/SpeechSource.cs`. |
| M2 | Partial recognition text raises `Hypothesis` and updates the verbatim box live. | Manual: speak French and watch the text change mid-sentence. |
| M3 | A finalized result raises `Phrase` once per utterance. | Manual: one caption per sentence spoken. |
| M4 | If the French recognizer or microphone is unavailable, `StartAsync` returns false, the badge reads "Micro éteint", and the hint explains how to add the French speech pack. | Manual: launch on a PC without the French pack. |

### L. System playback source

| ID | Requirement | Verify |
|---|---|---|
| L1 | `LoopbackTranscriber` captures the default render endpoint with WASAPI loopback, downmixes to mono, and resamples to 16 kHz. | Manual: read `Services/LoopbackTranscriber.cs`. |
| L2 | The Whisper model is `ggml-small-q5_1.bin`, looked up next to the exe then in `%LOCALAPPDATA%\LiveLinguist\`, with language fixed to French. | Manual: read `WhisperModelPath()` in `MainWindow.xaml.cs`. |
| L3 | Audio is gated on energy in 30 ms frames with an RMS threshold of 0.006. An utterance flushes to Whisper after 700 ms of trailing silence or at 12 s, and utterances under 400 ms are discarded. | Manual: play a French video and confirm one caption per pause. |
| L4 | The pump reads with `ReadFully = false` so it paces against the audio clock and does not spin a CPU core. | Manual: watch CPU use while idle in Réunion / vidéo mode. |
| L5 | Whisper output that is empty, "merci", or an amara.org credit line is dropped as a hallucination. | Manual: read `IsNoise()`. |
| L6 | If the Whisper model file is missing or the device cannot start, `StartAsync` returns false and the badge reads "Audio indisponible". | Manual: remove the Whisper model and switch sources. |
| L7 | A failed transcription of one utterance is skipped without stopping the capture loop. | None. |

### S. Simplifier

| ID | Requirement | Verify |
|---|---|---|
| S1 | The prompt is ChatML built by hand: the `Prompts.FrenchFalc` system prompt, the four `FrenchFalcExamples` as completed user and assistant turns, the live phrase as `Original: ... Rewritten:`, then an assistant turn pre-filled with an empty `<think>` block. | Manual: read `LlamaSimplifier.SimplifyAsync`. |
| S2 | Inference is greedy, max 256 tokens, stops at `<|im_end|>`, context size 2048, zero GPU layers, one `StatelessExecutor` per phrase with no state carried between phrases. | Manual: read `LlamaSimplifier.cs`. |
| S3 | Output has every `<think>` block, stray think tag, and `<|im_end|>` removed and is trimmed before display. | Manual: feed a phrase and confirm no markup appears in the caption. |
| S4 | The simplified caption replaces the previous one only when the result is non-empty. | Manual: read `OnPhraseAsync`. |
| S5 | The measured wall time of each simplification is shown as "Latence X,X s" with a French decimal comma. | Manual: look at the status bar after a caption. |
| S6 | A simplification error on one phrase is swallowed so the caption loop keeps running. | None. |
| S7 | The type-to-test box sends its text through the same `OnPhraseAsync` path as speech. | Manual: type a sentence and click Simplifier. |
| S8 | Training scripts read the system prompt from `winui/Services/Prompts.cs` so training and inference use the identical prompt. | Manual: read `build-data.js`, `v2/gen.py`, `v2/merge.py`. |

### P. Privacy and data

| ID | Requirement | Verify |
|---|---|---|
| P1 | The app makes no network calls. Audio, transcripts, and captions stay in memory on the PC. | Manual: grep `winui/` for `Http`, `Socket`, and URLs. |
| P2 | The app writes nothing to disk except the CI screenshot when `SCREENSHOT_PATH` is set. | Manual: read `MainWindow.xaml.cs`. |
| P3 | Model weights are never committed. `training/.gitignore` excludes `runs/`, `out/`, `out-v2/`, `v2/gen/`, and `train_v2.jsonl`. | Manual: `git status` after a training run shows no weights. |
| P4 | Inputs listed in `training/data-src/heldout.txt` are never trained on. `gen.py` filters generated utterances against it, and `build-data.js` drops matching upstream rows. | Manual: read `held_out_inputs()` and the `heldOut` set in `build-data.js`. |

### B. Build and release

| ID | Requirement | Verify |
|---|---|---|
| B1 | `winui/LiveLinguistWinUI.csproj` builds x64, self-contained, unpackaged, with Windows App SDK 1.6, LLamaSharp 0.24.0, NAudio 2.2.1, and Whisper.net 1.8.1. | Auto: `winui.yml` on push to `main`. |
| B2 | The app builds with Visual Studio MSBuild on `windows-latest`, launches headless, and produces a screenshot larger than 2,000 bytes. | Auto: `winui.yml` on push to `main`. |
| B3 | `smoke/` builds with `dotnet build` and renders the dual-transcript layout to a PNG with no display. | Auto: `smoke.yml` on push to `main`. |
| B4 | `bundle.yml` ships the app folder plus the x64 AVX2 `llama.dll` and `ggml*.dll` from `LLamaSharp.Backend.Cpu` 0.24.0, plus `RUN_ON_WINDOWS.md` and `setup.ps1`, as `LiveLinguist-fr-bundle.zip`. | Auto: `bundle.yml` on push to `main` touching `winui/**`. |
| B5 | `installer.yml` adds the Whisper.net.Runtime 1.8.1 DLLs, downloads both model files from release `v0.1-fr`, compiles `installer/installer.iss` with Inno Setup, and attaches `LiveLinguist-fr-Setup.exe` to the app release. | Manual: dispatch the workflow and download the asset. |
| B6 | The installer is per-user with no admin prompt, x64 only, and creates a Start menu and desktop shortcut. | Manual: run the installer on a clean account. |
| B7 | The installer places both models next to the exe so the app finds them without `setup.ps1`. | Manual: install and launch with an empty `%LOCALAPPDATA%\LiveLinguist\`. |
| B8 | `setup.ps1` creates `%LOCALAPPDATA%\LiveLinguist`, copies the model there if it sits in the bundle folder, and warns if no French language is installed. | Manual: run from an unzipped bundle. |

### T. Training data

| ID | Requirement | Verify |
|---|---|---|
| T1 | `build-data.js` produces `data/train.jsonl` and `data/valid.jsonl` in prompt/completion form, 1,891 and 208 rows, with a deterministic shuffle at seed 42. | Manual: rerun and diff. |
| T2 | The 29 replacements in `data-src/question-fixes.json` all apply, or the build fails. | Manual: `build-data.js` throws if any fix is unused. |
| T3 | The 70 examples in `data-src/targeted.jsonl` are repeated three times and the build fails if any duplicates a held-out input. | Manual: read `build-data.js`. |
| T4 | `v2/gen.py` runs three resumable stages, utterances, rewrites, verified, each appending to a JSONL file and skipping finished items on rerun. | Manual: stop and rerun, confirm counts continue. |
| T5 | A rewrite is accepted only if it passes the deterministic checks and the checker returns true for all six criteria: sens_preserve, rien_invente, nombres_ok, question_ok, grammaire_ok, simple. | Manual: read `stage_verify()`. |
| T6 | The writer and checker are different model families. Writer is Qwen3-30B-A3B-Instruct-2507 Q8_0, checker is Mistral-Small-3.2-24B-Instruct-2506 Q5_K_M. | Manual: read `train-v2.sh`. |
| T7 | About a quarter of generated inputs are converted to speech-recognition style, lowercase, no punctuation, optional filler, before rewriting. | Manual: read `asr_style()` and `stage_rewrites()`. |
| T8 | `v2/merge.py` writes `data/train_v2.jsonl` as the v1 training rows plus every accepted generated pair, rendered with the same prompt shape as `build-data.js`, shuffled at seed 42. | Manual: compare a row from each source. |

### R. Training runs

| ID | Requirement | Verify |
|---|---|---|
| R1 | All training runs inside `nvcr.io/nvidia/pytorch:25.09-py3` or the tag in `PYTORCH_IMAGE`, with the repo and a Hugging Face cache mounted. Nothing is installed on the Spark host. | Manual: read the `docker run` lines. |
| R2 | `train.py` offers three LoRA recipes on `Qwen/Qwen3-0.6B`: `dylan`, `wide`, and `wide2`, with the hyperparameters in `RECIPES`. | Manual: read `train.py`. |
| R3 | Loss is computed on the completion only, bf16, batch 8, max length 1024, cosine schedule, 20 warmup steps, eval every 50 steps, seed 42. | Manual: read `SFTConfig` in `train.py`. |
| R4 | Each run saves a merged Hugging Face model and a `metrics.json` with the recipe, final eval, a greedy sample on the first validation prompt, and the full log history. | Manual: inspect `runs/<recipe>/`. |
| R5 | `train.sh` converts each merged model to f16 GGUF named `out/ll-fr-0.6b-<recipe>-f16.gguf` and copies the metrics beside it. | Manual: `ls training/out` after a run. |
| R6 | `train-v2.sh` uploads `out-v2/` to a GitHub release tagged `weights-fr-0.6b-v2-YYYYMMDD` marked `--prerelease --latest=false`, or prints the exact command if `gh` is not logged in. | Manual: check the release page shows Pre-release. |
| R7 | `train_fr.sh` does a full fine-tune of `Qwen3-1.7B-Base` on `data/fr_aug` train plus valid, 4 epochs, lr 2e-5, batch 32, and converts to `out/ll-fr-1.7b-f16.gguf`. Q4_K_M quantization happens only if `LLAMA_QUANTIZE` points to an executable. | Manual: read `train_fr.py` and `train_fr.sh`. |
| R8 | No training release is ever marked Latest. The app's download link must keep pointing at the current app release. | Manual: check the Latest badge on the releases page after any upload. |

## 4. Interfaces

### App

| Item | Value |
|---|---|
| Executable | `LiveLinguistWinUI.exe`, WinUI 3, net8.0-windows10.0.19041.0, min Windows 10.0.17763. |
| Env var `SCREENSHOT_PATH` | When set, render one PNG to this path and exit. |
| Simplifier model path | `<exe dir>\qwen3-1.7b-easylang-fr-Q4_K_M.gguf`, else `%LOCALAPPDATA%\LiveLinguist\qwen3-1.7b-easylang-fr-Q4_K_M.gguf`. |
| Whisper model path | `<exe dir>\ggml-small-q5_1.bin`, else `%LOCALAPPDATA%\LiveLinguist\ggml-small-q5_1.bin`. |
| Native DLLs | `llama.dll`, `ggml.dll`, `ggml-base.dll`, `ggml-cpu.dll` and the Whisper.net runtime DLLs in the exe folder. |
| `ISpeechSource` | Events `Hypothesis(string)` and `Phrase(string)`. `Task<bool> StartAsync()`, `Task StopAsync()`, `Dispose()`. |
| `LlamaSimplifier` | `static Load(string modelPath)`, `Task<string> SimplifyAsync(string system, string userText, CancellationToken)`. |

### Training commands

| Command | Flags and inputs |
|---|---|
| `node build-data.js <upstream dir>` | Directory holding `fr-train.jsonl`, `fr-valid.jsonl`, `fr-test.jsonl`. Also reads `C:\Users\lharr260\ll-eval\eval.js` for regression probes. |
| `python train.py --recipe {dylan,wide,wide2} --out <dir> [--data <dir>] [--train-file <name>]` | Writes `<out>/merged/` and `<out>/metrics.json`. |
| `python v2/gen.py --writer <url> --checker <url> --out <dir> [--target 12000] [--workers 8]` | Two llama-server base URLs with an OpenAI-compatible chat endpoint. |
| `python v2/merge.py <gen dir>` | Writes `data/train_v2.jsonl`. |
| `python train_fr.py` | No flags. Reads `BASE_MODEL`, `OUT_DIR`, `DATA_DIR`. |
| `./train.sh`, `./train-v2.sh`, `./train_fr.sh` | Run from `training/` on the Spark. Env vars in README Configuration. |

### File formats

| File | Shape |
|---|---|
| `data/train.jsonl`, `data/valid.jsonl`, `data/train_v2.jsonl` | One JSON object per line: `{"prompt": "<ChatML through the empty think block>", "completion": "<rewrite><\|im_end\|>"}`. |
| `data/fr_aug/*.jsonl` | One JSON object per line: `{"messages": [system, user, assistant]}` in chat format. |
| `data-src/targeted.jsonl` | `{"why": "<failure kind>", "input": "...", "output": "..."}`. Kinds: short-identity, question-kept, no-invention, subjunctive, gender, fillers, coref. |
| `data-src/question-fixes.json` | Object keyed by 0-based upstream line number to replacement output. Keys starting with `_` are comments. |
| `data-src/heldout.txt` | One held-out input per line, 217 lines. |
| `v2/gen/utterances.jsonl` | `{"id", "text", "domain", "style"}`. |
| `v2/gen/rewrites.jsonl` | `{"id", "input", "output", "asr"}`. |
| `v2/gen/verified.jsonl` | Rewrite fields plus `{"ok": bool, "problems": [..], "checker": {..}}`. |
| `v2/gen/summary.json` | Counts per stage, accepted count, rejections by reason. |
| `runs/<recipe>/metrics.json` | Recipe hyperparameters, `eval`, `sample_output`, `log`. Round 1 copies live in `training/out/`. |

### Releases

| Tag | Holds |
|---|---|
| `v0.1-fr` | `qwen3-1.7b-easylang-fr-Q4_K_M.gguf` and `ggml-small-q5_1.bin`. Pulled by `installer.yml`. |
| `v0.6-fr`, `v0.7-fr`, `v0.8-fr` | App installer releases. `v0.8-fr` is the README download link. |
| `weights-fr-0.6b-20260925` | Round 1 checkpoints. |
| `weights-fr-0.6b-v2-20260929` | Round 2 `wide2` f16 checkpoint, `gen-summary.json`, `gen-verified.jsonl`, logs. Pre-release. |

### CI workflows

| Workflow | Trigger | Output |
|---|---|---|
| `smoke.yml` | push to `main`, dispatch | artifact `app-screenshot` |
| `winui.yml` | push to `main` on `winui/**` or `global.json`, dispatch | artifact `winui-screenshot` |
| `bundle.yml` | push to `main` on `winui/**` or `scripts/setup.ps1`, dispatch | artifact `LiveLinguist-fr-bundle` |
| `installer.yml` | dispatch | release asset `LiveLinguist-fr-Setup.exe` |

## 5. Open decisions

1. **Version mismatch on `main`.** `installer/installer.iss` says `AppVersion=0.7` and `installer.yml` deletes and recreates `v0.7-fr`. The README download points at `v0.8-fr`. The old README says the current app code is on a `v0.8` branch, which is not in this clone. Someone needs to merge or rebase so `main` builds what users download.
2. **Which model ships.** The code on `main` loads the 1.7B file and the status bar says "Qwen3-1.7B". The `v0.8` branch defines `FastModelName` as `ll-fr-0.6b-v2-Q4_K_M.gguf` and `LegacyModelName` as the 1.7B file. `DGX_TASKS.md` on `main` still says the 0.6B is waiting on the 216-sentence Dell evaluation. Merge `v0.8` into `main`, or state which branch is the source of truth, then update `installer.iss` and this spec to match.
3. **0.6B ship decision.** Round 2 `wide2` is published as pre-release `weights-fr-0.6b-v2-20260929`, f16 only. It needs Q4_K_M and Q5_K_M quantization on the Dell and the 216-sentence evaluation before any ship decision. Ship bar from `DGX_TASKS.md`: clears round 1's meaning errors at about 1.2 s per caption.
4. **Task 3 not done.** The optional 1.7B full fine-tune on `fr_aug` plus round 2 accepted pairs has not been run. It is the fallback if the 0.6B falls short.
5. **Machine-specific path in `build-data.js`.** It reads `C:\Users\lharr260\ll-eval\eval.js` for regression probes. `gen.py` reads `data-src/heldout.txt` instead. `build-data.js` should switch to `heldout.txt` so the data build runs anywhere.
6. **Hard-coded quantize path in `train_fr.sh`.** Default `LLAMA_QUANTIZE` is `/home/lharr260/spark-transfer/llama-cpp-build/build/bin/llama-quantize`, a path on one machine.
7. **Workflows do not run on pull requests.** All four trigger on push to `main` or by hand. There are no unit tests. A pull request gets no automated check before merge.
8. **Smoke test shows German.** `smoke/Program.cs` renders a German sample, a leftover from the upstream project. Harmless, but confusing in screenshots.
9. **No code signing.** Every user sees the SmartScreen warning. A signing certificate would remove step 2 of the install instructions.
10. **Stale status note.** `DGX_TASKS.md` Task 2 says `v0.7-fr` must stay Latest, while the Status section says `v0.6-fr` remains Latest. The README now says `v0.8-fr`.
11. **`training-kit` branch.** `training/README.md` tells the Spark to `git switch training-kit`. That branch is not in this clone. Confirm whether `main` is now the training branch and fix the instruction.
12. **Review gate before ship.** `DGX_TASKS.md` says the Dell side ships only after evaluation and after Abby reviews. That gate is not written down anywhere else. Record who signs off on an app release.
13. **GPU layers fixed at zero.** `LlamaSimplifier.Load` sets `GpuLayerCount = 0` with a comment to raise it on capable machines. No way to do that without a rebuild.
14. **Whisper model size unknown.** `ggml-small-q5_1.bin` is pulled from `v0.1-fr` but its size is not recorded in the repo.

## 6. How changes ship

1. Open a branch. If behavior changes, update this spec first, in the same branch.
2. Open a pull request into `main`.
3. No CI runs on pull requests today. `smoke.yml`, `winui.yml`, and `bundle.yml` run after merge to `main`. Before asking for review, build with the quick-start commands by hand and, for app changes, run the app once with `SCREENSHOT_PATH` set to confirm it launches. For training changes, run the affected script on the Spark and attach the metrics.
4. One approving review before merge.
5. To publish an app release, dispatch `installer.yml` by hand after bumping `AppVersion` in `installer.iss` and the tag in the workflow. To publish training weights, let `train-v2.sh` upload, or use `gh release create ... --prerelease --latest=false`.

## 7. Review log

None yet.
