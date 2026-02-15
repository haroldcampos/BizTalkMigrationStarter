// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BizTalktoLogicApps.ODXtoWFMigrator
{
    /// <summary>
    /// Represents a complete Azure Logic Apps workflow with triggers, actions, and metadata.
    /// </summary>
    /// <remarks>
    /// This is the intermediate model used for mapping BizTalk orchestrations to Logic Apps.
    /// Contains all workflow elements before JSON serialization.
    /// </remarks>
    public sealed class LogicAppWorkflowMap
    {
        /// <summary>
        /// Gets or sets the workflow name (typically the orchestration fully-qualified name).
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets the collection of workflow triggers (typically one per workflow).
        /// </summary>
        public List<LogicAppTrigger> Triggers { get; } = new List<LogicAppTrigger>();

        /// <summary>
        /// Gets the collection of workflow actions in execution sequence.
        /// </summary>
        public List<LogicAppAction> Actions { get; } = new List<LogicAppAction>();

        /// <summary>
        /// Gets the variable names for expression mapping context.
        /// </summary>
        /// <remarks>
        /// Used by ExpressionMapper to resolve variable references in XLANG expressions.
        /// </remarks>
        public List<string> VariableNames { get; } = new List<string>();
    }

    /// <summary>
    /// Represents a Logic Apps workflow trigger mapped from a BizTalk receive location.
    /// </summary>
    /// <remarks>
    /// Contains all binding metadata from BizTalk including WCF settings, credentials, and transport-specific properties.
    /// Preserves configuration for faithful migration to Logic Apps connectors.
    /// </remarks>
    public sealed class LogicAppTrigger
    {
        public string Name { get; set; }
        public string Kind { get; set; }
        public string TransportType { get; set; }
        public string Address { get; set; }
        public string FolderPath { get; set; }
        public string FileMask { get; set; }
        public int Sequence { get; set; }
        public int? PollingIntervalSeconds { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        public string ConnectionString { get; set; }

        // Additional bindings properties
        public string PrimaryTransport { get; set; }
        public string Endpoint { get; set; }
        public string SecurityMode { get; set; }

        // WCF-specific metadata for context preservation
        public string MessageClientCredentialType { get; set; }
        public string TransportClientCredentialType { get; set; }
        public string MessageEncoding { get; set; }
        public string AlgorithmSuite { get; set; }
        public int? MaxReceivedMessageSize { get; set; }
        public int? MaxConcurrentCalls { get; set; }
        public string OpenTimeout { get; set; }
        public string CloseTimeout { get; set; }
        public string SendTimeout { get; set; }
        public bool? EstablishSecurityContext { get; set; }
        public bool? NegotiateServiceCredential { get; set; }
        public bool? IncludeExceptionDetailInFaults { get; set; }
        public bool? UseSSO { get; set; }
        public bool? SuspendMessageOnFailure { get; set; }
    }

    /// <summary>
    /// Represents a Logic Apps workflow action mapped from a BizTalk orchestration shape.
    /// </summary>
    /// <remarks>
    /// Supports hierarchical structures for If/Else branches, loops, scopes, and parallel execution.
    /// Contains connector metadata for send operations and expression details for transformations.
    /// </remarks>
    public sealed class LogicAppAction
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Details { get; set; }
        public int Sequence { get; set; }
        public List<LogicAppAction> Children { get; set; } = new List<LogicAppAction>();

        /// <summary>
        /// Separate collections for If/Else branches.
        /// </summary>
        public List<LogicAppAction> TrueBranch { get; set; } = new List<LogicAppAction>();
        public List<LogicAppAction> FalseBranch { get; set; } = new List<LogicAppAction>();

        public bool IsBranchContainer { get; set; }
        public int? LoopThreshold { get; set; }
        public string ConnectorKind { get; set; }
        public string TargetAddress { get; set; }
        public bool IsTopic { get; set; }
        public bool HasSubscription { get; set; }
        public string QueueOrTopicName { get; set; }
        public string SubscriptionName { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        public string ConnectionString { get; set; }
        public string SoapAction { get; set; }
        public string HttpMethod { get; set; }
        public string RelativePath { get; set; }

        // Additional bindings properties
        public string PrimaryTransport { get; set; }
        public string Endpoint { get; set; }
        public string SecurityMode { get; set; }

        // WCF-specific metadata for send ports
        public string MessageClientCredentialType { get; set; }
        public string TransportClientCredentialType { get; set; }
        public string MessageEncoding { get; set; }
        public string AlgorithmSuite { get; set; }
        public int? MaxReceivedMessageSize { get; set; }
        public int? MaxConcurrentCalls { get; set; }
        public string OpenTimeout { get; set; }
        public string CloseTimeout { get; set; }
        public string SendTimeout { get; set; }
        public bool? EstablishSecurityContext { get; set; }
        public bool? NegotiateServiceCredential { get; set; }

        /// <summary>
        /// Message assignments from Construct blocks.
        /// </summary>
        public Dictionary<string, string> MessagePropertyAssignments { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Transform class name for XSLT actions.
        /// </summary>
        public string TransformClassName { get; set; }

        /// <summary>
        /// The normalized action name that produced the message this action consumes.
        /// When null, the generator falls back to @triggerBody().
        /// </summary>
        public string InputMessageSourceAction { get; set; }

        /// <summary>
        /// The BizTalk message name this action produces (e.g., "Message_2").
        /// Used by downstream actions to resolve their InputMessageSourceAction.
        /// </summary>
        public string OutputMessageName { get; set; }
    }
}
