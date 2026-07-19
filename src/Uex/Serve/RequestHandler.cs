using System.Text.Json;
using System.Text.Json.Nodes;

namespace Uex.Serve;

/// <summary>JSON-lines envelope: parse request, dispatch to the executor, wrap result/error. Never throws.</summary>
public sealed class RequestHandler(Func<string, string?, JsonNode?, JsonNode?> execute)
{
    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };

    public string Handle(string requestLine)
    {
        JsonNode? id = null;
        try
        {
            JsonNode? request;
            try { request = JsonNode.Parse(requestLine); }
            catch (JsonException e) { throw new UexException($"Invalid JSON request: {e.Message}"); }
            id = request?["id"]?.DeepClone();
            var cmd = request?["cmd"]?.GetValue<string>()
                ?? throw new UexException("Request is missing 'cmd'.");
            var profile = request?["profile"]?.GetValue<string>();
            var result = execute(cmd, profile, request?["args"]);
            return new JsonObject { ["id"] = id, ["ok"] = true, ["result"] = result }
                .ToJsonString(Compact);
        }
        catch (Exception e)
        {
            var message = e is UexException ? e.Message : e.ToString().ReplaceLineEndings(" | ");
            return new JsonObject { ["id"] = id?.DeepClone(), ["ok"] = false, ["error"] = message }
                .ToJsonString(Compact);
        }
    }
}
