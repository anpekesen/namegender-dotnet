using System;
using System.Text.Json;

namespace NameGender
{
    /// <summary>
    /// A non-2xx response. <see cref="Code"/> is the machine-readable error, e.g.
    /// <c>no_credits</c>, <c>invalid_key</c> or <c>rate_limited</c>; the full list is at
    /// https://namegender.com/docs.
    /// </summary>
    public sealed class NameGenderException : Exception
    {
        /// <summary>HTTP status code.</summary>
        public int StatusCode { get; }

        /// <summary>The <c>error</c> field, or null when the body was not JSON.</summary>
        public string? Code { get; }

        /// <summary>Quote this to support.</summary>
        public string? RequestId { get; }

        /// <summary>Link to the documentation for this error.</summary>
        public string? Docs { get; }

        /// <summary>How long to wait before retrying, on <c>rate_limited</c>.</summary>
        public TimeSpan? RetryAfter { get; }

        /// <summary>The response body as received.</summary>
        public string RawBody { get; }

        /// <summary>Creates the exception. Usually thrown by <see cref="NameGenderClient"/>.</summary>
        public NameGenderException(int statusCode, string? code, string message, string? requestId, string? docs, TimeSpan? retryAfter, string rawBody)
            : base(message)
        {
            StatusCode = statusCode;
            Code = code;
            RequestId = requestId;
            Docs = docs;
            RetryAfter = retryAfter;
            RawBody = rawBody;
        }

        internal static NameGenderException From(int status, string body, TimeSpan? retryAfterHeader)
        {
            ErrorBody? error = null;

            try
            {
                error = JsonSerializer.Deserialize<ErrorBody>(body);
            }
            catch (JsonException)
            {
                // A proxy or load balancer answered, not the API. Keep the raw body.
            }

            var retryAfter = retryAfterHeader
                ?? (error?.RetryAfter is int seconds ? TimeSpan.FromSeconds(seconds) : (TimeSpan?)null);

            return new NameGenderException(
                status,
                error?.Error,
                error?.Message ?? $"NameGender API returned HTTP {status}.",
                error?.RequestId,
                error?.Docs,
                retryAfter,
                body);
        }
    }
}
