using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using GamingMode.Services;

internal static class LivePerformanceSystemUsageTests
{
    internal static void Verify()
    {
        int stride = IntPtr.Size + 16;
        nint data = Marshal.AllocHGlobal(stride * 3);
        try {
            void Write(int index, int status, double value) {
                Marshal.WriteInt32(data, index * stride + IntPtr.Size, status);
                Marshal.WriteInt64(data, index * stride + IntPtr.Size + 8, BitConverter.DoubleToInt64Bits(value));
            }
            Write(0, 0, 12.5); Write(1, 1, 3.5); Write(2, unchecked((int)0xC0000BC6), 900);
            if (LivePerformanceSystemUsage.SumGpu(data, (uint)(stride * 3), 3) != 16) throw new Exception("Valid/new GPU counters sum; invalid entries never become load");
            if (LivePerformanceSystemUsage.SumGpu(data, 1, 3) is not null) throw new Exception("Invalid native capacity refused");
            Write(0, 0, double.NaN); Write(1, 1, -1);
            if (LivePerformanceSystemUsage.SumGpu(data, (uint)(stride * 3), 3) is not null) throw new Exception("Unavailable GPU is null, never a fake zero");
        } finally { Marshal.FreeHGlobal(data); }
        var a="luid_0x00000000_0x00000001_phys_0"; var b="luid_0x00000000_0x00000002_phys_0";
        LivePerformanceSystemUsage.NamedValue E(int pid,string adapter,double value)=>new($"pid_{pid}_{adapter}_eng_0_engtype_3D",value);
        void Check(bool test){if(!test)throw new Exception("check failed");}
        Check(LivePerformanceSystemUsage.SelectAdapter(new[]{E(42,a,0)},42)==a);
        Check(LivePerformanceSystemUsage.SelectAdapter(new[]{E(43,a,90),E(42,b,5)},42)==b);
        Check(LivePerformanceSystemUsage.SelectAdapter(new[]{E(42,a,50),E(42,b,5)},42)==a);
        Check(LivePerformanceSystemUsage.SelectAdapter(new[]{E(42,a,5),E(42,b,5)},42)==null);
        Check(LivePerformanceSystemUsage.SelectAdapter(new[]{E(42,a,double.NaN)},42)==null);
        Check(LivePerformanceSystemUsage.SelectAdapter(new[]{E(42,a,-1)},42)==null);
        Check(LivePerformanceSystemUsage.SelectAdapter(new[]{E(42,a,5)},0)==null);
        Check(LivePerformanceSystemUsage.SelectAdapter(new[]{new LivePerformanceSystemUsage.NamedValue("pid_42_bad_eng_0_engtype_3D",50)},42)==null);
        Check(LivePerformanceSystemUsage.SelectAdapter(new[]{E(42,a,double.PositiveInfinity)},42)==null);
        Check(LivePerformanceSystemUsage.SelectAdapter(new[]{E(42,a,3),new LivePerformanceSystemUsage.NamedValue($"pid_42_{a}_eng_1_engtype_3D",4),E(42,b,6)},42)==a);
        int memoryStride=IntPtr.Size+16; nint buf=Marshal.AllocHGlobal(memoryStride*2), nameA=Marshal.StringToHGlobalUni(a),nameB=Marshal.StringToHGlobalUni(b);
        try {
         void Item(int i,nint name,int status,long bytes){nint item=buf+i*memoryStride;Marshal.WriteIntPtr(item,name);Marshal.WriteInt32(item,IntPtr.Size,status);Marshal.WriteInt64(item,IntPtr.Size+8,bytes);}
         Item(0,nameA,0,9_007_199_254_740_993);Check(LivePerformanceSystemUsage.ReadDedicatedValue(buf,(uint)memoryStride,1,a)==9_007_199_254_740_993);
         Item(0,nameA,1,0);Check(LivePerformanceSystemUsage.ReadDedicatedValue(buf,(uint)memoryStride,1,a)==0);
         Item(0,nameA,2,5);Check(LivePerformanceSystemUsage.ReadDedicatedValue(buf,(uint)memoryStride,1,a)==null);
         Item(0,nameA,0,-1);Check(LivePerformanceSystemUsage.ReadDedicatedValue(buf,(uint)memoryStride,1,a)==null);
         Item(0,nameB,0,5);Check(LivePerformanceSystemUsage.ReadDedicatedValue(buf,(uint)memoryStride,1,a)==null);
         Item(0,nameA,0,5);Item(1,nameA,0,6);Check(LivePerformanceSystemUsage.ReadDedicatedValue(buf,(uint)memoryStride*2,2,a)==null);
         Check(LivePerformanceSystemUsage.ReadDedicatedValue(buf,(uint)memoryStride,2,a)==null);
         Check(LivePerformanceSystemUsage.ReadDedicatedValue(0,(uint)memoryStride,1,a)==null);
        } finally {Marshal.FreeHGlobal(buf);Marshal.FreeHGlobal(nameA);Marshal.FreeHGlobal(nameB);}
    }

    internal static int Proof(int period)
    {
        if (period is not (50 or 100 or 500)) return 2;
        using var usage = new LivePerformanceSystemUsage();
        usage.Read(); Thread.Sleep(200); usage.Read();
        using var self = Process.GetCurrentProcess(); self.Refresh();
        var before = self.TotalProcessorTime;
        long totalTicks = 0, maximumTicks = 0;
        int count = 0, cpuValues = 0, gpuValues = 0, memoryValues = 0;
        var elapsed = Stopwatch.StartNew();
        while (elapsed.ElapsedMilliseconds < 5000) {
            long start = Stopwatch.GetTimestamp(); var sample = usage.Read(); long ticks = Stopwatch.GetTimestamp() - start;
            totalTicks += ticks; maximumTicks = Math.Max(maximumTicks, ticks); count++;
            if (sample.CpuPercent.HasValue) cpuValues++;
            if (sample.GpuPercent.HasValue) gpuValues++;
            if (sample.UsedMemoryBytes is > 0) memoryValues++;
            Thread.Sleep(period);
        }
        self.Refresh();
        Console.WriteLine(JsonSerializer.Serialize(new {
            usagePeriodMs = period, elapsedMs = elapsed.ElapsedMilliseconds, samples = count, cpuValues, gpuValues, memoryValues,
            processCpuMs = (self.TotalProcessorTime - before).TotalMilliseconds, workingSetBytes = self.WorkingSet64,
            meanReadMicroseconds = totalTicks * 1_000_000.0 / Stopwatch.Frequency / count,
            maximumReadMicroseconds = maximumTicks * 1_000_000.0 / Stopwatch.Frequency,
            externalProcessStarted = false, gameOrSteamTouched = false
        }));
        return 0;
    }
}
