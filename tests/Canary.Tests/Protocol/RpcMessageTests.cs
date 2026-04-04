using System.Text.Json;
using Canary.Agent.Protocol;
using Xunit;

namespace Canary.Tests.Protocol;

public class RpcMessageTests
{
    [Trait("Category", "Unit")]
    [Fact]
    public void RpcMessage_SerializeRequest_RoundTrips()
    {
        var request = new RpcRequest
        {
            Id = 1,
            Method = RpcMethods.Heartbeat,
            Params = new Dictionary<string, JsonElement>()
        };

        var json = RpcSerializer.Serialize(request);
        var deserialized = RpcSerializer.DeserializeRequest(json);

        Assert.Equal("2.0", deserialized.JsonRpc);
        Assert.Equal(1, deserialized.Id);
        Assert.Equal(RpcMethods.Heartbeat, deserialized.Method);
        Assert.NotNull(deserialized.Params);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void RpcMessage_SerializeResponse_WithResult_RoundTrips()
    {
        var response = RpcSerializer.SuccessResponse(1, new { ok = true });

        var json = RpcSerializer.Serialize(response);
        var deserialized = RpcSerializer.DeserializeResponse(json);

        Assert.Equal("2.0", deserialized.JsonRpc);
        Assert.Equal(1, deserialized.Id);
        Assert.NotNull(deserialized.Result);
        Assert.Null(deserialized.Error);

        var result = deserialized.Result!.Value;
        Assert.True(result.GetProperty("ok").GetBoolean());
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void RpcMessage_SerializeError_RoundTrips()
    {
        var response = RpcSerializer.ErrorResponse(1, -1, "timeout");

        var json = RpcSerializer.Serialize(response);
        var deserialized = RpcSerializer.DeserializeResponse(json);

        Assert.Equal(1, deserialized.Id);
        Assert.Null(deserialized.Result);
        Assert.NotNull(deserialized.Error);
        Assert.Equal(-1, deserialized.Error!.Code);
        Assert.Equal("timeout", deserialized.Error.Message);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void RpcMessage_DeserializeInvalid_ThrowsClear()
    {
        Assert.Throws<JsonException>(() => RpcSerializer.DeserializeRequest("{not valid json"));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void RpcMessage_SerializeRequest_WithParams_RoundTrips()
    {
        var paramsDict = new Dictionary<string, JsonElement>
        {
            ["path"] = JsonSerializer.SerializeToElement("test.3dm"),
            ["action"] = JsonSerializer.SerializeToElement("OpenFile")
        };

        var request = new RpcRequest
        {
            Id = 42,
            Method = RpcMethods.Execute,
            Params = paramsDict
        };

        var json = RpcSerializer.Serialize(request);
        var deserialized = RpcSerializer.DeserializeRequest(json);

        Assert.Equal(42, deserialized.Id);
        Assert.Equal(RpcMethods.Execute, deserialized.Method);
        Assert.Equal("test.3dm", deserialized.Params!["path"].GetString());
        Assert.Equal("OpenFile", deserialized.Params["action"].GetString());
    }
}
