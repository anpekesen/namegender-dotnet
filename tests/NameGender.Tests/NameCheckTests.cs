using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NameGender.Tests;

public class NameCheckTests
{
    private const string Asdf = """
        {"query":"asdf qwerty","assessment":"implausible","score":0,
         "signals":[{"code":"keyboard_pattern","severity":"high","part":"first_name","value":"asdf"},
                    {"code":"keyboard_pattern","severity":"high","part":"last_name","value":"qwerty"},
                    {"code":"first_name_not_found","severity":"medium","part":null,"value":null}],
         "first_name":"Asdf","last_name":"Qwerty","name_type":"personal",
         "evidence":{"first_name_status":"not_found","first_name_counted_records":0}}
        """;

    private const string Jennifer = """
        {"query":"Jennifer Null","assessment":"plausible","score":96,
         "signals":[{"code":"first_name_attested","severity":"positive","part":"first_name","value":"Jennifer"}],
         "first_name":"Jennifer","last_name":"Null","name_type":"personal",
         "evidence":{"first_name_status":"counted","first_name_counted_records":1468211}}
        """;

    private const string Acme = """
        {"query":"Acme Ltd","assessment":"suspicious","score":40,
         "signals":[{"code":"organization_name","severity":"medium","part":"full","value":null}],
         "first_name":null,"last_name":null,"name_type":"organization",
         "evidence":{"first_name_status":null,"first_name_counted_records":0}}
        """;

    private static string Single(string item)
        => """{"credits_charged":1,"credits_remaining":4999,"data_version":"2026.10","request_id":"req_1","country_source":"country",""" + item.Trim().Substring(1);

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        public FakeHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") };
        }
    }

    private static (NameGenderClient, FakeHandler) Client(HttpStatusCode status, string body)
    {
        var handler = new FakeHandler(status, body);
        return (new NameGenderClient("secret", new HttpClient(handler), "https://example.test/api/v1/"), handler);
    }

    private static JsonElement Sent(FakeHandler handler) => JsonDocument.Parse(handler.RequestBody!).RootElement;

    private static string[] Fields(FakeHandler handler) => Sent(handler).EnumerateObject().Select(p => p.Name).ToArray();

    [Fact]
    public async Task Sends_only_the_name_when_nothing_is_set()
    {
        var (client, handler) = Client(HttpStatusCode.OK, Single(Asdf));

        await client.NameCheckAsync("asdf qwerty");

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://example.test/api/v1/name-check", handler.Request.RequestUri!.ToString());
        Assert.Equal(new[] { "name" }, Fields(handler));

        await client.NameCheckAsync("asdf qwerty", new NameCheckOptions { Country = "", Locale = null });
        Assert.Equal(new[] { "name" }, Fields(handler));
    }

    [Fact]
    public async Task Sends_every_option_under_its_api_name()
    {
        var (client, handler) = Client(HttpStatusCode.OK, Single(Asdf));

        await client.NameCheckAsync("asdf qwerty", new NameCheckOptions { Country = "DE", Locale = "de-AT", Ip = "203.0.113.7" });

        var sent = Sent(handler);
        Assert.Equal(new[] { "name", "country", "locale", "ip" }, Fields(handler));
        Assert.Equal("DE", sent.GetProperty("country").GetString());
        Assert.Equal("de-AT", sent.GetProperty("locale").GetString());
        Assert.Equal("203.0.113.7", sent.GetProperty("ip").GetString());
    }

    [Fact]
    public async Task From_parts_sends_first_and_last_name_instead_of_name()
    {
        var (client, handler) = Client(HttpStatusCode.OK, Single(Jennifer));

        await client.NameCheckFromPartsAsync("Jennifer", "Null", new NameCheckOptions { Country = "US" });
        Assert.Equal(new[] { "first_name", "last_name", "country" }, Fields(handler));
        Assert.Equal("Null", Sent(handler).GetProperty("last_name").GetString());

        await client.NameCheckFromPartsAsync("Jennifer", null);
        Assert.Equal(new[] { "first_name" }, Fields(handler));

        await Assert.ThrowsAsync<ArgumentException>(() => client.NameCheckFromPartsAsync(null, null));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.NameCheckAsync(null!));
    }

    [Fact]
    public async Task Reads_an_implausible_name()
    {
        var (client, _) = Client(HttpStatusCode.OK, Single(Asdf));

        var result = await client.NameCheckAsync("asdf qwerty");

        Assert.Equal("asdf qwerty", result.Query);
        Assert.Equal("implausible", result.Assessment);
        Assert.Equal(0, result.Score);
        Assert.Equal(3, result.Signals.Count);
        Assert.Equal("keyboard_pattern", result.Signals[0].Code);
        Assert.Equal("high", result.Signals[0].Severity);
        Assert.Equal("first_name", result.Signals[0].Part);
        Assert.Equal("asdf", result.Signals[0].Value);
        Assert.Equal("qwerty", result.Signals[1].Value);
        Assert.Equal("first_name_not_found", result.Signals[2].Code);
        Assert.Null(result.Signals[2].Part);
        Assert.Null(result.Signals[2].Value);
        Assert.Equal("Asdf", result.FirstName);
        Assert.Equal("Qwerty", result.LastName);
        Assert.Equal("personal", result.NameType);
        Assert.Equal("not_found", result.Evidence.FirstNameStatus);
        Assert.Equal(0, result.Evidence.FirstNameCountedRecords);
        Assert.Equal("country", result.CountrySource);
        Assert.Equal(1, result.CreditsCharged);
        Assert.Equal(4999, result.CreditsRemaining);
        Assert.Equal("2026.10", result.DataVersion);
        Assert.Equal("req_1", result.RequestId);
    }

    [Fact]
    public async Task Reads_a_plausible_name_and_null_evidence()
    {
        var (client, _) = Client(HttpStatusCode.OK, Single(Jennifer));
        var jennifer = await client.NameCheckAsync("Jennifer Null");

        Assert.Equal("plausible", jennifer.Assessment);
        Assert.Equal(96, jennifer.Score);
        Assert.Equal("positive", jennifer.Signals[0].Severity);
        Assert.Equal("counted", jennifer.Evidence.FirstNameStatus);
        Assert.Equal(1468211, jennifer.Evidence.FirstNameCountedRecords);

        var (other, _) = Client(HttpStatusCode.OK, Single(Acme));
        var acme = await other.NameCheckAsync("Acme Ltd");

        Assert.Equal("organization", acme.NameType);
        Assert.Null(acme.FirstName);
        Assert.Null(acme.LastName);
        Assert.Null(acme.Evidence.FirstNameStatus);
        Assert.Equal("full", acme.Signals[0].Part);
        Assert.Null(acme.Signals[0].Value);
    }

    [Fact]
    public async Task Bulk_sends_the_list_and_keeps_the_order()
    {
        var (client, handler) = Client(HttpStatusCode.OK,
            """{"credits_charged":3,"credits_remaining":4996,"data_version":"2026.10","request_id":"req_2","took_ms":4,"country_source":null,"summary":{"total":3,"plausible":1,"suspicious":1,"implausible":1},"results":["""
            + Jennifer + "," + Acme + "," + Asdf + "]}");

        var result = await client.NameCheckBulkAsync(new[] { "Jennifer Null", "Acme Ltd", "asdf qwerty" }, new NameCheckOptions { Locale = "en-US" });

        Assert.Equal("https://example.test/api/v1/name-check/bulk", handler.Request!.RequestUri!.ToString());
        Assert.Equal(new[] { "names", "locale" }, Fields(handler));
        var names = Sent(handler).GetProperty("names").EnumerateArray().Select(n => n.GetString()).ToArray();
        Assert.Equal(new[] { "Jennifer Null", "Acme Ltd", "asdf qwerty" }, names);

        Assert.Equal(new[] { "Jennifer Null", "Acme Ltd", "asdf qwerty" }, result.Results.Select(r => r.Query).ToArray());
        Assert.Equal(new[] { "plausible", "suspicious", "implausible" }, result.Results.Select(r => r.Assessment).ToArray());
        Assert.Equal(3, result.Summary.Total);
        Assert.Equal(1, result.Summary.Plausible);
        Assert.Equal(1, result.Summary.Suspicious);
        Assert.Equal(1, result.Summary.Implausible);
        Assert.Equal(3, result.CreditsCharged);
        Assert.Equal(4996, result.CreditsRemaining);
        Assert.Equal("req_2", result.RequestId);
        Assert.Null(result.CountrySource);
    }

    [Fact]
    public async Task An_api_error_is_thrown()
    {
        var (client, _) = Client((HttpStatusCode)402,
            """{"error":"no_credits","message":"No credits left.","request_id":"req_3"}""");

        var error = await Assert.ThrowsAsync<NameGenderException>(() => client.NameCheckAsync("Jennifer Null"));

        Assert.Equal(402, error.StatusCode);
        Assert.Equal("no_credits", error.Code);
        Assert.Equal("No credits left.", error.Message);
        Assert.Equal("req_3", error.RequestId);
    }
}
