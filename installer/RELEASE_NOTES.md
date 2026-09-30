**For review. This version is not yet the default download.**

Live Linguist listens to French as it is spoken and shows it in easy-to-read French (FALC). Everything runs on the computer; nothing is sent online.

## Install

1. Download **LiveLinguist-fr-Setup.exe** below (about 640 MB).
2. Open it. If Windows says "Windows protected your PC", click **More info**, then **Run anyway** (the installer is not code-signed).
3. Live Linguist opens with a short welcome screen. Choose where the French comes from:
   - **A call or video on this computer**: Teams, Zoom, YouTube. This needs no setup.
   - **Someone speaking in the room**: uses the microphone. It needs the French speech pack (Windows Settings → Time & language → Speech → add "French (France)").

## What's new since v0.6

- **Much faster captions.** The first words appear about 2 seconds after they are spoken (they used to take more than 10), and the easy-French caption follows about 1 to 2 seconds later.
- **A running transcript.** Every caption stays on screen, and you can scroll back to the start of the session. The newest line is the largest. "Show original" adds the French as it was actually said under each line.
- **Save transcript.** Saves the whole session as a text file. Every session is also saved automatically in **Documents\Live Linguist**.
- **Caption box.** Shrinks the app to a small window that stays on top of Zoom, Teams or YouTube.
- **English interface with a welcome screen.** The captions themselves stay in French. The ? button shows the welcome screen again.
- **A faster simplifier** (a fine-tuned Qwen3-0.6B model): about 3 times faster than before, and it keeps questions as questions.
- Follows the computer's sound output when headphones are plugged in, and warns when no sound is reaching the app.

## Known limitations

- Captions are only as good as what the speech recognizer hears. It can mishear names, numbers and fast or overlapping speech, and the simplifier sometimes changes the meaning. The "Show original" switch helps check.
- Tested so far on one fast laptop. Speed on typical student laptops still needs checking.

## Feedback

The most useful feedback is a saved transcript (Documents\Live Linguist) with a note on any line that is wrong or hard to read.
