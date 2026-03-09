using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using TicketSalesApp.AdminServer.Configuration;
using System;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Routing
{
    /// <summary>
    /// Custom action constraint that selects between legacy and refactored controllers
    /// based on feature flags. This allows dynamic routing at the action level.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class FeatureFlagActionConstraintAttribute : Attribute, IActionConstraint
    {
        private readonly string _featureFlagProperty;
        private readonly bool _requireEnabled;

        /// <summary>
        /// Creates a constraint that checks a specific feature flag.
        /// </summary>
        /// <param name="featureFlagProperty">The property name in FeatureFlagOptions (e.g., "EnableLoginRefactoring")</param>
        /// <param name="requireEnabled">If true, action is selected when flag is enabled. If false, selected when disabled.</param>
        public FeatureFlagActionConstraintAttribute(string featureFlagProperty, bool requireEnabled = true)
        {
            _featureFlagProperty = featureFlagProperty ?? throw new ArgumentNullException(nameof(featureFlagProperty));
            _requireEnabled = requireEnabled;
        }

        public int Order => 0;

        public bool Accept(ActionConstraintContext context)
        {
            var featureFlags = context.RouteContext.HttpContext.RequestServices
                .GetService(typeof(IOptions<FeatureFlagOptions>)) as IOptions<FeatureFlagOptions>;

            if (featureFlags == null)
            {
                // If feature flags service is not available, default to legacy (disabled)
                return !_requireEnabled;
            }

            // Use reflection to get the feature flag value
            var flagProperty = typeof(FeatureFlagOptions).GetProperty(_featureFlagProperty);
            if (flagProperty == null)
            {
                throw new InvalidOperationException($"Feature flag property '{_featureFlagProperty}' not found on FeatureFlagOptions");
            }

            var flagValue = (bool?)flagProperty.GetValue(featureFlags.Value) ?? false;

            // Return true if the flag state matches what we require
            return flagValue == _requireEnabled;
        }
    }

    /// <summary>
    /// Marks an action as the REFACTORED version (selected when feature flag is ENABLED)
    /// </summary>
    public class RefactoredActionAttribute : FeatureFlagActionConstraintAttribute
    {
        public RefactoredActionAttribute(string featureFlagProperty) 
            : base(featureFlagProperty, requireEnabled: true)
        {
        }
    }

    /// <summary>
    /// Marks an action as the LEGACY version (selected when feature flag is DISABLED)
    /// </summary>
    public class LegacyActionAttribute : FeatureFlagActionConstraintAttribute
    {
        public LegacyActionAttribute(string featureFlagProperty) 
            : base(featureFlagProperty, requireEnabled: false)
        {
        }
    }
}
