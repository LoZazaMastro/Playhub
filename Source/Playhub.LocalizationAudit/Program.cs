using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Playhub.Services;

var root = Path.GetFullPath(args.FirstOrDefault() ?? Path.Combine(AppContext.BaseDirectory, "../../../../../Playhub"));
var output = Path.GetFullPath(args.Skip(1).FirstOrDefault() ?? Path.Combine(root, "../Playhub.LocalizationAudit/Inventory"));
Directory.CreateDirectory(output);
var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
var sourceFiles = Directory.EnumerateFiles(root, "MainWindow*.cs")
    .Concat(Directory.EnumerateFiles(Path.Combine(root, "Services"), "*.cs"))
    .Concat(Directory.EnumerateFiles(Path.Combine(root, "Models"), "*.cs"))
    .Where(path => Path.GetFileName(path) != "LocalizationService.cs").Order().ToArray();

foreach (var path in sourceFiles)
{
    var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path);
    foreach (var expression in tree.GetRoot().DescendantNodes().OfType<ExpressionSyntax>())
    {
        if (expression is not InterpolatedStringExpressionSyntax &&
            expression is not LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression }) continue;
        var key = Template(expression);
        if (key is null) continue;
        var member = expression.Ancestors().OfType<MemberDeclarationSyntax>().FirstOrDefault();
        var memberName = member switch
        {
            MethodDeclarationSyntax method => method.Identifier.Text,
            PropertyDeclarationSyntax property => property.Identifier.Text,
            FieldDeclarationSyntax field => string.Join(",", field.Declaration.Variables.Select(variable => variable.Identifier.Text)),
            _ => member?.Kind().ToString() ?? ""
        };
        var statement = expression.Ancestors().FirstOrDefault(node => node is StatementSyntax or VariableDeclaratorSyntax or AttributeSyntax
            or AssignmentExpressionSyntax or ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax) ?? expression.Parent!;
        var context = statement.ToString();
        if (context.Length > 2400) context = context[..2400] + "\n[Context abbreviated; inspect the referenced source location.]";
        var invocation = expression.Ancestors().OfType<InvocationExpressionSyntax>().FirstOrDefault();
        var assignment = expression.Ancestors().OfType<AssignmentExpressionSyntax>().FirstOrDefault();
        var location = tree.GetLineSpan(expression.Span).StartLinePosition;
        var classification = ReviewedContexts.Classify(Path.GetFileName(path), memberName, key)
            ?? Classify(key, expression, invocation, assignment, memberName);
        Add(key, new Occurrence(Path.GetRelativePath(root, path).Replace('\\', '/'), location.Line + 1,
            memberName, context, classification.Category, classification.Reason));
    }
}
foreach (var path in Directory.EnumerateFiles(root, "*.xaml"))
{
    var xml = XDocument.Load(path, LoadOptions.SetLineInfo);
    foreach (var attribute in xml.Descendants().Attributes().Where(attribute =>
        attribute.Name.LocalName is "Text" or "Content" or "Header" or "PlaceholderText" or "ToolTip" or "Title" or "PrimaryButtonText" or "SecondaryButtonText" or "CloseButtonText"))
    {
        if (attribute.Value.StartsWith('{')) continue;
        Add(attribute.Value, new Occurrence(Path.GetRelativePath(root, path), ((System.Xml.IXmlLineInfo)attribute).LineNumber,
            "XAML", attribute.Parent!.ToString(), "ui", "XAML text property"));
    }
}

var decisionsPath = Path.Combine(output, "decisions.json");
var decisions = File.Exists(decisionsPath)
    ? JsonSerializer.Deserialize<Dictionary<string, Decision>>(File.ReadAllText(decisionsPath), options)!
    : new Dictionary<string, Decision>();
foreach (var entry in entries.Values)
{
    entry.Category = entry.Occurrences.Any(item => item.Category == "ui") ? "ui"
        : entry.Occurrences.Any(item => item.Category == "review") ? "review"
        : entry.Occurrences.Any(item => item.Category == "inline") ? "inline" : "excluded";
    if (decisions.TryGetValue(entry.Key, out var decision) && entry.Category == "review")
    {
        entry.Category = decision.Category;
        entry.Reason = decision.Reason;
    }
    else if (entry.Category == "excluded") entry.Reason = string.Join("; ", entry.Occurrences.Select(item => item.Reason).Distinct());
}
if (args.Contains("--record-reviewed"))
{
    foreach (var entry in entries.Values.Where(entry => entry.Category == "review"))
    {
        decisions[entry.Key] = new Decision("ui", "Coordinator reviewed source occurrence " + entry.Occurrences[0].File + ":" + entry.Occurrences[0].Line);
        entry.Category = "ui";
        entry.Reason = decisions[entry.Key].Reason;
    }
    Write("decisions.json", decisions);
}

var tables = new[] { "Strings", "NewStrings", "ExtraStrings" }.Select(name =>
    (Dictionary<string, string[]>)typeof(LocalizationService).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!).ToArray();
foreach (var entry in entries.Values.Where(entry => entry.Category == "review" && tables.Any(table => table.ContainsKey(entry.Key))))
{
    entry.Category = "ui";
    entry.Reason = "Existing interface translation key";
}
var languages = LocalizationService.Languages.Where(language => language.Key != "it").Select(language => language.Key).ToArray();
var inventory = entries.Values.OrderBy(entry => entry.Key, StringComparer.Ordinal).ToArray();
Write("all-strings.json", inventory);
Write("review.json", inventory.Where(entry => entry.Category == "review"));
Write("ui.json", inventory.Where(entry => entry.Category == "ui"));
Write("exclusions.json", inventory.Where(entry => entry.Category == "excluded"));
Write("inline-translations.json", inventory.Where(entry => entry.Category == "inline"));
Write("composed-expressions.json", sourceFiles.SelectMany(path =>
{
    var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path);
    return tree.GetRoot().DescendantNodes().OfType<BinaryExpressionSyntax>()
        .Where(expression => expression.IsKind(SyntaxKind.AddExpression) &&
            expression.Parent is not BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AddExpression } &&
            expression.DescendantNodes().OfType<LiteralExpressionSyntax>().Any(literal => literal.IsKind(SyntaxKind.StringLiteralExpression)))
        .Select(expression => new
        {
            File = Path.GetRelativePath(root, path).Replace('\\', '/'),
            Line = tree.GetLineSpan(expression.Span).StartLinePosition.Line + 1,
            Expression = expression.ToString(),
            Parts = expression.DescendantNodes().OfType<LiteralExpressionSyntax>()
                .Where(literal => literal.IsKind(SyntaxKind.StringLiteralExpression))
                .Select(literal => new { Key = literal.Token.ValueText, Category = entries[literal.Token.ValueText].Category }).ToArray()
        }).ToArray();
}).ToArray());
Write("missing.it.json", inventory.Where(entry => entry.Category == "ui"));
var coverage = new List<object>();
for (var index = 0; index < languages.Length; index++)
{
    var language = languages[index];
    var missing = inventory.Where(entry => entry.Category is "ui" or "review" && !HasTranslation(entry.Key, index)).ToArray();
    Write("missing." + language + ".json", missing);
    coverage.Add(new { Language = language, Missing = missing.Length, Ui = inventory.Count(entry => entry.Category == "ui"), Unreviewed = inventory.Count(entry => entry.Category == "review") });
}
Write("coverage.json", coverage);
Console.WriteLine($"Scanned {sourceFiles.Length} C# files plus XAML: {inventory.Length} unique expressions, {inventory.Count(entry => entry.Category == "ui")} UI, {inventory.Count(entry => entry.Category == "review")} need review, {inventory.Count(entry => entry.Category == "excluded")} excluded.");
Console.WriteLine(JsonSerializer.Serialize(coverage));
if (args.Contains("--verify") || args.Contains("--verify-existing"))
    Environment.ExitCode = AuditGate.Verify(root, output, inventory, tables, languages, args.Contains("--verify-existing"));

void Add(string key, Occurrence occurrence)
{
    if (!entries.TryGetValue(key, out var entry)) entries[key] = entry = new Entry(key);
    entry.Occurrences.Add(occurrence);
}
void Write(string name, object value) => File.WriteAllText(Path.Combine(output, name), JsonSerializer.Serialize(value, options) + "\n", new UTF8Encoding(false));
bool HasTranslation(string key, int index)
{
    foreach (var table in tables)
        if (table.TryGetValue(key, out var values)) return index < values.Length && !string.IsNullOrWhiteSpace(values[index]);
    return false;
}

static string? Template(ExpressionSyntax expression)
{
    if (expression is LiteralExpressionSyntax literal) return literal.Token.ValueText;
    if (expression is not InterpolatedStringExpressionSyntax interpolated) return null;
    var builder = new StringBuilder();
    var index = 0;
    foreach (var content in interpolated.Contents)
    {
        if (content is InterpolatedStringTextSyntax text) builder.Append(text.TextToken.ValueText);
        else if (content is InterpolationSyntax hole)
        {
            builder.Append('{').Append(index++);
            if (hole.AlignmentClause is not null) builder.Append(',').Append(hole.AlignmentClause.Value);
            if (hole.FormatClause is not null) builder.Append(':').Append(hole.FormatClause.FormatStringToken.ValueText);
            builder.Append('}');
        }
    }
    return builder.ToString();
}

static (string Category, string Reason) Classify(string key, ExpressionSyntax expression,
    InvocationExpressionSyntax? invocation, AssignmentExpressionSyntax? assignment, string member)
{
    var call = invocation?.Expression.ToString() ?? "";
    var target = assignment?.Left.ToString().Split('.').Last() ?? "";
    var argument = expression.Ancestors().OfType<ArgumentSyntax>().FirstOrDefault();
    var argumentIndex = argument?.Parent is ArgumentListSyntax list ? list.Arguments.IndexOf(argument) : -1;
    var name = call.Split('.').Last();
    var creation = expression.Ancestors().OfType<ObjectCreationExpressionSyntax>().FirstOrDefault();
    var createdType = creation?.Type.ToString().Split('.').Last() ?? "";
    var declarator = expression.Ancestors().OfType<VariableDeclaratorSyntax>().FirstOrDefault()?.Identifier.Text ?? "";
    if (string.IsNullOrWhiteSpace(key) || key.All(character => !char.IsLetterOrDigit(character))) return ("excluded", "Whitespace, punctuation or icon glyph");
    if (Regex.IsMatch(key, @"^(https?://|ms-settings:|steam://|shell:|[A-Za-z]:[\\/])", RegexOptions.IgnoreCase)) return ("excluded", "Technical URL, protocol or absolute path");
    if (Regex.IsMatch(key, @"^#[0-9a-fA-F]{3,8}$") || Regex.IsMatch(key, @"^\d+(?:\.\d+)*$")) return ("excluded", "Color or numeric/version literal");
    if (member is "DescriptionTranslations") return ("excluded", "Third-party plugin description translation data; original README must remain unchanged");
    if (createdType is "Regex" || call.StartsWith("Regex.") || member.EndsWith("Pattern") || member is "ProtectedSyntax" or "UnsupportedBlocks" or "MarkdownMediaDestination") return ("excluded", "Regular expression or parsing syntax");
    if (createdType is "Uri" or "FontFamily" or "ProcessStartInfo") return ("excluded", "URL/font/process metadata");
    if (createdType == "ComboOption") return argumentIndex == 0 ? ("excluded", "Combo option key") : ("ui", "Combo option label");
    if (Regex.IsMatch(key, @"^(?:\*?\.[a-zA-Z0-9]+|[\w.-]+\.(?:exe|dll|png|jpg|jpeg|ico|bmp|webp|mp4|gif|json|xml|vdf|ps1|bat|cmd|zip|txt|md|log|tmp|bak|cfg|config))$")) return ("excluded", "File name, extension or file filter");
    if (key.StartsWith('-') && Regex.IsMatch(key, @"^-{1,2}[A-Za-z]")) return ("excluded", "Command-line switch/template");
    if (key.StartsWith('/') && !key.Contains(' ')) return ("excluded", "API route or relative path");
    if (Regex.IsMatch(key, @"^[A-Fa-f0-9]{32,}$") || Regex.IsMatch(key, @"^[A-Za-z0-9_-]+/[A-Za-z0-9_.-]+$")) return ("excluded", "Digest or repository identifier");
    if (key.StartsWith("<DataTemplate") || key.StartsWith("</DataTemplate") || key.StartsWith("<ContentPresenter") || key.StartsWith("<PathIcon") || Regex.IsMatch(key, @"^M\d.*[LlCcHhVvZz]")) return ("excluded", "XAML/vector markup");
    if (expression.Ancestors().Any(node => node is AttributeSyntax)) return ("excluded", "Compiler/native API attribute");
    if (call is "DependencyProperty.RegisterAttached" or "DependencyProperty.Register" && argumentIndex == 0)
        return ("excluded", "Native dependency-property registration identifier");
    if (target is "Tag" or "Glyph" or "Name" or "AutomationId" or "Key" or "Property" or "TargetName" or "TargetProperty") return ("excluded", "Technical identifier property " + target);
    if (call.StartsWith("Diag.") || call.StartsWith("Debug.") || call.StartsWith("Console.")) return ("excluded", "Diagnostic-only output");
    if (name is "StyleResource" or "ResourceBrush" or "SetResource" or "SetBrush" or "SetAutomationId" or "GetEnvironmentVariable" or "GetManifestResourceStream") return ("excluded", "Resource/environment/automation identifier");
    if (name is "TryGetProperty" or "GetProperty" or "GetNamedString" or "GetValue" or "SetValue" or "GetRegistryValue" or "GetString" or "GetBool" or "GetInt" or "GetBoolean") return ("excluded", "Structured data/property identifier");
    if (member.StartsWith("Normalize") || member is "GetPluginCategories" or "CategoryKey" or "IsNormalComponent" or "IsIgnoredManifestPath" or "LooksInternalTitle" or "IsMeaningfulFolder") return ("excluded", "Normalization/parser token");
    if (member is "ApplyWindowsBrowserHookFix" or "StopPluginProcesses" && key.Contains('\n')) return ("excluded", "Embedded script/source code");
    if (call.StartsWith("Path.") || call.StartsWith("Registry.") || call.StartsWith("File.") && name is "Exists" or "OpenRead" or "ReadAllText" or "ReadAllTextAsync") return ("excluded", "Filesystem or registry argument");
    if (target is "Text" or "Content" or "Header" or "PlaceholderText" or "Title" or "Message" or "PrimaryButtonText" or "SecondaryButtonText" or "CloseButtonText" or "OnContent" or "OffContent") return ("ui", "UI text property " + target);
    if (name is "T" or "Body" or "SectionTitle" or "GroupTitle" or "Labeled" or "SetStatus" or "TranslateMessage" or "DeckyOperationButton" or "SetToolTip" or "SetName") return ("ui", "User-facing helper " + name);
    if (name is "Button" or "TextBox" or "Number" && argumentIndex == 0) return ("ui", "Control label");
    if (name == "AddExplainedToggle") return argumentIndex is 1 or 2 ? ("ui", "Toggle heading/body") : ("excluded", "Toggle settings key");
    if (name == "NumberWithHint" && argumentIndex == 1) return ("ui", "Numeric control explanatory text");
    if (name is "SetToggle" or "GetToggle") return ("excluded", "Toggle settings key");
    if (name is "AddToggle" or "Toggle" or "GamingToggle" or "AddGamingToggle" && argumentIndex > 0) return ("ui", "Toggle heading or description");
    if (name is "AddToggle" or "Toggle" or "GamingToggle" or "AddGamingToggle" && argumentIndex == 0) return ("excluded", "Toggle settings key");
    if (name is "ConfirmAsync" or "Confirm" or "ShowMessageAsync" or "ShowDialogAsync" or "ShowInfoDialogAsync" or "UpdatePlayhubUpdateDialogProgress") return ("ui", "Dialog/status helper");
    if (name is "IconHeader" or "IconButton" or "BuildDeckyStep" or "Page" && argumentIndex > 0) return ("ui", "Heading/body/command helper");
    if (name is "IconHeader" or "IconButton" or "BuildDeckyStep" or "Page" && argumentIndex == 0) return ("excluded", "Icon or page routing key");
    if (expression.Ancestors().Any(node => node is ThrowStatementSyntax or ThrowExpressionSyntax)) return ("ui", "Service/user-facing exception message (conservative)");
    if (declarator is "title" or "subtitle" or "label" or "message" or "tooltip" or "status" or "description" or "warning") return ("ui", "Display text variable");
    if (expression.Ancestors().Any(node => node is ReturnStatementSyntax) && !member.StartsWith("Normalize") && !member.StartsWith("Get")) return ("review", "Returned value may reach UI");
    return ("review", "Requires source-context classification");
}

sealed record Occurrence(string File, int Line, string Member, string Context, string Category, string Reason);
sealed record Decision(string Category, string Reason);
sealed class Entry(string key)
{
    public string Key { get; } = key;
    public string Category { get; set; } = "review";
    public string Reason { get; set; } = "";
    public List<Occurrence> Occurrences { get; } = [];
}
