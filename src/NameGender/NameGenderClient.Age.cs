using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NameGender
{
    // Age: how old the people recorded with a first name are.
    public sealed partial class NameGenderClient
    {
        /// <summary>
        /// The median age of the people recorded with a first name, with the middle half and middle
        /// 80% of their ages. One credit; none when the country is not covered.
        /// </summary>
        /// <remarks>
        /// It describes a group, not a person: never use it for decisions about an individual.
        /// Covers the US, France and Norway; for other countries <see cref="AgeResult.Age"/> is null and
        /// <see cref="AgeResult.Reason"/> is <c>country_not_covered</c>. A null age is an answer, not an error.
        /// </remarks>
        public Task<AgeResponse> AgeAsync(string name, AgeOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            var payload = new Dictionary<string, object> { ["name"] = name };
            ApplyAge(payload, options);

            return SendAsync<AgeResponse>(HttpMethod.Post, "/age", payload, cancellationToken);
        }

        /// <summary>
        /// Up to 100 names in one request, answered in the order sent. The options apply to every name.
        /// </summary>
        public Task<AgeBulkResponse> AgeBulkAsync(IEnumerable<string> names, AgeOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (names == null)
            {
                throw new ArgumentNullException(nameof(names));
            }

            var payload = new Dictionary<string, object> { ["names"] = names.ToList() };
            ApplyAge(payload, options);

            return SendAsync<AgeBulkResponse>(HttpMethod.Post, "/age/bulk", payload, cancellationToken);
        }

        private static void ApplyAge(Dictionary<string, object> payload, AgeOptions? options)
        {
            if (options == null)
            {
                return;
            }

            AddText(payload, "gender", options.Gender);
            AddText(payload, "country", options.Country);
            AddText(payload, "locale", options.Locale);
            AddText(payload, "ip", options.Ip);
        }
    }
}
