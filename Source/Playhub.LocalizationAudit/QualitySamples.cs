using System.Text.Encodings.Web;
using System.Text.Json;
using Playhub.Services;

internal static class QualitySamples
{
    private static readonly string[] Prefixes =
    [
        "Se il menu rapido non risponde,", "Apri Gaming Mode dal menu rapido di Decky,",
        "Avvia l'host scelto quando entri in Gaming Mode,", "Scegli una versione di DeckyLoader",
        "Usa questa opzione solo se ti serve una versione precisa.", "DeckyLoader con console",
        "Mostra una finestra con il registro in tempo reale.", "Scegli una cartella o un file .exe.",
        "CSS Loader {0} è pronto.", "La release ufficiale non contiene un unico pacchetto ZIP",
        "Playhub è un progetto indipendente", "Playhub verrà riavviato.",
        "Inattività prima di nascondere il cursore.", "Personalizza lo sfondo e il colore di Playhub.",
        "Windows ha impedito la scrittura del file shortcuts di Steam. Consenti"
    ];

    public static void Write(string output, Entry[] inventory, string[] languages)
    {
        var samples = Prefixes.Select(prefix => inventory.Where(entry => entry.Category == "ui" && entry.Key.StartsWith(prefix, StringComparison.Ordinal))
            .OrderByDescending(entry => entry.Key.Length).First()).ToArray();
        var report = samples.Select(entry => new
        {
            Source = entry.Key,
            Contexts = entry.Occurrences.Select(occurrence => occurrence.File + ":" + occurrence.Line).ToArray(),
            Translations = languages.Append("it").ToDictionary(language => language, language => LocalizationService.Translate(language, entry.Key))
        }).ToArray();
        File.WriteAllText(Path.Combine(output, "quality-samples.json"), JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
    }
}
