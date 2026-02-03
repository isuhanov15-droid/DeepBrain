using DeepBrain.Shared.BrainDtos.V4;

namespace DeepBrain.Host.BrainLife;

public sealed class AttentionInertiaEngine
{
    private readonly double _switchThreshold;
    private readonly int _holdTicks;
    private string _lastFocus = "body";
    private double _lastIntensity = 0.4;
    private int _pendingTicks;
    private string? _pendingFocus;

    public AttentionInertiaEngine(double switchThreshold = 0.15, int holdTicks = 3)
    {
        _switchThreshold = switchThreshold;
        _holdTicks = holdTicks;
    }

    public AttentionDto Apply(AttentionDto computed)
    {
        if (computed.Focus1 == _lastFocus)
        {
            _pendingTicks = 0;
            _pendingFocus = null;
            _lastIntensity = computed.Intensity;
            return computed with { TopTarget = computed.Focus1 };
        }

        if (computed.Intensity > _lastIntensity + _switchThreshold)
        {
            _lastFocus = computed.Focus1;
            _lastIntensity = computed.Intensity;
            _pendingTicks = 0;
            _pendingFocus = null;
            return computed with { TopTarget = computed.Focus1 };
        }

        if (_pendingFocus == computed.Focus1)
            _pendingTicks++;
        else
        {
            _pendingFocus = computed.Focus1;
            _pendingTicks = 1;
        }

        if (_pendingTicks >= _holdTicks)
        {
            _lastFocus = computed.Focus1;
            _lastIntensity = computed.Intensity;
            _pendingTicks = 0;
            _pendingFocus = null;
            return computed with { TopTarget = computed.Focus1 };
        }

        _lastIntensity = Math.Max(0, _lastIntensity - 0.02);
        return computed with
        {
            Focus1 = _lastFocus,
            Focus2 = null,
            Intensity = _lastIntensity,
            Reason = $"inertia:{computed.Reason}",
            TopTarget = _lastFocus
        };
    }
}
