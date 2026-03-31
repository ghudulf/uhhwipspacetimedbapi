using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using TicketSalesApp.AdminServer.Configuration;
using System;
using Microsoft.Extensions.Logging;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Routing
{
    /// <summary>
    /// Custom action constraint that selects between two controller actions at runtime
    /// based on a feature flag value. This enables dual-controller architectures where
    /// a legacy and a refactored implementation coexist and are toggled via configuration.
    ///
    /// KEPT FOR FUTURE USE: Even after the AuthController refactoring is complete and the
    /// legacy controller has been removed, this infrastructure is intentionally retained.
    /// It can be reused for any future dual-controller migrations or A/B testing scenarios:
    ///
    ///   1. Create a new "refactored" controller alongside the existing one.
    ///   2. Add a new boolean flag to <see cref="FeatureFlagOptions"/>.
    ///   3. Decorate the legacy action with <see cref="LegacyActionAttribute"/> and the
    ///      new action with <see cref="RefactoredActionAttribute"/>, both referencing the
    ///      new flag property name.
    ///   4. Toggle the flag at runtime via the admin API or appsettings.json to route
    ///      traffic between implementations with zero downtime and instant rollback.
    ///
    /// This pattern is production-grade and has been validated during the AuthController
    /// refactoring (Phases 1-7). Reuse it freely for future incremental migrations.
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

        public int Order => 100; // Higher order ensures this constraint is evaluated before ambiguity detection

        public bool Accept(ActionConstraintContext context)
        {
            var logger = context.RouteContext.HttpContext.RequestServices
                .GetService(typeof(ILogger<FeatureFlagActionConstraintAttribute>)) as ILogger<FeatureFlagActionConstraintAttribute>;

            var featureFlags = context.RouteContext.HttpContext.RequestServices
                .GetService(typeof(IOptions<FeatureFlagOptions>)) as IOptions<FeatureFlagOptions>;

            // ENHANCED DEBUG LOGGING - Log all candidates
            var actionDescriptor = context.CurrentCandidate.Action as Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor;
            var controllerName = actionDescriptor?.ControllerName ?? "Unknown";
            var actionName = actionDescriptor?.ActionName ?? "Unknown";
            var allCandidatesCount = context.Candidates?.Count ?? 0;
            
            logger?.LogInformation(
                "FeatureFlagConstraint.Accept CALLED: Controller={Controller}, Action={Action}, Property={Property}, RequireEnabled={RequireEnabled}, TotalCandidates={TotalCandidates}, Path={Path}, Method={Method}",
                controllerName, actionName, _featureFlagProperty, _requireEnabled, allCandidatesCount,
                context.RouteContext.HttpContext.Request.Path,
                context.RouteContext.HttpContext.Request.Method);

            // Log all candidates for debugging
            if (context.Candidates != null && logger != null)
            {
                for (int i = 0; i < context.Candidates.Count; i++)
                {
                    var candidate = context.Candidates[i];
                    var candidateDescriptor = candidate.Action as Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor;
                    var candidateController = candidateDescriptor?.ControllerName ?? "Unknown";
                    var candidateAction = candidateDescriptor?.ActionName ?? "Unknown";
                    var candidateRoute = candidateDescriptor?.AttributeRouteInfo?.Template ?? "No route";
                    
                    logger.LogInformation(
                        "  Candidate[{Index}]: Controller={Controller}, Action={Action}, Route={Route}",
                        i, candidateController, candidateAction, candidateRoute);
                }
            }

            if (featureFlags == null)
            {
                logger?.LogWarning(
                    "FeatureFlags service not available for {Controller}.{Action}, defaulting to legacy (requireEnabled={RequireEnabled})", 
                    controllerName, actionName, _requireEnabled);
                // If feature flags service is not available, default to legacy (disabled)
                return !_requireEnabled;
            }

            // Use reflection to get the feature flag value
            var flagProperty = typeof(FeatureFlagOptions).GetProperty(_featureFlagProperty);
            if (flagProperty == null)
            {
                logger?.LogError(
                    "Feature flag property '{Property}' not found on FeatureFlagOptions for {Controller}.{Action}",
                    _featureFlagProperty, controllerName, actionName);
                throw new InvalidOperationException($"Feature flag property '{_featureFlagProperty}' not found on FeatureFlagOptions");
            }

            var flagValue = (bool?)flagProperty.GetValue(featureFlags.Value) ?? false;
            var accepted = flagValue == _requireEnabled;

            logger?.LogInformation(
                "FeatureFlagConstraint RESULT: Controller={Controller}, Action={Action}, Property={Property}, FlagValue={FlagValue}, RequireEnabled={RequireEnabled}, Accepted={Accepted}, Path={Path}, Method={Method}",
                controllerName, actionName, _featureFlagProperty, flagValue, _requireEnabled, accepted, 
                context.RouteContext.HttpContext.Request.Path,
                context.RouteContext.HttpContext.Request.Method);

            // Return true if the flag state matches what we require
            return accepted;
        }
    }

    /// <summary>
    /// Marks an action as the REFACTORED (new) version of an endpoint.
    /// The action is selected when the named feature flag is <c>true</c> (ENABLED).
    ///
    /// Reuse this attribute whenever introducing a new controller implementation
    /// alongside a legacy one. Pair it with <see cref="LegacyActionAttribute"/> on
    /// the old action using the same flag property name.
    /// </summary>
    public class RefactoredActionAttribute : FeatureFlagActionConstraintAttribute
    {
        public RefactoredActionAttribute(string featureFlagProperty) 
            : base(featureFlagProperty, requireEnabled: true)
        {
        }
    }

    /// <summary>
    /// Marks an action as the LEGACY (old) version of an endpoint.
    /// The action is selected when the named feature flag is <c>false</c> (DISABLED).
    ///
    /// Reuse this attribute to keep the existing controller action active as the
    /// safe fallback while a refactored version is being validated. Pair it with
    /// <see cref="RefactoredActionAttribute"/> on the new action using the same flag
    /// property name.
    /// </summary>
    public class LegacyActionAttribute : FeatureFlagActionConstraintAttribute
    {
        public LegacyActionAttribute(string featureFlagProperty) 
            : base(featureFlagProperty, requireEnabled: false)
        {
        }
    }
}
