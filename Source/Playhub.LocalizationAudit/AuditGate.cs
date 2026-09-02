using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Playhub.Services;

internal static class AuditGate
{
    private static readonly Regex Placeholder = new(@"(?<!\{)\{(?<index>\d+)(?:,-?\d+)?(?::[^{}]*)?\}(?!\})");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static int Verify(string root, string output, Entry[] inventory, Dictionary<string, string[]>[] tables, string[] languages, bool existingOnly)
    {
        var failures = new List<string>();
        var report = new List<object>();
        var ui = inventory.Where(entry => entry.Category == "ui").ToArray();
        if (inventory.Any(entry => entry.Category == "review")) failures.Add("Unreviewed source expressions remain");
        var english = ReadDictionary(root, "en", failures, existingOnly);
        foreach (var language in languages.Append("it"))
        {
            var translations = ReadDictionary(root, language, failures, existingOnly);
            if (translations is null) continue;
            var languageIndex = Array.IndexOf(languages, language);
            var missing = new List<string>();
            var overrides = new List<string>();
            var formattedChecks = 0;
            foreach (var (key, value) in translations)
            {
                if (!Placeholder.Matches(key).Select(match => match.Value).Order(StringComparer.Ordinal).SequenceEqual(
                        Placeholder.Matches(value).Select(match => match.Value).Order(StringComparer.Ordinal)))
                    failures.Add(language + ": placeholder mismatch: " + key);
                if (key.Count(character => character == '\n') != value.Count(character => character == '\n'))
                    failures.Add(language + ": newline mismatch: " + key);
                if (string.IsNullOrWhiteSpace(value)) failures.Add(language + ": empty translation: " + key);
                if (LocalizationService.Translate(language, key) != value) failures.Add(language + ": supplemental value not used at runtime: " + key);
                if (tables.Any(table => table.ContainsKey(key))) overrides.Add(key);
                if (language != "en" && language != "it" && english?.TryGetValue(key, out var englishValue) == true &&
                    value == englishValue && Regex.Matches(value, @"\p{L}+").Count >= 5)
                    failures.Add(language + ": suspicious English sentence copy requiring review: " + key);
                if (key.Contains("LG e il logo LG") || key.Contains("Sony e il logo Sony"))
                    failures.Add(language + ": removed trademark text reintroduced");
            }
            if (language != "it")
            foreach (var entry in ui)
            {
                if (!LocalizationService.HasTranslation(language, entry.Key)) { missing.Add(entry.Key); continue; }
                var translatedTemplate = LocalizationService.Translate(language, entry.Key);
                if (!Placeholder.Matches(entry.Key).Select(match => match.Value).Order(StringComparer.Ordinal).SequenceEqual(
                        Placeholder.Matches(translatedTemplate).Select(match => match.Value).Order(StringComparer.Ordinal)))
                    failures.Add(language + ": live table placeholder mismatch: " + entry.Key);
                if (entry.Key.Count(character => character == '\n') != translatedTemplate.Count(character => character == '\n'))
                    failures.Add(language + ": live table newline mismatch: " + entry.Key);
                var placeholders = Placeholder.Matches(entry.Key);
                if (placeholders.Count == 0) continue;
                var arguments = Enumerable.Range(0, placeholders.Max(match => int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture)) + 1)
                    .Select(index => (object)(index + 1.25m)).ToArray();
                try
                {
                    var formattedSource = string.Format(CultureInfo.CurrentCulture, entry.Key, arguments);
                    var expected = LocalizationService.Format(language, entry.Key, arguments);
                    var actual = LocalizationService.Translate(language, formattedSource);
                    if (actual != expected) failures.Add(language + ": preformatted message differs: " + entry.Key + " => " + actual + " (expected " + expected + ")");
                    formattedChecks++;
                }
                catch (FormatException ex) { failures.Add(language + ": invalid format: " + entry.Key + ": " + ex.Message); }
            }
            if (missing.Count > 0) failures.Add(language + ": " + missing.Count + " missing UI translations");
            report.Add(new { Language = language, UiKeys = ui.Length, Supplemental = translations.Count, Overrides = overrides,
                Missing = missing, FormattedChecks = formattedChecks });
            Console.WriteLine($"{language}: {translations.Count} supplemental, {overrides.Count} targeted overrides, {missing.Count} missing, {formattedChecks} formatted checks");
        }

        if (File.Exists(Path.Combine(root, "Assets", "Localization", "it.json")))
        {
            foreach (var label in new[] { "Mica", "Acrylic", "Sfondo pieno", "Cover" })
                if (LocalizationService.Translate("it", label) != label) failures.Add("it: approved source label changed: " + label);
            foreach (var (source, expected) in new[] { ("Start DeckyLoader before Steam", "Avvia DeckyLoader prima di Steam"), ("Start streaming", "Avvia lo streaming") })
                if (LocalizationService.Translate("it", source) != expected) failures.Add("it: English startup alias is not localized: " + source);
        }
        VerifyInlineLanguages(root, languages.Append("it").ToArray(), failures);
        VerifyCompositionsAndAssets(root, ui.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal), failures);
        VerifyRuntimeHooks(root, failures, existingOnly);
        QualitySamples.Write(output, inventory, languages);
        var cached = typeof(LocalizationService).GetMethod("SupplementalFor", BindingFlags.NonPublic | BindingFlags.Static)!;
        if (!ReferenceEquals(cached.Invoke(null, ["en"]), cached.Invoke(null, ["en"]))) failures.Add("Supplemental dictionary is not cached");
        var timer = Stopwatch.StartNew();
        for (var i = 0; i < 1000; i++) LocalizationService.Translate("en", "CSS Loader 2.1.2 è pronto. Ora puoi applicare il profilo Playhub.");
        timer.Stop();
        Console.WriteLine($"1000 warmed dynamic lookups: {timer.ElapsedMilliseconds} ms");
        var results = new { Complete = !existingOnly && failures.Count == 0, ExistingLanguagesOnly = existingOnly,
            InventoryExpressions = inventory.Length, UiKeys = ui.Length, InlineNativeStrings = inventory.Count(entry => entry.Category == "inline"),
            Excluded = inventory.Count(entry => entry.Category == "excluded"), Languages = report, Failures = failures };
        File.WriteAllText(Path.Combine(output, existingOnly ? "verification.partial.json" : "verification.final.json"), JsonSerializer.Serialize(results, JsonOptions) + "\n");
        foreach (var failure in failures) Console.WriteLine("FAIL " + failure);
        return failures.Count == 0 ? 0 : 1;
    }

    private static void VerifyCompositionsAndAssets(string root, HashSet<string> uiKeys, List<string> failures)
    {
        var paths = Directory.GetFiles(root, "MainWindow*.cs").Concat(Directory.GetFiles(Path.Combine(root, "Services"), "*.cs"))
            .Where(path => Path.GetFileName(path) != "LocalizationService.cs");
        foreach (var path in paths)
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path));
            foreach (var literal in tree.GetRoot().DescendantNodes().OfType<LiteralExpressionSyntax>()
                .Where(literal => literal.IsKind(SyntaxKind.StringLiteralExpression) && uiKeys.Contains(literal.Token.ValueText)))
            {
                var composition = literal.Ancestors().OfType<BinaryExpressionSyntax>().FirstOrDefault(expression => expression.IsKind(SyntaxKind.AddExpression));
                if (composition is null) continue;
                if (!literal.Ancestors().TakeWhile(node => node != composition).OfType<InvocationExpressionSyntax>()
                    .Any(call => call.Expression.ToString() is "T" or "TranslateMessage"))
                    failures.Add("Unlocalized UI fragment in composition: " + Path.GetFileName(path) + ":" + (tree.GetLineSpan(literal.Span).StartLinePosition.Line + 1));
            }
        }
        var project = XDocument.Load(Path.Combine(root, "Playhub.csproj"));
        if (!project.Descendants("Content").Any(item => ((string?)item.Attribute("Include"))?.Replace('\\', '/') == "Assets/**/*.*" &&
            item.Element("CopyToOutputDirectory")?.Value is "PreserveNewest" or "Always"))
            failures.Add("Localization JSON assets are not covered by the application content-copy rule");
    }

    private static void VerifyInlineLanguages(string root, string[] languages, List<string> failures)
    {
        var sources = Directory.GetFiles(root, "MainWindow*.cs").Concat(Directory.GetFiles(Path.Combine(root, "Services"), "*.cs"));
        var switches = sources.SelectMany(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot().DescendantNodes().OfType<SwitchExpressionSyntax>());
        var inline = switches.SingleOrDefault(expression => expression.ToString().Contains("Automatic translation", StringComparison.Ordinal));
        if (inline is null) { failures.Add("Inline native translation notice not found"); return; }
        var explicitLanguages = inline.Arms.Select(arm => arm.Pattern).OfType<ConstantPatternSyntax>()
            .Select(pattern => (pattern.Expression as LiteralExpressionSyntax)?.Token.ValueText).ToHashSet();
        foreach (var language in languages.Where(language => language != "en"))
            if (!explicitLanguages.Contains(language)) failures.Add("Inline translation notice missing language: " + language);
        if (!inline.Arms.Any(arm => arm.Pattern is DiscardPatternSyntax && arm.Expression.ToString().Contains("Automatic translation", StringComparison.Ordinal)))
            failures.Add("Inline translation notice missing English default");
    }

    private static Dictionary<string, string>? ReadDictionary(string root, string language, List<string> failures, bool existingOnly)
    {
        var path = Path.Combine(root, "Assets", "Localization", language + ".json");
        if (!File.Exists(path))
        {
            if (!existingOnly) failures.Add("Missing language file: " + language);
            return null;
        }
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var translations = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String) { failures.Add(language + ": non-string JSON entry " + property.Name); continue; }
            if (!translations.TryAdd(property.Name, property.Value.GetString()!)) failures.Add(language + ": duplicate JSON key " + property.Name);
        }
        return translations;
    }

    private static void VerifyRuntimeHooks(string root, List<string> failures, bool existingOnly)
    {
        var files = Directory.GetFiles(root, "MainWindow*.cs");
        var sources = files.Select(File.ReadAllText).ToArray();
        var methods = sources.SelectMany(source => CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()).ToArray();
        var configure = methods.Single(method => method.Identifier.Text == "ConfigureDialogEntrance");
        var localizations = configure.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(call => call.Expression.ToString().Contains("Localiz", StringComparison.Ordinal)).ToArray();
        var motionCheck = configure.DescendantNodes().OfType<InvocationExpressionSyntax>().FirstOrDefault(call => call.Expression.ToString() == "MotionEnabled");
        var hookProblems = new List<string>();
        if (!localizations.Any(call => motionCheck is null || call.SpanStart < motionCheck.SpanStart)) hookProblems.Add("Dialog localization must precede MotionEnabled early return");
        foreach (var method in methods)
        foreach (var call in method.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(call => call.Expression is MemberAccessExpressionSyntax access && access.Name.Identifier.Text == "ShowAsync"))
        {
            var receiver = ((MemberAccessExpressionSyntax)call.Expression).Expression.ToString();
            if (!method.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(candidate => candidate.SpanStart < call.SpanStart &&
                candidate.Expression.ToString() == "ConfigureDialogEntrance" && candidate.ArgumentList.Arguments.FirstOrDefault()?.Expression.ToString() == receiver))
                hookProblems.Add("Dialog route bypasses localization: " + method.Identifier.Text);
        }
        var localizedSource = string.Join("\n", methods.Where(method => method.Identifier.Text.Contains("Localiz", StringComparison.Ordinal)).Select(method => method.ToString()));
        foreach (var property in new[] { "PrimaryButtonText", "SecondaryButtonText", "CloseButtonText", "PlaceholderText", "GetToolTip" })
            if (!localizedSource.Contains(property, StringComparison.Ordinal)) hookProblems.Add("Missing localized UI property hook: " + property);
        if (!localizedSource.Contains("panel.Children", StringComparison.Ordinal)) hookProblems.Add("Logical Grid/Panel child traversal missing");
        if (!localizedSource.Contains("noloc", StringComparison.Ordinal)) hookProblems.Add("Original content protection missing");
        var propertyLocalizer = methods.Single(method => method.Identifier.Text == "LocalizeProperty").ToString();
        if (!propertyLocalizer.Contains("owner.GetValue(LocalizationStorage.Properties)", StringComparison.Ordinal))
            hookProblems.Add("Localization state is not retained by the native control");
        foreach (var problem in hookProblems)
        {
            Console.WriteLine("UI HOOK PENDING " + problem);
            if (!existingOnly) failures.Add(problem);
        }
    }
}
