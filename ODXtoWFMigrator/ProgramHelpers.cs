// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BizTalktoLogicApps.ODXtoWFMigrator.Refactoring;
using System;
using System.IO;

namespace BizTalktoLogicApps.ODXtoWFMigrator
{
    partial class Program
    {
        /// <summary>
        /// Handles refactored migration using the RefactoredWorkflowGenerator.
        /// </summary>
        static int HandleRefactoredMigration(
            string odxPath,
            string bindingsPath,
            string outputPath,
            string schemaVersion,
            string targetPattern,
            string messagingSystem)
        {
            try
            {
                // Build refactoring options
                var options = new RefactoringOptions
                {
                    SchemaVersion = schemaVersion,
                    WorkflowType = "Stateful"
                };

                // Set target deployment based on messaging system
                if (!string.IsNullOrEmpty(messagingSystem))
                {
                    var msgLower = messagingSystem.ToLowerInvariant();
                    if (msgLower == "servicebus")
                    {
                        options.Target = DeploymentTarget.Cloud;
                        options.PreferredMessagingPlatform = "ServiceBus";
                    }
                    else if (msgLower == "ibmmq" || msgLower == "mq")
                    {
                        options.Target = DeploymentTarget.OnPremises;
                        options.PreferredMessagingPlatform = "IbmMq";
                    }
                    else if (msgLower == "rabbitmq")
                    {
                        options.Target = DeploymentTarget.OnPremises;
                        options.PreferredMessagingPlatform = "RabbitMQ";
                    }
                    else if (msgLower == "kafka")
                    {
                        options.Target = DeploymentTarget.OnPremises;
                        options.PreferredMessagingPlatform = "Kafka";
                    }
                    else if (msgLower == "sapodata")
                    {
                        options.PreferredMessagingPlatform = "SapOData";
                    }
                    else if (msgLower == "saperp" || msgLower == "sap")
                    {
                        options.PreferredMessagingPlatform = "SapErp";
                    }
                }

                // Set target pattern hint (for future use with pattern-specific optimizations)
                if (!string.IsNullOrEmpty(targetPattern))
                {
                    Console.WriteLine("  Target pattern hint: {0}", targetPattern);
                    // Future: options.TargetPattern = targetPattern;
                }

                // Generate refactored workflow
                Console.WriteLine("Generating refactored workflow...");
                var json = RefactoredWorkflowGenerator.GenerateRefactoredWorkflow(
                    odxPath,
                    bindingsPath,
                    options);

                // Validate
                Console.WriteLine("Validating workflow...");
                var validator = new WorkflowValidator();
                var validationResult = validator.Validate(json);
                Console.WriteLine("  {0}", validationResult.GetSummary());

                if (validationResult.HasErrors || validationResult.HasWarnings)
                {
                    Console.WriteLine();
                    validationResult.PrintIssues();
                }

                if (validationResult.HasErrors)
                {
                    Console.Error.WriteLine("\nValidation failed with errors. Workflow will still be saved but may not deploy successfully.");
                }

                // Save output
                EnsureDirectory(outputPath);
                File.WriteAllText(outputPath, json);
                Console.WriteLine("\nWorkflow definition written to: {0}", outputPath);
                Console.WriteLine("Schema version used: {0}", schemaVersion);
                Console.WriteLine("Done.");

                return validationResult.HasErrors ? 3 : 0;
            }
            catch (Exception ex) when (!ex.IsFatal())
            {
                Console.Error.WriteLine("Error during refactored migration: {0}", ex.Message);
                Console.Error.WriteLine(ex.StackTrace);
                return 3;
            }
        }
    }
}
