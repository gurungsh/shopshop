namespace Catalog.Api.Services;

internal static class LikePattern
{
    public const string Escape = "\\";

    // Npgsql emits ESCAPE '' unless one is passed, so ILike callers must pass Escape explicitly.
    public static string Contains(string value) =>
        $"%{value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
}
