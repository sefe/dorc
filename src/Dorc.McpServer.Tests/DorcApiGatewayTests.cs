using System.Net;
using System.Text;
using Dorc.ApiModel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dorc.McpServer.Tests;

[TestClass]
public sealed class DorcApiGatewayTests
{
    [TestMethod]
    public void McpToolOutcome_Properties_WorkAsExpected()
    {
        var ok = McpToolOutcome.Success("val", 201);
        Assert.IsTrue(ok.Succeeded);
        Assert.AreEqual("val", ok.Data);
        Assert.IsNull(ok.Error);
        Assert.AreEqual(201, ok.HttpStatusCode);

        var fail = McpToolOutcome.Failure<string>("boom", 500);
        Assert.IsFalse(fail.Succeeded);
        Assert.IsNull(fail.Data);
        Assert.AreEqual("boom", fail.Error);
        Assert.AreEqual(500, fail.HttpStatusCode);
    }

    [TestMethod]
    public async Task DorcApiGateway_CoversAllHttpVerbsAndResponseShapes()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new DorcApiGateway(null!));

        var mode = "json-ok";
        using var handler = new StubHttpMessageHandler((_, _) =>
        {
            return mode switch
            {
                "json-ok" => Task.FromResult(StubHttpMessageHandler.CreateJsonResponse("{\"Id\":7,\"Status\":\"Complete\"}")),
                "json-invalid" => Task.FromResult(StubHttpMessageHandler.CreateJsonResponse("{not-valid-json")),
                "empty-ok" => Task.FromResult(StubHttpMessageHandler.CreateTextResponse(string.Empty, HttpStatusCode.NoContent)),
                "error-with-body" => Task.FromResult(StubHttpMessageHandler.CreateTextResponse("Bad input payload", HttpStatusCode.BadRequest)),
                "error-empty" => Task.FromResult(StubHttpMessageHandler.CreateTextResponse("   ", HttpStatusCode.Forbidden, "text/plain", "Forbidden")),
                "string-quoted" => Task.FromResult(StubHttpMessageHandler.CreateJsonResponse("\"Line 1\\nLine 2\"")),
                "string-quoted-no-content-type" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Encoding.UTF8.GetBytes("\"NoContentType\""))
                }),
                "string-quoted-invalid-json" => Task.FromResult(StubHttpMessageHandler.CreateJsonResponse("\"unterminated\\x\"")),
                "string-quoted-plaintext" => Task.FromResult(StubHttpMessageHandler.CreateTextResponse("\"C:\\temp\\file.txt\"", HttpStatusCode.OK, "text/plain")),
                "string-null-literal" => Task.FromResult(StubHttpMessageHandler.CreateTextResponse("Plain unquoted text")),
                _ => Task.FromResult(StubHttpMessageHandler.CreateStatusResponse(HttpStatusCode.OK))
            };
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://dorc.local/") };
        var gateway = new DorcApiGateway(httpClient);

        mode = "json-ok";
        var getOk = await gateway.GetAsync<RequestStatusDto>("Request?id=7", CancellationToken.None);
        Assert.IsTrue(getOk.Succeeded);
        Assert.AreEqual(7, getOk.Data!.Id);

        var postOk = await gateway.PostAsync<RequestStatusDto, RequestStatusDto>("Request", new RequestStatusDto { Id = 7 }, CancellationToken.None);
        Assert.IsTrue(postOk.Succeeded);

        var putOk = await gateway.PutAsync<object?, RequestStatusDto>("Request/cancel?requestId=7", null, CancellationToken.None);
        Assert.IsTrue(putOk.Succeeded);

        mode = "json-invalid";
        var invalidJson = await gateway.GetAsync<RequestStatusDto>("Request?id=7", CancellationToken.None);
        Assert.IsFalse(invalidJson.Succeeded);
        StringAssert.Contains(invalidJson.Error!, "Invalid JSON response");

        mode = "empty-ok";
        var emptyJson = await gateway.GetAsync<RequestStatusDto>("Request?id=7", CancellationToken.None);
        Assert.IsTrue(emptyJson.Succeeded);
        Assert.IsNull(emptyJson.Data);

        var emptyStr = await gateway.GetStringAsync("ResultStatuses/Log", CancellationToken.None);
        Assert.IsTrue(emptyStr.Succeeded);
        Assert.AreEqual(string.Empty, emptyStr.Data);

        mode = "error-with-body";
        var errJson = await gateway.GetAsync<RequestStatusDto>("Request", CancellationToken.None);
        Assert.IsFalse(errJson.Succeeded);
        StringAssert.Contains(errJson.Error!, "Bad input payload");

        var errStr = await gateway.GetStringAsync("ResultStatuses/Log", CancellationToken.None);
        Assert.IsFalse(errStr.Succeeded);
        StringAssert.Contains(errStr.Error!, "Bad input payload");

        mode = "error-empty";
        var errEmptyJson = await gateway.GetAsync<RequestStatusDto>("Request", CancellationToken.None);
        Assert.IsFalse(errEmptyJson.Succeeded);
        StringAssert.Contains(errEmptyJson.Error!, "HTTP 403");

        var errEmptyStr = await gateway.GetStringAsync("ResultStatuses/Log", CancellationToken.None);
        Assert.IsFalse(errEmptyStr.Succeeded);
        StringAssert.Contains(errEmptyStr.Error!, "HTTP 403");

        mode = "string-quoted";
        var quotedStr = await gateway.GetStringAsync("ResultStatuses/Log", CancellationToken.None);
        Assert.IsTrue(quotedStr.Succeeded);
        Assert.AreEqual("Line 1\nLine 2", quotedStr.Data);

        mode = "string-quoted-no-content-type";
        var quotedNoCtStr = await gateway.GetStringAsync("ResultStatuses/Log", CancellationToken.None);
        Assert.IsTrue(quotedNoCtStr.Succeeded);
        Assert.AreEqual("NoContentType", quotedNoCtStr.Data);

        mode = "string-quoted-invalid-json";
        var badQuotedStr = await gateway.GetStringAsync("ResultStatuses/Log", CancellationToken.None);
        Assert.IsFalse(badQuotedStr.Succeeded);
        StringAssert.Contains(badQuotedStr.Error!, "Invalid JSON string response");

        mode = "string-quoted-plaintext";
        var quotedPlainText = await gateway.GetStringAsync("ResultStatuses/Log", CancellationToken.None);
        Assert.IsTrue(quotedPlainText.Succeeded);
        Assert.AreEqual("\"C:\\temp\\file.txt\"", quotedPlainText.Data);

        mode = "string-null-literal";
        var unquotedStr = await gateway.PostWithoutResponseBodyAsync<object?>("Terraform/plan/1/confirm", null, CancellationToken.None);
        Assert.IsTrue(unquotedStr.Succeeded);
        Assert.AreEqual("Plain unquoted text", unquotedStr.Data);

        var putStr = await gateway.PutStringResponseAsync("DaemonStatus", new DaemonStatusApiModel { ServerName = "srv" }, CancellationToken.None);
        Assert.IsTrue(putStr.Succeeded);
    }
}
