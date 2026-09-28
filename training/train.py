"""LoRA fine-tune of Qwen3 for Live Linguist French easy-language rewriting.

Run through train.sh (inside NVIDIA's PyTorch container on the DGX Spark):
    python train.py --recipe dylan  --out runs/dylan
    python train.py --recipe wide   --out runs/wide

Each run writes runs/<name>/merged (full HF model with the LoRA folded in) and
runs/<name>/metrics.json. train.sh then converts merged/ to an f16 GGUF.
"""
import argparse
import json
import os

import torch
from datasets import load_dataset
from peft import LoraConfig
from transformers import AutoModelForCausalLM, AutoTokenizer
from trl import SFTConfig, SFTTrainer

BASE = "Qwen/Qwen3-0.6B"  # the base Dylan's 0.6B was trained from (model card: base_model)

# "dylan" reproduces the published model-card recipe (mlx-lm style: scale 20 = alpha/r,
# top 16 of 28 layers, attention only, 1 epoch, low LR to match the high scale).
# "wide" is the standard PEFT alternative: all layers, attention + MLP, 3 epochs.
RECIPES = {
    "dylan": dict(r=16, alpha=320, dropout=0.05, targets=["q_proj", "k_proj", "v_proj", "o_proj"],
                  top_layers=16, epochs=1, lr=1e-5),
    "wide": dict(r=16, alpha=32, dropout=0.05,
                 targets=["q_proj", "k_proj", "v_proj", "o_proj", "gate_proj", "up_proj", "down_proj"],
                 top_layers=None, epochs=3, lr=2e-4),
    # "wide2": wide with the overfitting gap closed (train acc 0.90 vs eval 0.79 in round 1):
    # 2 epochs, half the learning rate. Meant for the larger train_v2.jsonl.
    "wide2": dict(r=16, alpha=32, dropout=0.05,
                  targets=["q_proj", "k_proj", "v_proj", "o_proj", "gate_proj", "up_proj", "down_proj"],
                  top_layers=None, epochs=2, lr=1e-4),
}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--recipe", choices=RECIPES, required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--data", default=os.path.join(os.path.dirname(__file__), "data"))
    ap.add_argument("--train-file", default="train.jsonl", help="file name inside --data")
    args = ap.parse_args()
    rc = RECIPES[args.recipe]

    tok = AutoTokenizer.from_pretrained(BASE)
    model = AutoModelForCausalLM.from_pretrained(BASE, torch_dtype=torch.bfloat16)
    n_layers = model.config.num_hidden_layers

    lora = LoraConfig(
        r=rc["r"], lora_alpha=rc["alpha"], lora_dropout=rc["dropout"],
        target_modules=rc["targets"], task_type="CAUSAL_LM",
        layers_to_transform=list(range(n_layers - rc["top_layers"], n_layers)) if rc["top_layers"] else None,
    )

    # prompt/completion rows: loss is computed on the completion (the rewrite) only.
    ds = load_dataset("json", data_files={"train": f"{args.data}/{args.train_file}", "valid": f"{args.data}/valid.jsonl"})

    cfg = SFTConfig(
        output_dir=args.out,
        num_train_epochs=rc["epochs"],
        learning_rate=rc["lr"],
        lr_scheduler_type="cosine",
        warmup_steps=20,  # trl>=1.0 dropped warmup_ratio from SFTConfig; ~3% of steps for both recipes
        per_device_train_batch_size=8,
        per_device_eval_batch_size=8,
        gradient_accumulation_steps=1,
        max_length=1024,
        completion_only_loss=True,
        bf16=True,
        logging_steps=10,
        eval_strategy="steps",
        eval_steps=50,
        save_strategy="no",
        report_to="none",
        seed=42,
    )
    trainer = SFTTrainer(model=model, args=cfg, train_dataset=ds["train"], eval_dataset=ds["valid"],
                         processing_class=tok, peft_config=lora)
    trainer.train()
    metrics = trainer.evaluate()

    merged = trainer.model.merge_and_unload()
    merged_dir = os.path.join(args.out, "merged")
    merged.save_pretrained(merged_dir, safe_serialization=True)
    tok.save_pretrained(merged_dir)

    # Quick sanity sample so a broken run is obvious before anything is copied off the Spark.
    probe = ds["valid"][0]["prompt"]
    ids = tok(probe, return_tensors="pt").to(merged.device)
    with torch.no_grad():
        out = merged.generate(**ids, max_new_tokens=80, do_sample=False)
    sample = tok.decode(out[0][ids["input_ids"].shape[1]:], skip_special_tokens=False)

    with open(os.path.join(args.out, "metrics.json"), "w", encoding="utf-8") as f:
        json.dump({"recipe": args.recipe, **rc, "eval": metrics, "sample_output": sample,
                   "log": trainer.state.log_history}, f, ensure_ascii=False, indent=2)
    print(f"[{args.recipe}] eval_loss={metrics.get('eval_loss'):.4f}\nsample: {sample}")


if __name__ == "__main__":
    main()
