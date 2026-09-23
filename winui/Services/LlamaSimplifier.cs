using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace LiveLinguistWinUI.Services;

/// On-device easy-language simplifier backed by the fine-tuned GGUF via LLamaSharp.
/// One-shot per phrase (StatelessExecutor) so there is no cross-phrase state.
public sealed class LlamaSimplifier : IDisposable
{
    private readonly LLamaWeights _weights;
    private readonly ModelParams _params;

    private LlamaSimplifier(LLamaWeights weights, ModelParams p)
    {
        _weights = weights;
        _params = p;
    }

    public static LlamaSimplifier Load(string modelPath)
    {
        var p = new ModelParams(modelPath)
        {
            ContextSize = 2048,
            GpuLayerCount = 0, // no-GPU floor; bump on capable machines
        };
        var weights = LLamaWeights.LoadFromFile(p);
        return new LlamaSimplifier(weights, p);
    }

    public async Task<string> SimplifyAsync(string system, string userText, CancellationToken ct = default)
    {
        // Qwen3 defaults to "thinking" mode. Building ChatML by hand (rather than
        // via the model's chat template) means we must disable it ourselves, or the
        // model emits a raw <think>...</think> block that leaks into the caption.
        // The canonical no-think form is to prefill an already-closed think block on
        // the assistant turn. (llama-server applied this for us during eval, which is
        // why the batch outputs were clean and the app's were not.)
        // The worked examples sit between the system turn and the real one, as
        // completed user/assistant exchanges in the same "Original:/Rewritten:"
        // shape the live turn uses.
        var shots = new StringBuilder();
        foreach (var (original, rewritten) in Prompts.FrenchFalcExamples)
            shots.Append($"<|im_start|>user\nOriginal: {original}\nRewritten:<|im_end|>\n")
                 .Append($"<|im_start|>assistant\n{rewritten}<|im_end|>\n");

        var prompt =
            $"<|im_start|>system\n{system}<|im_end|>\n" +
            shots +
            $"<|im_start|>user\nOriginal: {userText}\nRewritten:<|im_end|>\n" +
            "<|im_start|>assistant\n<think>\n\n</think>\n\n";

        var executor = new StatelessExecutor(_weights, _params);
        var infer = new InferenceParams
        {
            MaxTokens = 256,
            AntiPrompts = new List<string> { "<|im_end|>" },
            SamplingPipeline = new GreedySamplingPipeline(),
        };

        var sb = new StringBuilder();
        await foreach (var token in executor.InferAsync(prompt, infer, ct))
            sb.Append(token);

        return Clean(sb.ToString());
    }

    // Safety net: strip any think block (empty or not) and stray tags even if the
    // prefill above is ignored, so the caption never shows <think>/</think> markup.
    private static readonly Regex ThinkBlock =
        new(@"<think>.*?</think>", RegexOptions.Singleline | RegexOptions.Compiled);

    private static string Clean(string raw)
    {
        var s = raw.Replace("<|im_end|>", string.Empty);
        s = ThinkBlock.Replace(s, string.Empty);
        s = s.Replace("<think>", string.Empty).Replace("</think>", string.Empty);
        return s.Trim();
    }

    public void Dispose() => _weights.Dispose();
}
