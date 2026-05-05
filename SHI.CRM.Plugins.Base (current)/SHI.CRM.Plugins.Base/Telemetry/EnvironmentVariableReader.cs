using System;
using System.Collections.Concurrent;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace SHI.CRM.Plugins.Base.Telemetry
{
    /// <summary>
    /// Reads Dataverse Environment Variable values with fallback to defaults.
    /// Supports a per-process TTL cache so hot-path callers can avoid a Dataverse round-trip on every plug-in execution.
    /// </summary>
    internal static class EnvironmentVariableReader
    {
        private static readonly ConcurrentDictionary<string, CacheEntry> _cache =
            new ConcurrentDictionary<string, CacheEntry>(StringComparer.Ordinal);

        public static string GetValue(IOrganizationService orgService, string schemaName)
        {
            if (orgService == null || string.IsNullOrWhiteSpace(schemaName))
                return null;

            return Lookup(orgService, schemaName);
        }

        /// <summary>
        /// Returns a per-process cached value with the given TTL. On a hit within TTL, no Dataverse call is made.
        /// On expiry or miss, a fresh lookup runs and the result (including null) is cached.
        /// </summary>
        public static string GetValue(
            IOrganizationService orgService,
            string schemaName,
            TimeSpan cacheFor
        )
        {
            if (orgService == null || string.IsNullOrWhiteSpace(schemaName))
                return null;

            if (cacheFor <= TimeSpan.Zero)
                return Lookup(orgService, schemaName);

            var nowUtc = DateTime.UtcNow;
            if (_cache.TryGetValue(schemaName, out var entry) && entry.ExpiresUtc > nowUtc)
            {
                return entry.Value;
            }

            var value = Lookup(orgService, schemaName);
            _cache[schemaName] = new CacheEntry(value, nowUtc.Add(cacheFor));
            return value;
        }

        /// <summary>
        /// Clears the per-process cache. Intended for test isolation.
        /// </summary>
        public static void ClearCache() => _cache.Clear();

        private static string Lookup(IOrganizationService orgService, string schemaName)
        {
            try
            {
                var query = new QueryExpression("environmentvariabledefinition")
                {
                    ColumnSet = new ColumnSet("defaultvalue"),
                    Criteria =
                    {
                        Conditions =
                        {
                            new ConditionExpression(
                                "schemaname",
                                ConditionOperator.Equal,
                                schemaName
                            ),
                        },
                    },
                };

                query.LinkEntities.Add(
                    new LinkEntity(
                        "environmentvariabledefinition",
                        "environmentvariablevalue",
                        "environmentvariabledefinitionid",
                        "environmentvariabledefinitionid",
                        JoinOperator.LeftOuter
                    )
                    {
                        Columns = new ColumnSet("value"),
                        EntityAlias = "v",
                    }
                );

                var result = orgService.RetrieveMultiple(query);
                if (result?.Entities == null || result.Entities.Count == 0)
                    return null;

                var definition = result.Entities[0];
                var explicitValue =
                    definition.GetAttributeValue<AliasedValue>("v.value")?.Value as string;
                if (!string.IsNullOrWhiteSpace(explicitValue))
                    return explicitValue;

                return definition.GetAttributeValue<string>("defaultvalue");
            }
            catch
            {
                return null;
            }
        }

        private readonly struct CacheEntry
        {
            public CacheEntry(string value, DateTime expiresUtc)
            {
                Value = value;
                ExpiresUtc = expiresUtc;
            }

            public string Value { get; }
            public DateTime ExpiresUtc { get; }
        }
    }
}
