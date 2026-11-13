using GamingMode.Services;
using Windows.Gaming.Input;

int checks = 0;
void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
foreach (bool pattern in new[] { false, true })
{
    Gamepad.Reset(blockEnumeration: true);
    using var service = new ControllerHapticsService(new FileLogger());
    var pulse = Task.Run(() => pattern ? service.PlayPattern(7, 0x054c, 0x0ce6, 2, .5, "confirm", 100)
        : service.Pulse(7, 0x054c, 0x0ce6, 2, .5, 20, 100));
    Check(Gamepad.Enumerating.Wait(2000), "The real service did not reach the controlled Windows enumeration boundary.");
    try
    {
        service.Stop(requestId: 101);
        Gamepad.StopCompleted = true;
    }
    finally { Gamepad.Release.Set(); }
    var result = await pulse;
    Check(result.Path == "stale-request", "The interrupted native request was not revoked.");
    Check(Gamepad.PositiveAfterStop == 0, "The old pulse wrote positive output after the keyboard stop completed.");
}

Gamepad.Reset();
using (var service = new ControllerHapticsService(new FileLogger()))
{
    service.Stop(requestId: 101);
    Check(service.Pulse(7, 0x054c, 0x0ce6, 2, .5, 20, 100).Path == "stale-request",
        "An old request arriving after stop was accepted.");
    Check(!Gamepad.Enumerating.IsSet, "A stale request touched the Windows inventory.");
    Gamepad.StopCompleted = true;
    Check(service.Pulse(7, 0x054c, 0x0ce6, 2, .5, 20, 102).Path == "windows-gamepad",
        "Fresh physical controller input could not re-arm feedback.");
    Check(Gamepad.PositiveAfterStop == 1, "The fresh action did not produce exactly one initial output.");
    int writes = Gamepad.Fixture.Writes.Count;
    Check(service.Stop(requestId: 101).Path == "stale-request" && Gamepad.Fixture.Writes.Count == writes,
        "An older stop altered the newer physical action.");
    service.Stop(requestId: 103);
    Check(Gamepad.Fixture.Writes.Last().LeftMotor == 0 && Gamepad.Fixture.Writes.Last().RightMotor == 0,
        "The current stop did not clear active output.");
}
Console.WriteLine($"PASS: {checks} real-service haptic ordering checks; Windows APIs are inert test doubles.");
