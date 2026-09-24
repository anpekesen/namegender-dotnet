using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NameGender
{
    /// <summary>
    /// The request is not a genuine NameGender webhook. Answer it with 400 and do nothing else.
    /// </summary>
    public sealed class WebhookVerificationException : NameGenderException
    {
        /// <summary>Creates the exception. Usually thrown by <see cref="NameGenderWebhooks.Verify(byte[], string?, string, TimeSpan?, DateTimeOffset?)"/>.</summary>
        public WebhookVerificationException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Webhook signature check.
    /// </summary>
    /// <remarks>
    /// Pass the body exactly as received. Binding it to a model and serialising it again
    /// changes the bytes, and the signature no longer matches.
    /// </remarks>
    public static class NameGenderWebhooks
    {
        /// <summary>Header that carries the signature.</summary>
        public const string SignatureHeader = "NameGender-Signature";

        /// <summary>Header that carries the event id; the same as <see cref="WebhookEvent.Id"/>.</summary>
        public const string EventIdHeader = "NameGender-Event-Id";

        /// <summary>
        /// Checks <c>NameGender-Signature</c> and returns the parsed event. During a secret
        /// rotation the header carries two <c>v1</c> values; either one matching is enough.
        /// </summary>
        /// <param name="payload">The raw request body.</param>
        /// <param name="signatureHeader">The <c>NameGender-Signature</c> header; null when it is missing.</param>
        /// <param name="secret">The endpoint secret from the dashboard (<c>whsec_…</c>).</param>
        /// <param name="tolerance">How far the signed timestamp may be from <paramref name="now"/>. Default 5 minutes.</param>
        /// <param name="now">Current time; for tests.</param>
        /// <exception cref="WebhookVerificationException">Missing or malformed header, a stale timestamp, or no matching signature.</exception>
        public static WebhookEvent Verify(byte[] payload, string? signatureHeader, string secret, TimeSpan? tolerance = null, DateTimeOffset? now = null)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            if (string.IsNullOrEmpty(secret))
            {
                throw new ArgumentException("The webhook secret is required.", nameof(secret));
            }

            if (string.IsNullOrEmpty(signatureHeader))
            {
                throw new WebhookVerificationException("Missing NameGender-Signature header");
            }

            long? timestamp = null;
            var signatures = new List<string>();

            foreach (var part in signatureHeader!.Split(','))
            {
                var trimmed = part.Trim();
                var equals = trimmed.IndexOf('=');

                if (equals < 0)
                {
                    continue;
                }

                var key = trimmed.Substring(0, equals);
                var value = trimmed.Substring(equals + 1);

                if (key == "t" && IsDigits(value) && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
                {
                    timestamp = seconds;
                }
                else if (key == "v1" && value.Length > 0)
                {
                    signatures.Add(value);
                }
            }

            if (timestamp == null || signatures.Count == 0)
            {
                throw new WebhookVerificationException("Malformed NameGender-Signature header");
            }

            var current = (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();

            if (Math.Abs(current - timestamp.Value) > (tolerance ?? TimeSpan.FromMinutes(5)).TotalSeconds)
            {
                throw new WebhookVerificationException("Webhook timestamp is outside the tolerance window");
            }

            var prefix = Encoding.UTF8.GetBytes(timestamp.Value.ToString(CultureInfo.InvariantCulture) + ".");
            var signed = new byte[prefix.Length + payload.Length];
            Buffer.BlockCopy(prefix, 0, signed, 0, prefix.Length);
            Buffer.BlockCopy(payload, 0, signed, prefix.Length, payload.Length);

            string expected;

            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret)))
            {
                expected = ToHex(hmac.ComputeHash(signed));
            }

            var matched = false;

            foreach (var signature in signatures)
            {
                // No early exit: every candidate is compared, in constant time.
                matched |= FixedTimeEquals(expected, signature);
            }

            if (!matched)
            {
                throw new WebhookVerificationException("Webhook signature does not match");
            }

            return JsonSerializer.Deserialize<WebhookEvent>(payload)
                ?? throw new WebhookVerificationException("Webhook body is empty");
        }

        /// <summary>
        /// Same as <see cref="Verify(byte[], string?, string, TimeSpan?, DateTimeOffset?)"/> for a body
        /// already read as text. Prefer the bytes: decoding and re-encoding can alter them.
        /// </summary>
        public static WebhookEvent Verify(string payload, string? signatureHeader, string secret, TimeSpan? tolerance = null, DateTimeOffset? now = null)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            return Verify(Encoding.UTF8.GetBytes(payload), signatureHeader, secret, tolerance, now);
        }

        private static bool IsDigits(string value)
        {
            if (value.Length == 0)
            {
                return false;
            }

            foreach (var c in value)
            {
                if (c < '0' || c > '9')
                {
                    return false;
                }
            }

            return true;
        }

        private static string ToHex(byte[] bytes)
        {
            var hex = new StringBuilder(bytes.Length * 2);

            foreach (var b in bytes)
            {
                hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            }

            return hex.ToString();
        }

        // Constant-time: the comparison must not reveal how many leading characters matched.
        // CryptographicOperations.FixedTimeEquals is not in netstandard2.0.
        private static bool FixedTimeEquals(string a, string b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            var diff = 0;

            for (var i = 0; i < a.Length; i++)
            {
                diff |= a[i] ^ b[i];
            }

            return diff == 0;
        }
    }
}
