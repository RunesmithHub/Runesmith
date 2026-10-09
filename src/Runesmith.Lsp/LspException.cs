using System.Text.Json;

namespace Runesmith.Lsp;

/// <summary>An error response to a JSON-RPC request.</summary>
public sealed class LspException : Exception
{
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;
    public const int RequestCancelled = -32800;
    public const int ContentModified = -32801;

    public LspException(int code, string message, JsonElement? errorData = null)
        : base(message)
    {
        Code = code;
        ErrorData = errorData;
    }

    public LspException()
    {
    }

    public LspException(string message)
        : base(message)
    {
    }

    public LspException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Gets the JSON-RPC error code, such as <see cref="MethodNotFound"/>.</summary>
    public int Code { get; }

    /// <summary>Gets the error's <c>data</c>, when the server sent any.</summary>
    public JsonElement? ErrorData { get; }
}
