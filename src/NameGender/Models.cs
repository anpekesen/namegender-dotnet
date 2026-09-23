using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NameGender
{
    /// <summary>
    /// Options for <see cref="NameGenderClient.NameAsync"/>, the email and username
    /// lookups and <see cref="NameGenderClient.BulkAsync"/>.
    /// </summary>
    public sealed class LookupOptions
    {
        /// <summary>ISO 3166-1 alpha-2 country hint, e.g. "IT". Andrea is male in Italy and female in Germany.</summary>
        public string? Country { get; set; }

        /// <summary>
        /// Fall back to a language model for names not in the database. Needs AI consent
        /// on the account; the API answers <c>ai_consent_required</c> otherwise.
        /// </summary>
        public bool AiFallback { get; set; }

        /// <summary>Return the most likely gender even below the probability threshold.</summary>
        public bool BestGuess { get; set; }
    }

    /// <summary>Kind of value sent to <see cref="NameGenderClient.BulkAsync"/>.</summary>
    public enum InputType
    {
        /// <summary>First or full names.</summary>
        Name,
        /// <summary>Email addresses.</summary>
        Email,
        /// <summary>Usernames.</summary>
        Username,
    }

    /// <summary>Fields every successful response carries.</summary>
    public abstract class Envelope
    {
        /// <summary>Credits this request used.</summary>
        [JsonPropertyName("credits_charged")] public int CreditsCharged { get; set; }

        /// <summary>Credits left on the account after this request.</summary>
        [JsonPropertyName("credits_remaining")] public int CreditsRemaining { get; set; }

        /// <summary>Version of the name data that answered.</summary>
        [JsonPropertyName("data_version")] public string? DataVersion { get; set; }

        /// <summary>Quote this to support.</summary>
        [JsonPropertyName("request_id")] public string? RequestId { get; set; }

        /// <summary>Whole milliseconds spent on our side, network time excluded.</summary>
        [JsonPropertyName("took_ms")] public int TookMs { get; set; }
    }

    /// <summary>One lookup result.</summary>
    public class GenderResult
    {
        /// <summary>The value you sent, unchanged.</summary>
        [JsonPropertyName("query")] public string Query { get; set; } = "";

        /// <summary>The first name that was looked up.</summary>
        [JsonPropertyName("name")] public string? Name { get; set; }

        [JsonPropertyName("first_name")] public string? FirstName { get; set; }
        [JsonPropertyName("middle_name")] public string? MiddleName { get; set; }
        [JsonPropertyName("last_name")] public string? LastName { get; set; }

        /// <summary><c>personal</c>, <c>organization</c> or <c>role</c>; null when the input could not be read.</summary>
        [JsonPropertyName("name_type")] public string? NameType { get; set; }

        /// <summary><c>male</c>, <c>female</c> or null. Null is an answer: keep it nullable in your own model.</summary>
        [JsonPropertyName("gender")] public string? Gender { get; set; }

        /// <summary>0 to 100. Not a fraction.</summary>
        [JsonPropertyName("probability")] public int Probability { get; set; }

        /// <summary>How many recorded people the answer rests on. 0 when the source has no counts.</summary>
        [JsonPropertyName("sample_size")] public int SampleSize { get; set; }

        [JsonPropertyName("country")] public string? Country { get; set; }

        /// <summary><c>db</c>, <c>script</c>, <c>fuzzy</c>, <c>llm</c>, <c>cache</c> or <c>none</c>.</summary>
        [JsonPropertyName("source")] public string Source { get; set; } = "";

        /// <summary><c>high</c>, <c>medium</c>, <c>low</c>, <c>unverified</c> or <c>unknown</c>.</summary>
        [JsonPropertyName("confidence")] public string Confidence { get; set; } = "";

        /// <summary>The name actually matched, on a fuzzy or transliterated match.</summary>
        [JsonPropertyName("matched_as")] public string? MatchedAs { get; set; }

        [JsonPropertyName("took_ms")] public int TookMs { get; set; }

        /// <summary>True when <see cref="Gender"/> is not null.</summary>
        [JsonIgnore] public bool IsKnown => Gender != null;
    }

    /// <summary>A single lookup: the result plus the envelope.</summary>
    public sealed class GenderResponse : GenderResult
    {
        [JsonPropertyName("credits_charged")] public int CreditsCharged { get; set; }
        [JsonPropertyName("credits_remaining")] public int CreditsRemaining { get; set; }
        [JsonPropertyName("data_version")] public string? DataVersion { get; set; }
        [JsonPropertyName("request_id")] public string? RequestId { get; set; }
    }

    /// <summary>Counts for a whole batch.</summary>
    public sealed class BulkSummary
    {
        [JsonPropertyName("total")] public int Total { get; set; }
        [JsonPropertyName("identified")] public int Identified { get; set; }
        [JsonPropertyName("unknown")] public int Unknown { get; set; }

        /// <summary>Percentage identified.</summary>
        [JsonPropertyName("match_rate")] public double MatchRate { get; set; }
    }

    /// <summary>A batch lookup. Results are in the order the values were sent.</summary>
    public sealed class BulkResponse : Envelope
    {
        [JsonPropertyName("results")] public List<GenderResult> Results { get; set; } = new List<GenderResult>();
        [JsonPropertyName("summary")] public BulkSummary Summary { get; set; } = new BulkSummary();
    }

    /// <summary>
    /// Which countries a name is recorded in. Not a country-of-origin or ethnicity
    /// inference: <see cref="Registrations"/> is counted volume, comparable only among
    /// countries that publish counted birth statistics, and <see cref="AttestedIn"/> is
    /// presence with no weight. Show <see cref="CountriesBasis.Note"/> next to any percentage.
    /// </summary>
    public sealed class CountriesResponse : Envelope
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("basis")] public CountriesBasis Basis { get; set; } = new CountriesBasis();
        [JsonPropertyName("registrations")] public List<CountryRegistration> Registrations { get; set; } = new List<CountryRegistration>();
        [JsonPropertyName("attested_in")] public List<string> AttestedIn { get; set; } = new List<string>();
    }

    /// <summary>What the numbers in a <see cref="CountriesResponse"/> rest on.</summary>
    public sealed class CountriesBasis
    {
        [JsonPropertyName("counted_sources")] public List<string> CountedSources { get; set; } = new List<string>();
        [JsonPropertyName("counted_countries")] public int CountedCountries { get; set; }
        [JsonPropertyName("attested_countries")] public int AttestedCountries { get; set; }
        [JsonPropertyName("note")] public string Note { get; set; } = "";
    }

    /// <summary>One counted country.</summary>
    public sealed class CountryRegistration
    {
        [JsonPropertyName("country")] public string Country { get; set; } = "";
        [JsonPropertyName("count")] public int Count { get; set; }

        /// <summary>This country's percentage of the name's registrations across all counting countries.</summary>
        [JsonPropertyName("share")] public double Share { get; set; }

        [JsonPropertyName("gender")] public string? Gender { get; set; }
        [JsonPropertyName("probability")] public int Probability { get; set; }
        [JsonPropertyName("source")] public string Source { get; set; } = "";
    }

    /// <summary>The account behind the API key.</summary>
    public sealed class AccountResponse
    {
        [JsonPropertyName("email")] public string Email { get; set; } = "";
        [JsonPropertyName("credits_remaining")] public int CreditsRemaining { get; set; }
        [JsonPropertyName("purchased_credits")] public int PurchasedCredits { get; set; }
        [JsonPropertyName("free_today")] public int FreeToday { get; set; }
        [JsonPropertyName("free_daily_limit")] public int FreeDailyLimit { get; set; }
        [JsonPropertyName("lifetime_requests")] public int LifetimeRequests { get; set; }
        [JsonPropertyName("data_version")] public string? DataVersion { get; set; }
    }

    internal sealed class ErrorBody
    {
        [JsonPropertyName("error")] public string? Error { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
        [JsonPropertyName("request_id")] public string? RequestId { get; set; }
        [JsonPropertyName("docs")] public string? Docs { get; set; }
        [JsonPropertyName("retry_after")] public int? RetryAfter { get; set; }
    }
}
