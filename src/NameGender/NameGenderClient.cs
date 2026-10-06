using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NameGender
{
    /// <summary>
    /// Client for the NameGender API. Success is read from the HTTP status: a
    /// non-2xx response throws <see cref="NameGenderException"/>, never returns a result.
    /// </summary>
    /// <remarks>
    /// Reuse one instance, or pass an <see cref="HttpClient"/> from IHttpClientFactory.
    /// The client is safe to share across threads.
    /// </remarks>
    public sealed partial class NameGenderClient : IDisposable
    {
        /// <summary>Production API root.</summary>
        public const string DefaultBaseUrl = "https://namegender.com/api/v1";

        private readonly HttpClient _http;
        private readonly bool _ownsHttp;
        private readonly string _apiKey;
        private readonly string _baseUrl;

        /// <param name="apiKey">Key from the dashboard. Sent as a Bearer token.</param>
        /// <param name="httpClient">Optional; the caller keeps ownership of it.</param>
        /// <param name="baseUrl">Optional; defaults to <see cref="DefaultBaseUrl"/>.</param>
        public NameGenderClient(string apiKey, HttpClient? httpClient = null, string? baseUrl = null)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ArgumentException("An API key is required.", nameof(apiKey));
            }

            _apiKey = apiKey;
            _baseUrl = (baseUrl ?? DefaultBaseUrl).TrimEnd('/');
            _ownsHttp = httpClient == null;
            _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        }

        /// <summary>Gender from a first or full name.</summary>
        public Task<GenderResponse> NameAsync(string name, LookupOptions? options = null, CancellationToken cancellationToken = default)
            => SingleAsync("/gender", "name", name, options, cancellationToken);

        /// <summary>Gender from an email address.</summary>
        public Task<GenderResponse> EmailAsync(string email, LookupOptions? options = null, CancellationToken cancellationToken = default)
            => SingleAsync("/gender/email", "email", email, options, cancellationToken);

        /// <summary>Gender from a username.</summary>
        public Task<GenderResponse> UsernameAsync(string username, LookupOptions? options = null, CancellationToken cancellationToken = default)
            => SingleAsync("/gender/username", "username", username, options, cancellationToken);

        /// <summary>
        /// Up to 100 values in one request, answered in the order sent. One credit per value.
        /// </summary>
        public Task<BulkResponse> BulkAsync(IEnumerable<string> values, LookupOptions? options = null, InputType type = InputType.Name, CancellationToken cancellationToken = default)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            var payload = new Dictionary<string, object> { ["names"] = values.ToList() };
            Apply(payload, options);

            if (type != InputType.Name)
            {
                payload["type"] = type == InputType.Email ? "email" : "username";
            }

            return SendAsync<BulkResponse>(HttpMethod.Post, "/gender/bulk", payload, cancellationToken);
        }

        /// <summary>Which countries a name is recorded in. <paramref name="limit"/> is 1 to 100; null keeps the server default of 25.</summary>
        public Task<CountriesResponse> CountriesAsync(string name, int? limit = null, CancellationToken cancellationToken = default)
        {
            var payload = new Dictionary<string, object> { ["name"] = name };

            if (limit.HasValue)
            {
                payload["limit"] = limit.Value;
            }

            return SendAsync<CountriesResponse>(HttpMethod.Post, "/gender/countries", payload, cancellationToken);
        }

        /// <summary>Balance and usage of the account behind the key. Costs no credits.</summary>
        public Task<AccountResponse> AccountAsync(CancellationToken cancellationToken = default)
            => SendAsync<AccountResponse>(HttpMethod.Get, "/me", null, cancellationToken);

        private Task<GenderResponse> SingleAsync(string path, string field, string value, LookupOptions? options, CancellationToken cancellationToken)
        {
            var payload = new Dictionary<string, object> { [field] = value };
            Apply(payload, options);

            return SendAsync<GenderResponse>(HttpMethod.Post, path, payload, cancellationToken);
        }

        private static void Apply(Dictionary<string, object> payload, LookupOptions? options)
        {
            if (options == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(options.Country))
            {
                payload["country"] = options.Country!;
            }

            if (!string.IsNullOrEmpty(options.Locale))
            {
                payload["locale"] = options.Locale!;
            }

            if (!string.IsNullOrEmpty(options.Ip))
            {
                payload["ip"] = options.Ip!;
            }

            if (options.AiFallback)
            {
                payload["ai_fallback"] = true;
            }

            if (options.BestGuess)
            {
                payload["best_guess"] = true;
            }
        }

        private Task<T> SendAsync<T>(HttpMethod method, string path, Dictionary<string, object>? payload, CancellationToken cancellationToken)
        {
            var content = payload == null ? null : new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            return SendAsync<T>(method, path, content, null, cancellationToken);
        }

        private async Task<T> SendAsync<T>(HttpMethod method, string path, HttpContent? content, string? idempotencyKey, CancellationToken cancellationToken)
        {
            using var request = NewRequest(method, path, content, idempotencyKey);
            using var response = await SendCoreAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            return JsonSerializer.Deserialize<T>(body)
                ?? throw new NameGenderException((int)response.StatusCode, null, "The API returned an empty body.", null, null, null, body);
        }

        private HttpRequestMessage NewRequest(HttpMethod method, string path, HttpContent? content = null, string? idempotencyKey = null)
        {
            var request = new HttpRequestMessage(method, _baseUrl + path) { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.UserAgent.ParseAdd("namegender-dotnet/" + typeof(NameGenderClient).Assembly.GetName().Version?.ToString(3));

            if (idempotencyKey != null)
            {
                request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
            }

            return request;
        }

        /// <summary>Sends the request and returns a 2xx response undisposed; anything else throws.</summary>
        private async Task<HttpResponseMessage> SendCoreAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                throw NameGenderException.From((int)response.StatusCode, body, response.Headers.RetryAfter?.Delta);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_ownsHttp)
            {
                _http.Dispose();
            }
        }
    }
}
