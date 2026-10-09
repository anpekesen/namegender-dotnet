# NameGender .NET

Official .NET client for the [NameGender API](https://namegender.com/docs): gender
from a first name, full name, email address or username, with the probability,
sample size and source behind every answer.

Targets .NET Standard 2.0 (so .NET Framework 4.6.2 and later) and .NET 8.

```sh
dotnet add package NameGender
```

```csharp
using NameGender;

using var client = new NameGenderClient(Environment.GetEnvironmentVariable("NAMEGENDER_API_KEY")!);

var result = await client.NameAsync("Andrea", new LookupOptions { Country = "IT" });

Console.WriteLine($"{result.Gender} {result.Probability}% from {result.SampleSize} records ({result.Confidence})");
```

`Gender` is `male`, `female` or `null`. Null is an answer, not an error: keep it
nullable in your own model. `Probability` is 0–100, not a fraction.

A free account gives 100 lookups a day: https://namegender.com/register

## Emails, usernames and full names

```csharp
await client.EmailAsync("ayse.yilmaz@example.com");
await client.UsernameAsync("andrea_89", new LookupOptions { Country = "IT" });

var full = await client.NameAsync("Dr. Ayşe Nur Yılmaz");
// full.FirstName "Ayşe", full.MiddleName "Nur", full.LastName "Yılmaz", full.NameType "personal"
```

## Bulk

Up to 100 values per request, answered in the order sent, one credit each.

```csharp
var batch = await client.BulkAsync(new[] { "Ayşe", "Mehmet", "Priya", "Wei" }, new LookupOptions { Country = "TR" });

foreach (var row in batch.Results)
{
    Console.WriteLine($"{row.Query} {row.Gender ?? "?"} {row.Probability}");
}

Console.WriteLine(batch.Summary.MatchRate);
```

Pass `type: InputType.Email` or `InputType.Username` for a list of emails or
usernames. For files of hundreds of thousands of rows, a [file job](#file-jobs) is
faster than looping over this method.

## Options

| Option | Sent as | Effect |
|---|---|---|
| `Country` | `country` | ISO 3166-1 alpha-2 hint. Andrea is male in Italy and female in Germany. |
| `Locale` | `locale` | The end user's language tag, e.g. `it-IT` or `pt_BR`. Its region is used as the country when `Country` is not set; `en` alone sets none. |
| `Ip` | `ip` | The end user's IP address. Its country is used when neither `Country` nor a regional `Locale` is set. Not stored. |
| `AiFallback` | `ai_fallback` | Ask a language model when the name is not in the data. Needs AI consent on the account. |
| `BestGuess` | `best_guess` | Return the likelier gender even below the probability threshold. |

`Country` wins over `Locale`, and `Locale` over `Ip`. `CountrySource` on the
response (on a bulk response, once for the whole request) says which one was used: `country`, `locale`,
`ip`, or null when none was.

```csharp
var result = await client.NameAsync("Andrea", new LookupOptions
{
    Locale = "it-IT",                                         // the user's language setting
    Ip = httpContext.Connection.RemoteIpAddress?.ToString(),  // the user's address, not your server's
});
// result.Country "IT", result.CountrySource "locale"
```

## Salutation

A ready-made greeting for a name, in the language you ask for. One credit per name.

```csharp
var anna = await client.SalutationAsync("Dr. Anna Müller", new SalutationOptions { Language = "de" });
// anna.Salutation.Formal "Sehr geehrte Frau Dr. Müller,", anna.Salutation.Informal "Liebe Anna,"

var ahmet = await client.SalutationAsync("Ahmet Yılmaz", new SalutationOptions { Language = "tr" });
// ahmet.Salutation.Formal "Sayın Ahmet Bey,"

// First and last name stored separately: nothing is parsed
await client.SalutationFromPartsAsync("Anna", "Müller", new SalutationOptions { Language = "de", Title = "Dr." });

// Up to 100 names; the options apply to every name, results come back in order
var many = await client.SalutationBulkAsync(new[] { "Dr. Anna Müller", "Acme GmbH" }, new SalutationOptions { Language = "de" });
Console.WriteLine(many.Summary.Gendered);
```

When the gender is not certain, the salutation uses the neutral form
("Guten Tag Anna Müller,") instead of guessing. `Form` is `gendered`, `neutral`
or `organization`, and `Reason` says why it is not gendered (`gender_unknown`,
`below_min_probability`, ...). `BestGuess` does not apply here; lower
`MinProbability` (50–100, default 90) or set a gender you already know with
`Gender = "female"` instead.

| Option | Sent as | Effect |
|---|---|---|
| `Language` | `language` | en, en-US, en-GB, de, de-AT, de-CH, fr, es, it, pt, pt-PT, pt-BR, nl, tr, pl or ja. Unset: the language of `Locale`, else the country's main language, else en. Another value is a 422. |
| `Country`, `Locale`, `Ip` | `country`, `locale`, `ip` | Country hint for the gender lookup, as in [Options](#options). |
| `Gender` | `gender` | `male`, `female` or `neutral`. Overrides the lookup; `neutral` always gives the neutral form. |
| `MinProbability` | `min_probability` | 50–100, default 90. Below it the neutral form is used. |
| `Title` | `title` | An academic title in its own field, e.g. `Dr.`; used in German and English. |

`Parts` holds the pieces of the formal salutation (`Opening`, `Courtesy`,
`Academic`, `Name`); any of them may be null.

## Name check

Whether a name typed into a form looks like a real person's name, with the
reasons. One credit per name.

```csharp
var junk = await client.NameCheckAsync("asdf qwerty");
// junk.Assessment "implausible", junk.Score 0,
// junk.Signals: keyboard_pattern on "asdf" and on "qwerty", ...

var jennifer = await client.NameCheckAsync("Jennifer Null");
// jennifer.Assessment "plausible"

// First and last name stored separately: nothing is parsed
await client.NameCheckFromPartsAsync("Jennifer", "Null", new NameCheckOptions { Country = "US" });

// Up to 100 names; results come back in order
var many = await client.NameCheckBulkAsync(new[] { "Jennifer Null", "asdf qwerty" });
Console.WriteLine(many.Summary.Implausible);
```

`Assessment` is `plausible`, `suspicious` or `implausible`, `Score` runs from
0 to 100, and each signal has a `Code` (`keyboard_pattern`, `placeholder`,
`contains_digits`, `first_name_not_found`, ...), a `Severity` and the `Part`
and `Value` it is about. It never calls a name fake: use it to flag records for
a look, not to reject people automatically. Surnames are judged by their shape
only. `NameCheckOptions` takes `Country`, `Locale` and `Ip`, as in
[Options](#options).

## Age from name

How old the people recorded with a first name are: the median age, the middle
half and the middle 80% of their ages. One credit per name.

```csharp
var brittany = await client.AgeAsync("Brittany");
// brittany.Age 36, brittany.AgeRange 32–38 (middle half), brittany.AgeRange80 28–41,
// brittany.BirthYear 1990, brittany.CountrySource "default" (no hint: US data)

// Only one gender's records
await client.AgeAsync("Jordan", new AgeOptions { Gender = "female", Country = "US" });

// Up to 100 names; the options apply to every name, results come back in order
var many = await client.AgeBulkAsync(new[] { "Brittany", "Margaret" }, new AgeOptions { Country = "US" });
Console.WriteLine(many.Results[1].Age);
```

It covers the US, France and Norway. For another country `Age` is null and
`Reason` is `country_not_covered`, and no credit is charged; `not_found` and
`insufficient_data` are the other reasons. A null `Age` is an answer, not an
exception. `AgeOptions` takes `Gender` (`male` or `female`, narrows to that
gender's records), `Country`, `Locale` and `Ip`, as in [Options](#options);
with no hint the US data is used and `CountrySource` is `default`.

The age describes a group, not a person: never use it for decisions about an
individual.

## Country distribution

Which countries a name is recorded in. This is not a country-of-origin or
ethnicity inference: `Registrations` is counted volume, comparable only among the
countries that publish counted birth statistics, and `AttestedIn` is presence with
no weight attached. Show `Basis.Note` next to any percentage.

```csharp
var dist = await client.CountriesAsync("Mehmet", limit: 10);

foreach (var r in dist.Registrations)
{
    Console.WriteLine($"{r.Country} {r.Share:0.##}%");
}
```

## Account

```csharp
var account = await client.AccountAsync(); // costs no credits
Console.WriteLine(account.CreditsRemaining);
```

## File jobs

Upload a CSV or XLSX file (up to 100 MB and 1,000,000 rows) and get it back
with gender columns added. One credit per row, charged only if the job
completes.

```csharp
var job = await client.CreateBatchAsync("customers.csv", new BatchOptions
{
    NameColumn = "first_name",     // required to start
    CountryColumn = "country",     // optional: a country code per row
});

var done = await client.WaitBatchAsync(job.Id, onProgress: j => Console.WriteLine(j.Progress));
if (done.Status == "failed")
{
    throw new InvalidOperationException(done.Error!.Code);
}

await File.WriteAllBytesAsync("customers-gender.csv", await client.DownloadBatchAsync(done.Id));
```

`CreateBatchAsync` also takes a `byte[]` or a `Stream` with a file name; the
extension (`.csv`, `.xlsx`) tells the API the format. `DownloadBatchAsync` has an
overload that copies into a `Stream`.

`NameColumn` is required to start: a guessed column that turns out to be
wrong would spend credits on the wrong data. To see the columns and the cost
first, upload with `Start = false`, read `job.Inspection`, then call
`client.StartBatchAsync(job.Id, new BatchSettings { NameColumn = "first_name" })`.

`CreateBatchAsync` sends an `Idempotency-Key` and retries network errors,
timeouts and 502/503/504 with the same key, so a retry never opens a second job.
Set `IdempotencyKey` to keep that guarantee across your own retries; `Retries`
(default 2) sets how many extra attempts are made.

`WaitBatchAsync` returns a failed job rather than throwing; branch on
`job.Error.Code`. It throws `NameGenderException` with `StatusCode` 0 when the
timeout (default one hour) runs out. `CancelBatchAsync` returns the credit of a
job that has not started, and deletes a finished one. `ListBatchesAsync(limit, page)`
includes jobs started from the dashboard. Up to three jobs can be queued or
running at once; a fourth is refused with `429 too_many_batches`.

The result appends `gender`, `probability`, `sample_size`, `country`, `source`,
`matched_as`, `first_name`, `middle_name`, `last_name` and `name_type` to every
row. A CSV result starts with a UTF-8 byte order mark so that Excel reads it
correctly.

## Webhooks

Add an endpoint under Webhooks in the dashboard, and NameGender sends a signed
`POST` to it when a file job completes or fails, and when credits are about to
run out (`credits.low`) or have run out (`credits.depleted`, checked hourly).
`NameGenderWebhooks.Verify` checks the signature and the timestamp, and returns
the event. It throws `WebhookVerificationException` for a request that is not
genuine.

```csharp
using NameGender;

var app = WebApplication.CreateBuilder(args).Build();
var secret = Environment.GetEnvironmentVariable("NAMEGENDER_WEBHOOK_SECRET")!;

app.MapPost("/namegender", async (HttpRequest request) =>
{
    // The raw bytes, not a bound model: the signature covers the exact body sent.
    using var body = new MemoryStream();
    await request.Body.CopyToAsync(body);

    WebhookEvent evt;
    try
    {
        evt = NameGenderWebhooks.Verify(body.ToArray(), request.Headers["NameGender-Signature"], secret);
    }
    catch (WebhookVerificationException)
    {
        return Results.BadRequest();
    }

    if (evt.Type == "batch.completed")
    {
        var job = evt.AsBatchJob()!;         // the job, as GetBatchAsync returns it
        // queue the work and answer at once
    }
    else if (evt.Type == "credits.low")
    {
        var alert = evt.AsCreditsAlert()!;   // alert.CreditsRemaining, alert.RunwayDays
    }

    return Results.NoContent();
});

app.Run();
```

If middleware reads the body before your endpoint, call
`request.EnableBuffering()` early and rewind `request.Body` before copying it.

Use `evt.Id` (also the `NameGender-Event-Id` header) to ignore a delivery you
have already handled. A retry carries the same id, and order is not guaranteed.
Answer quickly and do slow work afterwards: anything other than a 2xx within 10
seconds is retried, up to 8 attempts over about 45 hours.

## Errors

Success is the HTTP status. A non-2xx response throws `NameGenderException`:

```csharp
try
{
    await client.NameAsync("Andrea");
}
catch (NameGenderException e) when (e.Code == "no_credits")
{
    // e.StatusCode 402, e.RequestId for support, e.Docs links to the error
}
catch (NameGenderException e) when (e.Code == "rate_limited")
{
    await Task.Delay(e.RetryAfter ?? TimeSpan.FromSeconds(1));
}
```

The full list of codes is in the [API reference](https://namegender.com/docs).

## HttpClient

Reuse one `NameGenderClient`; it is thread-safe. With dependency injection, pass
a client from `IHttpClientFactory` and it will not be disposed for you:

```csharp
services.AddHttpClient("namegender");
services.AddSingleton(sp => new NameGenderClient(
    configuration["NameGender:ApiKey"]!,
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("namegender")));
```

## License

MIT
