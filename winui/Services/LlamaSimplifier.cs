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
    // Partial captions are shown only once they are this long, so the English check has
    // enough words to judge before anything reaches the screen.
    private const int MinWordsBeforeStreaming = 6;

    private readonly LLamaWeights _weights;
    private readonly ModelParams _params;
    private readonly bool _useWorkedExamples;

    private LlamaSimplifier(LLamaWeights weights, ModelParams p, bool useWorkedExamples)
    {
        _weights = weights;
        _params = p;
        _useWorkedExamples = useWorkedExamples;
    }

    /// useWorkedExamples: true only for the original 1.7B, which needs the four worked
    /// examples to hold the rules. The 0.6B was fine-tuned on the bare prompt, so for it
    /// they are out of distribution and only cost prefill time.
    public static LlamaSimplifier Load(string modelPath, bool useWorkedExamples)
    {
        var p = new ModelParams(modelPath)
        {
            ContextSize = 2048,
            GpuLayerCount = 0, // no-GPU floor; bump on capable machines
        };
        var weights = LLamaWeights.LoadFromFile(p);
        return new LlamaSimplifier(weights, p, useWorkedExamples);
    }

    /// Streams the rewrite through onPartial as it is generated. Returns the finished,
    /// guarded caption, or null when the model answered in English: the caller should
    /// then show the transcript itself rather than a wrong-language caption.
    public async Task<string?> SimplifyAsync(string system, string userText,
                                             Action<string>? onPartial = null, CancellationToken ct = default)
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
        if (_useWorkedExamples)
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
        {
            sb.Append(token);
            var partial = Clean(sb.ToString());
            if (OutputGuard.LooksEnglish(partial)) return null;
            // Leaving the loop disposes the enumerator, which stops generation.
            if (OutputGuard.IsLooping(partial) || OutputGuard.IsRunaway(partial)) break;
            if (onPartial != null &&
                partial.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= MinWordsBeforeStreaming)
                onPartial(partial);
        }

        var text = OutputGuard.RemoveRepeats(OutputGuard.CutRunaway(Clean(sb.ToString())));
        return OutputGuard.LooksEnglish(text) ? null : text;
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
