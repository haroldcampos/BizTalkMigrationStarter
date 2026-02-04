// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BizTalktoLogicApps.ODXtoWFMigrator.Refactoring
{
    /// <summary>
    /// Specifies the deployment target for the refactored workflow.
    /// </summary>
    public enum DeploymentTarget
    {
        /// <summary>
        /// Azure cloud deployment - all connectors available including Service Bus.
        /// </summary>
        Cloud,

        /// <summary>
        /// On-premises deployment (Logic Apps Kubernetes/Docker) - limited to on-prem connectors.
        /// Service Bus NOT available - use RabbitMQ, Kafka, or IBM MQ instead.
        /// </summary>
        OnPremises
    }

    /// <summary>
    /// Defines the refactoring strategy for workflow optimization.
    /// </summary>
    public enum RefactoringStrategy
    {
        /// <summary>
        /// Minimal changes, close to original BizTalk structure.
        /// </summary>
        Conservative,

        /// <summary>
        /// Apply recommended patterns selectively based on complexity.
        /// </summary>
        Balanced,

        /// <summary>
        /// Maximum optimization, may require code changes and testing.
        /// </summary>
        Aggressive
    }

    /// <summary>
    /// Configuration options for refactored workflow generation.
    /// </summary>
    /// <remarks>
    /// Controls how BizTalk orchestrations are optimized for Logic Apps,
    /// including connector selection, pattern application, and deployment target.
    /// </remarks>
    public class RefactoringOptions
    {
        /// <summary>
        /// Gets or sets the deployment target (cloud or on-premises).
        /// Determines connector availability (e.g., Service Bus only in cloud).
        /// </summary>
        public DeploymentTarget Target { get; set; } = DeploymentTarget.Cloud;

        /// <summary>
        /// Gets or sets the preferred messaging platform.
        /// Options: "ServiceBus" (cloud only), "RabbitMQ", "Kafka", "IbmMq".
        /// </summary>
        public string PreferredMessagingPlatform { get; set; } = "ServiceBus";

        /// <summary>
        /// Gets or sets the preferred database connector.
        /// Options: "Sql", "CosmosDb" (cloud only), "Postgres", "OracleDb".
        /// </summary>
        public string PreferredDatabaseConnector { get; set; } = "Sql";

        /// <summary>
        /// Gets or sets a value indicating whether to prefer managed connectors over custom HTTP calls.
        /// </summary>
        public bool PreferManagedConnectors { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether to simplify convoy patterns using native session support.
        /// </summary>
        public bool SimplifyConvoyPatterns { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether to use native parallel branches instead of manual correlation.
        /// </summary>
        public bool UseNativeParallelBranches { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether to consolidate nested scopes for cleaner workflow structure.
        /// </summary>
        public bool ConsolidateNestedScopes { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether to optimize transforms (prefer Data Mapper over XSLT where possible).
        /// </summary>
        public bool OptimizeTransforms { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether to include pattern explanation comments in the generated workflow.
        /// </summary>
        public bool IncludePatternComments { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether to generate a separate parameters.json file.
        /// </summary>
        public bool GenerateParametersJson { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether to use Data Mapper for BizTalk map migration (vs. XSLT).
        /// </summary>
        public bool UseDataMapper { get; set; } = true;

        /// <summary>
        /// Gets or sets the refactoring strategy level.
        /// </summary>
        public RefactoringStrategy Strategy { get; set; } = RefactoringStrategy.Balanced;

        /// <summary>
        /// Gets or sets the path to a custom connector registry JSON file.
        /// If null, uses the default embedded registry.
        /// </summary>
        public string ConnectorRegistryPath { get; set; }

        /// <summary>
        /// Gets or sets the Logic Apps schema version for the generated workflow.
        /// </summary>
        public string SchemaVersion { get; set; } = "2016-06-01";

        /// <summary>
        /// Gets or sets the workflow type (Stateful or Stateless).
        /// </summary>
        public string WorkflowType { get; set; } = "Stateful";

        /// <summary>
        /// Validates the refactoring options for consistency.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when options are inconsistent.</exception>
        public void Validate()
        {
            // Service Bus is cloud-only
            if (this.Target == DeploymentTarget.OnPremises && 
                string.Equals(this.PreferredMessagingPlatform, "ServiceBus", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Service Bus is not available for on-premises deployments. " +
                    "Use RabbitMQ, Kafka, or IBM MQ instead.");
            }

            // Cosmos DB is cloud-only
            if (this.Target == DeploymentTarget.OnPremises && 
                string.Equals(this.PreferredDatabaseConnector, "CosmosDb", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Cosmos DB is not available for on-premises deployments. " +
                    "Use Sql, Postgres, or OracleDb instead.");
            }

            // Validate workflow type
            if (!string.Equals(this.WorkflowType, "Stateful", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(this.WorkflowType, "Stateless", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "WorkflowType must be either 'Stateful' or 'Stateless'.");
            }
        }
    }
}
