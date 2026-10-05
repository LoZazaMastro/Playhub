namespace Playhub;

internal static class BuildFeatures
{
#if PLAYHUB_EMULATION
    internal const bool EmulationEnabled = true;
#else
    internal const bool EmulationEnabled = false;
#endif
}
