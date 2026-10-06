using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NameGender
{
    // Salutations: a ready-made greeting for a name, in the language asked for.
    public sealed partial class NameGenderClient
    {
        /// <summary>
        /// A salutation for a full name, titles included ("Dr. Anna Müller"). One credit.
        /// </summary>
        /// <remarks>
        /// The neutral form is used when the gender is not certain; <see cref="SalutationResult.Form"/>
        /// and <see cref="SalutationResult.Reason"/> say why.
        /// </remarks>
        public Task<SalutationResponse> SalutationAsync(string name, SalutationOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            var payload = new Dictionary<string, object> { ["name"] = name };
            Apply(payload, options);

            return SendAsync<SalutationResponse>(HttpMethod.Post, "/salutation", payload, cancellationToken);
        }

        /// <summary>
        /// As <see cref="SalutationAsync"/>, for a first and last name stored separately. Nothing is
        /// parsed. Either may be null, not both.
        /// </summary>
        public Task<SalutationResponse> SalutationFromPartsAsync(string? firstName, string? lastName, SalutationOptions? options = null, CancellationToken cancellationToken = default)
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

            Apply(payload, options);

            return SendAsync<SalutationResponse>(HttpMethod.Post, "/salutation", payload, cancellationToken);
        }

        /// <summary>
        /// Up to 100 names in one request, answered in the order sent. One credit per name; the
        /// options apply to every name.
        /// </summary>
        public Task<SalutationBulkResponse> SalutationBulkAsync(IEnumerable<string> names, SalutationOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (names == null)
            {
                throw new ArgumentNullException(nameof(names));
            }

            var payload = new Dictionary<string, object> { ["names"] = names.ToList() };
            Apply(payload, options);

            return SendAsync<SalutationBulkResponse>(HttpMethod.Post, "/salutation/bulk", payload, cancellationToken);
        }

        private static void Apply(Dictionary<string, object> payload, SalutationOptions? options)
        {
            if (options == null)
            {
                return;
            }

            AddText(payload, "language", options.Language);
            AddText(payload, "country", options.Country);
            AddText(payload, "locale", options.Locale);
            AddText(payload, "ip", options.Ip);
            AddText(payload, "gender", options.Gender);

            if (options.MinProbability.HasValue)
            {
                payload["min_probability"] = options.MinProbability.Value;
            }

            AddText(payload, "title", options.Title);
        }

        private static void AddText(Dictionary<string, object> payload, string field, string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                payload[field] = value!;
            }
        }
    }
}
