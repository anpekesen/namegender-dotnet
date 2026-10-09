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

public class AgeTests
{
    private const string Brittany = """
        {"name":"Brittany","first_name":"Brittany","gender":null,
         "age":36,"age_range":{"low":32,"high":38},"age_range_80":{"low":28,"high":41},
         "birth_year":1990,"sample_size":353775,"births":361434,
         "country":"US","country_source":"default",
         "source":"ssa","series":"1880-2024","reference_year":2026,"reason":null}
        """;

    private const string Yuki = """
        {"name":"Yuki","first_name":"Yuki","gender":null,
         "age":null,"age_range":null,"age_range_80":null,
         "birth_year":null,"sample_size":0,"births":0,
         "country":"JP","country_source":"country",
         "source":null,"series":null,"reference_year":2026,"reason":"country_not_covered"}
        """;

    private static string Single(string item, int charged = 1)
        => "{\"credits_charged\":" + charged + ",\"credits_remaining\":49999,\"request_id\":\"req_1\"," + item.Trim().Substring(1);

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
        var (client, handler) = Client(HttpStatusCode.OK, Single(Brittany));

        await client.AgeAsync("Brittany");

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://example.test/api/v1/age", handler.Request.RequestUri!.ToString());
        Assert.Equal(new[] { "name" }, Fields(handler));
        Assert.Equal("Brittany", Sent(handler).GetProperty("name").GetString());

        await client.AgeAsync("Brittany", new AgeOptions { Gender = "", Country = null, Locale = "" });
        Assert.Equal(new[] { "name" }, Fields(handler));

        await Assert.ThrowsAsync<ArgumentNullException>(() => client.AgeAsync(null!));
    }

    [Fact]
    public async Task Sends_every_option_under_its_api_name()
    {
        var (client, handler) = Client(HttpStatusCode.OK, Single(Brittany));

        await client.AgeAsync("Brittany", new AgeOptions { Gender = "female", Country = "US", Locale = "en-US", Ip = "203.0.113.7" });

        var sent = Sent(handler);
        Assert.Equal(new[] { "name", "gender", "country", "locale", "ip" }, Fields(handler));
        Assert.Equal("female", sent.GetProperty("gender").GetString());
        Assert.Equal("US", sent.GetProperty("country").GetString());
        Assert.Equal("en-US", sent.GetProperty("locale").GetString());
        Assert.Equal("203.0.113.7", sent.GetProperty("ip").GetString());
    }

    [Fact]
    public async Task Reads_a_full_result_with_both_ranges()
    {
        var (client, _) = Client(HttpStatusCode.OK, Single(Brittany));

        var result = await client.AgeAsync("Brittany");

        Assert.Equal("Brittany", result.Name);
        Assert.Equal("Brittany", result.FirstName);
        Assert.Null(result.Gender);
        Assert.Equal(36, result.Age);
        Assert.True(result.IsKnown);
        Assert.Equal(32, result.AgeRange!.Low);
        Assert.Equal(38, result.AgeRange.High);
        Assert.Equal(28, result.AgeRange80!.Low);
        Assert.Equal(41, result.AgeRange80.High);
        Assert.Equal(1990, result.BirthYear);
        Assert.Equal(353775, result.SampleSize);
        Assert.Equal(361434, result.Births);
        Assert.Equal("US", result.Country);
        Assert.Equal("default", result.CountrySource);
        Assert.Equal("ssa", result.Source);
        Assert.Equal("1880-2024", result.Series);
        Assert.Equal(2026, result.ReferenceYear);
        Assert.Null(result.Reason);
        Assert.Equal(1, result.CreditsCharged);
        Assert.Equal(49999, result.CreditsRemaining);
        Assert.Equal("req_1", result.RequestId);
    }

    [Fact]
    public async Task Reads_a_country_that_is_not_covered_as_an_answer()
    {
        var (client, _) = Client(HttpStatusCode.OK, Single(Yuki, charged: 0));

        var result = await client.AgeAsync("Yuki", new AgeOptions { Country = "JP" });

        Assert.Null(result.Age);
        Assert.False(result.IsKnown);
        Assert.Null(result.AgeRange);
        Assert.Null(result.AgeRange80);
        Assert.Null(result.BirthYear);
        Assert.Null(result.Source);
        Assert.Null(result.Series);
        Assert.Equal("country_not_covered", result.Reason);
        Assert.Equal("JP", result.Country);
        Assert.Equal("country", result.CountrySource);
        Assert.Equal(0, result.CreditsCharged);
    }

    [Fact]
    public async Task Bulk_sends_the_list_and_keeps_the_order()
    {
        var (client, handler) = Client(HttpStatusCode.OK,
            """{"credits_charged":1,"credits_remaining":49998,"request_id":"req_2","country_source":"country","results":["""
            + Brittany + "," + Yuki + "]}");

        var result = await client.AgeBulkAsync(new[] { "Brittany", "Yuki" }, new AgeOptions { Gender = "female", Country = "US" });

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://example.test/api/v1/age/bulk", handler.Request.RequestUri!.ToString());
        Assert.Equal(new[] { "names", "gender", "country" }, Fields(handler));
        var names = Sent(handler).GetProperty("names").EnumerateArray().Select(n => n.GetString()).ToArray();
        Assert.Equal(new[] { "Brittany", "Yuki" }, names);

        Assert.Equal(new[] { "Brittany", "Yuki" }, result.Results.Select(r => r.Name).ToArray());
        Assert.Equal(36, result.Results[0].Age);
        Assert.Equal(38, result.Results[0].AgeRange!.High);
        Assert.Null(result.Results[1].Age);
        Assert.Equal("country_not_covered", result.Results[1].Reason);
        Assert.Equal(1, result.CreditsCharged);
        Assert.Equal(49998, result.CreditsRemaining);
        Assert.Equal("req_2", result.RequestId);
        Assert.Null(result.DataVersion);
        Assert.Equal("country", result.CountrySource);

        await Assert.ThrowsAsync<ArgumentNullException>(() => client.AgeBulkAsync(null!));
    }

    [Fact]
    public async Task An_api_error_is_thrown()
    {
        var (client, _) = Client((HttpStatusCode)422,
            """{"error":"invalid_gender","message":"gender must be male or female.","request_id":"req_3"}""");

        var error = await Assert.ThrowsAsync<NameGenderException>(() => client.AgeAsync("Brittany", new AgeOptions { Gender = "x" }));

        Assert.Equal(422, error.StatusCode);
        Assert.Equal("invalid_gender", error.Code);
        Assert.Equal("gender must be male or female.", error.Message);
        Assert.Equal("req_3", error.RequestId);
    }
}
