// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// PATCH: Enforce max trigger name length (<=80) to avoid InvalidWorkflowTriggerName.
// Added AbbreviateTriggerName to shorten long trigger names with a hash suffix.
// Applied when non-HTTP trigger name exceeds 80 chars.
// UPDATE: BuildTrigger now uses ConnectorSchemaRegistry for accurate trigger generation
// UPDATE: BuildSendProviderMapping now uses ConnectorSchemaRegistry for accurate action generation
// FIX: Parallel branch synchronization - adds explicit join action after parallel execution
// FIX: Expression assignment now uses raw string value instead of escaped literal
// C# 7.3 compatible: removed indexer initializers inside object initializers.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BizTalktoLogicApps.ODXtoWFMigrator
{
    /// <summary>
    /// Generates Azure Logic Apps Standard workflow JSON definitions from mapped BizTalk orchestrations.
    /// Converts triggers, actions, and control flow structures to Logic Apps workflow definition language.
    /// </summary>
    public static class LogicAppJSONGenerator   
    {
        private const int MaxNameLen = 80;
        private const string SchemaVersion = "2016-06-01";

        /// <summary>
        /// Generates a complete Logic Apps Standard workflow JSON from a workflow map.
        /// </summary>
        /// <param name="map">The workflow map containing triggers, actions, and variables.</param>
        /// <param name="workflowKind">The workflow kind ("Stateful" or "Stateless"). Defaults to "Stateful".</param>
        /// <param name="schemaVersion">The Logic Apps schema version. Defaults to "2023-01-31-preview".</param>
        /// <param name="registry">Optional connector schema registry for accurate connector generation.</param>
        /// <returns>A JSON string representing the complete Logic Apps workflow definition.</returns>
        /// <exception cref="ArgumentNullException">Thrown when map is null.</exception>
        public static string GenerateStandardWorkflow(LogicAppWorkflowMap map, string workflowKind = "Stateful", string schemaVersion = null, ConnectorSchemaRegistry registry = null)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            JObject definition = BuildDefinition(map, schemaVersion ?? SchemaVersion, registry);

            var root = new JObject();
            root["kind"] = workflowKind;
            root["definition"] = definition;
            return JsonConvert.SerializeObject(root, Formatting.Indented);
        }

        /// <summary>
        /// Builds the workflow definition object containing schema, triggers, actions, and outputs.
        /// </summary>
        /// <param name="map">The workflow map to convert.</param>
        /// <param name="schemaVersion">The Logic Apps schema version.</param>
        /// <param name="registry">Optional connector schema registry.</param>
        /// <returns>A JObject containing the complete workflow definition.</returns>
        private static JObject BuildDefinition(LogicAppWorkflowMap map, string schemaVersion, ConnectorSchemaRegistry registry)
        {
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var trigger = map.Triggers.OrderBy(t => t.Sequence).FirstOrDefault()
                          ?? new LogicAppTrigger { Name = "When_an_HTTP_request_is_received", Kind = "Request", Sequence = -1 };

            string triggerName = AllocateName(NormalizeName(trigger.Name ?? "Trigger"), usedNames);
            var triggersObj = new JObject();
            triggersObj[triggerName] = BuildTrigger(trigger, registry);

            // ✅ PASS VARIABLE NAMES to BuildActions
            var actionsObj = BuildActions(map.Actions.OrderBy(a => a.Sequence), usedNames, registry, map.VariableNames);

            // Post-process: hoist Terminate actions out of Until loops.
            // Logic Apps forbids Terminate inside Until. Replace with variable
            // signalling inside the loop and a conditional Terminate after it.
            HoistTerminateFromUntilLoops(actionsObj, usedNames);

            var def = new JObject();
            def["$schema"] = string.Format("https://schema.management.azure.com/providers/Microsoft.Logic/schemas/{0}/workflowdefinition.json#", schemaVersion);
            def["contentVersion"] = "1.0.0.0";
            def["triggers"] = triggersObj;
            def["actions"] = actionsObj;
            def["outputs"] = new JObject();
            return def;
        }

        /// <summary>
        /// Builds a Logic Apps trigger JSON object from a trigger model.
        /// Uses connector registry if available, otherwise falls back to legacy mapping.
        /// </summary>
        /// <param name="t">The trigger model to convert.</param>
        /// <param name="registry">Optional connector schema registry for accurate trigger generation.</param>
        /// <returns>A JObject representing the trigger configuration.</returns>
        private static JObject BuildTrigger(LogicAppTrigger t, ConnectorSchemaRegistry registry)
        {
            // ✅ FIXED: Also handle Kind = "Http" for WCF triggers that are mapped to HTTP
            if (t.Kind == null || t.Kind == "Request" || t.Kind.Equals("Http", StringComparison.OrdinalIgnoreCase))
            {
                var req = new JObject();
                req["type"] = "Request";
                req["kind"] = "Http";
                
                // Diagnostic: Log trigger properties
                Trace.TraceInformation("[GENERATOR] Building HTTP Request trigger: TransportType='{0}', SecurityMode='{1}', Address='{2}'",
                    t.TransportType ?? "NULL", t.SecurityMode ?? "NULL", t.Address ?? "NULL");
                
                // ✅ Add WCF metadata as description/metadata for context preservation
                if (!string.IsNullOrEmpty(t.SecurityMode) ||
                    !string.IsNullOrEmpty(t.MessageClientCredentialType) ||
                    !string.IsNullOrEmpty(t.TransportType))
                {
                    Trace.TraceInformation("[GENERATOR] WCF metadata detected - adding to trigger");
                    var metadata = BuildWcfMetadataDescription(t);
                    if (!string.IsNullOrEmpty(metadata))
                    {
                        req["description"] = metadata;
                    }
                    
                    // Add metadata object for programmatic access
                    var metadataObj = BuildWcfMetadataObject(t);
                    if (metadataObj.Count > 0)
                    {
                        req["metadata"] = metadataObj;
                    }
                }
                else
                {
                    Trace.TraceInformation("[GENERATOR] No WCF metadata found - skipping enrichment");
                }
                
                return req;
            }

            if (t.Kind.Equals("X12", StringComparison.OrdinalIgnoreCase) ||
                t.Kind.Equals("EDIFACT", StringComparison.OrdinalIgnoreCase))
            {
                var req = new JObject();
                req["type"] = "Request";
                req["kind"] = "Http";
                return req;
            }

            if (t.Kind.Equals("Http", StringComparison.OrdinalIgnoreCase))
            {
                var req = new JObject();
                req["type"] = "Request";
                req["kind"] = "Http";
                return req;
            }

            if (registry != null && registry.HasConnector(t.Kind))
            {
                return BuildTriggerFromRegistry(t, registry);
            }

            return BuildTriggerLegacy(t);
        }

        /// <summary>
        /// Builds a trigger using connector schema registry for accurate operation IDs and parameters.
        /// </summary>
        /// <param name="t">The trigger model.</param>
        /// <param name="registry">The connector schema registry.</param>
        /// <returns>A ServiceProvider trigger JObject with proper configuration.</returns>
        private static JObject BuildTriggerFromRegistry(LogicAppTrigger t, ConnectorSchemaRegistry registry)
        {
            var connector = registry.GetConnector(t.Kind);
            if (connector == null)
            {
                return BuildTriggerLegacy(t);
            }

            string operationId = InferTriggerOperationIdFromRegistry(t, connector);
            var operation = connector.Triggers.ContainsKey(operationId)
                ? connector.Triggers[operationId]
                : connector.Triggers.Values.FirstOrDefault();

            if (operation == null)
            {
                return BuildTriggerLegacy(t);
            }

            var parameters = BuildTriggerParameters(t, operation);

            var configuration = new JObject();
            configuration["connectionName"] = t.Kind.ToLowerInvariant();
            configuration["operationId"] = operation.OperationId;
            configuration["serviceProviderId"] = connector.ServiceProviderId;

            var inputs = new JObject();
            inputs["parameters"] = parameters;
            inputs["serviceProviderConfiguration"] = configuration;

            var trigger = new JObject();
            trigger["type"] = "ServiceProvider";
            trigger["inputs"] = inputs;

            if (operation.Kind != null && operation.Kind.Equals("Polling", StringComparison.OrdinalIgnoreCase))
            {
                trigger["kind"] = "Polling";
                trigger["recurrence"] = BuildRecurrence(t.PollingIntervalSeconds);
            }
            else if (operation.Kind != null)
            {
                trigger["kind"] = operation.Kind;
            }

            return trigger;
        }

        /// <summary>
        /// Infers the appropriate trigger operation ID from the connector schema based on trigger kind.
        /// Maps common patterns like FileSystem -> "whenFilesAreAdded", ServiceBus -> "receiveQueueMessage".
        /// </summary>
        /// <param name="t">The trigger model.</param>
        /// <param name="connector">The connector schema containing available trigger operations.</param>
        /// <returns>The operation ID to use for the trigger.</returns>
        private static string InferTriggerOperationIdFromRegistry(LogicAppTrigger t, ConnectorSchema connector)
        {
            string kind = t.Kind.ToLowerInvariant();

            if (kind == "filesystem")
            {
                if (connector.Triggers.ContainsKey("whenFilesAreAdded"))
                    return "whenFilesAreAdded";
                if (connector.Triggers.ContainsKey("whenFilesAreAddedOrModified"))
                    return "whenFilesAreAddedOrModified";
            }

            if (kind == "ftp" || kind == "sftp")
            {
                if (connector.Triggers.ContainsKey("whenFileIsAdded"))
                    return "whenFileIsAdded";
                if (connector.Triggers.ContainsKey("whenFileIsAddedOrModified"))
                    return "whenFileIsAddedOrModified";
            }

            if (kind == "sql")
            {
                if (connector.Triggers.ContainsKey("whenItemsAreModified"))
                    return "whenItemsAreModified";
            }

            if (kind == "servicebus")
            {
                if (connector.Triggers.ContainsKey("receiveQueueMessage"))
                    return "receiveQueueMessage";
                if (connector.Triggers.ContainsKey("receiveTopicMessage"))
                    return "receiveTopicMessage";
            }

            if (kind == "eventhub")
            {
                if (connector.Triggers.ContainsKey("receiveEvents"))
                    return "receiveEvents";
            }

            if (kind == "mllp")
            {
                if (connector.Triggers.ContainsKey("receiveMessage"))
                    return "receiveMessage";
            }

            if (kind == "ibmmq")
            {
                if (connector.Triggers.ContainsKey("receiveMessage"))
                    return "receiveMessage";
            }

            if (kind == "cosmosdb")
            {
                if (connector.Triggers.ContainsKey("whenDocumentsAreCreatedOrModified"))
                    return "whenDocumentsAreCreatedOrModified";
            }

            if (kind == "azureblob")
            {
                if (connector.Triggers.ContainsKey("whenBlobIsAdded"))
                    return "whenBlobIsAdded";
            }

            return connector.Triggers.Keys.FirstOrDefault() ?? "manual";
        }

        /// <summary>
        /// Builds trigger parameters based on the operation schema and trigger configuration.
        /// Maps trigger properties (FolderPath, FileMask, QueueName, etc.) to operation parameters.
        /// </summary>
        /// <param name="t">The trigger model with configuration details.</param>
        /// <param name="operation">The operation schema defining required parameters.</param>
        /// <returns>A JObject containing parameter name-value pairs.</returns>
        private static JObject BuildTriggerParameters(LogicAppTrigger t, OperationSchema operation)
        {
            var parameters = new JObject();
            /// This section need some serious refactoring in the future to make it more data-driven
            foreach (var paramName in operation.Parameters)
            {
                var lowerParam = paramName.ToLowerInvariant();

                if (lowerParam == "folderpath" || lowerParam == "path")
                {
                    parameters[paramName] = t.FolderPath ?? "/";
                }
                else if (lowerParam == "filemask" || lowerParam == "filter")
                {
                    parameters[paramName] = t.FileMask ?? "*";
                }
                else if (lowerParam == "queuename")
                {
                    parameters[paramName] = InferQueueName(t.Address) ?? "queue";
                }
                else if (lowerParam == "topicname")
                {
                    parameters[paramName] = InferQueueName(t.Address) ?? "topic";
                }
                else if (lowerParam == "subscriptionname")
                {
                    parameters[paramName] = "subscription";
                }
                else if (lowerParam == "table" || lowerParam == "tablename")
                {
                    parameters[paramName] = InferTableName(t.Address) ?? "Table";
                }
                else if (lowerParam == "eventhubname")
                {
                    parameters[paramName] = InferEventHubName(t.Address) ?? "eventhub";
                }
                else if (lowerParam == "consumergroup")
                {
                    parameters[paramName] = "$Default";
                }
                else if (lowerParam == "endpoint")
                {
                    parameters[paramName] = t.Address ?? "endpoint";
                }
                else if (lowerParam == "databasename")
                {
                    parameters[paramName] = "Database";
                }
                else if (lowerParam == "containername")
                {
                    parameters[paramName] = "container";
                }
                else if (lowerParam == "leasecontainername")
                {
                    parameters[paramName] = "leases";
                }
                else if (lowerParam == "blobprefix" || lowerParam == "prefix")
                {
                    parameters[paramName] = "";
                }
                else if (lowerParam == "includefilecontent")
                {
                    parameters[paramName] = true;
                }
                else if (lowerParam == "issessionsenabled")
                {
                    parameters[paramName] = false;
                }
                else if (lowerParam.Contains("max") && lowerParam.Contains("count"))
                {
                    parameters[paramName] = 10;
                }
                else if (lowerParam == "select" || lowerParam == "orderby")
                {
                    parameters[paramName] = "*";
                }
                else
                {
                    parameters[paramName] = "";
                }
            }

            return parameters;
        }

        /// <summary>
        /// Builds a trigger using legacy hardcoded mappings when connector registry is not available.
        /// Provides fallback trigger generation for common connector types.
        /// </summary>
        /// <param name="t">The trigger model.</param>
        /// <returns>A ServiceProvider trigger JObject with default configuration.</returns>
        private static JObject BuildTriggerLegacy(LogicAppTrigger t)
        {
            var configuration = new JObject();
            configuration["connectionName"] = t.Kind.ToLowerInvariant();
            configuration["operationId"] = InferTriggerOperationId(t.Kind);
            configuration["serviceProviderId"] = "/serviceProviders/" + t.Kind;

            var parameters = new JObject();
            switch (t.Kind)
            {
                case "FileSystem":
                    parameters["folderPath"] = t.FolderPath ?? "/";
                    parameters["fileMask"] = t.FileMask ?? "*";
                    break;
                case "Ftp":
                case "Sftp":
                    parameters["folderPath"] = t.FolderPath ?? "/";
                    parameters["fileMask"] = t.FileMask ?? "*";
                    break;
                case "Sql":
                    parameters["table"] = InferTableName(t.Address) ?? "Table";
                    break;
                case "ServiceBus":
                    parameters["queueName"] = InferQueueName(t.Address) ?? "queue";
                    break;
                case "EventHub":
                    parameters["eventHubName"] = InferEventHubName(t.Address) ?? "eventhub";
                    break;
                case "AS2":
                    parameters["partner"] = InferPartner(t.Address) ?? "Partner";
                    break;
                case "Mllp":
                    parameters["endpoint"] = t.Address ?? "mllp://host:port";
                    break;
                case "IbmMq":
                    parameters["queueName"] = InferQueueName(t.Address) ?? "queue";
                    break;
                case "OutlookEmail":
                case "GmailEmail":
                case "ExchangeOnlineEmail":
                    parameters["folder"] = "Inbox";
                    break;
            }

            var inputs = new JObject();
            inputs["parameters"] = parameters;
            inputs["serviceProviderConfiguration"] = configuration;

            var trigger = new JObject();
            trigger["type"] = "ServiceProvider";
            trigger["inputs"] = inputs;

            if (IsPollingConnector(t.Kind))
            {
                trigger["kind"] = "Polling";
                trigger["recurrence"] = BuildRecurrence(t.PollingIntervalSeconds);
            }

            return trigger;
        }

        /// <summary>
        /// Determines if a connector kind uses polling triggers.
        /// Polling connectors require recurrence configuration (FileSystem, FTP, SQL, Email, etc.).
        /// </summary>
        /// <param name="kind">The connector kind to check.</param>
        /// <returns>True if the connector uses polling triggers; otherwise false.</returns>
        private static bool IsPollingConnector(string kind)
        {
            return new[] { "FileSystem", "Ftp", "Sftp", "Sql", "OutlookEmail", "GmailEmail", "ExchangeOnlineEmail" }
                .Contains(kind ?? "", StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Builds a recurrence configuration for polling triggers.
        /// Converts polling interval from seconds to minutes with a minimum of 1 minute.
        /// </summary>
        /// <param name="pollingSeconds">The polling interval in seconds from BizTalk configuration.</param>
        /// <returns>A JObject containing frequency and interval configuration.</returns>
        private static JObject BuildRecurrence(int? pollingSeconds)
        {
            int minutes = pollingSeconds.HasValue && pollingSeconds.Value > 0
                ? Math.Max(1, pollingSeconds.Value / 60)
                : 1;
            var recur = new JObject();
            recur["frequency"] = "Minute";
            recur["interval"] = minutes;
            return recur;
        }

        /// <summary>
        /// Builds the actions object for the workflow, including variable initialization and action sequencing.
        /// Initializes all variables at the start, then processes actions in sequence with proper runAfter dependencies.
        /// </summary>
        /// <param name="ordered">The ordered collection of actions to process.</param>
        /// <param name="used">A set of already-used action names to prevent duplicates.</param>
        /// <param name="registry">Optional connector schema registry for accurate action generation.</param>
        /// <param name="variableNames">List of variable names to initialize at workflow start.</param>
        /// <returns>A JObject containing all workflow actions with proper sequencing.</returns>
        private static JObject BuildActions(IEnumerable<LogicAppAction> ordered, HashSet<string> used, ConnectorSchemaRegistry registry, List<string> variableNames)
        {
            var actionsObj = new JObject();
            string lastLinear = null;

            // ✅ REMOVED: Hardcoded variable initialization - now handled by VariableDeclaration shapes in mapper
            // Variables are initialized by InitializeVariable actions created from VariableDeclaration shapes,
            // which properly map BizTalk types to Logic Apps types

            // Process actions in sequence
            foreach (var act in ordered)
            {
                if (act.Type == "ParallelContainer")
                {
                    var parallelResult = BuildParallelBranches(act, lastLinear, used, registry, variableNames, actionsObj);
                    lastLinear = parallelResult.JoinActionName;
                    continue;
                }

                // ListenContainer: BizTalk Listen shape (first-branch-wins race).
                // Logic Apps has no native first-one-wins construct, so we generate
                // parallel branches with an explicit migration warning in the join action.
                if (act.Type == "ListenContainer")
                {
                    var listenResult = BuildParallelBranches(act, lastLinear, used, registry, variableNames, actionsObj);

                    // Replace the default join with a warning-annotated one
                    if (listenResult.JoinActionName != null && actionsObj[listenResult.JoinActionName] != null)
                    {
                        actionsObj[listenResult.JoinActionName]["inputs"] = new JObject
                        {
                            ["migrationWarning"] = "BizTalk Listen shape: only the FIRST branch should execute (race pattern). " +
                                                   "Logic Apps runs ALL branches in parallel. Manual review required.",
                            ["originalBizTalkShape"] = "Listen",
                            ["requiredAction"] = "Convert to Switch on status/timeout, or add Terminate actions to losing branches."
                        };
                    }

                    lastLinear = listenResult.JoinActionName;
                    continue;
                }

                var actionName = AllocateName(NormalizeName(act.Name), used);
                var actionObj = BuildAction(act, used, registry, variableNames);

                var runAfter = new JObject();
                if (lastLinear != null)
                {
                    runAfter[lastLinear] = new JArray("SUCCEEDED");
                }
                actionObj["runAfter"] = runAfter;
                actionsObj[actionName] = actionObj;
                lastLinear = actionName;
            }

            return actionsObj;
        }

        /// <summary>
        /// Result object returned from parallel branch processing.
        /// Contains the join action name that synchronizes all parallel branches.
        /// </summary>
        private sealed class ParallelBranchResult
        {
            /// <summary>
            /// Gets or sets the name of the join action that waits for all parallel branches to complete.
            /// </summary>
            public string JoinActionName { get; set; }
        }

        /// <summary>
        /// Builds parallel branch actions with explicit synchronization join action.
        /// Each branch executes concurrently, with a join action waiting for all branches to complete.
        /// Handles empty branches by adding placeholder actions to ensure proper execution flow.
        /// </summary>
        /// <param name="parallelContainer">The parallel container action with branch children.</param>
        /// <param name="lastLinear">The name of the last sequential action before the parallel split.</param>
        /// <param name="usedNames">Set of used action names to prevent duplicates.</param>
        /// <param name="registry">Optional connector schema registry.</param>
        /// <param name="variableNames">List of variable names for expression mapping.</param>
        /// <param name="actionsObj">The actions object to add branch actions to.</param>
        /// <returns>A ParallelBranchResult containing the join action name for subsequent sequencing.</returns>
        private static ParallelBranchResult BuildParallelBranches(
            LogicAppAction parallelContainer,
            string lastLinear,
            HashSet<string> usedNames,
            ConnectorSchemaRegistry registry,
            List<string> variableNames,
            JObject actionsObj)
        {
            var branchEndActions = new List<string>();
            var branchIndex = 0;

            foreach (var branch in parallelContainer.Children)
            {
                branchIndex++;
                string branchPrev = lastLinear;
                string lastAddedActionInBranch = null;
                var branchActions = FlattenBranch(branch).ToList();

                if (branchActions.Count == 0)
                {
                    // Empty branch - add placeholder
                    var placeholderName = AllocateName(
                        NormalizeName("Parallel_Branch_" + branchIndex + "_Placeholder"),
                        usedNames);

                    var placeholder = new JObject();
                    placeholder["type"] = "Compose";
                    placeholder["inputs"] = "Parallel branch " + branchIndex;

                    var runAfter = new JObject();
                    if (branchPrev != null)
                    {
                        runAfter[branchPrev] = new JArray("SUCCEEDED");
                    }
                    placeholder["runAfter"] = runAfter;

                    actionsObj[placeholderName] = placeholder;
                    branchEndActions.Add(placeholderName);
                }
                else
                {
                    // Process actions in this branch
                    foreach (var inner in branchActions)
                    {
                        // ✅ CRITICAL FIX: Detect nested ParallelContainer/ListenContainer and handle recursively
                        if (inner.Type == "ParallelContainer" || inner.Type == "ListenContainer")
                        {
                            
                            // Recursively build the nested parallel branches
                            var nestedParallelResult = BuildParallelBranches(inner, branchPrev, usedNames, registry, variableNames, actionsObj);
                            branchPrev = nestedParallelResult.JoinActionName;
                            lastAddedActionInBranch = nestedParallelResult.JoinActionName;
                            continue;  // Skip BuildAction - we handled it above
                        }
                        
                        var actionName = AllocateName(NormalizeName(inner.Name), usedNames);
                        var actionObj = BuildAction(inner, usedNames, registry, variableNames);

                        // ✅ FIX: Skip null actions (empty scopes, etc.)
                        if (actionObj == null)
                        {
                            continue;
                        }

                        var runAfter = new JObject();
                        if (branchPrev != null)
                        {
                            runAfter[branchPrev] = new JArray("SUCCEEDED");
                        }
                        actionObj["runAfter"] = runAfter;

                        actionsObj[actionName] = actionObj;
                        branchPrev = actionName;
                        lastAddedActionInBranch = actionName;
                    }

                    // ✅ FIX: Handle case where all actions in branch were null
                    if (lastAddedActionInBranch == null)
                    {

                        var placeholderName = AllocateName(
                            NormalizeName("Parallel_Branch_" + branchIndex + "_Empty"),
                            usedNames);

                        var placeholder = new JObject();
                        placeholder["type"] = "Compose";
                        placeholder["inputs"] = "// Empty parallel branch " + branchIndex + " (all child scopes were empty)";

                        var runAfter = new JObject();
                        if (lastLinear != null)
                        {
                            runAfter[lastLinear] = new JArray("SUCCEEDED");
                        }
                        placeholder["runAfter"] = runAfter;

                        actionsObj[placeholderName] = placeholder;
                        branchEndActions.Add(placeholderName);
                    }
                    else if (lastAddedActionInBranch != lastLinear)
                    {
                        // Successfully added at least one action
                        branchEndActions.Add(lastAddedActionInBranch);
                    }
                }
            }

            string joinActionName = null;

            if (branchEndActions.Count > 0)
            {
                joinActionName = AllocateName(
                    NormalizeName(parallelContainer.Name + "_Join"),
                    usedNames);

                var joinAction = new JObject();
                joinAction["type"] = "Compose";
                joinAction["inputs"] = new JObject
                {
                    ["parallelBranchesCompleted"] = true,
                    ["branchCount"] = branchEndActions.Count
                };

                var joinRunAfter = new JObject();
                foreach (var branchEnd in branchEndActions)
                {
                    joinRunAfter[branchEnd] = new JArray("SUCCEEDED");
                }
                joinAction["runAfter"] = joinRunAfter;

                actionsObj[joinActionName] = joinAction;
            }
            else
            {
                // No branches produced any actions - return the last linear action
                joinActionName = lastLinear;
            }

            return new ParallelBranchResult
            {
                JoinActionName = joinActionName
            };
        }

        /// <summary>
        /// Flattens a parallel branch scope to extract its child actions.
        /// If the scope is a branch container, returns its children; otherwise returns the scope and its children.
        /// </summary>
        /// <param name="scope">The scope action to flatten.</param>
        /// <returns>An enumerable of actions within the branch.</returns>
        private static IEnumerable<LogicAppAction> FlattenBranch(LogicAppAction scope)
        {
            if (scope.IsBranchContainer)
            {
                return scope.Children.OrderBy(c => c.Sequence).ToList();
            }

            var list = new List<LogicAppAction>();
            list.Add(scope);
            foreach (var c in scope.Children.OrderBy(c => c.Sequence))
                list.Add(c);
            return list;
        }

        /// <summary>
        /// Builds a Logic Apps action JSON object from an action model.
        /// Handles all action types including Compose, If, Foreach, Scope, ServiceProvider connectors, etc.
        /// </summary>
        /// <param name="act">The action model to convert.</param>
        /// <param name="usedNames">Set of used action names to prevent duplicates.</param>
        /// <param name="registry">Optional connector schema registry for connector actions.</param>
        /// <param name="variableNames">List of variable names for expression mapping context.</param>
        /// <returns>A JObject representing the action, or null if the action is empty/should be skipped.</returns>
        private static JObject BuildAction(LogicAppAction act, HashSet<string> usedNames, ConnectorSchemaRegistry registry, List<string> variableNames)
        {
            if (act.Type == "SendConnector" && !string.IsNullOrEmpty(act.ConnectorKind))
            {
                if (act.ConnectorKind.Equals("Http", StringComparison.OrdinalIgnoreCase) ||
                    act.ConnectorKind.Equals("Request", StringComparison.OrdinalIgnoreCase))
                {
                    var http = new JObject();
                    http["type"] = "Http";

                    var httpInputs = new JObject();
                    httpInputs["method"] = act.HttpMethod ?? "POST";
                    httpInputs["uri"] = act.TargetAddress ?? "http://localhost/service";
                    httpInputs["body"] = ResolveMessageBodyExpression(act);

                    var headers = new JObject();
                    headers["Content-Type"] = DetermineContentType(act);
                    if (!string.IsNullOrEmpty(act.SoapAction))
                    {
                        headers["SOAPAction"] = act.SoapAction;
                    }
                    httpInputs["headers"] = headers;

                    http["inputs"] = httpInputs;
                    return http;
                }

                var mapping = BuildSendProviderMapping(act, registry);
                var svcInputs = new JObject();
                svcInputs["parameters"] = mapping.Parameters;
                var svcConfig = new JObject();
                svcConfig["connectionName"] = mapping.ConnectionName;
                svcConfig["operationId"] = mapping.OperationId;
                svcConfig["serviceProviderId"] = "/serviceProviders/" + mapping.ServiceProviderId;
                svcInputs["serviceProviderConfiguration"] = svcConfig;

                var svcAction = new JObject();
                svcAction["type"] = "ServiceProvider";
                svcAction["inputs"] = svcInputs;
                return svcAction;
            }

            switch (act.Type)
            {
                case "InitializeVariable":
                    var initVar = new JObject();
                    initVar["type"] = "InitializeVariable";
                    
                    var initInputs = new JObject();
                    var variables = new JArray();
                    var variable = new JObject();
                    variable["name"] = act.Name;
                    
                    // ✅ FIX: Check if Details is already a Logic Apps type (from InferVariableType)
                    // or a BizTalk CLR type (from VariableDeclarationShapeModel.VarType)
                    var knownLogicAppsTypes = new[] { "string", "integer", "boolean", "object", "array", "float" };
                    var logicAppsType = knownLogicAppsTypes.Contains(act.Details?.ToLowerInvariant())
                        ? act.Details.ToLowerInvariant()  // Already a Logic Apps type - use directly
                        : MapBizTalkTypeToLogicApps(act.Details ?? "System.String");  // BizTalk type - map it
                    
                    variable["type"] = logicAppsType;
                    
                    var defaultValue = GetDefaultValueForType(logicAppsType);
                    if (defaultValue is JArray || defaultValue is JObject)
                    {
                        variable["value"] = (JToken)defaultValue;
                    }
                    else if (defaultValue == null)
                    {
                        variable["value"] = JValue.CreateNull();
                    }
                    else
                    {
                        variable["value"] = JToken.FromObject(defaultValue);
                    }
                    
                    variables.Add(variable);
                    initInputs["variables"] = variables;
                    initVar["inputs"] = initInputs;
                    
                    return initVar;

                case "Compose":
                    var compose = new JObject();
                    compose["type"] = "Compose";

                    // Get the details and preprocess for ActivationMessage references
                    var details = act.Details ?? "";

                    // Replace ActivationMessage references with triggerBody()
                    // This handles the BizTalk pattern where the activation message is the first receive
                    if (details.Contains("ActivationMessage"))
                    {
                        // Replace ActivationMessage with triggerBody() before expression mapping
                        details = System.Text.RegularExpressions.Regex.Replace(
                            details,
                            @"\bActivationMessage\b",
                            "triggerBody()",
                            System.Text.RegularExpressions.RegexOptions.None);
                    }

                    compose["inputs"] = ExpressionMapper.MapExpression(details, variableNames);
                    return compose;

                case "Wait":
                case "Delay":
                    var wait = new JObject();
                    wait["type"] = "Wait";
                    var waitInputs = new JObject();
                    var interval = new JObject();
                    var parsedDelay = ParseDelayExpression(act.Details);
                    interval["count"] = parsedDelay.Count;
                    interval["unit"] = parsedDelay.Unit;
                    waitInputs["interval"] = interval;
                    wait["inputs"] = waitInputs;
                    return wait;

                case "CSharpScriptCode":
                    var script = new JObject();
                    script["type"] = "CSharpScriptCode";
                    var scriptInputs = new JObject();
                    scriptInputs["CodeFile"] = "execute_csharp_script_code.csx";
                    script["inputs"] = scriptInputs;
                    return script;

                case "Xslt":
                    var xslt = new JObject();
                    xslt["type"] = "Xslt";
                    var xsltInputs = new JObject();

                    var mapShortName = ExtractMapShortName(act.Details);

                    xsltInputs["content"] = ResolveMessageBodyExpression(act);
                    xsltInputs["map"] = new JObject
                    {
                        ["name"] = mapShortName,
                        ["type"] = "Xslt"
                    };
                    xsltInputs["transformOptions"] = "ApplyXsltTemplates";

                    xslt["inputs"] = xsltInputs;
                    return xslt;

                case "RuleExecute":
                    var rule = new JObject();
                    rule["type"] = "RuleExecute";
                    var ruleInputs = new JObject();
                    ruleInputs["ruleSet"] = act.Details ?? "Ruleset";
                    ruleInputs["ruleStore"] = "FileFolder";
                    rule["inputs"] = ruleInputs;
                    return rule;

                case "Workflow":
                    var wf = new JObject();
                    wf["type"] = "Workflow";
                    var wfInputs = new JObject();

                    // Extract workflow name from Details
                    var workflowName = act.Details ?? "ChildWorkflow";
                    
                    // ✅ FIX: Check for empty string, not just null
                    if (string.IsNullOrWhiteSpace(workflowName))
                    {
                        workflowName = "ChildWorkflow";
                    }

                    // If it's a fully qualified BizTalk orchestration name, use just the last segment
                    if (workflowName.Contains("."))
                    {
                        var parts = workflowName.Split('.');
                        workflowName = parts[parts.Length - 1];
                    }
                    
                    // ✅ DEFENSIVE: Final check after split - if empty, use fallback
                    if (string.IsNullOrWhiteSpace(workflowName))
                    {
                        workflowName = "ChildWorkflow_" + (act.Sequence > 0 ? act.Sequence.ToString() : "1");
                    }

                    // ✅ FIX: Azure Logic Apps Standard - for workflows in the same Logic App, use just the workflow name
                    // The workflow name must contain only letters, digits, '-', '.', '(', ')', or '_'
                    // For cross-Logic App calls, you would need the full resource ID path
                    var host = new JObject();
                    var workflowRef = new JObject();
                    workflowRef["id"] = workflowName;  // ✅ Just the workflow name for same Logic App
                    host["workflow"] = workflowRef;
                    wfInputs["host"] = host;

                    // Body to pass to child workflow
                    var body = new JObject();
                    body["message"] = ResolveMessageBodyExpression(act);
                    wfInputs["body"] = body;

                    wf["inputs"] = wfInputs;
                    return wf;

                case "Terminate":
                    var term = new JObject();
                    term["type"] = "Terminate";
                    var termInputs = new JObject();
                    termInputs["runStatus"] = "Failed";
                    var runErr = new JObject();
                    runErr["code"] = "Terminated";
                    runErr["message"] = act.Details ?? "Terminated";
                    termInputs["runError"] = runErr;
                    term["inputs"] = termInputs;
                    return term;

                case "Foreach":
                    var foreachAction = new JObject();
                    foreachAction["type"] = "Foreach";

                    var collection = act.Details ?? "@triggerBody()?['items']";
                    foreachAction["foreach"] = ExpressionMapper.MapExpression(collection, variableNames);

                    var foreachActions = new JObject();
                    string prevForeach = null;
                    foreach (var c in act.Children.OrderBy(c => c.Sequence))
                    {
                        string childName = AllocateName(NormalizeName(c.Name), usedNames);
                        var childAction = BuildAction(c, usedNames, registry, variableNames);
                        childAction["runAfter"] = prevForeach == null
                            ? new JObject()
                            : new JObject { [prevForeach] = new JArray("SUCCEEDED") };
                        foreachActions[childName] = childAction;
                        prevForeach = childName;
                    }
                    foreachAction["actions"] = foreachActions;

                    var runtimeConfig = new JObject();
                    runtimeConfig["concurrency"] = new JObject
                    {
                        ["repetitions"] = 20
                    };
                    foreachAction["runtimeConfiguration"] = runtimeConfig;

                    return foreachAction;

                case "If":
                    var ifAction = new JObject();
                    ifAction["type"] = "If";

                    // ✅ KEY FIX: Pass variable names to expression mapper
                    var condition = act.Details ?? "@equals(1,1)";
                    var mappedExpression = ExpressionMapper.MapExpression(condition, variableNames);

                    // Assign the mapped expression directly as a string value
                    ifAction["expression"] = mappedExpression;

                    var trueActions = new JObject();
                    if (act.TrueBranch != null && act.TrueBranch.Count > 0)
                    {
                        string prevTrue = null;
                        foreach (var c in act.TrueBranch.OrderBy(c => c.Sequence))
                        {
                            // ✅ CRITICAL FIX: Detect ParallelContainer/ListenContainer in If branches
                            if (c.Type == "ParallelContainer" || c.Type == "ListenContainer")
                            {
                                // Build parallel branches into the true branch actions
                                var parallelResult = BuildParallelBranches(c, prevTrue, usedNames, registry, variableNames, trueActions);
                                prevTrue = parallelResult.JoinActionName;
                                continue;
                            }
                            
                            string childName = AllocateName(NormalizeName(c.Name), usedNames);
                            var childAction = BuildAction(c, usedNames, registry, variableNames);

                            if (prevTrue == null)
                            {
                                childAction["runAfter"] = new JObject();
                            }
                            else
                            {
                                childAction["runAfter"] = new JObject { [prevTrue] = new JArray("SUCCEEDED") };
                            }

                            trueActions[childName] = childAction;
                            prevTrue = childName;
                        }
                    }
                    ifAction["actions"] = trueActions;

                    var falseActions = new JObject();
                    if (act.FalseBranch != null && act.FalseBranch.Count > 0)
                    {
                        if (act.FalseBranch.Count == 1 && act.FalseBranch[0].Type == "If")
                        {
                            var nestedIf = act.FalseBranch[0];
                            string nestedName = AllocateName(NormalizeName(nestedIf.Name), usedNames);
                            var nestedAction = BuildAction(nestedIf, usedNames, registry, variableNames);
                            nestedAction["runAfter"] = new JObject();
                            falseActions[nestedName] = nestedAction;
                        }
                        else
                        {
                            string prevFalse = null;
                            foreach (var c in act.FalseBranch.OrderBy(c => c.Sequence))
                            {
                                // ✅ CRITICAL FIX: Detect ParallelContainer/ListenContainer in If FALSE branch
                                if (c.Type == "ParallelContainer" || c.Type == "ListenContainer")
                                {
                                    // Build parallel branches into the false branch actions
                                    var parallelResult = BuildParallelBranches(c, prevFalse, usedNames, registry, variableNames, falseActions);
                                    prevFalse = parallelResult.JoinActionName;
                                    continue;
                                }
                                
                                string childName = AllocateName(NormalizeName(c.Name), usedNames);
                                var childAction = BuildAction(c, usedNames, registry, variableNames);

                                if (prevFalse == null)
                                {
                                    childAction["runAfter"] = new JObject();
                                }
                                else
                                {
                                    childAction["runAfter"] = new JObject { [prevFalse] = new JArray("SUCCEEDED") };
                                }

                                falseActions[childName] = childAction;
                                prevFalse = childName;
                            }
                        }
                    }

                    ifAction["else"] = new JObject
                    {
                        ["actions"] = falseActions
                    };

                    return ifAction;

                case "Until":
                    var until = new JObject();
                    until["type"] = "Until";
                    
                    // Use action.Details which contains the inverted expression from mapper
                    var untilCondition = act.Details ?? "@equals(1,1)";
                    var untilExpr = ExpressionMapper.MapExpression(untilCondition, variableNames);
                    until["expression"] = untilExpr;
                    
                    var limit = new JObject();
                    limit["count"] = act.LoopThreshold ?? 60;
                    limit["timeout"] = "PT1H";
                    until["limit"] = limit;

                    var untilActions = new JObject();
                    string prev = null;
                    foreach (var c in act.Children.OrderBy(c => c.Sequence))
                    {
                        // Handle nested ParallelContainer/ListenContainer inside Until loops
                        if (c.Type == "ParallelContainer" || c.Type == "ListenContainer")
                        {
                            var nestedResult = BuildParallelBranches(c, prev, usedNames, registry, variableNames, untilActions);
                            prev = nestedResult.JoinActionName;
                            continue;
                        }

                        string allocated = AllocateName(NormalizeName(c.Name), usedNames);
                        var inner = BuildAction(c, usedNames, registry, variableNames);
                        if (inner == null) continue;
                        inner["runAfter"] = prev == null ? new JObject() : new JObject { [prev] = new JArray("SUCCEEDED") };
                        untilActions[allocated] = inner;
                        prev = allocated;
                    }

                    until["actions"] = untilActions;
                    return until;

                case "Scope":
                    var scope = new JObject();
                    scope["type"] = "Scope";

                    var childObj = new JObject();
                    string p = null;

                    // ✅ FIX: Filter out exception handlers AND Task containers that should be flattened
                    var normalActions = act.Children
                        .Where(c =>
                        {
                            // Keep non-Scope actions
                            if (c.Type != "Scope") return true;

                            // Filter out exception handler scopes
                            if (c.Details != null && c.Details.Contains("Exception handler")) return false;

                            // Keep all other scopes (including Task scopes, compensation scopes, etc.)
                            return true;
                        })
                        .ToList();

                    var catchActions = act.Children
                        .Where(c => c.Type == "Scope" &&
                                   c.Details != null && c.Details.Contains("Exception handler"))
                        .ToList();

                    foreach (var c in normalActions.OrderBy(c => c.Sequence))
                    {
                        // ✅ CRITICAL FIX: Detect ParallelContainer/ListenContainer and handle specially
                        if (c.Type == "ParallelContainer" || c.Type == "ListenContainer")
                        {
                            // Build parallel branches directly into this scope's action collection
                            var parallelResult = BuildParallelBranches(c, p, usedNames, registry, variableNames, childObj);
                            p = parallelResult.JoinActionName;
                            continue;  // Skip BuildAction - we handled it above
                        }
                        
                        string cnameAllocated = AllocateName(NormalizeName(c.Name), usedNames);
                        var cjson = BuildAction(c, usedNames, registry, variableNames);

                        // ✅ CRITICAL FIX: If the child action is null (empty scope), don't add it
                        if (cjson == null)
                        {
                            continue;
                        }

                        cjson["runAfter"] = p == null ? new JObject() : new JObject { [p] = new JArray("SUCCEEDED") };
                        childObj[cnameAllocated] = cjson;
                        p = cnameAllocated;
                    }
                    scope["actions"] = childObj;

                    // ✅ ENHANCED: Add defensive checks for catch block processing
                    if (catchActions.Count > 0 && p != null)
                    {
                        foreach (var catchAction in catchActions)
                        {
                            if (catchAction.Children == null || catchAction.Children.Count == 0)
                            {
                                continue;
                            }

                            string catchName = AllocateName(NormalizeName(catchAction.Name), usedNames);

                            // Build the catch scope directly with its children
                            var catchScope = new JObject();
                            catchScope["type"] = "Scope";

                            var catchChildObj = new JObject();
                            string catchPrev = null;

                            // Process catch block's children sequentially
                            foreach (var catchChild in catchAction.Children.OrderBy(c => c.Sequence))
                            {
                                string catchChildName = AllocateName(NormalizeName(catchChild.Name), usedNames);
                                var catchChildJson = BuildAction(catchChild, usedNames, registry, variableNames);

                                if (catchChildJson == null)
                                {
                                    continue;
                                }

                                catchChildJson["runAfter"] = catchPrev == null
                                    ? new JObject()
                                    : new JObject { [catchPrev] = new JArray("SUCCEEDED") };

                                catchChildObj[catchChildName] = catchChildJson;
                                catchPrev = catchChildName;
                            }

                            if (catchChildObj.Count > 0)
                            {
                                catchScope["actions"] = catchChildObj;
                                catchScope["runAfter"] = new JObject
                                {
                                    [p] = new JArray("FAILED", "TIMEDOUT", "SKIPPED")
                                };

                                childObj[catchName] = catchScope;
                            }
                        }
                    }

                    if (childObj.Count == 0)
                    {
                        return null;
                    }

                    return scope;

                case "X12Decode":
                    var x12 = new JObject();
                    x12["type"] = "ServiceProvider";
                    var x12Inputs = new JObject();
                    var x12Params = new JObject();
                    x12Params["content"] = "@triggerBody()";
                    x12Inputs["parameters"] = x12Params;
                    var x12Cfg = new JObject();
                    x12Cfg["connectionName"] = "x12";
                    x12Cfg["operationId"] = "decodeX12";
                    x12Cfg["serviceProviderId"] = "/serviceProviders/x12";
                    x12Inputs["serviceProviderConfiguration"] = x12Cfg;
                    x12["inputs"] = x12Inputs;
                    return x12;

                case "EdifactDecode":
                    var edf = new JObject();
                    edf["type"] = "ServiceProvider";
                    var edInputs = new JObject();
                    var edParams = new JObject();
                    edParams["content"] = "@triggerBody()";
                    edInputs["parameters"] = edParams;
                    var edCfg = new JObject();
                    edCfg["connectionName"] = "edifact";
                    edCfg["operationId"] = "decodeEdifact";
                    edCfg["serviceProviderId"] = "/serviceProviders/edifact";
                    edInputs["serviceProviderConfiguration"] = edCfg;
                    edf["inputs"] = edInputs;
                    return edf;

                case "ParallelContainer":
                case "ListenContainer":
                    return null;

                case "Switch":
                    var switchAction = new JObject();
                    switchAction["type"] = "Switch";
                    
                    // The expression to switch on (e.g., promoted property value)
                    // Details contains the promoted property name like "CBRSample.CountryCode"
                    var switchExpression = act.Details ?? "@triggerBody()?['property']";
                    
                    // Extract property name from namespace (e.g., "CBRSample.CountryCode" -> "CountryCode")
                    string propertyName = switchExpression;
                    if (switchExpression.Contains("."))
                    {
                        var parts = switchExpression.Split('.');
                        propertyName = parts[parts.Length - 1];
                    }
                    
                    // Build Logic Apps expression to access promoted property from trigger
                    // Note: Use ?['property'] syntax (no dot before square bracket)
                    switchAction["expression"] = "@triggerBody()?['" + propertyName + "']";
                    
                    // Build cases from children (each child is a case scope)
                    var cases = new JObject();
                    var defaultCase = new JObject();
                    var hasDefault = false;
                    
                    foreach (var caseScope in act.Children.OrderBy(c => c.Sequence))
                    {
                        // Determine if this is the default case
                        bool isDefault = caseScope.Name != null && 
                            (caseScope.Name.IndexOf("Default", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             caseScope.Name.IndexOf("Else", StringComparison.OrdinalIgnoreCase) >= 0);
                        
                        if (isDefault)
                        {
                            // Build default case actions
                            var defaultActions = new JObject();
                            string prevDefault = null;
                            
                            foreach (var child in caseScope.Children.OrderBy(c => c.Sequence))
                            {
                                string childName = AllocateName(NormalizeName(child.Name), usedNames);
                                var childAction = BuildAction(child, usedNames, registry, variableNames);
                                
                                if (childAction != null)
                                {
                                    childAction["runAfter"] = prevDefault == null
                                        ? new JObject()
                                        : new JObject { [prevDefault] = new JArray("SUCCEEDED") };
                                    defaultActions[childName] = childAction;
                                    prevDefault = childName;
                                }
                            }
                            
                            defaultCase["actions"] = defaultActions;
                            hasDefault = true;
                        }
                        else
                        {
                            // Extract case value from scope name (e.g., "Case_100" -> "100")
                            var caseName = caseScope.Name ?? "Case";
                            var caseValue = caseName;
                            
                            // Try to extract value after "Case_" prefix
                            if (caseName.IndexOf("Case_", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                var startIndex = caseName.IndexOf("Case_", StringComparison.OrdinalIgnoreCase) + 5;
                                if (startIndex < caseName.Length)
                                {
                                    caseValue = caseName.Substring(startIndex);
                                }
                            }
                            
                            // Build case object
                            var caseObj = new JObject();
                            caseObj["case"] = caseValue;
                            
                            var caseActions = new JObject();
                            string prevAction = null;
                            
                            foreach (var child in caseScope.Children.OrderBy(c => c.Sequence))
                            {
                                string childName = AllocateName(NormalizeName(child.Name), usedNames);
                                var childAction = BuildAction(child, usedNames, registry, variableNames);
                                
                                if (childAction != null)
                                {
                                    childAction["runAfter"] = prevAction == null
                                        ? new JObject()
                                        : new JObject { [prevAction] = new JArray("SUCCEEDED") };
                                    caseActions[childName] = childAction;
                                    prevAction = childName;
                                }
                            }
                            
                            caseObj["actions"] = caseActions;
                            cases[caseName] = caseObj;
                        }
                    }
                    
                    switchAction["cases"] = cases;
                    if (hasDefault)
                    {
                        switchAction["default"] = defaultCase;
                    }
                    
                    return switchAction;
                case "XmlParse":
                    var xmlParse = new JObject();
                    xmlParse["type"] = "XmlParse";
                    var xmlParseInputs = new JObject();
                    xmlParseInputs["content"] = "@triggerBody()";

                    var xmlParseSchema = new JObject();
                    xmlParseSchema["source"] = "LogicApp";
                    xmlParseSchema["name"] = "{{SCHEMA_NAME}}";
                    xmlParseInputs["schema"] = xmlParseSchema;

                    var xmlReaderSettings = new JObject();
                    xmlReaderSettings["dtdProcessing"] = "Prohibit";
                    xmlReaderSettings["xmlNormalization"] = true;
                    xmlReaderSettings["ignoreWhitespace"] = true;
                    xmlReaderSettings["ignoreProcessingInstructions"] = true;
                    xmlParseInputs["xmlReaderSettings"] = xmlReaderSettings;

                    var jsonWriterSettings = new JObject();
                    jsonWriterSettings["ignoreAttributes"] = false;
                    jsonWriterSettings["useFullyQualifiedNames"] = false;
                    xmlParseInputs["jsonWriterSettings"] = jsonWriterSettings;

                    xmlParse["inputs"] = xmlParseInputs;

                    if (!string.IsNullOrEmpty(act.Details))
                    {
                        xmlParse["metadata"] = new JObject
                        {
                            ["comment"] = act.Details
                        };
                    }
                    return xmlParse;

                case "XmlCompose":
                    var xmlCompose = new JObject();
                    xmlCompose["type"] = "XmlCompose";
                    var xmlComposeInputs = new JObject();

                    var xmlComposeSchema = new JObject();
                    xmlComposeSchema["source"] = "LogicApp";
                    xmlComposeSchema["name"] = "{{SCHEMA_NAME}}";
                    xmlComposeInputs["schema"] = xmlComposeSchema;

                    xmlComposeInputs["content"] = new JObject();

                    xmlCompose["inputs"] = xmlComposeInputs;

                    if (!string.IsNullOrEmpty(act.Details))
                    {
                        xmlCompose["metadata"] = new JObject
                        {
                            ["comment"] = act.Details
                        };
                    }
                    return xmlCompose;

                case "XmlValidation":
                    var xmlValidation = new JObject();
                    xmlValidation["type"] = "XmlValidation";
                    var xmlValidationInputs = new JObject();
                    xmlValidationInputs["content"] = "@triggerBody()";

                    var xmlValidationSchema = new JObject();
                    xmlValidationSchema["source"] = "LogicApp";
                    xmlValidationSchema["name"] = "{{SCHEMA_NAME}}";
                    xmlValidationInputs["schema"] = xmlValidationSchema;

                    xmlValidation["inputs"] = xmlValidationInputs;

                    if (!string.IsNullOrEmpty(act.Details))
                    {
                        xmlValidation["metadata"] = new JObject
                        {
                            ["comment"] = act.Details
                        };
                    }
                    return xmlValidation;

                case "FlatFileDecoding":
                    var ffDecode = new JObject();
                    ffDecode["type"] = "FlatFileDecoding";
                    var ffDecodeInputs = new JObject();
                    ffDecodeInputs["content"] = "@triggerBody()";

                    var ffDecodeSchema = new JObject();
                    ffDecodeSchema["source"] = "LogicApp";
                    ffDecodeSchema["name"] = "{{FLAT_FILE_SCHEMA_NAME}}";
                    ffDecodeInputs["schema"] = ffDecodeSchema;

                    ffDecode["inputs"] = ffDecodeInputs;

                    if (!string.IsNullOrEmpty(act.Details))
                    {
                        ffDecode["metadata"] = new JObject
                        {
                            ["comment"] = act.Details
                        };
                    }
                    return ffDecode;

                case "FlatFileEncoding":
                    var ffEncode = new JObject();
                    ffEncode["type"] = "FlatFileEncoding";
                    var ffEncodeInputs = new JObject();
                    ffEncodeInputs["content"] = "@triggerBody()";

                    var ffEncodeSchema = new JObject();
                    ffEncodeSchema["source"] = "LogicApp";
                    ffEncodeSchema["name"] = "{{FLAT_FILE_SCHEMA_NAME}}";
                    ffEncodeInputs["schema"] = ffEncodeSchema;

                    ffEncode["inputs"] = ffEncodeInputs;

                    if (!string.IsNullOrEmpty(act.Details))
                    {
                        ffEncode["metadata"] = new JObject
                        {
                            ["comment"] = act.Details
                        };
                    }
                    return ffEncode;

                case "SwiftMTDecode":
                    var swiftDecode = new JObject();
                    swiftDecode["type"] = "SwiftMTDecode";
                    var swiftDecodeInputs = new JObject();
                    swiftDecodeInputs["messageToDecode"] = "@triggerBody()";
                    swiftDecodeInputs["messageValidation"] = "Enable";

                    swiftDecode["inputs"] = swiftDecodeInputs;

                    if (!string.IsNullOrEmpty(act.Details))
                    {
                        swiftDecode["metadata"] = new JObject
                        {
                            ["comment"] = act.Details
                        };
                    }
                    return swiftDecode;

                case "SwiftMTEncode":
                    var swiftEncode = new JObject();
                    swiftEncode["type"] = "SwiftMTEncode";
                    var swiftEncodeInputs = new JObject();
                    swiftEncodeInputs["messageToEncode"] = "@triggerBody()";
                    swiftEncodeInputs["messageValidation"] = "Enable";

                    swiftEncode["inputs"] = swiftEncodeInputs;

                    if (!string.IsNullOrEmpty(act.Details))
                    {
                        swiftEncode["metadata"] = new JObject
                        {
                            ["comment"] = act.Details
                        };
                    }
                    return swiftEncode;
                default:
                    var unmapped = new JObject();
                    unmapped["type"] = "Compose";
                    unmapped["inputs"] = "// Unmapped: " + act.Type + " " + (act.Details ?? "");
                    return unmapped;
            }
        }

        /// <summary>
        /// Returns the appropriate body expression for an action based on message flow.
        /// When InputMessageSourceAction is set, references that action's output via @body('ActionName').
        /// When null, falls back to @triggerBody() (the activation message).
        /// </summary>
        /// <param name="act">The action to resolve the body expression for.</param>
        /// <returns>A workflow expression string referencing the correct message source.</returns>
        private static string ResolveMessageBodyExpression(LogicAppAction act)
        {
            if (!string.IsNullOrEmpty(act.InputMessageSourceAction))
            {
                return string.Format("@body('{0}')", act.InputMessageSourceAction);
            }
            return "@triggerBody()";
        }

        /// <summary>
        /// Represents the mapping configuration for a send connector action.
        /// Contains connection details, operation ID, and parameters for ServiceProvider actions.
        /// </summary>
        private sealed class SendProviderMapping
        {
            /// <summary>
            /// Gets or sets the connection name for the connector.
            /// </summary>
            public string ConnectionName { get; set; }

            /// <summary>
            /// Gets or sets the operation ID identifying the specific connector action.
            /// </summary>
            public string OperationId { get; set; }

            /// <summary>
            /// Gets or sets the service provider ID (connector type identifier).
            /// </summary>
            public string ServiceProviderId { get; set; }

            /// <summary>
            /// Gets or sets the parameters object containing connector-specific configuration.
            /// </summary>
            public JObject Parameters { get; set; }
        }

        /// <summary>
        /// Builds connector configuration for send actions (send ports).
        /// Uses connector registry if available, otherwise falls back to legacy hardcoded mappings.
        /// </summary>
        /// <param name="act">The send action model with connector kind and configuration.</param>
        /// <param name="registry">Optional connector schema registry.</param>
        /// <returns>A SendProviderMapping containing connection name, operation ID, and parameters.</returns>
        private static SendProviderMapping BuildSendProviderMapping(LogicAppAction act, ConnectorSchemaRegistry registry)
        {
            string kind = act.ConnectorKind ?? "generic";

            if (registry != null && registry.HasConnector(kind))
            {
                return BuildSendProviderMappingFromRegistry(act, registry);
            }

            return BuildSendProviderMappingLegacy(act);
        }

        /// <summary>
        /// Builds send connector mapping using connector schema registry for accurate configuration.
        /// </summary>
        /// <param name="act">The send action model.</param>
        /// <param name="registry">The connector schema registry.</param>
        /// <returns>A SendProviderMapping with registry-based configuration.</returns>
        private static SendProviderMapping BuildSendProviderMappingFromRegistry(LogicAppAction act, ConnectorSchemaRegistry registry)
        {
            var connector = registry.GetConnector(act.ConnectorKind);
            if (connector == null)
            {
                return BuildSendProviderMappingLegacy(act);
            }

            string operationId = InferActionOperationIdFromRegistry(act, connector);
            var operation = connector.Actions.ContainsKey(operationId)
                ? connector.Actions[operationId]
                : connector.Actions.Values.FirstOrDefault();

            if (operation == null)
            {
                return BuildSendProviderMappingLegacy(act);
            }

            var parameters = BuildActionParameters(act, operation);

            var mapping = new SendProviderMapping();
            mapping.ConnectionName = act.ConnectorKind.ToLowerInvariant();
            mapping.OperationId = operation.OperationId;
            mapping.ServiceProviderId = connector.ServiceProviderId.Replace("/serviceProviders/", "");
            mapping.Parameters = parameters;

            return mapping;
        }

        /// <summary>
        /// Infers the appropriate action operation ID from the connector schema based on action properties.
        /// Maps patterns like FileSystem -> "createFile", ServiceBus Topic -> "sendMessage", SQL -> "executeQuery".
        /// </summary>
        /// <param name="act">The action model with connector configuration.</param>
        /// <param name="connector">The connector schema containing available action operations.</param>
        /// <returns>The operation ID to use for the action.</returns>
        private static string InferActionOperationIdFromRegistry(LogicAppAction act, ConnectorSchema connector)
        {
            string kind = act.ConnectorKind.ToLowerInvariant();

            if (kind == "filesystem")
            {
                if (connector.Actions.ContainsKey("createFile"))
                    return "createFile";
                if (connector.Actions.ContainsKey("updateFile"))
                    return "updateFile";
            }

            if (kind == "ftp" || kind == "sftp")
            {
                if (connector.Actions.ContainsKey("createFile"))
                    return "createFile";
                if (connector.Actions.ContainsKey("uploadFile"))
                    return "uploadFile";
            }

            if (kind == "sql")
            {
                if (connector.Actions.ContainsKey("executeQuery"))
                    return "executeQuery";
                if (connector.Actions.ContainsKey("insertRow"))
                    return "insertRow";
            }

            if (kind == "servicebus")
            {
                if (act.IsTopic && connector.Actions.ContainsKey("sendMessage"))
                    return "sendMessage";
                if (connector.Actions.ContainsKey("sendMessage"))
                    return "sendMessage";
            }

            if (kind == "eventhub")
            {
                if (connector.Actions.ContainsKey("sendEvent"))
                    return "sendEvent";
            }

            if (kind == "smtp" || kind.Contains("email"))
            {
                if (connector.Actions.ContainsKey("sendEmail"))
                    return "sendEmail";
            }

            if (kind == "mllp")
            {
                if (connector.Actions.ContainsKey("sendMessage"))
                    return "sendMessage";
            }

            if (kind == "ibmmq")
            {
                if (connector.Actions.ContainsKey("sendMessage"))
                    return "sendMessage";
            }

            if (kind == "azureblob")
            {
                if (connector.Actions.ContainsKey("createBlob"))
                    return "createBlob";
            }

            if (kind == "cosmosdb")
            {
                if (connector.Actions.ContainsKey("createDocument"))
                    return "createDocument";
            }

            return connector.Actions.Keys.FirstOrDefault() ?? "sendMessage";
        }

        /// <summary>
        /// Builds action parameters based on the operation schema and action configuration.
        /// Maps action properties (TargetAddress, QueueName, Content, etc.) to operation parameters.
        /// </summary>
        /// <param name="act">The action model with configuration details.</param>
        /// <param name="operation">The operation schema defining required parameters.</param>
        /// <returns>A JObject containing parameter name-value pairs.</returns>
        private static JObject BuildActionParameters(LogicAppAction act, OperationSchema operation)
        {
            var parameters = new JObject();

            foreach (var paramName in operation.Parameters)
            {
                var lowerParam = paramName.ToLowerInvariant();
            /// This code needs to be consolidated as well.
                if (lowerParam == "filepath" || lowerParam == "path")
                {
                    parameters[paramName] = act.TargetAddress ?? "/output/file.txt";
                }
                else if (lowerParam == "folderpath")
                {
                    parameters[paramName] = ExtractFolderFromPath(act.TargetAddress) ?? "/output";
                }
                else if (lowerParam == "filename")
                {
                    parameters[paramName] = ExtractFileNameFromPath(act.TargetAddress) ?? "file.txt";
                }
                else if (lowerParam == "content" || lowerParam == "body" || lowerParam == "message")
                {
                    parameters[paramName] = "@triggerBody()";
                }
                else if (lowerParam == "queuename")
                {
                    parameters[paramName] = act.QueueOrTopicName ?? "queue";
                }
                else if (lowerParam == "topicname")
                {
                    parameters[paramName] = act.QueueOrTopicName ?? "topic";
                }
                else if (lowerParam == "subscriptionname")
                {
                    parameters[paramName] = act.SubscriptionName ?? "subscription";
                }
                else if (lowerParam == "eventhubname")
                {
                    parameters[paramName] = act.QueueOrTopicName ?? "eventhub";
                }
                else if (lowerParam == "entityname")
                {
                    parameters[paramName] = act.QueueOrTopicName ?? "entity";
                }
                else if (lowerParam == "table" || lowerParam == "tablename")
                {
                    parameters[paramName] = "Table";
                }
                else if (lowerParam == "query" || lowerParam == "statement")
                {
                    parameters[paramName] = "INSERT INTO Table VALUES (...)";
                }
                else if (lowerParam == "to")
                {
                    parameters[paramName] = "recipient@example.com";
                }
                else if (lowerParam == "subject")
                {
                    parameters[paramName] = "Email Subject";
                }
                else if (lowerParam == "from")
                {
                    parameters[paramName] = "sender@example.com";
                }
                else if (lowerParam == "endpoint")
                {
                    parameters[paramName] = act.TargetAddress ?? "endpoint";
                }
                else if (lowerParam == "containername")
                {
                    parameters[paramName] = "container";
                }
                else if (lowerParam == "blobname")
                {
                    parameters[paramName] = ExtractFileNameFromPath(act.TargetAddress) ?? "blob.dat";
                }
                else if (lowerParam == "databasename")
                {
                    parameters[paramName] = "Database";
                }
                else if (lowerParam == "document")
                {
                    parameters[paramName] = "@triggerBody()";
                }
                else if (lowerParam == "partitionkey")
                {
                    parameters[paramName] = "partitionKey";
                }
                else if (lowerParam == "sessionid")
                {
                    parameters[paramName] = "";
                }
                else if (lowerParam == "contenttype")
                {
                    parameters[paramName] = "application/json";
                }
                else if (lowerParam == "overwrite")
                {
                    parameters[paramName] = false;
                }
                else if (lowerParam == "source" || lowerParam == "destination")
                {
                    parameters[paramName] = act.TargetAddress ?? "/path";
                }
                else
                {
                    parameters[paramName] = "";
                }
            }

            return parameters;
        }

        /// <summary>
        /// Builds send connector mapping using legacy hardcoded mappings when registry is not available.
        /// Provides fallback connector configuration for common connector types (File, FTP, SQL, ServiceBus, etc.).
        /// </summary>
        /// <param name="act">The send action model.</param>
        /// <returns>A SendProviderMapping with default configuration for the connector kind.</returns>
        private static SendProviderMapping BuildSendProviderMappingLegacy(LogicAppAction act)
        {
            string kind = act.ConnectorKind ?? "generic";
            var p = new JObject();
            p["content"] = "@triggerBody()";

            switch (kind)
            {
                case "FileSystem":
                    p["filePath"] = act.TargetAddress ?? "\\\\path\\out.txt";
                    return Map(kind.ToLowerInvariant(), "createFile", p);

                case "Ftp":
                case "Sftp":
                    p["filePath"] = act.TargetAddress ?? "/upload/out.txt";
                    return Map(kind.ToLowerInvariant(), "createFile", p);

                case "Sql":
                    p["statement"] = "INSERT ...";
                    return Map("sql", "executeNonQuery", p);

                case "ServiceBus":
                    if (act.IsTopic)
                    {
                        p["topicName"] = act.QueueOrTopicName ?? "topic";
                        if (act.HasSubscription) p["subscriptionName"] = act.SubscriptionName ?? "subscription";
                        p["message"] = "@triggerBody()";
                        return Map("mq", "sendTopicMessage", p);
                    }
                    p["queueName"] = act.QueueOrTopicName ?? "queue";
                    p["message"] = "@triggerBody()";
                    return Map("mq", "sendMessage", p);

                case "EventHub":
                    p["eventHubName"] = act.QueueOrTopicName ?? "eventhub";
                    p["eventBody"] = "@triggerBody()";
                    return Map("eventhub", "sendEvent", p);

                case "AS2":
                    p["partner"] = "Partner";
                    p["payload"] = "@triggerBody()";
                    return Map("as2", "sendAs2Message", p);

                case "Mllp":
                    p["endpoint"] = act.TargetAddress ?? "mllp://host:port";
                    p["message"] = "@triggerBody()";
                    return Map("mllp", "sendMllpMessage", p);

                case "IbmMq":
                    p["queueName"] = act.QueueOrTopicName ?? "queue";
                    p["message"] = "@triggerBody()";
                    return Map("ibmmq", "sendMessage", p);

                case "OutlookEmail":
                case "GmailEmail":
                case "ExchangeOnlineEmail":
                case "Smtp":
                    p["to"] = "recipient@example.com";
                    p["subject"] = "Subject";
                    p["body"] = "@triggerBody()";
                    return Map(kind.ToLowerInvariant(), "sendEmail", p);

                case "Http":
                case "Request":
                    p.Remove("content");
                    p["method"] = act.HttpMethod ?? "POST";
                    p["uri"] = act.TargetAddress ?? "http://localhost/service";
                    p["body"] = "@triggerBody()";
                    p["headers"] = new JObject
                    {
                        ["Content-Type"] = DetermineContentType(act)
                    };
                    if (!string.IsNullOrEmpty(act.SoapAction))
                    {
                        p["headers"]["SOAPAction"] = act.SoapAction;
                    }
                    return Map("http", "invokeHttp", p);

                case "Db2":
                    p.Remove("content");
                    p["query"] = "SELECT * FROM TABLE";
                    return Map("db2", "executeQuery", p);

                case "Cics":
                    p.Remove("content");
                    p["programName"] = "PROGRAM";
                    p["commarea"] = "@triggerBody()";
                    return Map("cics", "invokeProgram", p);

                case "Ims":
                    p.Remove("content");
                    p["transactionCode"] = "TRAN";
                    p["inputData"] = "@triggerBody()";
                    return Map("ims", "invokeTransaction", p);

                case "SapEcc":
                    p.Remove("content");
                    p["bapiName"] = "BAPI_NAME";
                    p["parameters"] = "@triggerBody()";
                    return Map("sapecc", "callBapi", p);

                case "Vsam":
                    p.Remove("content");
                    p["fileName"] = "DATASET";
                    p["key"] = "KEY";
                    p["record"] = "@triggerBody()";
                    return Map("vsam", "writeRecord", p);

                case "Informix":
                    p.Remove("content");
                    p["query"] = "SELECT * FROM TABLE";
                    return Map("informix", "executeQuery", p);

                case "HostFile":
                    p.Remove("content");
                    p["fileName"] = "FILE";
                    p["datasetName"] = "DATASET";
                    p["content"] = "@triggerBody()";
                    return Map("hostfile", "writeFile", p);

                default:
                    p["target"] = act.TargetAddress ?? "endpoint";
                    return Map("generic", "sendMessage", p);
            }
        }

        /// <summary>
        /// Determines the appropriate HTTP Content-Type header based on target address and SOAP action.
        /// Detects SOAP (text/xml), XML (application/xml), or defaults to JSON (application/json).
        /// </summary>
        /// <param name="act">The action model with target address and SOAP action information.</param>
        /// <returns>The Content-Type header value to use.</returns>
        private static string DetermineContentType(LogicAppAction act)
        {
            var addr = (act.TargetAddress ?? "").ToLowerInvariant();

            if (addr.EndsWith(".asmx") || !string.IsNullOrEmpty(act.SoapAction))
                return "text/xml; charset=utf-8";

            if (addr.Contains("/xml") || addr.Contains("contenttype=xml"))
                return "application/xml";

            return "application/json";
        }

        /// <summary>
        /// Allocates a unique action name by appending numeric suffixes if name already exists.
        /// Ensures each action in the workflow has a unique identifier.
        /// </summary>
        /// <param name="raw">The desired base name for the action.</param>
        /// <param name="used">The set of already-used names to check against.</param>
        /// <returns>A unique name, either the original or with a numeric suffix (_1, _2, etc.).</returns>
        private static string AllocateName(string raw, HashSet<string> used)
        {
            var baseName = raw;
            if (!used.Contains(baseName))
            {
                used.Add(baseName);
                return baseName;
            }
            int i = 1;
            while (true)
            {
                var candidate = baseName + "_" + i;
                if (!used.Contains(candidate))
                {
                    used.Add(candidate);
                    return candidate;
                }
                i++;
            }
        }

        /// <summary>
        /// Normalizes a name for use in Logic Apps by removing non-alphanumeric characters.
        /// Preserves underscores as they are valid in Logic Apps action names.
        /// Truncates long names (>80 chars) and appends a hash to ensure uniqueness.
        /// </summary>
        /// <param name="name">The name to normalize.</param>
        /// <returns>A normalized name safe for use in Logic Apps workflow definitions.</returns>
        private static string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Item";
            var chars = name.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray();
            var cleaned = chars.Length == 0 ? "Item" : new string(chars);
            if (cleaned.Length > MaxNameLen)
            {
                using (var sha = SHA256.Create())
                {
                    var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(cleaned));
                    var hash = BitConverter.ToString(hashBytes).Replace("-", "").Substring(0, 8);
                    cleaned = cleaned.Substring(0, 60) + "_" + hash;
                }
            }
            return cleaned;
        }

        /// <summary>
        /// Extracts the short map name from a fully qualified BizTalk map class name.
        /// Converts "MyNamespace.MyProject.MapName" to "MapName".
        /// </summary>
        /// <param name="mapClassName">The fully qualified map class name.</param>
        /// <returns>The short map name (last segment of the qualified name).</returns>
        private static string ExtractMapShortName(string mapClassName)
        {
            if (string.IsNullOrWhiteSpace(mapClassName)) return "MapName";

            var parts = mapClassName.Split('.');
            return parts.Length > 0 ? parts[parts.Length - 1] : mapClassName;
        }

        /// <summary>
        /// Creates a SendProviderMapping with the specified connection, operation, and parameters.
        /// Helper method to simplify mapping creation in legacy mode.
        /// </summary>
        /// <param name="connection">The connection name.</param>
        /// <param name="operationId">The operation ID.</param>
        /// <param name="parameters">The parameters object.</param>
        /// <returns>A configured SendProviderMapping instance.</returns>
        private static SendProviderMapping Map(string connection, string operationId, JObject parameters)
        {
            var mapping = new SendProviderMapping();
            mapping.ConnectionName = connection;
            mapping.OperationId = operationId;
            mapping.ServiceProviderId = connection;
            mapping.Parameters = parameters;
            return mapping;
        }

        /// <summary>
        /// Extracts the folder path from a file path by removing the file name.
        /// Supports both forward slash (/) and backslash (\) path separators.
        /// </summary>
        /// <param name="path">The full file path.</param>
        /// <returns>The folder path, or "/" if extraction fails.</returns>
        private static string ExtractFolderFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var lastSlash = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
            return lastSlash > 0 ? path.Substring(0, lastSlash) : "/";
        }

        /// <summary>
        /// Extracts the file name from a full file path.
        /// Supports both forward slash (/) and backslash (\) path separators.
        /// </summary>
        /// <param name="path">The full file path.</param>
        /// <returns>The file name, or the original path if no separator is found.</returns>
        private static string ExtractFileNameFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var lastSlash = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
            return lastSlash >= 0 ? path.Substring(lastSlash + 1) : path;
        }

        /// <summary>
        /// Infers the trigger operation ID from connector kind using legacy hardcoded mappings.
        /// Maps connector types to their default trigger operations (e.g., FileSystem -> "whenFilesAreAdded").
        /// Updated to align with InferTriggerOperationIdFromRegistry for consistency.
        /// </summary>
        /// <param name="kind">The connector kind.</param>
        /// <returns>The inferred trigger operation ID.</returns>
        private static string InferTriggerOperationId(string kind)
        {
            string k = (kind ?? "").ToLowerInvariant();
            switch (k)
            {
                case "filesystem": return "whenFilesAreAdded";
                case "ftp": return "whenFileIsAdded";
                case "sftp": return "whenFileIsAdded";
                case "sql": return "whenItemsAreModified";
                case "servicebus": return "receiveQueueMessage";
                case "eventhub": return "receiveEvent";
                case "as2": return "receiveAs2Message";
                case "mllp": return "receiveMllpMessage";
                case "ibmmq": return "receiveMessage";
                case "outlookemail":
                case "gmailemail":
                case "exchangeonlineemail": return "receiveEmail";
                default: return "manual";
            }
        }

        /// <summary>
        /// Parses a delay count (numeric value) from a string by extracting all digit characters.
        /// Used for Wait action duration configuration.
        /// </summary>
        /// <param name="details">The string containing delay information.</param>
        /// <returns>The parsed numeric delay count, or null if parsing fails.</returns>
        private static int? ParseDelayCount(string details)
        {
            if (string.IsNullOrWhiteSpace(details)) return null;
            var digits = new string(details.Where(char.IsDigit).ToArray());
            int value;
            return int.TryParse(digits, out value) ? value : (int?)null;
        }

        /// <summary>
        /// Result of parsing a BizTalk delay expression into Logic Apps Wait interval.
        /// </summary>
        private sealed class DelayInterval
        {
            public int Count { get; set; }
            public string Unit { get; set; }
        }

        /// <summary>
        /// Parses a BizTalk delay expression into a Logic Apps Wait interval (count + unit).
        /// Handles common BizTalk patterns:
        ///   - new System.TimeSpan(ticks)               → converts ticks to seconds
        ///   - new System.TimeSpan(hours, minutes, seconds) → picks the largest non-zero unit
        ///   - new System.TimeSpan(days, hours, minutes, seconds) → picks the largest non-zero unit
        ///   - System.TimeSpan.FromMinutes(n)            → n minutes
        ///   - System.TimeSpan.FromSeconds(n)            → n seconds
        ///   - System.TimeSpan.FromHours(n)              → n hours
        ///   - PT5M (ISO 8601 duration)                  → 5 minutes
        /// Falls back to 1 Minute if parsing fails.
        /// </summary>
        /// <param name="expression">The BizTalk delay expression (e.g., "new System.TimeSpan(0, 5, 0)").</param>
        /// <returns>A DelayInterval with count and unit suitable for Logic Apps Wait action.</returns>
        private static DelayInterval ParseDelayExpression(string expression)
        {
            var fallback = new DelayInterval { Count = 1, Unit = "Minute" };

            if (string.IsNullOrWhiteSpace(expression))
                return fallback;

            var expr = expression.Trim();

            // Pattern: System.TimeSpan.FromMinutes(n) / FromSeconds(n) / FromHours(n) / FromDays(n)
            var fromMatch = Regex.Match(expr, @"TimeSpan\.From(\w+)\s*\(\s*(\d+(?:\.\d+)?)\s*\)", RegexOptions.IgnoreCase);
            if (fromMatch.Success)
            {
                var unit = fromMatch.Groups[1].Value;
                double value;
                if (double.TryParse(fromMatch.Groups[2].Value, out value))
                {
                    int count = (int)Math.Max(1, value);
                    if (unit.Equals("Seconds", StringComparison.OrdinalIgnoreCase))
                        return new DelayInterval { Count = count, Unit = "Second" };
                    if (unit.Equals("Minutes", StringComparison.OrdinalIgnoreCase))
                        return new DelayInterval { Count = count, Unit = "Minute" };
                    if (unit.Equals("Hours", StringComparison.OrdinalIgnoreCase))
                        return new DelayInterval { Count = count, Unit = "Hour" };
                    if (unit.Equals("Days", StringComparison.OrdinalIgnoreCase))
                        return new DelayInterval { Count = count, Unit = "Day" };
                }
            }

            // Pattern: new System.TimeSpan(args) or new TimeSpan(args)
            var ctorMatch = Regex.Match(expr, @"new\s+(?:System\.)?TimeSpan\s*\(([^)]*)\)", RegexOptions.IgnoreCase);
            if (ctorMatch.Success)
            {
                var argsStr = ctorMatch.Groups[1].Value.Trim();
                var argParts = argsStr.Split(',');
                var args = new List<long>();
                foreach (var part in argParts)
                {
                    long val;
                    if (long.TryParse(part.Trim(), out val))
                        args.Add(val);
                }

                if (args.Count == 1)
                {
                    // TimeSpan(ticks) — 10,000,000 ticks per second
                    var ticks = args[0];
                    if (ticks <= 0)
                        return new DelayInterval { Count = 0, Unit = "Second" };
                    var totalSeconds = ticks / 10000000;
                    if (totalSeconds < 1) totalSeconds = 1;
                    return new DelayInterval { Count = (int)totalSeconds, Unit = "Second" };
                }
                if (args.Count == 3)
                {
                    // TimeSpan(hours, minutes, seconds)
                    long hours = args[0], minutes = args[1], seconds = args[2];
                    if (hours == 0 && minutes == 0 && seconds == 0)
                        return new DelayInterval { Count = 0, Unit = "Second" };
                    if (hours > 0 && minutes == 0 && seconds == 0)
                        return new DelayInterval { Count = (int)hours, Unit = "Hour" };
                    if (hours == 0 && seconds == 0)
                        return new DelayInterval { Count = (int)minutes, Unit = "Minute" };
                    if (hours == 0 && minutes == 0)
                        return new DelayInterval { Count = (int)seconds, Unit = "Second" };
                    // Mixed: convert to total seconds
                    var totalSec = hours * 3600 + minutes * 60 + seconds;
                    if (totalSec % 3600 == 0)
                        return new DelayInterval { Count = (int)(totalSec / 3600), Unit = "Hour" };
                    if (totalSec % 60 == 0)
                        return new DelayInterval { Count = (int)(totalSec / 60), Unit = "Minute" };
                    return new DelayInterval { Count = (int)totalSec, Unit = "Second" };
                }
                if (args.Count == 4)
                {
                    // TimeSpan(days, hours, minutes, seconds)
                    long days = args[0], hours = args[1], minutes = args[2], seconds = args[3];
                    if (days == 0 && hours == 0 && minutes == 0 && seconds == 0)
                        return new DelayInterval { Count = 0, Unit = "Second" };
                    if (days > 0 && hours == 0 && minutes == 0 && seconds == 0)
                        return new DelayInterval { Count = (int)days, Unit = "Day" };
                    // Convert to total seconds and pick best unit
                    var totalSec = days * 86400 + hours * 3600 + minutes * 60 + seconds;
                    if (totalSec % 86400 == 0)
                        return new DelayInterval { Count = (int)(totalSec / 86400), Unit = "Day" };
                    if (totalSec % 3600 == 0)
                        return new DelayInterval { Count = (int)(totalSec / 3600), Unit = "Hour" };
                    if (totalSec % 60 == 0)
                        return new DelayInterval { Count = (int)(totalSec / 60), Unit = "Minute" };
                    return new DelayInterval { Count = (int)totalSec, Unit = "Second" };
                }
            }

            // Pattern: ISO 8601 duration (PT5M, PT30S, PT1H, P1D)
            var isoMatch = Regex.Match(expr, @"^P(?:(\d+)D)?T?(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?$", RegexOptions.IgnoreCase);
            if (isoMatch.Success)
            {
                int days = 0, hours = 0, mins = 0, secs = 0;
                if (isoMatch.Groups[1].Success) int.TryParse(isoMatch.Groups[1].Value, out days);
                if (isoMatch.Groups[2].Success) int.TryParse(isoMatch.Groups[2].Value, out hours);
                if (isoMatch.Groups[3].Success) int.TryParse(isoMatch.Groups[3].Value, out mins);
                if (isoMatch.Groups[4].Success) int.TryParse(isoMatch.Groups[4].Value, out secs);

                if (days > 0 && hours == 0 && mins == 0 && secs == 0)
                    return new DelayInterval { Count = days, Unit = "Day" };
                if (hours > 0 && mins == 0 && secs == 0)
                    return new DelayInterval { Count = hours, Unit = "Hour" };
                if (mins > 0 && secs == 0)
                    return new DelayInterval { Count = mins, Unit = "Minute" };
                if (secs > 0)
                    return new DelayInterval { Count = secs, Unit = "Second" };
            }

            // Fallback: try to extract any number
            var numericCount = ParseDelayCount(expr);
            if (numericCount.HasValue && numericCount.Value > 0)
                return new DelayInterval { Count = numericCount.Value, Unit = "Minute" };

            return fallback;
        }

        /// <summary>
        /// Infers a queue name from an address by extracting the last path segment.
        /// Used for ServiceBus queue and topic name extraction.
        /// </summary>
        /// <param name="address">The address containing the queue/topic name.</param>
        /// <returns>The inferred queue name, or null if extraction fails.</returns>
        private static string InferQueueName(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return null;
            var parts = address.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0 ? null : parts[parts.Length - 1];
        }

        /// <summary>
        /// Infers an Event Hub name from an address by extracting the last path segment.
        /// Delegates to InferQueueName as the logic is identical.
        /// </summary>
        /// <param name="address">The address containing the Event Hub name.</param>
        /// <returns>The inferred Event Hub name, or null if extraction fails.</returns>
        private static string InferEventHubName(string address)
        {
            return InferQueueName(address);
        }

        /// <summary>
        /// Infers a trading partner name from an AS2 address by extracting the first path segment.
        /// </summary>
        /// <param name="address">The AS2 address containing partner information.</param>
        /// <returns>The inferred partner name, or "Partner" as default.</returns>
        private static string InferPartner(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return null;
            var parts = address.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0 ? "Partner" : parts[0];
        }

        /// <summary>
        /// Infers a SQL table name from an address by looking for "table=" parameter.
        /// Parses query string-style addresses to extract table name configuration.
        /// </summary>
        /// <param name="address">The SQL address containing table information.</param>
        /// <returns>The inferred table name, or "Table" as default.</returns>
        private static string InferTableName(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return null;
            var lower = address.ToLowerInvariant();
            if (lower.Contains("table="))
            {
                int idx = lower.IndexOf("table=", StringComparison.Ordinal);
                var rest = address.Substring(idx + 6);
                int end = rest.IndexOfAny(new[] { '&', ';', ' ' });
                return end > 0 ? rest.Substring(0, end) : rest;
            }
            return "Table";
        }
        
        /// <summary>
        /// Maps BizTalk CLR type names to Logic Apps variable types.
        /// Converts System.Int32 → "integer", System.String → "string", etc.
        /// </summary>
        /// <param name="biztalkType">The BizTalk CLR type (e.g., "System.Int32", "System.Collections.Generic.List`1").</param>
        /// <returns>The corresponding Logic Apps type ("string", "integer", "boolean", "array", "object", "float").</returns>
        private static string MapBizTalkTypeToLogicApps(string biztalkType)
        {
            if (string.IsNullOrWhiteSpace(biztalkType))
                return "object";
            
            var lowerType = biztalkType.ToLowerInvariant();
            
            // String types
            if (lowerType.Contains("string"))
                return "string";
            
            // Integer types
            if (lowerType.Contains("int32") || lowerType.Contains("int64") || 
                lowerType.Contains("int16") || lowerType.Contains("byte") || 
                lowerType.Contains("sbyte") || lowerType.Contains("uint") ||
                lowerType.Contains("short") || lowerType.Contains("long"))
                return "integer";
            
            // Boolean types
            if (lowerType.Contains("boolean") || lowerType.Contains("bool"))
                return "boolean";
            
            // Array/Collection types
            if (lowerType.Contains("list") || lowerType.Contains("array") || 
                lowerType.Contains("collection") || lowerType.Contains("ienumerable") ||
                lowerType.Contains("pipelineinputmessages") || lowerType.Contains("pipelineoutputmessages") ||
                lowerType.Contains("[]"))
                return "array";
            
            // Floating point types
            if (lowerType.Contains("single") || lowerType.Contains("double") || 
                lowerType.Contains("decimal") || lowerType.Contains("float"))
                return "float";
            
            // Default to object for complex types (XmlDocument, custom classes, etc.)
            return "object";
        }
        
        /// <summary>
        /// Gets the appropriate default value for a Logic Apps variable type.
        /// Returns null for object types, empty values for primitives.
        /// </summary>
        /// <param name="logicAppsType">The Logic Apps type ("string", "integer", "boolean", "array", "object", "float").</param>
        /// <returns>The default value appropriate for the type.</returns>
        private static object GetDefaultValueForType(string logicAppsType)
        {
            switch (logicAppsType.ToLowerInvariant())
            {
                case "string":
                    return "";
                case "integer":
                    return 0;
                case "boolean":
                    return false;
                case "array":
                    return new JArray();
                case "object":
                    return null;
                case "float":
                    return 0.0;
                default:
                    return null;
            }
        }
        
        /// <summary>
        /// Builds a human-readable description of WCF configuration metadata.
        /// Preserves BizTalk WCF binding context for operational reference.
        /// </summary>
        /// <param name="t">The trigger with WCF metadata.</param>
        /// <returns>A formatted description string with WCF configuration details.</returns>
        private static string BuildWcfMetadataDescription(LogicAppTrigger t)
        {
            var parts = new List<string>();
            
            if (!string.IsNullOrEmpty(t.TransportType))
            {
                parts.Add(string.Format("BizTalk Adapter: {0}", t.TransportType));
            }
            
            if (!string.IsNullOrEmpty(t.Address))
            {
                parts.Add(string.Format("Original Address: {0}", t.Address));
            }
            
            if (!string.IsNullOrEmpty(t.SecurityMode))
            {
                parts.Add(string.Format("Security Mode: {0}", t.SecurityMode));
            }
            
            if (!string.IsNullOrEmpty(t.MessageClientCredentialType))
            {
                parts.Add(string.Format("Message Authentication: {0}", t.MessageClientCredentialType));
            }
            
            if (!string.IsNullOrEmpty(t.TransportClientCredentialType))
            {
                parts.Add(string.Format("Transport Authentication: {0}", t.TransportClientCredentialType));
            }
            
            if (!string.IsNullOrEmpty(t.MessageEncoding))
            {
                parts.Add(string.Format("Encoding: {0}", t.MessageEncoding));
            }
            
            if (t.MaxReceivedMessageSize.HasValue)
            {
                parts.Add(string.Format("Max Message Size: {0:N0} bytes", t.MaxReceivedMessageSize.Value));
            }
            
            if (t.MaxConcurrentCalls.HasValue)
            {
                parts.Add(string.Format("Max Concurrent Calls: {0}", t.MaxConcurrentCalls.Value));
            }
            
            if (!string.IsNullOrEmpty(t.OpenTimeout) || !string.IsNullOrEmpty(t.CloseTimeout) || !string.IsNullOrEmpty(t.SendTimeout))
            {
                var timeouts = new List<string>();
                if (!string.IsNullOrEmpty(t.OpenTimeout))
                    timeouts.Add(string.Format("Open={0}", t.OpenTimeout));
                if (!string.IsNullOrEmpty(t.CloseTimeout))
                    timeouts.Add(string.Format("Close={0}", t.CloseTimeout));
                if (!string.IsNullOrEmpty(t.SendTimeout))
                    timeouts.Add(string.Format("Send={0}", t.SendTimeout));
                parts.Add(string.Format("Timeouts: {0}", string.Join(", ", timeouts)));
            }
            
            if (!string.IsNullOrEmpty(t.AlgorithmSuite))
            {
                parts.Add(string.Format("Algorithm Suite: {0}", t.AlgorithmSuite));
            }
            
            if (t.EstablishSecurityContext.HasValue)
            {
                parts.Add(string.Format("Establish Security Context: {0}", t.EstablishSecurityContext.Value ? "Yes" : "No"));
            }
            
            if (t.NegotiateServiceCredential.HasValue)
            {
                parts.Add(string.Format("Negotiate Service Credential: {0}", t.NegotiateServiceCredential.Value ? "Yes" : "No"));
            }
            
            if (parts.Count == 0)
                return null;
            
            return "Migrated from BizTalk WCF endpoint - " + string.Join("; ", parts);
        }
        
        /// <summary>
        /// Builds a metadata object containing WCF configuration for programmatic access.
        /// Stores original BizTalk configuration as JSON metadata for runtime reference.
        /// </summary>
        /// <param name="t">The trigger with WCF metadata.</param>
        /// <returns>A JObject with WCF configuration properties.</returns>
        private static JObject BuildWcfMetadataObject(LogicAppTrigger t)
        {
            var metadata = new JObject();
            
            if (!string.IsNullOrEmpty(t.TransportType))
                metadata["biztalkTransportType"] = t.TransportType;
            
            if (!string.IsNullOrEmpty(t.Address))
                metadata["biztalkOriginalAddress"] = t.Address;
            
            if (!string.IsNullOrEmpty(t.SecurityMode))
                metadata["wcfSecurityMode"] = t.SecurityMode;
            
            if (!string.IsNullOrEmpty(t.MessageClientCredentialType))
                metadata["wcfMessageCredentialType"] = t.MessageClientCredentialType;
            
            if (!string.IsNullOrEmpty(t.TransportClientCredentialType))
                metadata["wcfTransportCredentialType"] = t.TransportClientCredentialType;
            
            if (!string.IsNullOrEmpty(t.MessageEncoding))
                metadata["wcfMessageEncoding"] = t.MessageEncoding;
            
            if (!string.IsNullOrEmpty(t.AlgorithmSuite))
                metadata["wcfAlgorithmSuite"] = t.AlgorithmSuite;
            
            if (t.MaxReceivedMessageSize.HasValue)
                metadata["wcfMaxMessageSize"] = t.MaxReceivedMessageSize.Value;
            
            if (t.MaxConcurrentCalls.HasValue)
                metadata["wcfMaxConcurrentCalls"] = t.MaxConcurrentCalls.Value;
            
            if (!string.IsNullOrEmpty(t.OpenTimeout))
                metadata["wcfOpenTimeout"] = t.OpenTimeout;
            
            if (!string.IsNullOrEmpty(t.CloseTimeout))
                metadata["wcfCloseTimeout"] = t.CloseTimeout;
            
            if (!string.IsNullOrEmpty(t.SendTimeout))
                metadata["wcfSendTimeout"] = t.SendTimeout;
            
            if (t.EstablishSecurityContext.HasValue)
                metadata["wcfEstablishSecurityContext"] = t.EstablishSecurityContext.Value;
            
            if (t.NegotiateServiceCredential.HasValue)
                metadata["wcfNegotiateServiceCredential"] = t.NegotiateServiceCredential.Value;
            
            if (t.IncludeExceptionDetailInFaults.HasValue)
                metadata["wcfIncludeExceptionDetails"] = t.IncludeExceptionDetailInFaults.Value;
            
            if (t.UseSSO.HasValue)
                metadata["biztalkUseSSO"] = t.UseSSO.Value;
            
            if (t.SuspendMessageOnFailure.HasValue)
                metadata["biztalkSuspendOnFailure"] = t.SuspendMessageOnFailure.Value;
            
            return metadata;
        }

        #region Terminate-inside-Until hoisting

        /// <summary>
        /// Rewrites Terminate actions nested inside Until loops.
        /// Logic Apps forbids Terminate inside Until. This method:
        ///   1. Adds shouldTerminate (bool) and terminateMessage (string) InitializeVariable actions.
        ///   2. Replaces each nested Terminate with two SetVariable actions (flag + message).
        ///   3. Inserts a Condition action after the Until that terminates when the flag is set.
        /// Only modifies the workflow when nested Terminate actions are actually found.
        /// </summary>
        private static void HoistTerminateFromUntilLoops(JObject actionsObj, HashSet<string> usedNames)
        {
            // Collect Until actions that contain nested Terminate actions.
            var untilsWithTerminate = new List<KeyValuePair<string, JObject>>();

            foreach (var prop in actionsObj.Properties().ToList())
            {
                var action = prop.Value as JObject;
                if (action == null) continue;

                if (!string.Equals(action["type"]?.ToString(), "Until", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Search recursively for Terminate actions inside this Until
                var terminates = FindTerminateActions(action);
                if (terminates.Count > 0)
                {
                    untilsWithTerminate.Add(new KeyValuePair<string, JObject>(prop.Name, action));
                }
            }

            if (untilsWithTerminate.Count == 0)
                return;

            // --- Step 1: Add sentinel variables at the top of the workflow ---
            string flagVarName = AllocateName("shouldTerminate", usedNames);
            string msgVarName = AllocateName("terminateMessage", usedNames);

            var flagInit = new JObject
            {
                ["type"] = "InitializeVariable",
                ["inputs"] = new JObject
                {
                    ["variables"] = new JArray
                    {
                        new JObject
                        {
                            ["name"] = "shouldTerminate",
                            ["type"] = "boolean",
                            ["value"] = false
                        }
                    }
                },
                ["runAfter"] = new JObject()
            };

            var msgInit = new JObject
            {
                ["type"] = "InitializeVariable",
                ["inputs"] = new JObject
                {
                    ["variables"] = new JArray
                    {
                        new JObject
                        {
                            ["name"] = "terminateMessage",
                            ["type"] = "string",
                            ["value"] = ""
                        }
                    }
                },
                ["runAfter"] = new JObject
                {
                    [flagVarName] = new JArray("SUCCEEDED")
                }
            };

            // Rewire: find the first action that has an empty runAfter (the original
            // first action) and make it depend on the new variable inits.
            foreach (var prop in actionsObj.Properties().ToList())
            {
                var act = prop.Value as JObject;
                if (act == null) continue;
                var ra = act["runAfter"] as JObject;
                if (ra != null && ra.Count == 0)
                {
                    ra[flagVarName] = new JArray("SUCCEEDED");
                    // Only rewire the first one with empty runAfter – but the flag
                    // init itself must have empty runAfter, so we need to be careful.
                    // Since we haven't added flag/msg yet, this is safe.
                    break;
                }
            }

            // But actually the first empty-runAfter action may now depend on
            // flagVarName, so we need msgVarName to chain before the first
            // action. Let's rebuild: first action depends on msgVarName.
            // Re-scan: update the action we just changed to depend on msgVarName.
            foreach (var prop in actionsObj.Properties().ToList())
            {
                var act = prop.Value as JObject;
                if (act == null) continue;
                var ra = act["runAfter"] as JObject;
                if (ra != null && ra.Property(flagVarName) != null && prop.Name != msgVarName)
                {
                    ra.Remove(flagVarName);
                    ra[msgVarName] = new JArray("SUCCEEDED");
                    break;
                }
            }

            actionsObj.AddFirst(new JProperty(msgVarName, msgInit));
            actionsObj.AddFirst(new JProperty(flagVarName, flagInit));

            // --- Step 2 & 3: For each Until, replace nested Terminates and add post-loop Condition ---
            foreach (var kvp in untilsWithTerminate)
            {
                string untilName = kvp.Key;
                var untilAction = kvp.Value;

                // Replace all nested Terminate actions with SetVariable pairs
                ReplaceTerminateWithSignal(untilAction, usedNames);

                // Build a post-loop Condition that checks shouldTerminate
                string condName = AllocateName("Check_Terminate_" + NormalizeName(untilName), usedNames);
                string termName = AllocateName("Terminate_" + NormalizeName(untilName), usedNames);

                var terminateAction = new JObject
                {
                    ["type"] = "Terminate",
                    ["inputs"] = new JObject
                    {
                        ["runStatus"] = "Failed",
                        ["runError"] = new JObject
                        {
                            ["code"] = "Terminated",
                            ["message"] = "@variables('terminateMessage')"
                        }
                    },
                    ["runAfter"] = new JObject()
                };

                var condition = new JObject
                {
                    ["type"] = "If",
                    ["expression"] = "@equals(variables('shouldTerminate'), true)",
                    ["actions"] = new JObject
                    {
                        [termName] = terminateAction
                    },
                    ["else"] = new JObject
                    {
                        ["actions"] = new JObject()
                    },
                    ["runAfter"] = new JObject
                    {
                        [untilName] = new JArray("SUCCEEDED", "FAILED", "TIMEDOUT")
                    }
                };

                // Find actions that depended on the Until and rewire them to depend on the Condition
                foreach (var prop in actionsObj.Properties().ToList())
                {
                    var act = prop.Value as JObject;
                    if (act == null) continue;
                    var ra = act["runAfter"] as JObject;
                    if (ra == null) continue;

                    if (ra.Property(untilName) != null)
                    {
                        var statuses = ra[untilName];
                        ra.Remove(untilName);
                        ra[condName] = statuses;
                    }
                }

                actionsObj[condName] = condition;
            }
        }

        /// <summary>
        /// Recursively finds all Terminate actions nested inside an action's children.
        /// </summary>
        private static List<KeyValuePair<string, JObject>> FindTerminateActions(JObject container)
        {
            var result = new List<KeyValuePair<string, JObject>>();

            // Check direct "actions" object
            FindTerminateInActions(container["actions"] as JObject, result);

            // Check If true/false branches
            var ifActions = container["actions"] as JObject;
            if (ifActions != null)
            {
                foreach (var prop in ifActions.Properties())
                {
                    var child = prop.Value as JObject;
                    if (child == null) continue;

                    if (string.Equals(child["type"]?.ToString(), "If", StringComparison.OrdinalIgnoreCase))
                    {
                        FindTerminateInActions(child["actions"] as JObject, result);
                        var elseObj = child["else"] as JObject;
                        if (elseObj != null)
                        {
                            FindTerminateInActions(elseObj["actions"] as JObject, result);
                        }
                    }

                    // Recurse into Scope actions
                    if (string.Equals(child["type"]?.ToString(), "Scope", StringComparison.OrdinalIgnoreCase))
                    {
                        result.AddRange(FindTerminateActions(child));
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Searches an actions object for Terminate actions and recurses into containers.
        /// </summary>
        private static void FindTerminateInActions(JObject actionsObj, List<KeyValuePair<string, JObject>> result)
        {
            if (actionsObj == null) return;

            foreach (var prop in actionsObj.Properties())
            {
                var action = prop.Value as JObject;
                if (action == null) continue;

                var type = action["type"]?.ToString();
                if (string.Equals(type, "Terminate", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(new KeyValuePair<string, JObject>(prop.Name, action));
                }
                else if (string.Equals(type, "If", StringComparison.OrdinalIgnoreCase))
                {
                    FindTerminateInActions(action["actions"] as JObject, result);
                    var elseObj = action["else"] as JObject;
                    if (elseObj != null)
                    {
                        FindTerminateInActions(elseObj["actions"] as JObject, result);
                    }
                }
                else if (string.Equals(type, "Scope", StringComparison.OrdinalIgnoreCase))
                {
                    FindTerminateInActions(action["actions"] as JObject, result);
                }
            }
        }

        /// <summary>
        /// Replaces all Terminate actions inside a container with SetVariable pairs
        /// that signal the post-loop Condition to terminate.
        /// </summary>
        private static void ReplaceTerminateWithSignal(JObject container, HashSet<string> usedNames)
        {
            ReplaceTerminateInActions(container["actions"] as JObject, usedNames);
        }

        /// <summary>
        /// Walks an actions object and replaces Terminate actions with SetVariable pairs.
        /// </summary>
        private static void ReplaceTerminateInActions(JObject actionsObj, HashSet<string> usedNames)
        {
            if (actionsObj == null) return;

            foreach (var prop in actionsObj.Properties().ToList())
            {
                var action = prop.Value as JObject;
                if (action == null) continue;

                var type = action["type"]?.ToString();

                if (string.Equals(type, "Terminate", StringComparison.OrdinalIgnoreCase))
                {
                    // Extract the error message from the Terminate action
                    var errorMessage = action.SelectToken("inputs.runError.message")?.ToString() ?? "Terminated";
                    var runAfter = action["runAfter"] as JObject ?? new JObject();

                    // Build SetVariable for shouldTerminate = true
                    string setFlagName = AllocateName("Set_shouldTerminate_" + NormalizeName(prop.Name), usedNames);
                    var setFlag = new JObject
                    {
                        ["type"] = "SetVariable",
                        ["inputs"] = new JObject
                        {
                            ["name"] = "shouldTerminate",
                            ["value"] = true
                        },
                        ["runAfter"] = runAfter.DeepClone()
                    };

                    // Build SetVariable for terminateMessage
                    string setMsgName = AllocateName("Set_terminateMessage_" + NormalizeName(prop.Name), usedNames);
                    var setMsg = new JObject
                    {
                        ["type"] = "SetVariable",
                        ["inputs"] = new JObject
                        {
                            ["name"] = "terminateMessage",
                            ["value"] = errorMessage
                        },
                        ["runAfter"] = new JObject
                        {
                            [setFlagName] = new JArray("SUCCEEDED")
                        }
                    };

                    // Rewire any actions that depended on the old Terminate name
                    foreach (var otherProp in actionsObj.Properties().ToList())
                    {
                        if (otherProp.Name == prop.Name) continue;
                        var otherAction = otherProp.Value as JObject;
                        if (otherAction == null) continue;
                        var otherRunAfter = otherAction["runAfter"] as JObject;
                        if (otherRunAfter == null) continue;

                        if (otherRunAfter.Property(prop.Name) != null)
                        {
                            var statuses = otherRunAfter[prop.Name];
                            otherRunAfter.Remove(prop.Name);
                            otherRunAfter[setMsgName] = statuses;
                        }
                    }

                    // Remove the Terminate and add the two SetVariable actions
                    actionsObj.Remove(prop.Name);
                    actionsObj[setFlagName] = setFlag;
                    actionsObj[setMsgName] = setMsg;
                }
                else if (string.Equals(type, "If", StringComparison.OrdinalIgnoreCase))
                {
                    ReplaceTerminateInActions(action["actions"] as JObject, usedNames);
                    var elseObj = action["else"] as JObject;
                    if (elseObj != null)
                    {
                        ReplaceTerminateInActions(elseObj["actions"] as JObject, usedNames);
                    }
                }
                else if (string.Equals(type, "Scope", StringComparison.OrdinalIgnoreCase))
                {
                    ReplaceTerminateInActions(action["actions"] as JObject, usedNames);
                }
            }
        }

        #endregion
    }
}