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
        /// <returns>Upgraded connector kind, or null if no change is needed.</returns>
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
            var targetString = options.Target.ToString(); // "Cloud" or "OnPremises"

            // ---------------------------------------------------------------
            // Part A: Intentional upgrades — migrate legacy BizTalk adapters
            // to preferred modern equivalents regardless of compatibility.
            // These are policy decisions, not compatibility fixes.
            // ---------------------------------------------------------------

            // MSMQ / NetMsmq ? ServiceBus (cloud) or preferred messaging (on-prem)
            if (adapter.IndexOf("msmq") >= 0 || adapter.IndexOf("netmsmq") >= 0)
            {
                if (options.Target == DeploymentTarget.Cloud)
                {
                    if (registry.HasConnector("ServiceBus"))
                    {
                        return "ServiceBus";
                    }
                }
                else
                {
                    if (string.Equals(options.PreferredMessagingPlatform, "RabbitMQ", StringComparison.OrdinalIgnoreCase) &&
                        registry.HasConnector("RabbitMQ"))
                    {
                        return "RabbitMQ";
                    }

                    if (string.Equals(options.PreferredMessagingPlatform, "Kafka", StringComparison.OrdinalIgnoreCase) &&
                        registry.HasConnector("ConfluentKafka"))
                    {
                        return "ConfluentKafka";
                    }

                    if (registry.HasConnector("IbmMq"))
                    {
                        return "IbmMq";
                    }
                }
            }

            // FILE adapter ? AzureBlob when targeting cloud with managed connectors preferred.
            // FileSystem works on-premises — no replacement needed for that target.
            if (adapter.IndexOf("file") >= 0 && adapter.IndexOf("hostfile") < 0)
            {
                if (options.Target == DeploymentTarget.Cloud && options.PreferManagedConnectors &&
                    registry.HasConnector("AzureBlob"))
                {
                    return "AzureBlob";
                }
            }

            // ---------------------------------------------------------------
            // Part B: Compatibility replacement — use registry to detect
            // connectors that cannot run on the deployment target and find
            // the best same-category alternative that can.
            // ---------------------------------------------------------------

            // If the current connector is not in the registry, nothing to do.
            if (!registry.IsCompatibleWith(currentKind, targetString))
            {
                var currentConnector = registry.GetConnector(currentKind);
                var category = currentConnector != null ? currentConnector.MessagingCategory : null;

                var replacement = PickBestAlternative(
                    registry.FindAlternatives(category, targetString),
                    options);

                if (replacement != null)
                {
                    Trace.TraceWarning(
                        "[CONNECTOR OPTIMIZER] {0} is not compatible with {1}. Replacing with {2}.",
                        currentKind,
                        targetString,
                        replacement);

                    return replacement;
                }
            }

            // No upgrade needed.
            return null;
        }

        /// <summary>
        /// Picks the best replacement connector from a set of compatible alternatives,
        /// honouring <see cref="RefactoringOptions.PreferredMessagingPlatform"/> and
        /// <see cref="RefactoringOptions.PreferredDatabaseConnector"/>.
        /// </summary>
        /// <param name="alternatives">
        /// The compatible connectors in the same category, as returned by
        /// <see cref="ConnectorSchemaRegistry.FindAlternatives"/>.
        /// </param>
        /// <param name="options">Refactoring options that carry preference signals.</param>
        /// <returns>
        /// The canonical connector name of the chosen alternative, or <c>null</c> when
        /// <paramref name="alternatives"/> is empty.
        /// </returns>
        private static string PickBestAlternative(
            System.Collections.Generic.IEnumerable<ConnectorSchema> alternatives,
            RefactoringOptions options)
        {
            ConnectorSchema first = null;

            foreach (var schema in alternatives)
            {
                // Remember the first compatible entry as a fallback.
                if (first == null)
                {
                    first = schema;
                }

                // Honour PreferredMessagingPlatform (e.g., "RabbitMQ", "Kafka", "IbmMq").
                if (!string.IsNullOrEmpty(options.PreferredMessagingPlatform) &&
                    string.Equals(schema.Name, options.PreferredMessagingPlatform, StringComparison.OrdinalIgnoreCase))
                {
                    return schema.Name;
                }

                // Honour PreferredDatabaseConnector (e.g., "Sql", "OracleDb").
                if (!string.IsNullOrEmpty(options.PreferredDatabaseConnector) &&
                    string.Equals(schema.Name, options.PreferredDatabaseConnector, StringComparison.OrdinalIgnoreCase))
                {
                    return schema.Name;
                }
            }

            return first != null ? first.Name : null;
        }
    }
}
