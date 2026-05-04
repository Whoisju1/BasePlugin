using System;
using Microsoft.Xrm.Sdk;

namespace SHI.CRM.Plugins.Base.Validation
{
    /// <summary>
    /// Helpers for reading plugin execution context inputs.
    /// </summary>
    public static class ContextInputExtensions
    {
        public static T GetInputParameter<T>(IPluginExecutionContext context, string key)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Parameter key is required.", nameof(key));

            if (!context.InputParameters.Contains(key))
                return default(T);

            var value = context.InputParameters[key];
            if (value == null)
                return default(T);
            if (value is T typed)
                return typed;

            throw new InvalidPluginExecutionException(
                $"Input parameter '{key}' is not of expected type {typeof(T).Name}."
            );
        }

        public static T GetRequiredInputParameter<T>(IPluginExecutionContext context, string key)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Parameter key is required.", nameof(key));

            if (!context.InputParameters.Contains(key) || context.InputParameters[key] == null)
            {
                throw new InvalidPluginExecutionException(
                    $"Missing required input parameter '{key}'."
                );
            }

            var value = context.InputParameters[key];
            if (value is T typed)
                return typed;

            throw new InvalidPluginExecutionException(
                $"Input parameter '{key}' is not of expected type {typeof(T).Name}."
            );
        }
    }
}
