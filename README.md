# Live Linguist for Windows

Live Linguist listens to French as it is spoken and shows it, live, in easy-to-read
French (FALC). It works during a Teams or Zoom call, with a YouTube video, or with
someone speaking in the room. Everything runs on the computer; nothing is sent online.

## ⬇️ Download

**[Download the installer (LiveLinguist-fr-Setup.exe, about 640 MB)](https://github.com/lailajtharrison/live-linguist-win/releases/download/v0.8-fr/LiveLinguist-fr-Setup.exe)**

This is version 0.8, which is currently being reviewed. See
[what's new and how to install](https://github.com/lailajtharrison/live-linguist-win/releases/tag/v0.8-fr).

1. Click the link above. The installer downloads to your Downloads folder.
2. Open it. If Windows says "Windows protected your PC", click **More info**, then
   **Run anyway** (the installer is not code-signed).
3. Live Linguist opens with a short welcome screen that shows you how to start.

Needs Windows 10 or 11 on a 64-bit PC. No graphics card is needed.

---

## For developers

Windows port of [Live Linguist](https://www.ndgold.com/live-linguist): real-time,
on-device easy-language caption simplification (it rewrites live speech into simpler
text in the *same* language; it does not translate).

- App: WinUI 3 (`winui/`). Speech: Whisper (calls and videos) or the Windows speech
  recognizer (microphone). Simplifier: a fine-tuned Qwen3-0.6B Q4 GGUF via LLamaSharp.
- The current app code is on the `v0.8` branch. Builds run on GitHub Actions
  (`.github/workflows/installer.yml`), which publishes the installer to a release.
- Model training kits are in `training/`.
