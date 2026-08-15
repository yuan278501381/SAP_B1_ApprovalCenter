using System.Net;
using System.Text;
using Approval.SapAdapter.Adapters;
using Approval.SapAdapter.ServiceLayer;
using FluentAssertions;
using Xunit;

namespace Approval.Domain.Tests;

public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

    public MockHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
    {
        _handler = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return _handler(request);
    }
}

public class ServiceLayerClientTests
{
    private readonly ServiceLayerOptions _options = new()
    {
        BaseUrl = "https://127.0.0.1:50000/b1s/v2",
        CompanyDb = "DB_KCC",
        UserName = "manager",
        Password = "password123",
        MirrorEnabled = true,
        Objects = new()
        {
            new ServiceLayerObjectOptions
            {
                ObjectCode = "CHORDR",
                EntitySet = "CHORDR",
                KeyType = "Number",
                TitleField = "U_CardName",
                DocTotalField = "U_DocTotal",
                CreatorCodeField = "Creator",
                LineCollection = "CH_ORDR_1Collection",
                StatusField = "U_APStatus",
                InstanceIdField = "U_APInstance",
                HashField = "U_APHash"
            }
        }
    };

    [Fact]
    public async Task ServiceLayerClient_ShouldLogin_AndFetchRawDocument()
    {
        var loginHit = false;
        var fetchHit = false;

        var handler = new MockHttpMessageHandler(async req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("Login"))
            {
                loginHit = true;
                var res = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"SessionId": "sess_123"}""", Encoding.UTF8, "application/json")
                };
                res.Headers.Add("Set-Cookie", "B1SESSION=sess_123; ROUTEID=.node1");
                return res;
            }

            if (req.RequestUri.AbsolutePath.Contains("CHORDR(1001)"))
            {
                fetchHit = true;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                        "DocEntry": 1001,
                        "U_CardName": "测试客户",
                        "U_DocTotal": 58000.0,
                        "Creator": "SALES01",
                        "CH_ORDR_1Collection": [
                            {"LineId": 1, "U_ItemCode": "A001", "U_LineTotal": 58000.0}
                        ]
                    }
                    """, Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var client = new ServiceLayerClient(_options, handler);
        var adapter = new ServiceLayerObjectAdapter(client, _options.Objects[0]);

        var payload = await adapter.FetchObjectAsync("DB_KCC", "1001");

        loginHit.Should().BeTrue();
        fetchHit.Should().BeTrue();
        payload.Should().NotBeNull();
        payload.DocTotal.Should().Be(58000.0m);
        payload.Title.Should().Be("测试客户");
        payload.LineRows.Should().HaveCount(1);
    }

    [Fact]
    public async Task ServiceLayerClient_PatchMirror_ShouldSendCorrectPayload()
    {
        var patchHit = false;

        var handler = new MockHttpMessageHandler(async req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("Login"))
            {
                var res = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"SessionId": "sess_123"}""", Encoding.UTF8, "application/json")
                };
                res.Headers.Add("Set-Cookie", "B1SESSION=sess_123");
                return res;
            }

            if (req.Method == HttpMethod.Patch && req.RequestUri.AbsolutePath.Contains("CHORDR(1001)"))
            {
                patchHit = true;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = new ServiceLayerClient(_options, handler);
        var adapter = new ServiceLayerObjectAdapter(client, _options.Objects[0]);

        var result = await adapter.WriteApprovalMirrorAsync("DB_KCC", "1001", "Approved", "INST_01", "hash_123");
        result.Should().BeTrue();
        patchHit.Should().BeTrue();
    }

    [Fact]
    public async Task ServiceLayerClient_ShouldAutoRelogin_WhenReceives401()
    {
        var loginAttempts = 0;
        var requestAttempts = 0;

        var handler = new MockHttpMessageHandler(async req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("Login"))
            {
                loginAttempts++;
                var res = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"SessionId": "sess_fresh"}""", Encoding.UTF8, "application/json")
                };
                res.Headers.Add("Set-Cookie", "B1SESSION=sess_fresh");
                return res;
            }

            requestAttempts++;
            if (requestAttempts == 1)
            {
                // 第一次请求返回 401 过期
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }

            // 重新登录后返回成功
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"DocEntry": 1002, "U_CardName": "401恢复客户"}""", Encoding.UTF8, "application/json")
            };
        });

        using var client = new ServiceLayerClient(_options, handler);
        var raw = await client.GetRawAsync(_options.Objects[0], "1002", CancellationToken.None);

        loginAttempts.Should().Be(2); // 初始登录 + 401 自动重连
        raw.Should().Contain("401恢复客户");
    }

    [Fact]
    public async Task ServiceLayerClient_SaveDraftToDocument_ShouldReturnPostedDocEntry()
    {
        var handler = new MockHttpMessageHandler(async req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("Login"))
            {
                var res = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"SessionId": "sess_123"}""", Encoding.UTF8, "application/json")
                };
                res.Headers.Add("Set-Cookie", "B1SESSION=sess_123");
                return res;
            }

            if (req.RequestUri.AbsolutePath.Contains("DraftsService_SaveDraftToDocument"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"DocEntry": 8888, "DocNum": 9999}""", Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = new ServiceLayerClient(_options, handler);
        var (entry, num) = await client.SaveDraftToDocumentAsync("101", CancellationToken.None);

        entry.Should().Be("8888");
        num.Should().Be("9999");
    }
}
