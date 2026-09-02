namespace BookHeaven.EbookManager;

public static class EbookManagerGlobals
{
    public static string CachePath { get; internal set; } = null!;
    public static bool UseCustomScheme { get; internal set; } = true;
}