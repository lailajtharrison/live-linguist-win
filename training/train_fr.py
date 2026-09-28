"""Full-SFT of Qwen3-1.7B for Live Linguist French easy-language (FALC) rewriting.

This is the *shipped-quality* recipe — full fine-tune (no LoRA) from the 1.7B
base on the question-augmented French data (data/fr_aug), which fixes the model
answering/inventing on question inputs by teaching it to rewrite the question
without answering. Run through train_fr.sh (inside NVIDIA's PyTorch container):

    BASE_MODEL and OUT can be overridden via env vars; defaults pull the public
    Qwen3-1.7B base from the Hugging Face Hub.

Writes runs/fr/merged (full HF model) and runs/fr/metrics.json. train_fr.sh
then converts merged/ to an f16 GGUF (and Q4_K_M when a llama-quantize is found).
"""
import json
import os

import torch
from datasets import load_dataset
from transformers import AutoTokenizer
from trl import SFTConfig, SFTTrainer

torch.backends.cuda.matmul.allow_tf32 = True
torch.backends.cudnn.allow_tf32 = True

# Liger fuses Qwen3 kernels for a ~1.5x speedup; optional, tolerate absence.
try:
    from liger_kernel.transformers import apply_liger_kernel_to_qwen3
    apply_liger_kernel_to_qwen3()
    print("[fr] Liger applied", flush=True)
except Exception as e:  # noqa: BLE001
    print(f"[fr] Liger not applied ({e})", flush=True)

HERE = os.path.dirname(os.path.abspath(__file__))
BASE = os.environ.get("BASE_MODEL", "Qwen/Qwen3-1.7B-Base")
OUT = os.environ.get("OUT_DIR", os.path.join(HERE, "runs", "fr"))
DATA = os.environ.get("DATA_DIR", os.path.join(HERE, "data", "fr_aug"))

# Train on the full augmented set (train + valid) — the augmentation notes call for
# replaying the whole French set to preserve quality; there is no held-out eval here.
files = [os.path.join(DATA, "train.jsonl"), os.path.join(DATA, "valid.jsonl")]
ds = load_dataset("json", data_files=files, split="train")
if "messages" in ds.column_names:
    ds = ds.select_columns(["messages"])
ds = ds.shuffle(seed=7)
print(f"[fr] {len(ds)} French examples from {DATA}", flush=True)

cfg = SFTConfig(
    output_dir=OUT,
    num_train_epochs=4,
    per_device_train_batch_size=32,
    gradient_accumulation_steps=1,
    learning_rate=2e-5,
    lr_scheduler_type="cosine",
    warmup_steps=20,  # trl>=1.0 dropped warmup_ratio
    bf16=True,
    max_length=1024,
    packing=False,  # avoid cross-sample contamination without flash-attn varlen
    logging_steps=10,
    save_strategy="no",
    report_to="none",
    seed=42,
    model_init_kwargs={"dtype": "bfloat16", "attn_implementation": "sdpa"},
)
trainer = SFTTrainer(model=BASE, args=cfg, train_dataset=ds)
trainer.train()

merged_dir = os.path.join(OUT, "merged")
trainer.save_model(merged_dir)
tok = AutoTokenizer.from_pretrained(BASE)
tok.save_pretrained(merged_dir)

# Quick sanity sample so a broken run is obvious before anything is copied off the Spark.
# Rewrite a question — the exact failure mode the augmented data targets.
probe_msgs = [
    {"role": "system", "content": ds[0]["messages"][0]["content"]},
    {"role": "user", "content": "Original: Est-ce que vous pourriez expliquer la difference entre un nom et un verbe ?"},
]
probe = tok.apply_chat_template(probe_msgs, tokenize=False, add_generation_prompt=True)
ids = tok(probe, return_tensors="pt").to(trainer.model.device)
with torch.no_grad():
    gen = trainer.model.generate(**ids, max_new_tokens=80, do_sample=False)
sample = tok.decode(gen[0][ids["input_ids"].shape[1]:], skip_special_tokens=True)

with open(os.path.join(OUT, "metrics.json"), "w", encoding="utf-8") as f:
    json.dump({"recipe": "fr-1.7b-full-sft", "base": BASE, "n_examples": len(ds),
               "epochs": 4, "lr": 2e-5, "batch_size": 32,
               "sample_output": sample, "log": trainer.state.log_history},
              f, ensure_ascii=False, indent=2)
print(f"[fr] saved -> {merged_dir}\nsample: {sample}", flush=True)
