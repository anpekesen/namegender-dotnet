using System;
using System.Collections.Generic;
using System.Text.Json;
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

    /// <summary>
    /// How a file job reads the file. Used by <see cref="NameGenderClient.StartBatchAsync"/>;
    /// <see cref="BatchOptions"/> adds the upload settings.
    /// </summary>
    public class BatchSettings
    {
        /// <summary>
        /// Header of the column holding the names, exactly as written in the file. Required
        /// to start: a guessed column that turns out to be wrong would spend credits on the wrong data.
        /// </summary>
        public string? NameColumn { get; set; }

        /// <summary>Header of a column holding a country code per row.</summary>
        public string? CountryColumn { get; set; }

        /// <summary>Default country (ISO 3166-1 alpha-2) for rows without one.</summary>
        public string? Country { get; set; }

        /// <summary>Ask a language model for names not in the data. Needs AI consent on the account.</summary>
        public bool? AiFallback { get; set; }

        /// <summary>Return the likelier gender even below the probability threshold.</summary>
        public bool? BestGuess { get; set; }

        /// <summary>The result can be downloaded once, then it is deleted.</summary>
        public bool? DeleteAfterDownload { get; set; }
    }

    /// <summary>Options for <see cref="NameGenderClient.CreateBatchAsync(byte[], string, BatchOptions?, System.Threading.CancellationToken)"/>.</summary>
    public sealed class BatchOptions : BatchSettings
    {
        /// <summary>
        /// False uploads and inspects only: the job stays <c>uploaded</c> and carries
        /// <see cref="BatchJob.Inspection"/> until <see cref="NameGenderClient.StartBatchAsync"/>.
        /// </summary>
        public bool Start { get; set; } = true;

        /// <summary>
        /// Sent as <c>Idempotency-Key</c> on every attempt. One is generated per call when
        /// null; set your own to keep the guarantee across your own retries.
        /// </summary>
        public string? IdempotencyKey { get; set; }

        /// <summary>Extra attempts after a network error or a 502/503/504.</summary>
        public int Retries { get; set; } = 2;
    }

    /// <summary>A file job, as every batch endpoint and the batch webhooks return it.</summary>
    public sealed class BatchJob
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";

        /// <summary><c>uploaded</c>, <c>queued</c>, <c>processing</c>, <c>completed</c>, <c>failed</c> or <c>cancelled</c>.</summary>
        [JsonPropertyName("status")] public string Status { get; set; } = "";

        /// <summary><c>api</c> or <c>panel</c> (started from the dashboard).</summary>
        [JsonPropertyName("source")] public string Source { get; set; } = "";

        [JsonPropertyName("file")] public BatchFile File { get; set; } = new BatchFile();
        [JsonPropertyName("columns")] public BatchColumns Columns { get; set; } = new BatchColumns();
        [JsonPropertyName("options")] public BatchJobOptions Options { get; set; } = new BatchJobOptions();
        [JsonPropertyName("rows")] public BatchRows Rows { get; set; } = new BatchRows();

        /// <summary>0 to 100.</summary>
        [JsonPropertyName("progress")] public int Progress { get; set; }

        [JsonPropertyName("credits")] public BatchCredits Credits { get; set; } = new BatchCredits();

        /// <summary>Set once completed.</summary>
        [JsonPropertyName("summary")] public BatchJobSummary? Summary { get; set; }

        [JsonPropertyName("data_version")] public string? DataVersion { get; set; }

        /// <summary>Set when <see cref="Status"/> is <c>failed</c>. Branch on <see cref="BatchError.Code"/>.</summary>
        [JsonPropertyName("error")] public BatchError? Error { get; set; }

        /// <summary>Set once completed.</summary>
        [JsonPropertyName("result")] public BatchResult? Result { get; set; }

        /// <summary>Only while <see cref="Status"/> is <c>uploaded</c>.</summary>
        [JsonPropertyName("inspection")] public BatchInspection? Inspection { get; set; }

        /// <summary>How long to wait before polling again; null once there is nothing to wait for.</summary>
        [JsonPropertyName("poll_after_seconds")] public int? PollAfterSeconds { get; set; }

        [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
        [JsonPropertyName("started_at")] public DateTimeOffset? StartedAt { get; set; }
        [JsonPropertyName("finished_at")] public DateTimeOffset? FinishedAt { get; set; }
        [JsonPropertyName("expires_at")] public DateTimeOffset? ExpiresAt { get; set; }

        /// <summary>True when <see cref="Status"/> is <c>completed</c>, <c>failed</c> or <c>cancelled</c>.</summary>
        [JsonIgnore] public bool IsFinished => Status == "completed" || Status == "failed" || Status == "cancelled";
    }

    public sealed class BatchFile
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";

        /// <summary><c>csv</c> or <c>xlsx</c>.</summary>
        [JsonPropertyName("format")] public string Format { get; set; } = "";
    }

    public sealed class BatchColumns
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("country")] public string? Country { get; set; }
    }

    public sealed class BatchJobOptions
    {
        [JsonPropertyName("country")] public string? Country { get; set; }
        [JsonPropertyName("ai_fallback")] public bool AiFallback { get; set; }
        [JsonPropertyName("best_guess")] public bool BestGuess { get; set; }
        [JsonPropertyName("delete_after_download")] public bool DeleteAfterDownload { get; set; }
    }

    public sealed class BatchRows
    {
        [JsonPropertyName("total")] public int Total { get; set; }
        [JsonPropertyName("processed")] public int Processed { get; set; }

        /// <summary>Rows given a gender. Set once completed.</summary>
        [JsonPropertyName("identified")] public int? Identified { get; set; }
    }

    public sealed class BatchCredits
    {
        /// <summary>Held from the balance when processing began.</summary>
        [JsonPropertyName("reserved")] public int? Reserved { get; set; }

        /// <summary>Final charge. Null until completed; a failed job is not charged.</summary>
        [JsonPropertyName("charged")] public int? Charged { get; set; }
    }

    public sealed class BatchJobSummary
    {
        [JsonPropertyName("male")] public int Male { get; set; }
        [JsonPropertyName("female")] public int Female { get; set; }
        [JsonPropertyName("unknown")] public int Unknown { get; set; }
        [JsonPropertyName("from_llm")] public int FromLlm { get; set; }
    }

    public sealed class BatchError
    {
        /// <summary>
        /// <c>source_missing</c>, <c>no_columns</c>, <c>name_column_missing</c>, <c>empty_file</c>,
        /// <c>bad_format</c>, <c>unreadable</c>, <c>no_credits</c>, <c>processing_error</c> or <c>stalled</c>.
        /// </summary>
        [JsonPropertyName("code")] public string Code { get; set; } = "";
        [JsonPropertyName("message")] public string Message { get; set; } = "";
    }

    public sealed class BatchResult
    {
        /// <summary>Needs the API key; <see cref="NameGenderClient.DownloadBatchAsync(string, System.Threading.CancellationToken)"/> sends it.</summary>
        [JsonPropertyName("url")] public string Url { get; set; } = "";
        [JsonPropertyName("format")] public string Format { get; set; } = "";
        [JsonPropertyName("expires_at")] public DateTimeOffset? ExpiresAt { get; set; }
    }

    /// <summary>What an uploaded file holds, to decide how to start it.</summary>
    public sealed class BatchInspection
    {
        [JsonPropertyName("columns")] public List<string> Columns { get; set; } = new List<string>();

        /// <summary>The first rows, as text.</summary>
        [JsonPropertyName("preview")] public List<List<string>> Preview { get; set; } = new List<List<string>>();

        [JsonPropertyName("guessed_name_column")] public string? GuessedNameColumn { get; set; }
        [JsonPropertyName("guessed_country_column")] public string? GuessedCountryColumn { get; set; }
        [JsonPropertyName("credits_needed")] public int CreditsNeeded { get; set; }
        [JsonPropertyName("credits_available")] public int CreditsAvailable { get; set; }
    }

    /// <summary>A page of file jobs, newest first.</summary>
    public sealed class BatchList
    {
        [JsonPropertyName("data")] public List<BatchJob> Data { get; set; } = new List<BatchJob>();
        [JsonPropertyName("page")] public int Page { get; set; }
        [JsonPropertyName("per_page")] public int PerPage { get; set; }
        [JsonPropertyName("total")] public int Total { get; set; }
        [JsonPropertyName("has_more")] public bool HasMore { get; set; }
    }

    /// <summary>A verified webhook request. Returned by <see cref="NameGenderWebhooks.Verify(byte[], string?, string, TimeSpan?, DateTimeOffset?)"/>.</summary>
    public sealed class WebhookEvent
    {
        /// <summary>Stable across retries: deduplicate on it.</summary>
        [JsonPropertyName("id")] public string Id { get; set; } = "";

        /// <summary>
        /// <c>batch.completed</c>, <c>batch.failed</c>, <c>credits.low</c>, <c>credits.depleted</c> or
        /// <c>webhook.test</c>. New types can be added: answer 2xx to one you do not handle and ignore it.
        /// </summary>
        [JsonPropertyName("type")] public string Type { get; set; } = "";

        [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }
        [JsonPropertyName("api_version")] public string ApiVersion { get; set; } = "";
        [JsonPropertyName("data")] public WebhookEventData Data { get; set; } = new WebhookEventData();

        /// <summary>The job, on <c>batch.completed</c> and <c>batch.failed</c>; otherwise null.</summary>
        public BatchJob? AsBatchJob() => Type.StartsWith("batch.", StringComparison.Ordinal) ? Data.Object.Deserialize<BatchJob>() : null;

        /// <summary>The alert, on <c>credits.low</c> and <c>credits.depleted</c>; otherwise null.</summary>
        public CreditsAlert? AsCreditsAlert() => Type.StartsWith("credits.", StringComparison.Ordinal) ? Data.Object.Deserialize<CreditsAlert>() : null;
    }

    public sealed class WebhookEventData
    {
        /// <summary>The job (<see cref="WebhookEvent.AsBatchJob"/>), the alert (<see cref="WebhookEvent.AsCreditsAlert"/>) or, on <c>webhook.test</c>, a message.</summary>
        [JsonPropertyName("object")] public JsonElement Object { get; set; }
    }

    /// <summary><c>data.object</c> of <c>credits.low</c> and <c>credits.depleted</c>. Checked hourly; a heads-up, not a balance feed.</summary>
    public sealed class CreditsAlert
    {
        /// <summary><c>credits_low</c> or <c>credits_out</c>.</summary>
        [JsonPropertyName("kind")] public string Kind { get; set; } = "";

        /// <summary>Same as <see cref="AccountResponse.CreditsRemaining"/>: purchased, subscription and today's free credits.</summary>
        [JsonPropertyName("credits_remaining")] public int CreditsRemaining { get; set; }

        [JsonPropertyName("purchased")] public int Purchased { get; set; }
        [JsonPropertyName("subscription")] public int Subscription { get; set; }

        /// <summary>Average credits per day over the last 14 days.</summary>
        [JsonPropertyName("daily_burn")] public int DailyBurn { get; set; }

        /// <summary>Days the balance lasts at that rate. <c>credits.low</c> only.</summary>
        [JsonPropertyName("runway_days")] public int? RunwayDays { get; set; }

        /// <summary>When the condition was first seen.</summary>
        [JsonPropertyName("since")] public DateTimeOffset? Since { get; set; }
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
