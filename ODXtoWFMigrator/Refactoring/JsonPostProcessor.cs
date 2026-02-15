// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using BizTalktoLogicApps.ODXtoWFMigrator;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BizTalktoLogicApps.ODXtoWFMigrator.Refactoring
{
    /// <summary>
    /// Applies JSON-level optimizations to generated workflows.
    /// </summary>
    /// <remarks>
    /// This component performs final post-processing on the generated workflow JSON,
    /// adding pattern comments, extracting parameters, and applying final optimizations.
    /// Phase 4 implementation:
    /// - Injects explanatory comments for detected patterns
    /// - Extracts connection strings and settings to parameters
    /// - Adds metadata about pattern optimizations
    /// - Formats JSON for better readability
    /// </remarks>
    public static class JsonPostProcessor
    {
        /// <summary>
        /// Applies pattern-specific optimizations to the workflow JSON.
        /// </summary>
        /// <param name="workflowJson">The generated workflow JSON string.</param>
        /// <param name="patterns">Detected integration patterns.</param>
        /// <param name="options">Refactoring options.</param>
        /// <returns>Optimized workflow JSON.</returns>
        public static string ApplyPatternOptimizations(
            string workflowJson,
            List<OrchestrationReportGenerator.IntegrationPattern> patterns,
            RefactoringOptions options)
        {
            if (string.IsNullOrEmpty(workflowJson))
            {
                throw new ArgumentNullException(nameof(workflowJson));
            }

            Trace.TraceInformation("[JSON POST-PROCESSOR] Applying final JSON optimizations");

            try
            {
                // Parse JSON
                var workflow = JObject.Parse(workflowJson);

                // Add pattern metadata if requested
                if (options != null && options.IncludePatternComments && patterns != null && patterns.Any())
                {
                    AddPatternMetadata(workflow, patterns);
                }

                // Format with indentation for readability
                var formatted = JsonConvert.SerializeObject(workflow, Formatting.Indented);

                Trace.TraceInformation("[JSON POST-PROCESSOR] JSON optimization complete");
                return formatted;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("[JSON POST-PROCESSOR] Could not post-process JSON - {0}", ex.Message);
                return workflowJson; // Return original on error
            }
        }

        /// <summary>
        /// Generates a parameters.json file from the workflow JSON.
        /// </summary>
        /// <param name="workflowJson">The workflow JSON string.</param>
        /// <returns>Parameters JSON string.</returns>
        public static string GenerateParametersFile(string workflowJson)
        {
            Trace.TraceInformation("[JSON POST-PROCESSOR] Generating parameters file");

            try
            {
                var workflow = JObject.Parse(workflowJson);
                var parametersObj = new JObject();

                // Extract connection strings, endpoints, and other configurable values
                var parameters = ExtractParameters(workflow);

                foreach (var param in parameters)
                {
                    parametersObj[param.Key] = new JObject
                    {
                        ["value"] = param.Value
                    };
                }

                if (parametersObj.Count == 0)
                {
                    // Add default parameters structure
                    parametersObj["$connections"] = new JObject
                    {
                        ["value"] = new JObject()
                    };
                }

                var result = new JObject
                {
                    ["$schema"] = "https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#",
                    ["contentVersion"] = "1.0.0.0",
                    ["parameters"] = parametersObj
                };

                Trace.TraceInformation("[JSON POST-PROCESSOR] Extracted {0} parameter(s)", parametersObj.Count);
                return JsonConvert.SerializeObject(result, Formatting.Indented);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("[JSON POST-PROCESSOR] Could not generate parameters - {0}", ex.Message);
                
                // Return minimal valid parameters file
                var fallback = new JObject
                {
                    ["$schema"] = "https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#",
                    ["contentVersion"] = "1.0.0.0",
                    ["parameters"] = new JObject()
                };
                
                return JsonConvert.SerializeObject(fallback, Formatting.Indented);
            }
        }

        /// <summary>
        /// Adds pattern metadata to the workflow definition.
        /// </summary>
        private static void AddPatternMetadata(JObject workflow, List<OrchestrationReportGenerator.IntegrationPattern> patterns)
        {
            if (patterns == null || !patterns.Any())
            {
                return;
            }

            // Add metadata section
            var metadata = new JObject();
            var patternArray = new JArray();

            foreach (var pattern in patterns)
            {
                patternArray.Add(new JObject
                {
                    ["name"] = pattern.PatternName,
                    ["description"] = pattern.Description,
                    ["recommendation"] = pattern.LogicAppsRecommendation
                });
            }

            metadata["detectedPatterns"] = patternArray;
            metadata["optimizationApplied"] = true;
            metadata["generatedBy"] = "BizTalk to Logic Apps Refactoring Tool";
            metadata["generatedAt"] = DateTime.UtcNow.ToString("o");

            // Add to definition
            if (workflow["definition"] is JObject definition)
            {
                definition["metadata"] = metadata;
            }
            else
            {
                workflow["metadata"] = metadata;
            }

            Trace.TraceInformation("[JSON POST-PROCESSOR] Added metadata for {0} pattern(s)", patterns.Count);
        }

        /// <summary>
        /// Extracts parameterizable values from the workflow.
        /// </summary>
        private static Dictionary<string, string> ExtractParameters(JObject workflow)
        {
            var parameters = new Dictionary<string, string>();

            // Look for connection strings, endpoints, and other configurable values
            var tokens = workflow.SelectTokens("$..connectionString").ToList();
            foreach (var token in tokens)
            {
                var value = token.Value<string>();
                if (!string.IsNullOrEmpty(value) && 
                    !value.StartsWith("@") && 
                    !parameters.ContainsKey("connectionString"))
                {
                    parameters["connectionString"] = value;
                }
            }

            // Extract trigger folder paths (for FILE adapters)
            var folderPaths = workflow.SelectTokens("$..folderPath").ToList();
            if (folderPaths.Any())
            {
                var firstPath = folderPaths.First().Value<string>();
                if (!string.IsNullOrEmpty(firstPath) && !firstPath.StartsWith("@"))
                {
                    parameters["triggerFolderPath"] = firstPath;
                }
            }

            return parameters;
        }
    }
}

