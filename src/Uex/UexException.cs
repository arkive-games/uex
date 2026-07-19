namespace Uex;

/// <summary>User-facing error: printed as a plain message (no stack trace), exit code 1.</summary>
public sealed class UexException(string message) : Exception(message);
