# Running Live Linguist (French) on a Windows PC

The app is code-complete and builds. To make it *live* (mic → model → captions) on a
real Windows PC (e.g. the Dell), three things must be in place: the **French speech
pack**, the **native llama.cpp DLLs**, and the **model file**. Until they are, the app
runs in **demo mode** (shows the sample potter caption).

## 1. Install the French speech recognizer (one time)
Settings → **Time & language → Speech** → add **Français (France)** speech.
(Also Settings → Privacy → **Microphone** → allow desktop apps.)

## 2. Put the native llama.cpp DLLs next to the app
LLamaSharp loads its native library from the app folder at runtime. Get the Windows
x64 CPU build (AVX2) from the `LLamaSharp.Backend.Cpu` **0.24.0** NuGet package:
`runtimes/win-x64/native/avx2/` → copy **llama.dll, ggml.dll, ggml-base.dll,
ggml-cpu.dll** into the folder that contains `LiveLinguistWinUI.exe`.
(These are x86-64 Windows DLLs — the Spark's `llama-cpp-build` binaries are ARM/Linux
and will NOT work here.)

## 3. Put the model where the app looks for it
Copy the shipping model to:
`%LOCALAPPDATA%\LiveLinguist\qwen3-1.7b-easylang-fr-Q4_K_M.gguf`
(≈1.1 GB. Runs on CPU on a no-GPU 8 GB laptop; AVX2 required.)

## 4. Build & run
Open `winui/LiveLinguistWinUI.csproj` in **Visual Studio 2022** (with the *Windows App
SDK / WinUI* workload) and press F5 — or run the CI-built binary. On launch it tries to
start French recognition and load the model; if both succeed the badge shows **En
direct** and it captions live, otherwise **Mode démo**.

## How it works
- `Services/SpeechSource.cs` — Windows on-device fr-FR continuous recognition (near-zero
  app CPU), fires partial (`Hypothesis`) and finalized (`Phrase`) events.
- `Services/LlamaSimplifier.cs` — one-shot FALC simplification per finalized phrase via
  LLamaSharp `StatelessExecutor` on the GGUF (greedy decoding).
- `ViewModels/MainViewModel.cs` + `MainWindow.xaml` — the dual-transcript, data-bound.
- STT runs continuously; the LLM fires per phrase in the gap — they time-slice on a weak CPU.
