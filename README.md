# Live Linguist — Windows

Windows port of [Live Linguist](https://www.ndgold.com/live-linguist): real-time,
on-device **easy-language caption simplification** (rewrites live speech into
simpler text in the *same* language — not translation).

Target: ordinary Windows laptops, floor = **no-GPU, 8 GB RAM** (AVX2).
Stack: Qwen3-0.6B Q4 GGUF + Windows on-device speech recognizer + WinUI 3.

## Status: CI substrate smoke test

`smoke/` is a throwaway proof that GitHub Actions can build a Windows GUI app and
return a **screenshot** of the dual-transcript UI — the substrate the visual
critic loop needs. See `.github/workflows/smoke.yml`. Once green, the real WinUI
app and the adversarial builder/critic gauntlet replace it.
