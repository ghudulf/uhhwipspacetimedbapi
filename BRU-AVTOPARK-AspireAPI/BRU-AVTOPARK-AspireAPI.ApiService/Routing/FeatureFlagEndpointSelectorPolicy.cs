using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Matching;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketSalesApp.AdminServer.Configuration;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Reflection;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Routing
{
    /// <summary>
    /// CRITICAL POLICY: Resolves ambiguous endpoint matches using feature flags.
    /// This policy runs AFTER action constraints but BEFORE the endpoint selector.
    /// It prevents AmbiguousMatchException by forcing selection of the correct endpoint.
    /// 
    /// Order: 1000 (runs late, after standard policies)
    /// </summary>
    public class FeatureFlagEndpointSelectorPolicy : IEndpointSelectorPolicy
    {
        private readonly ILogger<FeatureFlagEndpointSelectorPolicy> _logger;
        private readonly IOptions<FeatureFlagOptions> _featureFlags;

        public FeatureFlagEndpointSelectorPolicy(
            ILogger<FeatureFlagEndpointSelectorPolicy> logger,
            IOptions<FeatureFlagOptions> featureFlags)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _featureFlags = featureFlags ?? throw new ArgumentNullException(nameof(featureFlags));
        }

        /// <summary>
        /// High order ensures this runs AFTER standard policies but BEFORE ambiguity detection
        /// </summary>
        public int Order => 1000;

        /// <summary>
        /// Determines which endpoints this policy applies to.
        /// Returns true for all endpoints to ensure we can resolve any ambiguity.
        /// </summary>
        public bool AppliesToEndpoints(IReadOnlyList<Endpoint> endpoints)
        {
            // Apply to all endpoints - we need to check every request for potential ambiguity
            // This ensures the policy runs for EVERY request and can resolve ambiguity
            return true;
        }

        public Task ApplyAsync(HttpContext httpContext, CandidateSet candidates)
        {
            try
            {
                var path = httpContext.Request.Path.Value ?? "";
                var method = httpContext.Request.Method;

                _logger.LogInformation(
                    "FeatureFlagEndpointSelectorPolicy.ApplyAsync: CALLED for {Method} {Path} with {Count} candidates",
                    method, path, candidates.Count);

                // Count valid candidates
                int validCount = 0;
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (candidates.IsValidCandidate(i))
                    {
                        validCount++;
                        var endpoint = candidates[i].Endpoint;
                        _logger.LogInformation("  Policy Candidate[{Index}]: {DisplayName} - VALID", 
                            i, endpoint.DisplayName);
                    }
                    else
                    {
                        var endpoint = candidates[i].Endpoint;
                        _logger.LogInformation("  Policy Candidate[{Index}]: {DisplayName} - INVALID", 
                            i, endpoint.DisplayName);
                    }
                }

                _logger.LogInformation(
                    "FeatureFlagEndpointSelectorPolicy: Found {ValidCount} valid candidates",
                    validCount);

                // NO AMBIGUITY - let default selector handle it
                if (validCount <= 1)
                {
                    _logger.LogInformation("FeatureFlagEndpointSelectorPolicy: No ambiguity - skipping policy");
                    return Task.CompletedTask;
                }

                // AMBIGUITY DETECTED - resolve using feature flags
                _logger.LogWarning(
                    "FeatureFlagEndpointSelectorPolicy: AMBIGUITY DETECTED - {Count} valid candidates - RESOLVING",
                    validCount);

                // Find candidates with feature flag attributes
                int? refactoredIndex = null;
                int? legacyIndex = null;
                string? featureFlagProperty = null;

                for (int i = 0; i < candidates.Count; i++)
                {
                    if (!candidates.IsValidCandidate(i))
                        continue;

                    var endpoint = candidates[i].Endpoint;
                    var metadata = endpoint.Metadata;

                    // Check for RefactoredAction
                    var refactoredAttr = metadata.GetMetadata<RefactoredActionAttribute>();
                    if (refactoredAttr != null)
                    {
                        refactoredIndex = i;
                        featureFlagProperty = ExtractFeatureFlagProperty(refactoredAttr);
                        _logger.LogInformation("  Policy found Refactored candidate[{Index}] with flag: {Flag}", 
                            i, featureFlagProperty);
                    }

                    // Check for LegacyAction
                    var legacyAttr = metadata.GetMetadata<LegacyActionAttribute>();
                    if (legacyAttr != null)
                    {
                        legacyIndex = i;
                        if (featureFlagProperty == null)
                        {
                            featureFlagProperty = ExtractFeatureFlagProperty(legacyAttr);
                        }
                        _logger.LogInformation("  Policy found Legacy candidate[{Index}] with flag: {Flag}", 
                            i, featureFlagProperty);
                    }
                }

                // Resolve ambiguity
                if (refactoredIndex.HasValue && legacyIndex.HasValue && featureFlagProperty != null)
                {
                    ResolveWithFeatureFlag(candidates, refactoredIndex.Value, legacyIndex.Value, 
                        featureFlagProperty, httpContext);
                }
                else
                {
                    // Fallback: No feature flag attributes - select first valid
                    _logger.LogWarning(
                        "FeatureFlagEndpointSelectorPolicy: No feature flags found - selecting first valid candidate");
                    
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        if (candidates.IsValidCandidate(i))
                        {
                            ForceSelectCandidate(candidates, i);
                            break;
                        }
                    }
                }

                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "FeatureFlagEndpointSelectorPolicy: CRITICAL ERROR - selecting first valid candidate");
                
                // CRITICAL FAILSAFE
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (candidates.IsValidCandidate(i))
                    {
                        ForceSelectCandidate(candidates, i);
                        break;
                    }
                }

                return Task.CompletedTask;
            }
        }

        private void ResolveWithFeatureFlag(
            CandidateSet candidates,
            int refactoredIndex,
            int legacyIndex,
            string featureFlagProperty,
            HttpContext httpContext)
        {
            try
            {
                var flagProperty = typeof(FeatureFlagOptions).GetProperty(featureFlagProperty);
                if (flagProperty == null)
                {
                    _logger.LogError(
                        "FeatureFlagEndpointSelectorPolicy: Feature flag property {Property} NOT FOUND",
                        featureFlagProperty);
                    
                    ForceSelectCandidate(candidates, refactoredIndex);
                    return;
                }

                var flagValue = (bool?)flagProperty.GetValue(_featureFlags.Value) ?? false;

                int selectedIndex = flagValue ? refactoredIndex : legacyIndex;
                string selectedController = flagValue ? "AuthControllerRefactored" : "AuthController";

                _logger.LogWarning(
                    "FeatureFlagEndpointSelectorPolicy: Flag {Flag} = {Value} -> SELECTING candidate[{Index}] ({Controller})",
                    featureFlagProperty, flagValue, selectedIndex, selectedController);

                ForceSelectCandidate(candidates, selectedIndex);

                // Store decision in HttpContext
                httpContext.Items["FeatureFlagRouting_SelectedController"] = selectedController;
                httpContext.Items["FeatureFlagRouting_Flag"] = featureFlagProperty;
                httpContext.Items["FeatureFlagRouting_Value"] = flagValue;
                httpContext.Items["FeatureFlagRouting_ResolvedAmbiguity"] = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "FeatureFlagEndpointSelectorPolicy: ERROR reading feature flag {Property}",
                    featureFlagProperty);
                
                ForceSelectCandidate(candidates, refactoredIndex);
            }
        }

        private void ForceSelectCandidate(CandidateSet candidates, int selectedIndex)
        {
            _logger.LogWarning(
                "FeatureFlagEndpointSelectorPolicy: FORCING selection of candidate[{Index}] - invalidating all others",
                selectedIndex);

            // Mark all OTHER candidates as invalid
            for (int i = 0; i < candidates.Count; i++)
            {
                if (i != selectedIndex && candidates.IsValidCandidate(i))
                {
                    candidates.SetValidity(i, false);
                    _logger.LogInformation("  Policy marked candidate[{Index}] as INVALID", i);
                }
            }

            _logger.LogInformation(
                "FeatureFlagEndpointSelectorPolicy: Successfully selected candidate[{Index}] - {DisplayName}",
                selectedIndex, candidates[selectedIndex].Endpoint.DisplayName);
        }

        private string? ExtractFeatureFlagProperty(object attribute)
        {
            try
            {
                var attributeType = attribute.GetType();
                
                // Try multiple strategies
                var field = attributeType.GetField("_featureFlagProperty", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    return field.GetValue(attribute) as string;
                }

                field = attributeType.BaseType?.GetField("_featureFlagProperty", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    return field.GetValue(attribute) as string;
                }

                // Walk up inheritance chain
                var currentType = attributeType;
                while (currentType != null && currentType != typeof(object))
                {
                    field = currentType.GetField("_featureFlagProperty", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field != null)
                    {
                        return field.GetValue(attribute) as string;
                    }
                    currentType = currentType.BaseType;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FeatureFlagEndpointSelectorPolicy: ERROR extracting feature flag property");
                return null;
            }
        }
    }
}
