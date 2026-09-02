using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class SourceContracts
{
    public static void Run()
    {
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Source"), "MainWindow*.cs");
        var methods = files.SelectMany(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>()).ToArray();
        var restart = methods.Single(method => method.Identifier.Text == "RestartPlayhub");
        var calls = restart.DescendantNodes().OfType<InvocationExpressionSyntax>().Select(call => call.Expression.ToString()).ToArray();
        Require(calls.Contains("Microsoft.Windows.AppLifecycle.AppInstance.Restart"), "SDK restart not wired");
        Require(!calls.Contains("Process.Start") && !calls.Contains("Close"), "Launch-before-exit race reintroduced");
        Require(restart.ToString().Contains("AppRestartFailureReason.RestartPending"), "Pending restart not retained");
        Require(restart.ToString().Contains("Diag.Crash") && restart.ToString().Contains("InfoBarSeverity.Warning"), "Restart failure not reported");

        var settings = methods.Single(method => method.Identifier.Text == "BuildSettingsPage");
        Require(settings.ToString().Contains("await ChangeLanguageAsync()"), "Settings handler bypasses tested flow");
        var localize = methods.Single(method => method.Identifier.Text == "LocalizeElement" && method.ParameterList.Parameters.Count == 2);
        Require(localize.ToString().Contains("case TextBlock") && localize.ToString().Contains("panel.Children"), "Heading/body logical traversal missing");
        Require(localize.ToString().Contains("Tag: \"noloc\""), "Original-description protection missing");
        var header = methods.Single(method => method.Identifier.Text == "IconHeader");
        Require(header.ToString().Contains("LocalizedText") && header.ToString().Contains("Body(subtitle)"), "Heading/body source keys not captured at construction");
        var property = methods.Single(method => method.Identifier.Text == "LocalizeProperty");
        Require(property.ToString().Contains("RegisterPropertyChangedCallback") && property.ToString().Contains("state.Rendered"), "Dynamic property localization is missing");
        Require(property.ToString().Contains("owner.GetValue(LocalizationStorage.Properties)"), "Managed-wrapper-only localization state reintroduced");
        var apply = methods.Single(method => method.Identifier.Text == "ApplyLanguage");
        Require(apply.ToString().Contains("LocalizeElement(root)"), "Startup root not localized");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
