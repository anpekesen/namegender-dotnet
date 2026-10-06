using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NameGender
{
    // Name check: whether a name typed into a form looks like a real person's name.
    public sealed partial class NameGenderClient
    {
        /// <summary>
        /// Whether a full name typed into a form looks like a real person's name, with the reasons. One credit.
        /// </summary>
        /// <remarks>
        /// It never calls a name fake: use <see cref="NameCheckResult.Assessment"/> to flag records for a look,
        /// not to reject people automatically. Surnames are judged by their shape only.
        /// </remarks>
        public Task<NameCheckResponse> NameCheckAsync(string name, NameCheckOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            var payload = new Dictionary<string, object> { ["name"] = name };
            ApplyNameCheck(payload, options);

            return SendAsync<NameCheckResponse>(HttpMethod.Post, "/name-check", payload, cancellationToken);
        }

        /// <summary>
        /// As <see cref="NameCheckAsync"/>, for a first and last name stored separately. Nothing is
        /// parsed. Either may be null, not both.
        /// </summary>
        public Task<NameCheckResponse> NameCheckFromPartsAsync(string? firstName, string? lastName, NameCheckOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (firstName == null && lastName == null)
            {
                throw new ArgumentException("A first or last name is required.", nameof(firstName));
            }

            var payload = new Dictionary<string, object>();

            if (firstName != null)
            {
                payload["first_name"] = firstName;
            }

            if (lastName != null)
            {
                payload["last_name"] = lastName;
            }

            ApplyNameCheck(payload, options);

            return SendAsync<NameCheckResponse>(HttpMethod.Post, "/name-check", payload, cancellationToken);
        }

        /// <summary>
        /// Up to 100 names in one request, answered in the order sent. One credit per name; the
        /// options apply to every name.
        /// </summary>
        public Task<NameCheckBulkResponse> NameCheckBulkAsync(IEnumerable<string> names, NameCheckOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (names == null)
            {
                throw new ArgumentNullException(nameof(names));
            }

            var payload = new Dictionary<string, object> { ["names"] = names.ToList() };
            ApplyNameCheck(payload, options);

            return SendAsync<NameCheckBulkResponse>(HttpMethod.Post, "/name-check/bulk", payload, cancellationToken);
        }

        private static void ApplyNameCheck(Dictionary<string, object> payload, NameCheckOptions? options)
        {
            if (options == null)
            {
                return;
            }

            AddText(payload, "country", options.Country);
            AddText(payload, "locale", options.Locale);
            AddText(payload, "ip", options.Ip);
        }
    }
}
