using System.Collections.Concurrent;

// These test doubles are compiled only by this isolated console project.
// The production ControllerHapticsService is linked without modification.
namespace GamingMode.Services
{
    public sealed class FileLogger { public void Error(string message, Exception error) { } }
    internal enum SteamUiHapticsState { Allowed }
    internal sealed class SteamUiHapticsGate
    {
        public SteamUiHapticsState ReadState() => SteamUiHapticsState.Allowed;
        public bool CanPlay() => true;
    }
}

namespace Windows.Devices.Haptics
{
    public sealed class SimpleHapticsControllerFeedback { public ushort Waveform { get; set; } }
    public sealed class SimpleHapticsController
    {
        public string Id => "fixture";
        public bool IsPlayDurationSupported => true;
        public IReadOnlyList<SimpleHapticsControllerFeedback> SupportedFeedback => [];
        public void StopFeedback() { }
        public void SendHapticFeedbackForDuration(SimpleHapticsControllerFeedback feedback, double strength, TimeSpan duration) { }
        public void SendHapticFeedback(SimpleHapticsControllerFeedback feedback, double strength) { }
    }
    public static class KnownSimpleHapticsControllerWaveforms { public const ushort Click = 1, Press = 2, RumbleContinuous = 3, BuzzContinuous = 4; }
}

namespace Windows.Gaming.Input
{
    public readonly record struct GamepadVibration(double LeftMotor, double RightMotor, double LeftTrigger, double RightTrigger);
    public sealed class Gamepad
    {
        public static readonly ManualResetEventSlim Enumerating = new(), Release = new();
        public static Gamepad Fixture = new();
        public static bool StopCompleted;
        public static int PositiveAfterStop;
        public ConcurrentQueue<GamepadVibration> Writes { get; } = new();
        public static void Reset(bool blockEnumeration = false)
        {
            Fixture = new(); StopCompleted = false; PositiveAfterStop = 0; Enumerating.Reset();
            if (blockEnumeration) Release.Reset(); else Release.Set();
        }
        public static IReadOnlyList<Gamepad> Gamepads
        {
            get { Enumerating.Set(); if (!Release.Wait(2000)) throw new TimeoutException(); return [Fixture]; }
        }
        public GamepadVibration Vibration
        {
            set
            {
                Writes.Enqueue(value);
                if (StopCompleted && (value.LeftMotor > 0 || value.RightMotor > 0)) Interlocked.Increment(ref PositiveAfterStop);
            }
        }
    }
    public sealed class RawGameController
    {
        public static RawGameController? FromGameController(Gamepad gamepad) => new();
        public static IReadOnlyList<RawGameController> RawGameControllers => [];
        public ushort HardwareVendorId => 0x054c;
        public ushort HardwareProductId => 0x0ce6;
        public string NonRoamableId => "owned-test-controller";
        public string DisplayName => "Inert Windows input fixture";
        public IReadOnlyList<Windows.Devices.Haptics.SimpleHapticsController> SimpleHapticsControllers => [];
    }
}
