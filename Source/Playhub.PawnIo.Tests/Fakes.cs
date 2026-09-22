namespace Playhub.Services;

// Compila i servizi PawnIO senza WinUI e senza il file di diagnostica di Playhub.
internal static class Diag
{
    public static void Crash(string source, object? error) { _ = source; _ = error; }
}
