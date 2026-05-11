using System.Text.Json;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: gen-entities <html-entities.json> <NamedCharacterReferences.cs>");
    return 2;
}

var entities = JsonDocument.Parse(File.ReadAllText(args[0]));
using var output = new StreamWriter(args[1]);

output.WriteLine("namespace Tessera.Html.Tokenizer;");
output.WriteLine();
output.WriteLine("/// <summary>WHATWG HTML named character-reference table.</summary>");
output.WriteLine("public static class NamedCharacterReferences");
output.WriteLine("{");
output.WriteLine("    public readonly record struct Match(int Length, int CodePoint1, int? CodePoint2);");
output.WriteLine();
output.WriteLine("""
    public static Match? FindLongest(ReadOnlySpan<char> input)
    {
        Match? best = null;
        foreach (var (name, cp1, cp2) in Table)
        {
            if (input.Length < name.Length) continue;
            if (!input.StartsWith(name)) continue;
            if (best is null || name.Length > best.Value.Length)
                best = new Match(name.Length, cp1, cp2);
        }
        return best;
    }

    private static readonly (string Name, int Cp1, int? Cp2)[] Table =
    [
""");

foreach (var entity in entities.RootElement.EnumerateObject()
             .OrderBy(e => e.Name[1..], StringComparer.Ordinal))
{
    var name = Escape(entity.Name[1..]);
    var codepoints = entity.Value.GetProperty("codepoints").EnumerateArray()
        .Select(v => v.GetInt32()).ToArray();
    var cp2 = codepoints.Length == 1 ? "null" : $"0x{codepoints[1]:X}";
    output.WriteLine($"        (\"{name}\", 0x{codepoints[0]:X}, {cp2}),");
}

output.WriteLine("    ];");
output.WriteLine("}");
return 0;

static string Escape(string value)
    => value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal);
