using System;
using System.IO;
using System.Text;

namespace Playhub.Services;

/// <summary>
/// Logging diagnostico robusto per i crash (soprattutto all'avvio).
///
/// Scrive una traccia "breadcrumb" a ogni tappa dell'avvio: se il processo muore
/// — anche per un crash NATIVO che .NET non riesce a intercettare — l'ultima riga
/// del file dice esattamente qual è stato l'ultimo passo raggiunto. Le eccezioni
/// gestite (da qualunque thread) vengono registrate con lo stack completo.
///
/// Scrive in "playhub_crash.txt" accanto all'eseguibile e, se presente, ne fa il
/// mirror in F:\Playhub per la diagnosi durante lo sviluppo.
/// </summary>
public static class Diag
{
    private static readonly object Gate = new();

    private static string PrimaryLog => Path.Combine(AppContext.BaseDirectory, "playhub_crash.txt");

    private static string? MirrorLog
    {
        get
        {
            try { return Directory.Exists(@"F:\Playhub") ? @"F:\Playhub\playhub_crash.txt" : null; }
            catch { return null; }
        }
    }

    /// <summary>Registra una tappa dell'avvio (breadcrumb).</summary>
    public static void Step(string stage) => Write("STEP  " + stage);

    /// <summary>Registra un'eccezione/crash con la sorgente e lo stack.</summary>
    public static void Crash(string source, object? error) => Write("CRASH " + source + "\n" + error);

    private static void Write(string message)
    {
        var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + "\n";
        lock (Gate)
        {
            foreach (var path in new[] { PrimaryLog, MirrorLog })
            {
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                try
                {
                    if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024)
                    {
                        File.Delete(path);
                    }
                }
                catch
                {
                }

                try { File.AppendAllText(path, line, Encoding.UTF8); } catch { }
            }
        }
    }
}
