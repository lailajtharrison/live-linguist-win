using System;
using System.Collections.Generic;
using System.Text;
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
        var prompt =
            $"<|im_start|>system\n{system}<|im_end|>\n" +
            $"<|im_start|>user\nOriginal: {userText}\nRewritten:<|im_end|>\n" +
            "<|im_start|>assistant\n";

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

        return sb.ToString().Replace("<|im_end|>", string.Empty).Trim();
    }

    public void Dispose() => _weights.Dispose();
}
