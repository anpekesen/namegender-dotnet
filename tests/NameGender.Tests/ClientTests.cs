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

public class ClientTests
{
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

    [Fact]
    public async Task Name_sends_the_key_and_reads_every_field()
    {
        var (client, handler) = Client(HttpStatusCode.OK, """
            {"query":"Ayşe Yılmaz","name":"Ayşe","first_name":"Ayşe","middle_name":null,"last_name":"Yılmaz",
             "name_type":"personal","gender":"female","probability":99,"sample_size":12345,"country":"TR",
             "took_ms":4,"source":"db","confidence":"high","matched_as":null,
             "credits_charged":1,"credits_remaining":9,"data_version":"2026-09","request_id":"req_1"}
            """);

        var result = await client.NameAsync("Ayşe Yılmaz", new LookupOptions { Country = "TR" });

        Assert.Equal("Bearer secret", handler.Request!.Headers.Authorization!.ToString());
        Assert.Equal("https://example.test/api/v1/gender", handler.Request.RequestUri!.ToString());
        Assert.Equal("Ayşe Yılmaz", Sent(handler).GetProperty("name").GetString());
        Assert.Equal("TR", Sent(handler).GetProperty("country").GetString());

        Assert.Equal("female", result.Gender);
        Assert.True(result.IsKnown);
        Assert.Equal(99, result.Probability);
        Assert.Equal(12345, result.SampleSize);
        Assert.Equal("Yılmaz", result.LastName);
        Assert.Equal("personal", result.NameType);
        Assert.Equal(9, result.CreditsRemaining);
        Assert.Equal("req_1", result.RequestId);
    }

    [Fact]
    public async Task Options_are_sent_only_when_set()
    {
        var (client, handler) = Client(HttpStatusCode.OK, """{"query":"x","gender":null,"source":"none","confidence":"unknown"}""");

        var result = await client.EmailAsync("j.smith@example.com");

        Assert.Equal("https://example.test/api/v1/gender/email", handler.Request!.RequestUri!.ToString());
        Assert.Equal(new[] { "email" }, Sent(handler).EnumerateObject().Select(p => p.Name));
        Assert.False(result.IsKnown);

        await client.UsernameAsync("andrea_89", new LookupOptions { Country = "IT", AiFallback = true, BestGuess = true });

        var sent = Sent(handler);
        Assert.Equal("andrea_89", sent.GetProperty("username").GetString());
        Assert.True(sent.GetProperty("ai_fallback").GetBoolean());
        Assert.True(sent.GetProperty("best_guess").GetBoolean());
    }

    [Fact]
    public async Task Bulk_sends_a_list_and_keeps_the_order()
    {
        var (client, handler) = Client(HttpStatusCode.OK, """
            {"credits_charged":2,"credits_remaining":98,"took_ms":3,
             "summary":{"total":2,"identified":1,"unknown":1,"match_rate":50},
             "results":[{"query":"Wei","gender":"male","probability":80,"source":"db","confidence":"medium"},
                        {"query":"zz","gender":null,"source":"none","confidence":"unknown"}]}
            """);

        // Tek elemanlı liste: bazı istemcilerde tek isim dizi yerine metin olarak gidiyordu.
        var result = await client.BulkAsync(new[] { "Wei", "zz" }, type: InputType.Username);

        var names = Sent(handler).GetProperty("names");
        Assert.Equal(JsonValueKind.Array, names.ValueKind);
        Assert.Equal(2, names.GetArrayLength());
        Assert.Equal("username", Sent(handler).GetProperty("type").GetString());

        Assert.Equal(new[] { "Wei", "zz" }, result.Results.Select(r => r.Query));
        Assert.Equal(50, result.Summary.MatchRate);
        Assert.Equal(98, result.CreditsRemaining);

        await client.BulkAsync(new[] { "Ayşe" });
        Assert.Equal(JsonValueKind.Array, Sent(handler).GetProperty("names").ValueKind);
        Assert.False(Sent(handler).TryGetProperty("type", out _));
    }

    [Fact]
    public async Task Countries_reads_the_basis_and_nullable_gender()
    {
        var (client, handler) = Client(HttpStatusCode.OK, """
            {"took_ms":12,"name":"Mehmet","basis":{"counted_sources":["insee","ons"],"counted_countries":2,"attested_countries":3,"note":"n"},
             "registrations":[{"country":"FR","count":3775,"share":58.97,"gender":"male","probability":99,"source":"insee"},
                              {"country":"GB","count":1130,"share":17.65,"gender":null,"probability":0,"source":"ons"}],
             "attested_in":["FR","GB","TR"]}
            """);

        var result = await client.CountriesAsync("Mehmet", limit: 10);

        Assert.Equal(10, Sent(handler).GetProperty("limit").GetInt32());
        Assert.Equal(2, result.Registrations.Count);
        Assert.Equal(58.97, result.Registrations[0].Share);
        Assert.Null(result.Registrations[1].Gender);
        Assert.Equal(3, result.Basis.AttestedCountries);
        Assert.Equal(new[] { "FR", "GB", "TR" }, result.AttestedIn);
    }

    [Fact]
    public async Task Account_is_a_get_without_a_body()
    {
        var (client, handler) = Client(HttpStatusCode.OK, """{"email":"a@b.c","credits_remaining":5,"free_today":100,"free_daily_limit":100}""");

        var account = await client.AccountAsync();

        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
        Assert.Null(handler.RequestBody);
        Assert.Equal(100, account.FreeDailyLimit);
    }

    [Fact]
    public async Task Errors_throw_with_the_code_and_request_id()
    {
        var (client, _) = Client(HttpStatusCode.PaymentRequired,
            """{"error":"no_credits","message":"Out of credits.","request_id":"req_9","docs":"https://namegender.com/docs#error-no_credits"}""");

        var error = await Assert.ThrowsAsync<NameGenderException>(() => client.NameAsync("Andrea"));

        Assert.Equal(402, error.StatusCode);
        Assert.Equal("no_credits", error.Code);
        Assert.Equal("Out of credits.", error.Message);
        Assert.Equal("req_9", error.RequestId);
    }

    [Fact]
    public async Task A_non_json_error_keeps_the_raw_body()
    {
        var (client, _) = Client(HttpStatusCode.BadGateway, "<html>bad gateway</html>");

        var error = await Assert.ThrowsAsync<NameGenderException>(() => client.NameAsync("Andrea"));

        Assert.Equal(502, error.StatusCode);
        Assert.Null(error.Code);
        Assert.Contains("bad gateway", error.RawBody);
    }

    [Fact]
    public async Task Rate_limit_carries_retry_after()
    {
        var (client, _) = Client((HttpStatusCode)429, """{"error":"rate_limited","message":"Slow down.","retry_after":7}""");

        var error = await Assert.ThrowsAsync<NameGenderException>(() => client.NameAsync("Andrea"));

        Assert.Equal(TimeSpan.FromSeconds(7), error.RetryAfter);
    }

    [Fact]
    public void An_empty_key_is_refused_before_any_request()
    {
        Assert.Throws<ArgumentException>(() => new NameGenderClient(" "));
    }
}
