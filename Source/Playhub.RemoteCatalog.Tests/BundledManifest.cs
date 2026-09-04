using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.Json.Nodes;

internal static class BundledManifest
{
    internal static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    // Parse C# literals with Roslyn so descriptions, escapes and glyphs are not retyped.
    internal static JsonObject Assemble()
    {
        var catalog = JsonNode.Parse(File.ReadAllText(Path.Combine(Root,
            "Source/Playhub/Assets/PluginCatalog/external-plugins.json")))!.AsObject();
        var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(Root,
            "Source/Playhub/Services/PluginCatalogService.cs"))).GetRoot();
        var variables = syntax.DescendantNodes().OfType<VariableDeclaratorSyntax>().ToList();
        var owner = ((LiteralExpressionSyntax)variables.Single(v => v.Identifier.Text == "Owner").Initializer!.Value).Token.ValueText;
        Dictionary<string, string> Dictionary(string name) => variables.Single(v => v.Identifier.Text == name)
            .Initializer!.Value.DescendantNodes().OfType<AssignmentExpressionSyntax>()
            .ToDictionary(a => a.Left.DescendantNodes().OfType<LiteralExpressionSyntax>().Single().Token.ValueText,
                a => ((LiteralExpressionSyntax)a.Right).Token.ValueText);
        var versions = Dictionary("PlayhubCatalogVersions");
        var keywords = Dictionary("PlayhubKeywords");
        var builtIns = new JsonArray();
        foreach (var definition in variables.Single(v => v.Identifier.Text == "Definitions")
            .Initializer!.Value.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            var args = definition.ArgumentList!.Arguments;
            string Literal(int index) => ((LiteralExpressionSyntax)args[index].Expression).Token.ValueText;
            var glyph = args[4].DescendantNodes().OfType<LiteralExpressionSyntax>().Single().Token.Value!;
            var repository = Literal(0);
            builtIns.Add(new JsonObject
            {
                ["active"] = true,
                ["name"] = Literal(2),
                ["installFolder"] = Literal(3),
                ["author"] = owner,
                ["repository"] = owner + "/" + repository,
                ["repositoryUrl"] = "https://github.com/" + owner + "/" + repository,
                ["version"] = versions[repository],
                ["releaseAssetName"] = "",
                ["catalogReleaseUrl"] = "",
                ["releasePublishedAt"] = "",
                ["category"] = "Playhub",
                ["shortDescription"] = Literal(5),
                ["longDescription"] = Literal(6),
                ["coverUrl"] = repository == "Shortcuts"
                    ? "https://raw.githubusercontent.com/LoZazaMastro/Shortcuts/main/assets/cover.jpg"
                    : "",
                ["iconGlyph"] = ((char)Convert.ToInt32(glyph)).ToString(),
                ["catalogStatus"] = "playhub",
                ["catalogSource"] = "playhub",
                ["catalogPluginId"] = 0,
                ["compatibility"] = "",
                ["keywords"] = new JsonArray(keywords[repository].Split(' ').Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()),
                ["aliases"] = new JsonArray(Literal(2), Literal(1), Literal(3), repository)
            });
        }
        foreach (var external in catalog["plugins"]!.AsArray()) builtIns.Add(external!.DeepClone());
        catalog["catalogRevision"] = 2;
        catalog["plugins"] = builtIns;
        return catalog;
    }
}
