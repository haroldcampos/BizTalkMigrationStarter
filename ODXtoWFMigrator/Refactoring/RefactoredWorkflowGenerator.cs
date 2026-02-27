// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using BizTalktoLogicApps.ODXtoWFMigrator;

namespace BizTalktoLogicApps.ODXtoWFMigrator.Refactoring
{
    /// <summary>
    /// Generates pattern-optimized Logic Apps workflows from BizTalk orchestrations.
    /// </summary>
    /// <remarks>
    /// This generator produces refactored workflows that leverage modern Logic Apps patterns
    /// and connectors, as opposed to direct 1:1 translation. It analyzes the orchestration,
    /// detects integration patterns, and applies best-practice transformations.
    /// 
    /// Key differences from standard conversion:
    /// - Uses native Logic Apps patterns (sessions, parallel branches, etc.)
    /// - Optimizes connector selection based on deployment target (cloud vs. on-prem)
    /// - Simplifies complex BizTalk constructs (convoy ? sessions, nested scopes ? flat)
    /// - Generates cleaner, more maintainable workflow definitions
    /// </remarks>
    public static class RefactoredWorkflowGenerator
    {
        /// <summary>
        /// Generates a pattern-optimized Logic Apps workflow from a BizTalk orchestration.
        /// </summary>
        /// <param name="odxPath">Path to the ODX orchestration file.</param>
        /// <param name="bindingsFilePath">Path to the BizTalk bindings XML file.</param>
        /// <param name="options">Refactoring options controlling optimization behavior.</param>
        /// <returns>JSON string representing the optimized Logic Apps workflow.</returns>
        /// <exception cref="ArgumentNullException">Thrown when required paths are null or empty.</exception>
        /// <exception cref="FileNotFoundException">Thrown when ODX or bindings file does not exist.</exception>
        /// <exception cref="InvalidOperationException">Thrown when refactoring options are invalid.</exception>
        public static string GenerateRefactoredWorkflow(
            string odxPath,
            string bindingsFilePath,
            RefactoringOptions options = null)
        {
            // Validate inputs
            if (string.IsNullOrEmpty(odxPath))
            {
                throw new ArgumentNullException(nameof(odxPath));
            }

            if (string.IsNullOrEmpty(bindingsFilePath))
            {
                throw new ArgumentNullException(nameof(bindingsFilePath));
            }

            if (!File.Exists(odxPath))
            {
                throw new FileNotFoundException($"ODX file not found: {odxPath}", odxPath);
            }

            if (!File.Exists(bindingsFilePath))
            {
                throw new FileNotFoundException($"Bindings file not found: {bindingsFilePath}", bindingsFilePath);
            }

            // Use default options if not provided
            options = options ?? new RefactoringOptions();

            // Validate options
            options.Validate();

            try
            {
                // STEP 1: Parse orchestration using EXISTING parser (no changes to parser)
                var model = BizTalkOrchestrationParser.ParseOdx(odxPath);
                var binding = BindingSnapshot.Parse(bindingsFilePath);

                // STEP 2: Detect integration patterns using EXISTING report generator
                var detectedPatterns = OrchestrationReportGenerator.ExportDetectedPatterns(model);

                // STEP 3: Generate baseline workflow using EXISTING mapper
                var baselineWorkflow = LogicAppsMapper.MapToLogicApp(model, binding, isCallable: false);

                // STEP 4: Apply pattern-based optimizations (NEW - Phase 2)
                var optimizedWorkflow = WorkflowReconstructor.OptimizeWorkflow(
                    baselineWorkflow,
                    detectedPatterns,
                    options);

                // STEP 5: Optimize connector selections (NEW - Phase 3)
                var connectorRegistry = LoadConnectorRegistry(options.ConnectorRegistryPath);
                ConnectorOptimizer.OptimizeConnectors(optimizedWorkflow, connectorRegistry, options);

                // STEP 6: Generate JSON using EXISTING generator
                var json = LogicAppJSONGenerator.GenerateStandardWorkflow(
                    optimizedWorkflow,
                    options.WorkflowType,
                    options.SchemaVersion,
                    connectorRegistry);

                // STEP 7: Post-process JSON for final enhancements (NEW - Phase 4)
                var finalJson = JsonPostProcessor.ApplyPatternOptimizations(json, detectedPatterns, options);

                return finalJson;
            }
            catch (Exception ex) when (!ex.IsFatal())
            {
                Trace.TraceError("[REFACTORING] Failed: {0}", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Generates a refactored workflow and writes it to the specified output path.
        /// </summary>
        /// <param name="odxPath">Path to the ODX orchestration file.</param>
        /// <param name="bindingsFilePath">Path to the BizTalk bindings XML file.</param>
        /// <param name="outputPath">Path where the refactored workflow JSON will be written.</param>
        /// <param name="options">Refactoring options controlling optimization behavior.</param>
        public static void GenerateRefactoredWorkflowToFile(
            string odxPath,
            string bindingsFilePath,
            string outputPath,
            RefactoringOptions options = null)
        {
            if (string.IsNullOrEmpty(outputPath))
            {
                throw new ArgumentNullException(nameof(outputPath));
            }

            var json = GenerateRefactoredWorkflow(odxPath, bindingsFilePath, options);

            // Ensure directory exists
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(outputPath, json);

            // Generate parameters file if requested
            options = options ?? new RefactoringOptions();
            if (options.GenerateParametersJson)
            {
                var parametersPath = Path.ChangeExtension(outputPath, null) + ".parameters.json";
                var parametersJson = JsonPostProcessor.GenerateParametersFile(json);
                File.WriteAllText(parametersPath, parametersJson);
            }
        }

        /// <summary>
        /// Loads the connector registry from file or searches standard locations.
        /// </summary>
        /// <param name="registryPath">Optional path to custom registry.</param>
        /// <returns>Connector schema registry instance, or null if not found.</returns>
        private static ConnectorSchemaRegistry LoadConnectorRegistry(string registryPath)
        {
            if (!string.IsNullOrEmpty(registryPath) && File.Exists(registryPath))
            {
                return ConnectorSchemaRegistry.LoadFromFile(registryPath);
            }
            else
            {
                return BizTalkOrchestrationParser.TryLoadConnectorRegistry();
            }
        }
    }
}
