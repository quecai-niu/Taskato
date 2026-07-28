using System.Text.Json;

namespace Taskato.Services.WebBridge
{
    public sealed class WebBridgeRequest
    {
        public string Id { get; init; } = string.Empty;
        public string Method { get; init; } = string.Empty;
        public JsonElement Params { get; init; }
    }

    public sealed record WebBridgeResponse(string Id, bool Ok, object? Result, WebBridgeError? Error)
    {
        public static WebBridgeResponse Success(string id, object? result) => new(id, true, result, null);
        public static WebBridgeResponse Failure(string id, string code, string message) =>
            new(id, false, null, new WebBridgeError(code, message));
    }

    public sealed record WebBridgeError(string Code, string Message);
    public sealed record WebBridgeEvent(string Event, object? Payload);

    public sealed class BridgeRequestException : Exception
    {
        public string Code { get; }

        public BridgeRequestException(string code, string message) : base(message) => Code = code;
    }
}
