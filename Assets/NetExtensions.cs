public static class NetExtensions
{
    public static bool SetIfChanged(this NetworkVariable<int> nv, int value)
    {
        if (nv.Value == value) return false;
        nv.Value = value;
        return true;
    }

    public static bool SetIfChanged(this NetworkVariable<bool> nv, bool value)
    {
        if (nv.Value == value) return false;
        nv.Value = value;
        return true;
    }
}
