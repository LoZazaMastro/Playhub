using System;

// Minimal NLog replacement so the reused GoTweaks AMD classes compile without
// the NLog package. Logging is sent to stderr (stdout is reserved for JSON).
namespace NLog
{
    public sealed class Logger
    {
        public void Info(string m) { try { Console.Error.WriteLine(m); } catch { } }
        public void Info(string m, params object[] a) { }
        public void Warn(string m) { try { Console.Error.WriteLine(m); } catch { } }
        public void Warn(string m, params object[] a) { }
        public void Error(string m) { try { Console.Error.WriteLine(m); } catch { } }
        public void Error(Exception e, string m) { try { Console.Error.WriteLine(m + " " + e); } catch { } }
        public void Debug(string m) { }
        public void Trace(string m) { }
    }

    public static class LogManager
    {
        public static Logger GetCurrentClassLogger() { return new Logger(); }
    }
}
