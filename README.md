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
usernames. For files of hundreds of thousands of rows, the CSV/XLSX upload in the
dashboard is faster than looping over this method.

## Options

| Option | Sent as | Effect |
|---|---|---|
| `Country` | `country` | ISO 3166-1 alpha-2 hint. Andrea is male in Italy and female in Germany. |
| `AiFallback` | `ai_fallback` | Ask a language model when the name is not in the data. Needs AI consent on the account. |
| `BestGuess` | `best_guess` | Return the likelier gender even below the probability threshold. |

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
