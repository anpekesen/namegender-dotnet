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

public class SalutationTests
{
    private const string Anna = """
        {"query":"Dr. Anna Müller","language":"de","form":"gendered","reason":null,
         "salutation":{"formal":"Sehr geehrte Frau Dr. Müller,","informal":"Liebe Anna,","neutral":"Guten Tag Dr. Anna Müller,"},
         "parts":{"opening":"Sehr geehrte","courtesy":"Frau","academic":"Dr.","name":"Müller"},
         "gender":"female","gender_source":"lookup","probability":99,"confidence":"high",
         "first_name":"Anna","last_name":"Müller","name_type":"personal","country":"DE"}
        """;

    private const string Alex = """
        {"query":"Alex Weber","language":"de","form":"neutral","reason":"below_min_probability",
         "salutation":{"formal":"Guten Tag Alex Weber,","informal":"Hallo Alex,","neutral":"Guten Tag Alex Weber,"},
         "parts":{"opening":"Guten Tag","courtesy":null,"academic":null,"name":"Alex Weber"},
         "gender":null,"gender_source":null,"probability":null,"confidence":null,
         "first_name":"Alex","last_name":"Weber","name_type":"personal","country":null}
        """;

    private const string Acme = """
        {"query":"Acme GmbH","language":"de","form":"organization","reason":null,
         "salutation":{"formal":"Sehr geehrte Damen und Herren,","informal":"Hallo zusammen,","neutral":"Sehr geehrte Damen und Herren,"},
         "parts":{"opening":"Sehr geehrte Damen und Herren","courtesy":null,"academic":null,"name":null},
         "gender":null,"gender_source":null,"probability":null,"confidence":null,
         "first_name":null,"last_name":null,"name_type":"organization","country":null}
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
        var (client, handler) = Client(HttpStatusCode.OK, Single(Anna));

        await client.SalutationAsync("Dr. Anna Müller");

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://example.test/api/v1/salutation", handler.Request.RequestUri!.ToString());
        Assert.Equal(new[] { "name" }, Fields(handler));

        await client.SalutationAsync("Anna Müller", new SalutationOptions { Language = "", Title = null, MinProbability = null });
        Assert.Equal(new[] { "name" }, Fields(handler));
    }

    [Fact]
    public async Task Sends_every_option_under_its_api_name()
    {
        var (client, handler) = Client(HttpStatusCode.OK, Single(Anna));

        await client.SalutationAsync("Anna Müller", new SalutationOptions
        {
            Language = "de", Country = "DE", Locale = "de-AT", Ip = "203.0.113.7",
            Gender = "female", MinProbability = 80, Title = "Dr.",
        });

        var sent = Sent(handler);
        Assert.Equal(new[] { "name", "language", "country", "locale", "ip", "gender", "min_probability", "title" }, Fields(handler));
        Assert.Equal(JsonValueKind.Number, sent.GetProperty("min_probability").ValueKind);
        Assert.Equal(80, sent.GetProperty("min_probability").GetInt32());
        Assert.Equal("Dr.", sent.GetProperty("title").GetString());
        Assert.False(sent.TryGetProperty("best_guess", out _));
        Assert.False(sent.TryGetProperty("ai_fallback", out _));
    }

    [Fact]
    public async Task From_parts_sends_first_and_last_name_instead_of_name()
    {
        var (client, handler) = Client(HttpStatusCode.OK, Single(Anna));

        await client.SalutationFromPartsAsync("Ahmet", "Yılmaz", new SalutationOptions { Language = "tr" });
        Assert.Equal(new[] { "first_name", "last_name", "language" }, Fields(handler));
        Assert.Equal("Yılmaz", Sent(handler).GetProperty("last_name").GetString());

        await client.SalutationFromPartsAsync(null, "Müller");
        Assert.Equal(new[] { "last_name" }, Fields(handler));

        await Assert.ThrowsAsync<ArgumentException>(() => client.SalutationFromPartsAsync(null, null));
    }

    [Fact]
    public async Task Reads_a_gendered_salutation_and_the_envelope()
    {
        var (client, _) = Client(HttpStatusCode.OK, Single(Anna));

        var result = await client.SalutationAsync("Dr. Anna Müller", new SalutationOptions { Language = "de" });

        Assert.Equal("Sehr geehrte Frau Dr. Müller,", result.Salutation.Formal);
        Assert.Equal("Liebe Anna,", result.Salutation.Informal);
        Assert.Equal("Guten Tag Dr. Anna Müller,", result.Salutation.Neutral);
        Assert.Equal("gendered", result.Form);
        Assert.Null(result.Reason);
        Assert.Equal("Frau", result.Parts.Courtesy);
        Assert.Equal("Dr.", result.Parts.Academic);
        Assert.Equal("female", result.Gender);
        Assert.Equal("lookup", result.GenderSource);
        Assert.Equal(99, result.Probability);
        Assert.Equal("personal", result.NameType);
        Assert.Equal("DE", result.Country);
        Assert.Equal("country", result.CountrySource);
        Assert.Equal(1, result.CreditsCharged);
        Assert.Equal(4999, result.CreditsRemaining);
        Assert.Equal("2026.10", result.DataVersion);
        Assert.Equal("req_1", result.RequestId);
    }

    [Fact]
    public async Task Reads_a_neutral_salutation_with_null_parts()
    {
        var (client, _) = Client(HttpStatusCode.OK, Single(Alex));

        var result = await client.SalutationAsync("Alex Weber");

        Assert.Equal("neutral", result.Form);
        Assert.Equal("below_min_probability", result.Reason);
        Assert.Equal("Guten Tag Alex Weber,", result.Salutation.Formal);
        Assert.Null(result.Parts.Courtesy);
        Assert.Null(result.Parts.Academic);
        Assert.Null(result.Gender);
        Assert.Null(result.GenderSource);
        Assert.Null(result.Probability);
        Assert.Null(result.Country);
    }

    [Fact]
    public async Task Bulk_sends_a_list_and_keeps_the_order()
    {
        var (client, handler) = Client(HttpStatusCode.OK,
            """{"credits_charged":3,"credits_remaining":4996,"data_version":"2026.10","request_id":"req_2","took_ms":4,"country_source":null,"language":"de","summary":{"total":3,"gendered":1,"neutral":1,"organization":1},"results":["""
            + Anna + "," + Alex + "," + Acme + "]}");

        var result = await client.SalutationBulkAsync(new[] { "Dr. Anna Müller", "Alex Weber", "Acme GmbH" },
            new SalutationOptions { Language = "de", MinProbability = 95 });

        Assert.Equal("https://example.test/api/v1/salutation/bulk", handler.Request!.RequestUri!.ToString());
        Assert.Equal(new[] { "names", "language", "min_probability" }, Fields(handler));
        Assert.Equal(JsonValueKind.Array, Sent(handler).GetProperty("names").ValueKind);
        Assert.Equal(3, Sent(handler).GetProperty("names").GetArrayLength());

        Assert.Equal(new[] { "Dr. Anna Müller", "Alex Weber", "Acme GmbH" }, result.Results.Select(r => r.Query));
        Assert.Equal(new[] { "gendered", "neutral", "organization" }, result.Results.Select(r => r.Form));
        Assert.Null(result.Results[2].Parts.Name);
        Assert.Equal(3, result.Summary.Total);
        Assert.Equal(1, result.Summary.Gendered);
        Assert.Equal(1, result.Summary.Neutral);
        Assert.Equal(1, result.Summary.Organization);
        Assert.Equal("de", result.Language);
        Assert.Equal(3, result.CreditsCharged);
        Assert.Equal(4, result.TookMs);
        Assert.Null(result.CountrySource);
    }

    [Fact]
    public async Task An_unsupported_language_throws_the_api_error()
    {
        var (client, _) = Client((HttpStatusCode)422,
            """{"error":"invalid_input","message":"Unsupported language.","field":"language","supported":["en","de","tr"],"request_id":"req_3"}""");

        var error = await Assert.ThrowsAsync<NameGenderException>(
            () => client.SalutationAsync("Anna Müller", new SalutationOptions { Language = "xx" }));

        Assert.Equal(422, error.StatusCode);
        Assert.Equal("invalid_input", error.Code);
        Assert.Equal("Unsupported language.", error.Message);
        Assert.Equal("req_3", error.RequestId);
        Assert.Contains("\"supported\"", error.RawBody);
    }
}
