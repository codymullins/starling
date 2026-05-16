namespace Tessera.Url;

/// <summary>
/// The "special scheme" set from WHATWG URL §3.1
/// (<see href="https://url.spec.whatwg.org/#special-scheme"/>). Special
/// schemes have built-in default ports and an authority requirement; they
/// route through the state machine's special-* states.
/// </summary>
internal static class SpecialSchemes
{
    private static readonly Dictionary<string, int?> Map = new(StringComparer.Ordinal)
    {
        ["ftp"]   = 21,
        ["file"]  = null, // file has no default port
        ["http"]  = 80,
        ["https"] = 443,
        ["ws"]    = 80,
        ["wss"]   = 443,
    };

    public static bool IsSpecial(string scheme) => Map.ContainsKey(scheme);

    public static int? DefaultPort(string scheme) =>
        Map.TryGetValue(scheme, out var p) ? p : null;
}
