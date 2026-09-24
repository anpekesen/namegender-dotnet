using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NameGender
{
    // File jobs: upload a CSV or XLSX file, get it back with gender columns added.
    public sealed partial class NameGenderClient
    {
        /// <summary>
        /// Upload a file and, unless <see cref="BatchOptions.Start"/> is false, start it.
        /// </summary>
        /// <remarks>
        /// The extension of <paramref name="fileName"/> (<c>.csv</c>, <c>.xlsx</c>) tells the API the
        /// format. One Idempotency-Key is used for every attempt, so a retry after a dropped
        /// connection returns the first job instead of opening a second one and reserving
        /// credit twice.
        /// </remarks>
        public async Task<BatchJob> CreateBatchAsync(byte[] content, string fileName, BatchOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            if (string.IsNullOrEmpty(fileName))
            {
                throw new ArgumentException("A file name is required: its extension tells the API the format.", nameof(fileName));
            }

            options ??= new BatchOptions();

            var fields = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("start", options.Start ? "true" : "false"),
            };
            AddField(fields, "name_column", options.NameColumn);
            AddField(fields, "country_column", options.CountryColumn);
            AddField(fields, "country", options.Country);
            AddField(fields, "ai_fallback", options.AiFallback);
            AddField(fields, "best_guess", options.BestGuess);
            AddField(fields, "delete_after_download", options.DeleteAfterDownload);

            var boundary = Guid.NewGuid().ToString("N");
            var body = Multipart(boundary, fields, fileName, content);
            var idempotencyKey = options.IdempotencyKey ?? Guid.NewGuid().ToString();

            for (var attempt = 0; ; attempt++)
            {
                // HttpContent is disposed with its request, so each attempt gets a new one over the same bytes.
                var httpContent = new ByteArrayContent(body);
                httpContent.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data; boundary=" + boundary);

                try
                {
                    return await SendAsync<BatchJob>(HttpMethod.Post, "/batches", httpContent, idempotencyKey, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception error) when (attempt < options.Retries && IsRetryable(error, cancellationToken))
                {
                }

                await Task.Delay(TimeSpan.FromSeconds(1 << attempt), cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>Upload a file from a stream. See <see cref="CreateBatchAsync(byte[], string, BatchOptions?, CancellationToken)"/>.</summary>
        public async Task<BatchJob> CreateBatchAsync(Stream content, string fileName, BatchOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            // Buffered once so that a retry can send the same bytes again.
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);

            return await CreateBatchAsync(buffer.ToArray(), fileName, options, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Upload a file from disk; its name is sent as the file name.</summary>
        public async Task<BatchJob> CreateBatchAsync(string path, BatchOptions? options = null, CancellationToken cancellationToken = default)
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

            return await CreateBatchAsync(file, Path.GetFileName(path), options, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Start a job uploaded with <see cref="BatchOptions.Start"/> false. <see cref="BatchSettings.NameColumn"/> is required.</summary>
        public Task<BatchJob> StartBatchAsync(string id, BatchSettings settings, CancellationToken cancellationToken = default)
        {
            if (settings == null || string.IsNullOrEmpty(settings.NameColumn))
            {
                throw new ArgumentException("NameColumn is required to start a job.", nameof(settings));
            }

            var payload = new Dictionary<string, object> { ["name_column"] = settings.NameColumn! };
            AddSetting(payload, "country_column", settings.CountryColumn);
            AddSetting(payload, "country", settings.Country);
            AddSetting(payload, "ai_fallback", settings.AiFallback);
            AddSetting(payload, "best_guess", settings.BestGuess);
            AddSetting(payload, "delete_after_download", settings.DeleteAfterDownload);

            return SendAsync<BatchJob>(HttpMethod.Post, BatchPath(id) + "/start", payload, cancellationToken);
        }

        /// <summary>One job. Costs no credits.</summary>
        public Task<BatchJob> GetBatchAsync(string id, CancellationToken cancellationToken = default)
            => SendAsync<BatchJob>(HttpMethod.Get, BatchPath(id), null, cancellationToken);

        /// <summary>Newest first. Includes jobs started from the dashboard.</summary>
        public Task<BatchList> ListBatchesAsync(int? limit = null, int? page = null, CancellationToken cancellationToken = default)
        {
            var query = new List<string>();

            if (limit.HasValue)
            {
                query.Add("limit=" + limit.Value);
            }

            if (page.HasValue)
            {
                query.Add("page=" + page.Value);
            }

            var path = "/batches" + (query.Count > 0 ? "?" + string.Join("&", query) : "");

            return SendAsync<BatchList>(HttpMethod.Get, path, null, cancellationToken);
        }

        /// <summary>Cancel a job that has not started (credit is returned), or delete a finished one.</summary>
        public async Task CancelBatchAsync(string id, CancellationToken cancellationToken = default)
        {
            // 204: there is no body to read.
            using var request = NewRequest(HttpMethod.Delete, BatchPath(id));
            using var response = await SendCoreAsync(request, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Poll until the job is completed, failed or cancelled (or waiting in <c>uploaded</c>), and return it.
        /// </summary>
        /// <remarks>
        /// A failed job is returned, not thrown: check <see cref="BatchJob.Status"/> and
        /// <see cref="BatchError.Code"/>. Polls at the job's <c>poll_after_seconds</c>, 5 seconds
        /// when it has none. Throws <see cref="NameGenderException"/> with status 0 once
        /// <paramref name="timeout"/> (default one hour) would be passed.
        /// </remarks>
        public async Task<BatchJob> WaitBatchAsync(string id, TimeSpan? timeout = null, Action<BatchJob>? onProgress = null, CancellationToken cancellationToken = default)
        {
            var limit = timeout ?? TimeSpan.FromHours(1);
            var clock = Stopwatch.StartNew();

            while (true)
            {
                var job = await GetBatchAsync(id, cancellationToken).ConfigureAwait(false);
                onProgress?.Invoke(job);

                if (job.IsFinished || job.Status == "uploaded")
                {
                    return job;
                }

                var pause = TimeSpan.FromSeconds(Math.Max(0, job.PollAfterSeconds ?? 5));

                if (clock.Elapsed + pause > limit)
                {
                    throw new NameGenderException(0, null, $"Timed out waiting for {id}", null, null, null, JsonSerializer.Serialize(job));
                }

                await Task.Delay(pause, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>The result file, in the format that was uploaded. A CSV result starts with a UTF-8 byte order mark.</summary>
        public async Task<byte[]> DownloadBatchAsync(string id, CancellationToken cancellationToken = default)
        {
            using var request = NewRequest(HttpMethod.Get, BatchPath(id) + "/result");
            using var response = await SendCoreAsync(request, cancellationToken).ConfigureAwait(false);

            return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }

        /// <summary>Copies the result file into <paramref name="destination"/>, e.g. a <see cref="FileStream"/>.</summary>
        public async Task DownloadBatchAsync(string id, Stream destination, CancellationToken cancellationToken = default)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            using var request = NewRequest(HttpMethod.Get, BatchPath(id) + "/result");
            using var response = await SendCoreAsync(request, cancellationToken).ConfigureAwait(false);
            using var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

            await source.CopyToAsync(destination, 81920, cancellationToken).ConfigureAwait(false);
        }

        private static string BatchPath(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("A job id is required.", nameof(id));
            }

            return "/batches/" + Uri.EscapeDataString(id);
        }

        // Worth retrying an upload for: the request may never have reached the application.
        // Everything else (402, 422, 429 too_many_batches) would fail the same way again.
        private static bool IsRetryable(Exception error, CancellationToken cancellationToken) => error switch
        {
            NameGenderException e => e.StatusCode == 502 || e.StatusCode == 503 || e.StatusCode == 504,
            HttpRequestException _ => true,
            // HttpClient.Timeout surfaces as a cancellation the caller did not ask for.
            OperationCanceledException _ => !cancellationToken.IsCancellationRequested,
            _ => false,
        };

        private static void AddField(List<KeyValuePair<string, string>> fields, string key, string? value)
        {
            if (value != null)
            {
                fields.Add(new KeyValuePair<string, string>(key, value));
            }
        }

        private static void AddField(List<KeyValuePair<string, string>> fields, string key, bool? value)
        {
            if (value.HasValue)
            {
                fields.Add(new KeyValuePair<string, string>(key, value.Value ? "true" : "false"));
            }
        }

        private static void AddSetting(Dictionary<string, object> payload, string key, object? value)
        {
            if (value != null)
            {
                payload[key] = value;
            }
        }

        // Written by hand rather than with MultipartFormDataContent, which MIME-encodes a
        // non-ASCII file name (=?utf-8?B?...?=) and so hides the extension from the API.
        private static byte[] Multipart(string boundary, List<KeyValuePair<string, string>> fields, string fileName, byte[] content)
        {
            var head = new StringBuilder();

            foreach (var field in fields)
            {
                head.Append("--").Append(boundary).Append("\r\n")
                    .Append("Content-Disposition: form-data; name=\"").Append(field.Key).Append("\"\r\n\r\n")
                    .Append(field.Value).Append("\r\n");
            }

            // A quote or line break in a file name would end the header early.
            var safeName = fileName.Replace("\\", "\\\\").Replace("\"", "%22").Replace("\r", "").Replace("\n", "");

            head.Append("--").Append(boundary).Append("\r\n")
                .Append("Content-Disposition: form-data; name=\"file\"; filename=\"").Append(safeName).Append("\"\r\n")
                .Append("Content-Type: application/octet-stream\r\n\r\n");

            var headBytes = Encoding.UTF8.GetBytes(head.ToString());
            var tailBytes = Encoding.UTF8.GetBytes("\r\n--" + boundary + "--\r\n");
            var body = new byte[headBytes.Length + content.Length + tailBytes.Length];

            Buffer.BlockCopy(headBytes, 0, body, 0, headBytes.Length);
            Buffer.BlockCopy(content, 0, body, headBytes.Length, content.Length);
            Buffer.BlockCopy(tailBytes, 0, body, headBytes.Length + content.Length, tailBytes.Length);

            return body;
        }
    }
}
