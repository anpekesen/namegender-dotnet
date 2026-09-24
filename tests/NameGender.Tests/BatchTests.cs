using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NameGender.Tests;

public class BatchTests
{
    private const string Job = """
        {"id":"B-1","status":"queued","source":"api","file":{"name":"customers.csv","format":"csv"},
         "columns":{"name":"first_name","country":null},
         "options":{"country":null,"ai_fallback":false,"best_guess":true,"delete_after_download":false},
         "rows":{"total":3,"processed":0,"identified":null},"progress":0,
         "credits":{"reserved":null,"charged":null},"summary":null,"data_version":null,"error":null,
         "result":null,"inspection":null,"poll_after_seconds":0,
         "created_at":"2026-09-24T10:00:00Z","started_at":null,"finished_at":null,"expires_at":null}
        """;

    /// <summary>Answers from a queue and keeps every request with its body.</summary>
    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses = new();

        public List<(HttpRequestMessage Request, byte[]? Body)> Sent { get; } = new();

        public QueueHandler Then(HttpStatusCode status, string body)
        {
            _responses.Enqueue(() => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
            return this;
        }

        public QueueHandler ThenThrow(Exception error)
        {
            _responses.Enqueue(() => throw error);
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sent.Add((request, request.Content == null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken)));
            return _responses.Dequeue()();
        }
    }

    private static NameGenderClient Client(QueueHandler handler)
        => new("secret", new HttpClient(handler), "https://example.test/api/v1/");

    private static string Status(string status, int? poll = 0)
        => Job.Replace("\"status\":\"queued\"", $"\"status\":\"{status}\"")
              .Replace("\"poll_after_seconds\":0", "\"poll_after_seconds\":" + (poll?.ToString() ?? "null"));

    [Fact]
    public async Task Create_sends_the_file_the_fields_and_an_idempotency_key()
    {
        var handler = new QueueHandler().Then(HttpStatusCode.Created, Job);
        var bytes = Encoding.UTF8.GetBytes("id,first_name,country\n1,Ayşe,TR\n");

        var job = await Client(handler).CreateBatchAsync(bytes, "müşteriler.csv", new BatchOptions
        {
            NameColumn = "first_name",
            CountryColumn = "country",
            BestGuess = true,
            AiFallback = false,
        });

        var (request, body) = handler.Sent.Single();
        var text = Encoding.UTF8.GetString(body!);

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://example.test/api/v1/batches", request.RequestUri!.ToString());
        Assert.StartsWith("multipart/form-data; boundary=", request.Content!.Headers.ContentType!.ToString());
        Assert.True(Guid.TryParse(request.Headers.GetValues("Idempotency-Key").Single(), out _));

        Assert.Contains("name=\"file\"; filename=\"müşteriler.csv\"", text);
        Assert.Contains("id,first_name,country\n1,Ayşe,TR\n", text);
        Assert.Contains("name=\"name_column\"\r\n\r\nfirst_name\r\n", text);
        Assert.Contains("name=\"country_column\"\r\n\r\ncountry\r\n", text);
        Assert.Contains("name=\"best_guess\"\r\n\r\ntrue\r\n", text);
        Assert.Contains("name=\"ai_fallback\"\r\n\r\nfalse\r\n", text);
        Assert.Contains("name=\"start\"\r\n\r\ntrue\r\n", text);
        Assert.DoesNotContain("name=\"country\"", text);
        Assert.DoesNotContain("delete_after_download", text);

        Assert.Equal("B-1", job.Id);
        Assert.True(job.Options.BestGuess);
        Assert.Null(job.Summary);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 10, 0, 0, TimeSpan.Zero), job.CreatedAt);
    }

    [Fact]
    public async Task Create_retries_a_503_and_a_dropped_connection_with_the_same_key()
    {
        var handler = new QueueHandler()
            .Then(HttpStatusCode.ServiceUnavailable, """{"error":"maintenance"}""")
            .ThenThrow(new HttpRequestException("connection reset"))
            .Then(HttpStatusCode.Created, Job);

        var job = await Client(handler).CreateBatchAsync(new byte[] { 1, 2, 3 }, "a.xlsx", new BatchOptions { Start = false, IdempotencyKey = "k-1" });

        Assert.Equal("B-1", job.Id);
        Assert.Equal(3, handler.Sent.Count);
        Assert.All(handler.Sent, s => Assert.Equal("k-1", s.Request.Headers.GetValues("Idempotency-Key").Single()));
        Assert.Equal(handler.Sent[0].Body!.Length, handler.Sent[2].Body!.Length);
        Assert.Contains("name=\"start\"\r\n\r\nfalse\r\n", Encoding.UTF8.GetString(handler.Sent[2].Body!));
    }

    [Fact]
    public async Task Create_does_not_retry_a_4xx()
    {
        var handler = new QueueHandler().Then(HttpStatusCode.PaymentRequired, """{"error":"no_credits","message":"Out of credits."}""");

        var error = await Assert.ThrowsAsync<NameGenderException>(
            () => Client(handler).CreateBatchAsync(new byte[] { 1 }, "a.csv", new BatchOptions { NameColumn = "name" }));

        Assert.Equal(402, error.StatusCode);
        Assert.Equal("no_credits", error.Code);
        Assert.Single(handler.Sent);
    }

    [Fact]
    public async Task Create_gives_up_after_the_retries()
    {
        var handler = new QueueHandler()
            .Then(HttpStatusCode.BadGateway, "<html>bad gateway</html>")
            .Then(HttpStatusCode.BadGateway, "<html>bad gateway</html>");

        var error = await Assert.ThrowsAsync<NameGenderException>(
            () => Client(handler).CreateBatchAsync(new byte[] { 1 }, "a.csv", new BatchOptions { NameColumn = "name", Retries = 1 }));

        Assert.Equal(502, error.StatusCode);
        Assert.Equal(2, handler.Sent.Count);
    }

    [Fact]
    public async Task Start_posts_json_with_the_settings_that_are_set()
    {
        var handler = new QueueHandler().Then(HttpStatusCode.OK, Job);

        await Client(handler).StartBatchAsync("B-1/x", new BatchSettings { NameColumn = "first_name", Country = "TR", DeleteAfterDownload = true });

        var (request, body) = handler.Sent.Single();
        var sent = System.Text.Json.JsonDocument.Parse(body!).RootElement;

        Assert.Equal("https://example.test/api/v1/batches/B-1%2Fx/start", request.RequestUri!.AbsoluteUri);
        Assert.Equal(new[] { "name_column", "country", "delete_after_download" }, sent.EnumerateObject().Select(p => p.Name));
        Assert.True(sent.GetProperty("delete_after_download").GetBoolean());

        await Assert.ThrowsAsync<ArgumentException>(() => Client(handler).StartBatchAsync("B-1", new BatchSettings()));
    }

    [Fact]
    public async Task Wait_polls_until_the_job_finishes_and_returns_a_failed_job()
    {
        var handler = new QueueHandler()
            .Then(HttpStatusCode.OK, Status("queued"))
            .Then(HttpStatusCode.OK, Status("processing"))
            .Then(HttpStatusCode.OK, Status("failed", null).Replace("\"error\":null", "\"error\":{\"code\":\"no_credits\",\"message\":\"m\"}"));
        var seen = new List<string>();

        var job = await Client(handler).WaitBatchAsync("B-1", onProgress: j => seen.Add(j.Status));

        Assert.Equal(new[] { "queued", "processing", "failed" }, seen);
        Assert.Equal("no_credits", job.Error!.Code);
        Assert.True(job.IsFinished);
        Assert.All(handler.Sent, s => Assert.Equal(HttpMethod.Get, s.Request.Method));
        Assert.Equal("https://example.test/api/v1/batches/B-1", handler.Sent[0].Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task Wait_stops_at_uploaded_and_times_out_otherwise()
    {
        var uploaded = new QueueHandler().Then(HttpStatusCode.OK, Status("uploaded", null));
        Assert.Equal("uploaded", (await Client(uploaded).WaitBatchAsync("B-1")).Status);

        var slow = new QueueHandler().Then(HttpStatusCode.OK, Status("processing", 30));
        var error = await Assert.ThrowsAsync<NameGenderException>(() => Client(slow).WaitBatchAsync("B-1", TimeSpan.FromSeconds(1)));

        Assert.Equal(0, error.StatusCode);
        Assert.Contains("B-1", error.Message);
    }

    [Fact]
    public async Task Cancel_list_and_download_use_the_right_method_and_url()
    {
        var handler = new QueueHandler()
            .Then(HttpStatusCode.NoContent, "")
            .Then(HttpStatusCode.OK, "{\"data\":[" + Job + "],\"page\":2,\"per_page\":5,\"total\":6,\"has_more\":false}")
            .Then(HttpStatusCode.OK, "﻿id,first_name,gender\n");
        var client = Client(handler);

        await client.CancelBatchAsync("B-1");
        var list = await client.ListBatchesAsync(limit: 5, page: 2);
        var file = await client.DownloadBatchAsync("B-1");

        Assert.Equal(HttpMethod.Delete, handler.Sent[0].Request.Method);
        Assert.Equal("https://example.test/api/v1/batches/B-1", handler.Sent[0].Request.RequestUri!.ToString());
        Assert.Equal("https://example.test/api/v1/batches?limit=5&page=2", handler.Sent[1].Request.RequestUri!.ToString());
        Assert.Equal("https://example.test/api/v1/batches/B-1/result", handler.Sent[2].Request.RequestUri!.ToString());

        Assert.Equal(2, list.Page);
        Assert.Equal("B-1", list.Data.Single().Id);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, file.Take(3));
    }
}
