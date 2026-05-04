using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace SHI.CRM.Plugins.Base.Telemetry
{
    /// <summary>
    /// Reads Dataverse Environment Variable values with fallback to defaults.
    /// </summary>
    internal static class EnvironmentVariableReader
    {
        public static string GetValue(IOrganizationService orgService, string schemaName)
        {
            if (orgService == null || string.IsNullOrWhiteSpace(schemaName))
                return null;

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
    }
}
