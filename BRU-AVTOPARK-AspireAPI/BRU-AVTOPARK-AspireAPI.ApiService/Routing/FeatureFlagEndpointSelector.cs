using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Matching;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketSalesApp.AdminServer.Configuration;
using System;
using System.Threading.Tasks;
using System.Linq;
using System.Reflection;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Routing
{
    /// <summary>
    /// CUSTOM ENDPOINT SELECTOR that completely replaces ASP.NET Core's default selector.
    /// This selector NEVER throws AmbiguousMatchException and instead uses feature flags
    /// to resolve ambiguity between legacy and refactored controllers.
    /// </summary>
    public class FeatureFlagEndpointSelector : EndpointSelector
    {
        private readonly ILogger<FeatureFlagEndpointSelector> _logger;
        private readonly IOptions<FeatureFlagOptions> _featureFlags;

        public FeatureFlagEndpointSelector(
            ILogger<FeatureFlagEndpointSelector> logger,
            IOptions<FeatureFlagOptions> featureFlags)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _featureFlags = featureFlags ?? throw new ArgumentNullException(nameof(featureFlags));
        }

        public override Task SelectAsync(HttpContext httpContext, CandidateSet candidateSet)
        {
            try
            {
                var path = httpContext.Request.Path.Value ?? "";
                var method = httpContext.Request.Method;

                _logger.LogInformation(
                    "FeatureFlagEndpointSelector.SelectAsync: CALLED for {Method} {Path} with {Count} candidates",
                    method, path, candidateSet.Count);

                // Count valid candidates
                int validCount = 0;
                int? firstValidIndex = null;

                for (int i = 0; i < candidateSet.Count; i++)
                {
                    if (candidateSet.IsValidCandidate(i))
                    {
                        validCount++;
                        if (firstValidIndex == null)
                        {
                            firstValidIndex = i;
                        }

                        var endpoint = candidateSet[i].Endpoint;
                        _logger.LogInformation("  Candidate[{Index}]: {DisplayName} - VALID", 
                            i, endpoint.DisplayName);
                    }
                    else
                    {
                        var endpoint = candidateSet[i].Endpoint;
                        _logger.LogInformation("  Candidate[{Index}]: {DisplayName} - INVALID", 
                            i, endpoint.DisplayName);
                    }
                }

                _logger.LogInformation(
                    "FeatureFlagEndpointSelector: Found {ValidCount} valid candidates out of {TotalCount}",
                    validCount, candidateSet.Count);

                // NO AMBIGUITY - single or no valid candidate
                if (validCount <= 1)
                {
                    _logger.LogInformation("FeatureFlagEndpointSelector: No ambiguity - {Count} valid candidate(s)", validCount);
                    
                    // Log final state before returning
                    for (int i = 0; i < candidateSet.Count; i++)
                    {
                        var isValid = candidateSet.IsValidCandidate(i);
                        var displayName = candidateSet[i].Endpoint.DisplayName;
                        _logger.LogWarning("  FINAL Candidate[{Index}]: Valid={Valid}, DisplayName={DisplayName}", 
                            i, isValid, displayName);
                    }
                    
                    if (validCount == 0)
                    {
                        _logger.LogError("FeatureFlagEndpointSelector: CRITICAL - NO VALID CANDIDATES! This will result in 404!");
                    }
                    else if (validCount == 1)
                    {
                        _logger.LogInformation("FeatureFlagEndpointSelector: Single valid candidate - endpoint should be selected by ASP.NET Core");
                    }
                    
                    return Task.CompletedTask;
                }

                // AMBIGUITY DETECTED - resolve using feature flags
                _logger.LogWarning(
                    "FeatureFlagEndpointSelector: AMBIGUITY DETECTED - {Count} valid candidates for {Method} {Path} - RESOLVING WITH FEATURE FLAGS",
                    validCount, method, path);

                // Find candidates with feature flag attributes
                int? refactoredIndex = null;
                int? legacyIndex = null;
                string? featureFlagProperty = null;

                for (int i = 0; i < candidateSet.Count; i++)
                {
                    if (!candidateSet.IsValidCandidate(i))
                        continue;

                    var endpoint = candidateSet[i].Endpoint;
                    var metadata = endpoint.Metadata;

                    // Check for RefactoredAction
                    var refactoredAttr = metadata.GetMetadata<RefactoredActionAttribute>();
                    if (refactoredAttr != null)
                    {
                        refactoredIndex = i;
                        featureFlagProperty = ExtractFeatureFlagProperty(refactoredAttr);
                        _logger.LogInformation("  Found Refactored candidate[{Index}] with flag: {Flag}", 
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
                        _logger.LogInformation("  Found Legacy candidate[{Index}] with flag: {Flag}", 
                            i, featureFlagProperty);
                    }
                }

                // Resolve ambiguity using feature flags
                if (refactoredIndex.HasValue && legacyIndex.HasValue && featureFlagProperty != null)
                {
                    // BOTH refactored and legacy found - resolve with feature flag
                    ResolveWithFeatureFlag(candidateSet, refactoredIndex.Value, legacyIndex.Value, 
                        featureFlagProperty, httpContext);
                }
                else if (refactoredIndex.HasValue && featureFlagProperty != null)
                {
                    // Only refactored found - check if flag is enabled
                    _logger.LogWarning(
                        "FeatureFlagEndpointSelector: Only REFACTORED candidate found - checking feature flag {Flag}",
                        featureFlagProperty);
                    
                    var flagProperty = typeof(FeatureFlagOptions).GetProperty(featureFlagProperty);
                    if (flagProperty != null)
                    {
                        var flagValue = (bool?)flagProperty.GetValue(_featureFlags.Value) ?? false;
                        
                        if (flagValue)
                        {
                            // Flag is enabled - keep refactored, invalidate others
                            _logger.LogInformation(
                                "FeatureFlagEndpointSelector: Flag {Flag} = TRUE - keeping refactored candidate[{Index}]",
                                featureFlagProperty, refactoredIndex.Value);
                            
                            ForceSelectCandidate(candidateSet, refactoredIndex.Value);
                        }
                        else
                        {
                            // Flag is disabled but no legacy found - keep first valid
                            _logger.LogWarning(
                                "FeatureFlagEndpointSelector: Flag {Flag} = FALSE but no legacy candidate - keeping first valid",
                                featureFlagProperty);
                            
                            ForceSelectCandidate(candidateSet, firstValidIndex.Value);
                        }
                    }
                    else
                    {
                        ForceSelectCandidate(candidateSet, refactoredIndex.Value);
                    }
                }
                else if (legacyIndex.HasValue && featureFlagProperty != null)
                {
                    // Only legacy found - check if flag is disabled
                    _logger.LogWarning(
                        "FeatureFlagEndpointSelector: Only LEGACY candidate found - checking feature flag {Flag}",
                        featureFlagProperty);
                    
                    var flagProperty = typeof(FeatureFlagOptions).GetProperty(featureFlagProperty);
                    if (flagProperty != null)
                    {
                        var flagValue = (bool?)flagProperty.GetValue(_featureFlags.Value) ?? false;
                        
                        if (!flagValue)
                        {
                            // Flag is disabled - keep legacy, invalidate others
                            _logger.LogInformation(
                                "FeatureFlagEndpointSelector: Flag {Flag} = FALSE - keeping legacy candidate[{Index}]",
                                featureFlagProperty, legacyIndex.Value);
                            
                            ForceSelectCandidate(candidateSet, legacyIndex.Value);
                        }
                        else
                        {
                            // Flag is enabled but no refactored found - keep first valid
                            _logger.LogWarning(
                                "FeatureFlagEndpointSelector: Flag {Flag} = TRUE but no refactored candidate - keeping first valid",
                                featureFlagProperty);
                            
                            ForceSelectCandidate(candidateSet, firstValidIndex.Value);
                        }
                    }
                    else
                    {
                        ForceSelectCandidate(candidateSet, legacyIndex.Value);
                    }
                }
                else
                {
                    // Fallback: No feature flag attributes - select first valid candidate
                    _logger.LogWarning(
                        "FeatureFlagEndpointSelector: No feature flag attributes found - FORCING selection of first valid candidate[{Index}]",
                        firstValidIndex);
                    
                    ForceSelectCandidate(candidateSet, firstValidIndex.Value);
                }

                // Log final state after resolution
                _logger.LogInformation("FeatureFlagEndpointSelector: FINAL CANDIDATE STATE after resolution:");
                for (int i = 0; i < candidateSet.Count; i++)
                {
                    _logger.LogInformation("  FINAL Candidate[{Index}]: Valid={Valid}, DisplayName={DisplayName}", 
                        i, candidateSet.IsValidCandidate(i), 
                        candidateSet[i].Endpoint.DisplayName);
                }

                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "FeatureFlagEndpointSelector: CRITICAL ERROR in SelectAsync - forcing first valid candidate selection");
                
                // CRITICAL FAILSAFE: Select first valid candidate to prevent crash
                for (int i = 0; i < candidateSet.Count; i++)
                {
                    if (candidateSet.IsValidCandidate(i))
                    {
                        ForceSelectCandidate(candidateSet, i);
                        break;
                    }
                }

                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// Resolves ambiguity by reading feature flag and selecting appropriate candidate
        /// </summary>
        private void ResolveWithFeatureFlag(
            CandidateSet candidateSet,
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
                        "FeatureFlagEndpointSelector: Feature flag property {Property} NOT FOUND - selecting first valid",
                        featureFlagProperty);
                    
                    ForceSelectCandidate(candidateSet, refactoredIndex);
                    return;
                }

                var flagValue = (bool?)flagProperty.GetValue(_featureFlags.Value) ?? false;

                int selectedIndex = flagValue ? refactoredIndex : legacyIndex;
                int rejectedIndex = flagValue ? legacyIndex : refactoredIndex;
                string selectedController = flagValue ? "AuthController" : "AuthController (legacy fallback)";

                _logger.LogWarning(
                    "FeatureFlagEndpointSelector: Feature flag {Flag} = {Value} -> SELECTING candidate[{Selected}] ({Controller}), REJECTING candidate[{Rejected}]",
                    featureFlagProperty, flagValue, selectedIndex, selectedController, rejectedIndex);

                // CRITICAL: Use ForceSelectCandidate to ensure ONLY the selected candidate remains valid
                // This marks ALL other candidates (including the rejected one) as invalid
                ForceSelectCandidate(candidateSet, selectedIndex);

                // Store decision in HttpContext
                httpContext.Items["FeatureFlagRouting_SelectedController"] = selectedController;
                httpContext.Items["FeatureFlagRouting_Flag"] = featureFlagProperty;
                httpContext.Items["FeatureFlagRouting_Value"] = flagValue;
                httpContext.Items["FeatureFlagRouting_ResolvedAmbiguity"] = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "FeatureFlagEndpointSelector: ERROR reading feature flag {Property} - forcing first candidate",
                    featureFlagProperty);
                
                ForceSelectCandidate(candidateSet, refactoredIndex);
            }
        }

        /// <summary>
        /// Forces selection of a single candidate by marking all others as invalid
        /// CRITICAL: Does NOT mark the selected candidate as invalid
        /// </summary>
        private void ForceSelectCandidate(CandidateSet candidateSet, int selectedIndex)
        {
            try
            {
                _logger.LogWarning(
                    "FeatureFlagEndpointSelector: FORCING selection of candidate[{Index}] - marking all others as INVALID",
                    selectedIndex);

                // Verify the selected candidate is actually valid
                if (!candidateSet.IsValidCandidate(selectedIndex))
                {
                    _logger.LogError(
                        "FeatureFlagEndpointSelector: CRITICAL ERROR - selected candidate[{Index}] is NOT VALID!",
                        selectedIndex);
                    return;
                }

                // Mark all OTHER candidates as invalid
                for (int i = 0; i < candidateSet.Count; i++)
                {
                    if (i != selectedIndex && candidateSet.IsValidCandidate(i))
                    {
                        candidateSet.SetValidity(i, false);
                        _logger.LogDebug("  Marked candidate[{Index}] as INVALID", i);
                    }
                }

                // Double-check the selected candidate is still valid
                if (!candidateSet.IsValidCandidate(selectedIndex))
                {
                    _logger.LogError(
                        "FeatureFlagEndpointSelector: CRITICAL ERROR - selected candidate[{Index}] became INVALID after processing!",
                        selectedIndex);
                }
                else
                {
                    _logger.LogInformation(
                        "FeatureFlagEndpointSelector: Successfully selected candidate[{Index}] - {DisplayName}",
                        selectedIndex, candidateSet[selectedIndex].Endpoint.DisplayName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FeatureFlagEndpointSelector: ERROR in ForceSelectCandidate");
            }
        }

        /// <summary>
        /// Extracts feature flag property name from attribute using reflection
        /// ENHANCED: Tries multiple strategies to extract the property name
        /// </summary>
        private string? ExtractFeatureFlagProperty(object attribute)
        {
            try
            {
                var attributeType = attribute.GetType();
                _logger.LogDebug("ExtractFeatureFlagProperty: Attribute type = {Type}, BaseType = {BaseType}", 
                    attributeType.Name, attributeType.BaseType?.Name ?? "null");

                // Strategy 1: Try to get field from the attribute's own type
                var field = attributeType.GetField("_featureFlagProperty", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    var value = field.GetValue(attribute) as string;
                    _logger.LogDebug("ExtractFeatureFlagProperty: Found field in attribute type, value = {Value}", value);
                    return value;
                }

                // Strategy 2: Try to get field from base type
                field = attributeType.BaseType?.GetField("_featureFlagProperty", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    var value = field.GetValue(attribute) as string;
                    _logger.LogDebug("ExtractFeatureFlagProperty: Found field in base type, value = {Value}", value);
                    return value;
                }

                // Strategy 3: Walk up the inheritance chain
                var currentType = attributeType;
                while (currentType != null && currentType != typeof(object))
                {
                    field = currentType.GetField("_featureFlagProperty", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field != null)
                    {
                        var value = field.GetValue(attribute) as string;
                        _logger.LogDebug("ExtractFeatureFlagProperty: Found field in type {Type}, value = {Value}", 
                            currentType.Name, value);
                        return value;
                    }
                    currentType = currentType.BaseType;
                }

                _logger.LogWarning("ExtractFeatureFlagProperty: Could not find _featureFlagProperty field in {Type} or its base types", 
                    attributeType.Name);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FeatureFlagEndpointSelector: ERROR extracting feature flag property from {Type}", 
                    attribute?.GetType()?.Name ?? "null");
                return null;
            }
        }
    }
}
