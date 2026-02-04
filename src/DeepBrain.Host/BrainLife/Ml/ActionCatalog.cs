namespace DeepBrain.Host.BrainLife.Ml;

public static class ActionCatalog
{
    public static readonly string[] Actions =
    {
        "breathe_slow",
        "focus_narrow",
        "rest_short",
        "explore_signal",
        "focus_widen",
        "emit_message",
        "reframe_negative",
        "loop_break"
    };

    public static int Count => Actions.Length;

    public static int IndexOf(string actionName)
    {
        for (var i = 0; i < Actions.Length; i++)
        {
            if (Actions[i] == actionName)
                return i;
        }
        return -1;
    }
}
