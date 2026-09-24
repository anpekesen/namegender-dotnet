using System;
using System.Text;
using Xunit;

namespace NameGender.Tests;

public class WebhookTests
{
    // Shared with the other SDKs and the API docs: every implementation must accept it.
    private const string Secret = "whsec_test_vector";
    private const string Body = """{"id":"evt_1","type":"webhook.test"}""";
    private const string Signature = "857fcddfea47617c448b7a8e6537bbd59c9922a37c5273b2709812fbadb29e50";
    private const string Header = "t=1700000000,v1=" + Signature;
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1700000000);

    [Fact]
    public void The_shared_vector_verifies()
    {
        var evt = NameGenderWebhooks.Verify(Encoding.UTF8.GetBytes(Body), Header, Secret, now: Now);

        Assert.Equal("evt_1", evt.Id);
        Assert.Equal("webhook.test", evt.Type);
        Assert.Equal("evt_1", NameGenderWebhooks.Verify(Body, Header, Secret, now: Now).Id);
    }

    [Fact]
    public void Any_v1_may_match_during_a_rotation()
    {
        var header = "t=1700000000,v1=" + new string('0', 64) + ",v1=" + Signature;

        Assert.Equal("evt_1", NameGenderWebhooks.Verify(Body, header, Secret, now: Now).Id);
    }

    [Theory]
    [InlineData("""{"id":"evt_2","type":"webhook.test"}""", Header, Secret, 0)]
    [InlineData(Body, Header, "whsec_wrong", 0)]
    [InlineData(Body, Header, Secret, 301)]
    [InlineData(Body, Header, Secret, -301)]
    [InlineData(Body, null, Secret, 0)]
    [InlineData(Body, "", Secret, 0)]
    [InlineData(Body, "t=abc,v1=", Secret, 0)]
    [InlineData(Body, "v1=" + Signature, Secret, 0)]
    public void Anything_else_is_refused(string body, string? header, string secret, int skew)
    {
        var error = Assert.Throws<WebhookVerificationException>(
            () => NameGenderWebhooks.Verify(body, header, secret, now: Now.AddSeconds(skew)));

        Assert.IsAssignableFrom<NameGenderException>(error);
    }

    [Fact]
    public void Typed_helpers_read_the_object()
    {
        var batch = """{"id":"evt_3","type":"batch.completed","created_at":"2026-09-24T10:00:00Z","api_version":"v1","data":{"object":{"id":"B-1","status":"completed","summary":{"male":1,"female":2,"unknown":0,"from_llm":0}}}}""";
        var credits = """{"id":"evt_4","type":"credits.low","created_at":"2026-09-24T10:00:00Z","api_version":"v1","data":{"object":{"kind":"credits_low","credits_remaining":40,"purchased":40,"subscription":0,"daily_burn":20,"runway_days":2,"since":"2026-09-24T09:00:00Z"}}}""";

        var batchEvent = NameGenderWebhooks.Verify(batch, Sign(batch), Secret, now: Now);
        var creditsEvent = NameGenderWebhooks.Verify(credits, Sign(credits), Secret, now: Now);

        Assert.Equal(2, batchEvent.AsBatchJob()!.Summary!.Female);
        Assert.Null(batchEvent.AsCreditsAlert());
        Assert.Equal(2, creditsEvent.AsCreditsAlert()!.RunwayDays);
        Assert.Null(creditsEvent.AsBatchJob());
    }

    private static string Sign(string body)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var mac = hmac.ComputeHash(Encoding.UTF8.GetBytes("1700000000." + body));
        return "t=1700000000,v1=" + Convert.ToHexString(mac).ToLowerInvariant();
    }
}
