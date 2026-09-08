namespace DeepBrain.Host.BrainLife.Ml;

public static class MlMath
{
    public static double[] Softmax(double[] logits, float[]? mask = null)
    {
        if (logits.Length == 0) return Array.Empty<double>();
        var useMask = mask is not null && mask.Length == logits.Length;
        var max = double.NegativeInfinity;
        for (var i = 0; i < logits.Length; i++)
            if (!useMask || mask![i] > 0f) max = Math.Max(max, logits[i]);
        if (double.IsNegativeInfinity(max)) return new double[logits.Length];
        var exps = new double[logits.Length];
        double sum = 0;
        for (var i = 0; i < logits.Length; i++)
        {
            var e = useMask && mask![i] <= 0f ? 0.0 : Math.Exp(logits[i] - max);
            exps[i] = e;
            sum += e;
        }
        if (sum <= 0) return exps.Select(_ => 1.0 / logits.Length).ToArray();
        for (var i = 0; i < exps.Length; i++)
            exps[i] /= sum;
        return exps;
    }

    public static double ComputeEntropy(double[] probs)
    {
        if (probs.Length <= 1) return 0;
        double sum = 0;
        for (var i = 0; i < probs.Length; i++)
        {
            var p = probs[i];
            if (p > 0) sum -= p * Math.Log(p);
        }
        return sum / Math.Log(probs.Length);
    }

    public static float[]? NormalizeMask(float[]? mask)
    {
        if (mask is null || mask.Length != ActionCatalog.Count) return null;
        var normalized = new float[mask.Length];
        for (var i = 0; i < mask.Length; i++)
        {
            var v = mask[i] > 0f ? 1f : 0f;
            normalized[i] = v;
        }
        return normalized;
    }

    public static void ApplyMask(double[] probs, float[] mask)
    {
        double sum = 0;
        for (var i = 0; i < probs.Length; i++)
        {
            if (mask[i] <= 0f)
                probs[i] = 0;
            sum += probs[i];
        }
        if (sum <= 0) return;
        for (var i = 0; i < probs.Length; i++)
            probs[i] /= sum;
    }

    public static bool IsMaskedOut(string actionName, float[] mask)
    {
        var idx = ActionCatalog.IndexOf(actionName);
        if (idx < 0 || idx >= mask.Length) return false;
        return mask[idx] <= 0f;
    }
}
