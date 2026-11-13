using System.Reflection;
using System.Runtime.InteropServices;
using GamingMode.Services;
using Microsoft.Win32.SafeHandles;

internal static class SteamDownloadPowerLeaseTests
{
	public static void VerifyIdentityAndExpiry()
	{
		using var fixture = new Fixture();
		Check(!fixture.Lease.Start(0).Ok && fixture.Requests.Count == 0, "An empty game ID acquired a request.");
		string first = fixture.Start();
		Check(fixture.Lease.Start(42).Reason == "busy", "A second caller replaced the active request.");
		Check(!fixture.Lease.Stop("foreign") && !fixture.Lease.Renew("foreign", 42).Ok, "A foreign token controlled the request.");
		Check(!fixture.Lease.Renew(first, 43).Ok && !fixture.Requests[0].Disposed, "Another game renewed or closed the request.");
		Volatile.Write(ref fixture.Time, 7_999);
		Check(fixture.Lease.Renew(first, 42).Ok, "A timely heartbeat failed.");
		Volatile.Write(ref fixture.Time, 15_999);
		Check(!fixture.Lease.Renew(first, 42).Ok && fixture.Requests[0].Disposed, "The request survived its last heartbeat.");
		string second = fixture.Start();
		fixture.Requests[0].PowerEvent();
		Check(first != second && !fixture.Requests[1].Disposed, "A queued event from the previous request closed the new request.");
		Check(fixture.Lease.Stop(second) && fixture.Requests[1].Disposed, "An exact stop did not close the request.");
	}

	public static void VerifyRevocation()
	{
		using var fixture = new Fixture();
		fixture.Ac = false;
		Check(!fixture.Lease.Start(42).Ok && fixture.Requests.Count == 0, "DC power acquired a request.");
		fixture.Ac = true;
		fixture.Start();
		fixture.Ac = false;
		Check(SpinWait.SpinUntil(() => fixture.Requests[0].Disposed, 3_000), "The watchdog held power after AC was unplugged.");
		fixture.Ac = true;
		string second = fixture.Start();
		fixture.Allowed = false;
		Check(!fixture.Lease.Renew(second, 42).Ok && fixture.Requests[1].Disposed, "The game or mode guard did not release power.");
		fixture.Allowed = true;
		fixture.Start();
		fixture.Lease.Revoke();
		Check(fixture.Requests[2].Disposed, "A mode transition did not close the request.");
		fixture.Start();
		fixture.Requests[3].PowerEvent();
		Check(fixture.Requests[3].Disposed, "A system suspend notification did not close the request.");
		fixture.Start();
		fixture.Dispose();
		Check(fixture.Requests[4].Disposed && !fixture.Lease.Start(42).Ok, "A disposed owner accepted or retained a request.");
	}

	public static void VerifyNativeContext()
	{
		const BindingFlags members = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
		Type native = typeof(NativeSteamDownloadPowerRequest);
		Check((int)native.GetField("SystemRequired", members)!.GetRawConstantValue()! == 1,
			"The native request must permit display idle and explicit suspend.");
		if (!OperatingSystem.IsWindows()) return;

		Type contextType = native.GetNestedType("ReasonContext", BindingFlags.NonPublic)!;
		Type reasonType = native.GetNestedType("ReasonUnion", BindingFlags.NonPublic)!;
		object context = Activator.CreateInstance(contextType)!;
		object reason = Activator.CreateInstance(reasonType)!;
		nint message = Marshal.StringToHGlobalUni("Playhub power request test");
		try
		{
			contextType.GetField("Flags", members)!.SetValue(context, 1u);
			reasonType.GetField("Simple", members)!.SetValue(reason, message);
			contextType.GetField("Reason", members)!.SetValue(context, reason);
			using var handle = (SafeFileHandle)native.GetMethod("PowerCreateRequest", members)!.Invoke(null, [context])!;
			Check(!handle.IsInvalid, "Windows rejected the power request reason context.");
			// No PowerSetRequest call, so the test does not alter the system's idle behavior.
		}
		finally { Marshal.FreeHGlobal(message); }
	}

	private static void Check(bool valid, string message)
	{
		if (!valid) throw new Exception(message);
	}

	private sealed class Fixture : IDisposable
	{
		internal readonly SteamDownloadPowerLease Lease;
		internal readonly List<Request> Requests = [];
		internal volatile bool Ac = true;
		internal volatile bool Allowed = true;
		internal long Time;

		internal Fixture() => Lease = new(() => Allowed, () => Ac, powerEvent =>
		{
			var request = new Request(powerEvent);
			Requests.Add(request);
			return request;
		}, () => Volatile.Read(ref Time));

		internal string Start() => Lease.Start(42).Lease ?? throw new Exception("Expected an active request.");
		public void Dispose() => Lease.Dispose();
	}

	private sealed class Request(Action powerEvent) : IDisposable
	{
		internal readonly Action PowerEvent = powerEvent;
		internal volatile bool Disposed;
		public void Dispose() => Disposed = true;
	}
}
