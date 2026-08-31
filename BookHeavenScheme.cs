namespace BookHeaven.EbookManager;

public static class BookHeavenScheme
{
	public const string Scheme = "bookheaven";
	public const string Prefix = Scheme + "://";
	

	public static string BuildUrl(string relativePath)
	{
		if (string.IsNullOrWhiteSpace(relativePath))
		{
			throw new ArgumentException("Relative path is required.", nameof(relativePath));
		}

		var normalizedRelativePath = relativePath.Replace('\\', '/').TrimStart('/');
		if (normalizedRelativePath.Contains("..", StringComparison.Ordinal))
		{
			throw new InvalidOperationException("The relative path cannot contain parent traversal segments.");
		}

		return $"{Prefix}{normalizedRelativePath}";
	}
}
