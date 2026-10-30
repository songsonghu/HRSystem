namespace HRSystem.Web.Services;

/// <summary>Small formatting helpers shared by views.</summary>
public static class Display
{
    /// <summary>Up to two initials for an avatar, e.g. "Ada Lovelace" -> "AL", "ada@x.com" -> "A".</summary>
    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var words = name.Split('@')[0].Split(new[] { ' ', '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(words.Take(2).Select(w => char.ToUpperInvariant(w[0])));
    }

    /// <summary>Stable avatar colour index (0-5) so the same person always gets the same colour.</summary>
    public static int Hue(string? name) => string.IsNullOrEmpty(name) ? 0 : name.Sum(c => c) % 6;

    public static string Greeting(DateTime now)
        => now.Hour < 12 ? "Good morning" : now.Hour < 18 ? "Good afternoon" : "Good evening";
}
