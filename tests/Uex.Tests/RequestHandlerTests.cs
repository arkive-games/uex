using System.Text.Json.Nodes;
using Uex;
using Uex.Serve;
using Xunit;

namespace Uex.Tests;

public class RequestHandlerTests
{
    private static readonly RequestHandler Handler = new((cmd, profile, args) => cmd switch
    {
        "echo" => JsonValue.Create($"{profile}:{args?["x"]?.GetValue<string>()}"),
        "boom" => throw new UexException("kaboom"),
        _ => throw new UexException($"Unknown command '{cmd}'"),
    });

    [Fact]
    public void Dispatches_and_wraps_result()
    {
        var response = Handler.Handle("""{"id":7,"cmd":"echo","profile":"aion2","args":{"x":"hi"}}""");
        var node = JsonNode.Parse(response)!;
        Assert.Equal(7, node["id"]!.GetValue<int>());
        Assert.True(node["ok"]!.GetValue<bool>());
        Assert.Equal("aion2:hi", node["result"]!.GetValue<string>());
    }

    [Fact]
    public void UexException_becomes_error_response_with_id()
    {
        var node = JsonNode.Parse(Handler.Handle("""{"id":8,"cmd":"boom","profile":"p"}"""))!;
        Assert.Equal(8, node["id"]!.GetValue<int>());
        Assert.False(node["ok"]!.GetValue<bool>());
        Assert.Equal("kaboom", node["error"]!.GetValue<string>());
    }

    [Fact]
    public void Malformed_json_is_error_with_null_id()
    {
        var node = JsonNode.Parse(Handler.Handle("not json"))!;
        Assert.False(node["ok"]!.GetValue<bool>());
        Assert.Null(node["id"]);
        Assert.Contains("JSON", node["error"]!.GetValue<string>());
    }

    [Fact]
    public void Missing_cmd_is_error()
    {
        var node = JsonNode.Parse(Handler.Handle("""{"id":9}"""))!;
        Assert.False(node["ok"]!.GetValue<bool>());
        Assert.Contains("cmd", node["error"]!.GetValue<string>());
    }

    [Fact]
    public void Response_is_single_line()
    {
        var response = Handler.Handle("""{"id":1,"cmd":"echo","profile":"p","args":{"x":"a\nb"}}""");
        Assert.DoesNotContain('\n', response);
    }
}
