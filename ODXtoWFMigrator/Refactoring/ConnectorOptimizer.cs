// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Linq;
using BizTalktoLogicApps.ODXtoWFMigrator;

namespace BizTalktoLogicApps.ODXtoWFMigrator.Refactoring
{
    /// <summary>
    /// Optimizes connector selections based on deployment target and preferences.
    /// </summary>
    /// <remarks>
    /// This component upgrades legacy BizTalk adapters to modern Logic Apps connectors,
    /// respecting deployment target constraints (cloud vs. on-premises).
    /// 
    /// KEY PRINCIPLE: Only replace connectors when necessary for the deployment target.
    /// - On-premises triggers that work on-prem (FileSystem, FTP, SQL) are NOT replaced
    /// - Cloud-only triggers (Service Bus, Event Hub, Cosmos DB) ARE replaced when targeting on-prem
    /// - Messaging alternatives (RabbitMQ, Kafka) are ONLY used when the original is cloud-only
    /// 
    /// Phase 3 implementation:
    /// - Upgrades MSMQ to ServiceBus (cloud) or RabbitMQ/Kafka (on-prem)
    /// - Upgrades FILE to AzureBlob (cloud preferred) - FileSystem works on-prem
    /// - Replaces Service Bus with RabbitMQ/Kafka when targeting on-prem (cloud-only service)
    /// - Replaces Event Hub with Kafka/RabbitMQ when targeting on-prem (cloud-only service)
    /// - Replaces Cosmos DB with SQL when targeting on-prem (cloud-only service)
    /// - Replaces Azure Blob with FileSystem when targeting on-prem (cloud-only service)
    /// - Validates connector availability for deployment target
    /// - Recommends managed connectors over custom HTTP
    /// 
    /// Connectors that work on-premises (NO replacement needed):
    /// - FileSystem, FTP, SFTP, SQL, HTTP, SMTP, Oracle, DB2, File System
    /// 
    /// Cloud-only connectors (REQUIRE replacement for on-prem):
    /// - Service Bus, Event Hub, Cosmos DB, Azure Blob Storage, Azure Table Storage
    /// </remarks>
    internal static class ConnectorOptimizer
    {
        /// <summary>
        /// Upgrades connectors in the workflow based on deployment target and preferences.
        /// </summary>
        /// <param name="workflow">The workflow to optimize.</param>
        /// <param name="registry">Connector registry for available connectors.</param>
        /// <param name="options">Refactoring options including deployment target.</param>
        public static void OptimizeConnectors(
            LogicAppWorkflowMap workflow,
            ConnectorSchemaRegistry registry,
            RefactoringOptions options)
        {
            if (workflow == null)
            {
                throw new ArgumentNullException(nameof(workflow));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (registry == null)
            {
                // Registry is optional infrastructure (loaded from connector-registry.json).
                // When it is unavailable, skip connector optimization and leave the workflow
                // as-is — downstream legacy fallback paths in LogicAppJSONGenerator handle
                // connectors without a registry.
                Trace.TraceWarning("[CONNECTOR OPTIMIZER] Connector registry is null; skipping connector optimization.");
                return;
            }

            var upgradesApplied = 0;

            // Optimize trigger connector
            var trigger = workflow.Triggers.FirstOrDefault();
            if (trigger != null)
            {
                var upgraded = OptimizeTriggerConnector(trigger, registry, options);
                if (upgraded)
                {
                    upgradesApplied++;
                }
            }

            // Optimize all SendConnector actions (top-level and nested)
            foreach (var action in workflow.Actions)
            {
                if (action.Type == "SendConnector")
                {
                    if (OptimizeActionConnector(action, registry, options))
                    {
                        upgradesApplied++;
                    }
                }

                // Recurse into children and branches
                upgradesApplied += OptimizeNestedActionConnectors(action.Children, registry, options);
                upgradesApplied += OptimizeNestedActionConnectors(action.TrueBranch, registry, options);
                upgradesApplied += OptimizeNestedActionConnectors(action.FalseBranch, registry, options);
            }

        }

        /// <summary>
        /// Optimizes the trigger connector based on deployment target.
        /// </summary>
        private static bool OptimizeTriggerConnector(
            LogicAppTrigger trigger,
            ConnectorSchemaRegistry registry,
            RefactoringOptions options)
        {
            var originalKind = trigger.Kind;

            // Determine optimal connector kind
            var upgradedKind = SelectOptimalConnector(
                trigger.TransportType,
                trigger.Kind,
                "trigger",
                registry,
                options);

            if (upgradedKind != null && upgradedKind != originalKind)
            {
                trigger.Kind = upgradedKind;
                trigger.TransportType = upgradedKind;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Optimizes an action connector based on deployment target.
        /// </summary>
        private static bool OptimizeActionConnector(
            LogicAppAction action,
            ConnectorSchemaRegistry registry,
            RefactoringOptions options)
        {
            var originalKind = action.ConnectorKind;

            // Determine optimal connector kind
            var upgradedKind = SelectOptimalConnector(
                action.ConnectorKind,
                action.ConnectorKind,
                "action",
                registry,
                options);

            if (upgradedKind != null && upgradedKind != originalKind)
            {
                action.ConnectorKind = upgradedKind;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Recursively optimizes connectors in nested actions.
        /// </summary>
        private static int OptimizeNestedActionConnectors(
            System.Collections.Generic.List<LogicAppAction> actions,
            ConnectorSchemaRegistry registry,
            RefactoringOptions options)
        {
            var upgradesApplied = 0;

            foreach (var action in actions)
            {
                if (action.Type == "SendConnector")
                {
                    if (OptimizeActionConnector(action, registry, options))
                    {
                        upgradesApplied++;
                    }
                }

                // Recurse into children and branches
                upgradesApplied += OptimizeNestedActionConnectors(action.Children, registry, options);
                upgradesApplied += OptimizeNestedActionConnectors(action.TrueBranch, registry, options);
                upgradesApplied += OptimizeNestedActionConnectors(action.FalseBranch, registry, options);
            }

            return upgradesApplied;
        }

        /// <summary>
        /// Selects the optimal connector for the deployment target.
        /// </summary>
        /// <param name="originalAdapter">Original BizTalk adapter name.</param>
        /// <param name="currentKind">Current connector kind.</param>
        /// <param name="operationType">Operation type ("trigger" or "action").</param>
        /// <param name="registry">Connector registry.</param>
        /// <param name="options">Refactoring options.</param>
        /// <returns>Upgraded connector kind or null if no upgrade needed.</returns>
        private static string SelectOptimalConnector(
            string originalAdapter,
            string currentKind,
            string operationType,
            ConnectorSchemaRegistry registry,
            RefactoringOptions options)
        {
            if (string.IsNullOrEmpty(originalAdapter))
            {
                return null;
            }

            var adapter = originalAdapter.ToLowerInvariant();

            // MSMQ Upgrade Logic
            if (adapter.IndexOf("msmq") >= 0 || adapter.IndexOf("netmsmq") >= 0)
            {
                if (options.Target == DeploymentTarget.Cloud)
                {
                    // MSMQ to Service Bus (cloud only)
                    if (registry.HasConnector("ServiceBus"))
                    {
                        return "ServiceBus";
                    }
                }
                else
                {
                    // MSMQ to RabbitMQ (on-prem preferred)
                    if (string.Equals(options.PreferredMessagingPlatform, "RabbitMQ", StringComparison.OrdinalIgnoreCase) &&
                        registry.HasConnector("RabbitMQ"))
                    {
                        return "RabbitMQ";
                    }
                    else if (string.Equals(options.PreferredMessagingPlatform, "Kafka", StringComparison.OrdinalIgnoreCase) &&
                             registry.HasConnector("ConfluentKafka"))
                    {
                        return "ConfluentKafka";
                    }
                    else if (registry.HasConnector("IbmMq"))
                    {
                        return "IbmMq";
                    }
                }
            }

            // FILE Adapter Upgrade Logic - Only upgrade to Blob if Cloud target explicitly chosen
            if (adapter.IndexOf("file") >= 0 && adapter.IndexOf("hostfile") < 0)
            {
                if (options.Target == DeploymentTarget.Cloud && options.PreferManagedConnectors)
                {
                    // FILE to Azure Blob Storage (cloud preferred)
                    if (registry.HasConnector("AzureBlob"))
                    {
                        return "AzureBlob";
                    }
                }
                // FileSystem works on-premises, no replacement needed
            }

            // Service Bus validation for on-prem - ONLY replace if targeting on-premises
            // Service Bus is cloud-only and requires an alternative for on-premises
            if (string.Equals(adapter, "servicebus", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(adapter, "sb", StringComparison.OrdinalIgnoreCase))
            {
                if (options.Target == DeploymentTarget.OnPremises)
                {
                    Trace.TraceWarning("[CONNECTOR OPTIMIZER] Service Bus not available on-premises. Replacing with messaging alternative.");
                    
                    // Auto-replace with RabbitMQ for on-prem (Service Bus doesn't work on-prem)
                    if (string.Equals(options.PreferredMessagingPlatform, "RabbitMQ", StringComparison.OrdinalIgnoreCase) &&
                        registry.HasConnector("RabbitMQ"))
                    {
                        return "RabbitMQ";
                    }
                    else if (string.Equals(options.PreferredMessagingPlatform, "Kafka", StringComparison.OrdinalIgnoreCase) &&
                             registry.HasConnector("ConfluentKafka"))
                    {
                        return "ConfluentKafka";
                    }
                }
            }

            // Azure Event Hub - cloud-only, needs alternative for on-prem
            if (adapter.IndexOf("eventhub") >= 0 && options.Target == DeploymentTarget.OnPremises)
            {
                Trace.TraceWarning("[CONNECTOR OPTIMIZER] Event Hub not available on-premises. Replacing with messaging alternative.");
                
                if (string.Equals(options.PreferredMessagingPlatform, "Kafka", StringComparison.OrdinalIgnoreCase) &&
                    registry.HasConnector("ConfluentKafka"))
                {
                    return "ConfluentKafka";
                }
                else if (registry.HasConnector("RabbitMQ"))
                {
                    return "RabbitMQ";
                }
            }

            // Cosmos DB validation for on-prem - cloud-only, needs alternative
            if (adapter.IndexOf("cosmos") >= 0 && options.Target == DeploymentTarget.OnPremises)
            {
                Trace.TraceWarning("[CONNECTOR OPTIMIZER] Cosmos DB not available on-premises. Replacing with SQL.");
                
                // Suggest SQL as fallback
                if (registry.HasConnector("Sql"))
                {
                    return "Sql";
                }
            }

            // Azure Blob Storage - cloud-only, needs alternative for on-prem
            if (adapter.IndexOf("azureblob") >= 0 && options.Target == DeploymentTarget.OnPremises)
            {
                Trace.TraceWarning("[CONNECTOR OPTIMIZER] Azure Blob Storage not available on-premises. Replacing with FileSystem.");
                
                if (registry.HasConnector("FileSystem"))
                {
                    return "FileSystem";
                }
            }

            // No upgrade needed
            return null;
        }
    }
}
