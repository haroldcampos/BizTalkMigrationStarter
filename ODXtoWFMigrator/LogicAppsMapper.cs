// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

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
    }

    /// <summary>
    /// Provides functionality to map BizTalk orchestrations to Azure Logic Apps workflows.
    /// Converts BizTalk shapes, ports, and bindings into Logic Apps triggers and actions.
    /// </summary>
    public static class LogicAppsMapper
    {
        /// <summary>
        /// Tracks the current orchestration being processed for self-recursion detection.
        /// Thread-local storage ensures each thread maintains its own orchestration context,
        /// preventing race conditions when processing multiple orchestrations concurrently
        /// (e.g., MCP server handling parallel conversion requests).
        /// Set during MapToLogicApp() and used by IsSelfRecursiveCall() to prevent circular workflow calls.
        /// </summary>
        private static readonly ThreadLocal<string> _currentOrchestrationName = 
            new ThreadLocal<string>(() => null);
        private static readonly ThreadLocal<string> _currentOrchestrationFullName = 
            new ThreadLocal<string>(() => null);
        /// <summary>
        /// Maps BizTalk bindings to Logic Apps workflows WITHOUT orchestration files.
        /// Creates one workflow per receive location, using filtered send ports as actions.
        /// This enables migration scenarios where customers provide only bindings exports.
        /// </summary>
        /// <param name="binding">The binding snapshot containing receive locations and send ports.</param>
        /// <returns>A list of LogicAppWorkflowMaps, one per receive location.</returns>
        /// <exception cref="ArgumentNullException">Thrown when binding is null.</exception>
        public static List<LogicAppWorkflowMap> MapBindingsToWorkflows(BindingSnapshot binding)
        {
            if (binding == null) throw new ArgumentNullException(nameof(binding));

            var workflows = new List<LogicAppWorkflowMap>();
            var receiveLocationsByPort = binding.GetReceiveLocationsByPort();

            // Iterate through each receive port and its receive locations
            foreach (var portGroup in receiveLocationsByPort)
            {
                var receivePortName = portGroup.Key;
                var receiveLocations = portGroup.Value;
                var boundSendPorts = binding.GetSendPortsForReceivePort(receivePortName);

                // Create one workflow per receive location
                foreach (var rl in receiveLocations)
                {
                    var workflowName = SafeName(rl.Name ?? $"Workflow_{receivePortName}");

                    var workflow = new LogicAppWorkflowMap { Name = workflowName };

                    // Create trigger from receive location
                    var trigger = CreateTriggerFromReceiveLocation(rl);
                    workflow.Triggers.Add(trigger);

                    // Add EDI decode if needed
                    AddEdiDecodeIfNeeded(workflow, trigger);

                    // Add bound send ports as actions
                    int actionSeq = 0;
                    foreach (var sp in boundSendPorts)
                    {
                        var action = CreateActionFromSendPort(sp, actionSeq++);
                        workflow.Actions.Add(action);
                    }

                    workflows.Add(workflow);
                }
            }

            // Detect content-based routing patterns
            var cbrGroups = binding.DetectContentBasedRouting();

            // Track receive locations used by CBR workflows to avoid duplicate workflows
            var cbrReceiveLocationNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Create CBR workflows if detected
            if (cbrGroups.Count > 0)
            {
                Console.WriteLine("Detected " + cbrGroups.Count + " content-based routing pattern(s)");
                
                foreach (var cbrGroup in cbrGroups.Values)
                {
                    // For CBR, we need a receive location - use the first enabled one or any available
                    var defaultReceiveLocation = receiveLocationsByPort.Values
                        .SelectMany(rl => rl)
                        .FirstOrDefault(rl => rl.Enabled) 
                        ?? receiveLocationsByPort.Values.SelectMany(rl => rl).FirstOrDefault();
                    
                    if (defaultReceiveLocation != null)
                    {
                        var cbrWorkflowName = SafeName("CBR_" + cbrGroup.RoutingProperty.Split('.').Last() + "_Workflow");
                        
                        Console.WriteLine("  Creating CBR workflow: " + cbrWorkflowName);
                        Console.WriteLine("    Routing property: " + cbrGroup.RoutingProperty);
                        Console.WriteLine("    Routes: " + cbrGroup.RoutesByValue.Count);
                        
                        var cbrWorkflow = CreateCbrWorkflow(defaultReceiveLocation, cbrGroup, cbrWorkflowName);
                        workflows.Add(cbrWorkflow);
                        
                        // Track this receive location to avoid creating duplicate empty workflow
                        if (!string.IsNullOrEmpty(defaultReceiveLocation.Name))
                        {
                            cbrReceiveLocationNames.Add(defaultReceiveLocation.Name);
                        }
                    }
                }
            }

            // Remove empty workflows for receive locations that are already handled by CBR
            // This prevents duplicate workflows - CBR workflow already has trigger + Switch actions
            if (cbrReceiveLocationNames.Count > 0)
            {
                workflows.RemoveAll(w =>
                {
                    // Check if this is an empty receive location workflow (trigger but no actions)
                    var isEmpty = w.Actions.Count == 0 || (w.Actions.Count == 1 && 
                        (w.Actions[0].Type == "X12Decode" || w.Actions[0].Type == "EdifactDecode"));
                    
                    if (!isEmpty)
                        return false;
                    
                    // Check if this workflow's receive location is used by a CBR workflow
                    var triggerName = w.Triggers.FirstOrDefault()?.Name;
                    if (string.IsNullOrEmpty(triggerName))
                        return false;
                    
                    // If the trigger name matches a CBR-handled receive location, remove this duplicate
                    if (cbrReceiveLocationNames.Contains(triggerName))
                    {
                        Console.WriteLine("  Suppressing duplicate empty workflow for: " + w.Name + " (already in CBR workflow)");
                        return true;
                    }
                    
                    return false;
                });
            }

            // Track send ports already handled by CBR workflows
            var cbrHandledSendPorts = new HashSet<string>(
                cbrGroups.Values
                    .SelectMany(cbr => cbr.RoutesByValue.Values.SelectMany(sp => sp))
                    .Select(sp => sp.Name ?? ""),
                StringComparer.OrdinalIgnoreCase
            );

            // Handle orphaned send ports (no filter or filter to non-existent receive port)
            // Fixed: Added null check for GetReceivePortNameFromFilter() to prevent ArgumentNullException
            // when send ports use content-based routing filters (e.g., promoted properties) instead of BTS.ReceivePortName
            // EXCLUDE send ports already handled by CBR workflows
            var orphanedSendPorts = binding.SendPorts.Where(sp =>
            {
                // Skip if already handled by CBR workflow
                if (!string.IsNullOrEmpty(sp.Name) && cbrHandledSendPorts.Contains(sp.Name))
                    return false;
                
                // Send ports with no filters are orphaned
                if (sp.Filters == null || sp.Filters.Count == 0)
                    return true;
                
                // Get the receive port name from filter (may be null for CBR scenarios)
                var receivePortName = sp.GetReceivePortNameFromFilter();
                
                // If no BTS.ReceivePortName filter, check if it's CBR (already handled above)
                if (string.IsNullOrEmpty(receivePortName))
                {
                    // This shouldn't happen if CBR detection worked, but treat as orphaned for safety
                    var routingProperty = BindingSnapshot.GetRoutingPropertyFromFilter(sp);
                    if (!string.IsNullOrEmpty(routingProperty))
                    {
                        // It's CBR but somehow missed - log warning
                        Console.WriteLine("  Warning: Send port '" + sp.Name + "' looks like CBR but wasn't handled");
                    }
                    return true;
                }
                
                // If BTS.ReceivePortName filter exists but port not found, it's orphaned
                return !receiveLocationsByPort.ContainsKey(receivePortName);
            }).ToList();

            if (orphanedSendPorts.Count > 0)
            {
                // Create a single workflow for orphaned send ports with HTTP trigger
                var orphanWorkflow = new LogicAppWorkflowMap { Name = "OrphanedSendPorts" };
                orphanWorkflow.Triggers.Add(new LogicAppTrigger
                {
                    Name = "When_an_HTTP_request_is_received",
                    Kind = "Request",
                    TransportType = "HTTP",
                    Sequence = 0
                });

                int seq = 0;
                foreach (var sp in orphanedSendPorts)
                {
                    var action = CreateActionFromSendPort(sp, seq++);
                    orphanWorkflow.Actions.Add(action);
                }

                workflows.Add(orphanWorkflow);
            }

            return workflows;
        }

        /// <summary>
        /// Creates a Logic Apps trigger from a BizTalk receive location.
        /// Preserves all transport metadata including WCF and HostApps properties.
        /// </summary>
        /// <param name="rl">The BizTalk receive location to convert.</param>
        /// <returns>A configured LogicAppTrigger with connector kind and metadata.</returns>
        private static LogicAppTrigger CreateTriggerFromReceiveLocation(BindingReceiveLocation rl)
        {
            var kind = InferKind(rl.TransportType, rl.Address, rl.HostAppsSubType);

            return new LogicAppTrigger
            {
                Name = SafeName(rl.Name ?? "Trigger"),
                Kind = kind,
                TransportType = rl.TransportType,
                Address = rl.Address,
                FolderPath = rl.FolderPath,
                FileMask = rl.FileMask,
                PollingIntervalSeconds = rl.PollingIntervalSeconds,
                UserName = rl.UserName,
                Password = rl.Password,
                ConnectionString = rl.ConnectionString,
                PrimaryTransport = rl.PrimaryTransport,
                Endpoint = rl.Endpoint,
                SecurityMode = rl.SecurityMode,
                MessageClientCredentialType = rl.MessageClientCredentialType,
                TransportClientCredentialType = rl.TransportClientCredentialType,
                MessageEncoding = rl.MessageEncoding,
                AlgorithmSuite = rl.AlgorithmSuite,
                MaxReceivedMessageSize = rl.MaxReceivedMessageSize,
                MaxConcurrentCalls = rl.MaxConcurrentCalls,
                OpenTimeout = rl.OpenTimeout,
                CloseTimeout = rl.CloseTimeout,
                SendTimeout = rl.SendTimeout,
                EstablishSecurityContext = rl.EstablishSecurityContext,
                NegotiateServiceCredential = rl.NegotiateServiceCredential,
                IncludeExceptionDetailInFaults = rl.IncludeExceptionDetailInFaults,
                UseSSO = rl.UseSSO,
                SuspendMessageOnFailure = rl.SuspendMessageOnFailure,
                Sequence = 0
            };
        }

        /// <summary>
        /// Creates a Logic Apps action from a BizTalk send port.
        /// Preserves all transport metadata including WCF and HostApps properties.
        /// ✅ NEW: Adds XSLT Transform actions for outbound maps before the send action.
        /// </summary>
        /// <param name="sp">The BizTalk send port to convert.</param>
        /// <param name="sequence">The sequence number for the action.</param>
        /// <returns>A configured LogicAppAction (SendConnector), or a Scope containing transforms + SendConnector if transforms exist.</returns>
        private static LogicAppAction CreateActionFromSendPort(BindingSendPort sp, int sequence)
        {
            var kind = InferKind(sp.TransportType, sp.Address, sp.HostAppsSubType);

            // ✅ If the send port has transforms, create a Scope with Transform actions + SendConnector
            if (sp.Transforms != null && sp.Transforms.Count > 0)
            {
                // Create a container scope for transform + send
                var containerScope = new LogicAppAction
                {
                    Name = SafeName(sp.Name ?? "SendWithTransform"),
                    Type = "Scope",
                    Details = string.Format("{0} outbound map(s) applied before sending", sp.Transforms.Count),
                    Sequence = sequence
                };

                int childSeq = 0;
                
                // Add transform actions (in sequence - chained transformations)
                foreach (var transform in sp.Transforms)
                {
                    var transformAction = new LogicAppAction
                    {
                        Name = SafeName("Transform_" + (transform.ShortName ?? "Map")),
                        Type = "Xslt",
                        Details = transform.FullName,
                        TransformClassName = transform.FullName,
                        Sequence = childSeq++
                    };
                    containerScope.Children.Add(transformAction);
                }
                
                // Add send action (uses last transform output)
                var sendAction = new LogicAppAction
                {
                    Name = SafeName(sp.Name ?? "SendAction"),
                    Type = "SendConnector",
                    ConnectorKind = kind,
                    TargetAddress = sp.Address,
                    Sequence = childSeq++,
                    ConnectionString = sp.ConnectionString,
                    UserName = sp.UserName,
                    Password = sp.Password,
                    PrimaryTransport = sp.PrimaryTransport,
                    Endpoint = sp.Endpoint,
                    SecurityMode = sp.SecurityMode,
                    MessageClientCredentialType = sp.MessageClientCredentialType,
                    TransportClientCredentialType = sp.TransportClientCredentialType,
                    MessageEncoding = sp.MessageEncoding,
                    AlgorithmSuite = sp.AlgorithmSuite,
                    MaxReceivedMessageSize = sp.MaxReceivedMessageSize,
                    MaxConcurrentCalls = sp.MaxConcurrentCalls,
                    OpenTimeout = sp.OpenTimeout,
                    CloseTimeout = sp.CloseTimeout,
                    SendTimeout = sp.SendTimeout,
                    EstablishSecurityContext = sp.EstablishSecurityContext,
                    NegotiateServiceCredential = sp.NegotiateServiceCredential
                };

                // Parse Service Bus queue/topic details if applicable
                if (kind == "ServiceBus")
                {
                    PopulateServiceBusParts(sendAction, sp.Address);
                }

                containerScope.Children.Add(sendAction);
                return containerScope;
            }
            
            // ✅ No transforms - return simple SendConnector action
            var action = new LogicAppAction
            {
                Name = SafeName(sp.Name ?? "SendAction"),
                Type = "SendConnector",
                ConnectorKind = kind,
                TargetAddress = sp.Address,
                Sequence = sequence,
                ConnectionString = sp.ConnectionString,
                UserName = sp.UserName,
                Password = sp.Password,
                PrimaryTransport = sp.PrimaryTransport,
                Endpoint = sp.Endpoint,
                SecurityMode = sp.SecurityMode,
                MessageClientCredentialType = sp.MessageClientCredentialType,
                TransportClientCredentialType = sp.TransportClientCredentialType,
                MessageEncoding = sp.MessageEncoding,
                AlgorithmSuite = sp.AlgorithmSuite,
                MaxReceivedMessageSize = sp.MaxReceivedMessageSize,
                MaxConcurrentCalls = sp.MaxConcurrentCalls,
                OpenTimeout = sp.OpenTimeout,
                CloseTimeout = sp.CloseTimeout,
                SendTimeout = sp.SendTimeout,
                EstablishSecurityContext = sp.EstablishSecurityContext,
                NegotiateServiceCredential = sp.NegotiateServiceCredential
            };

            // Parse Service Bus queue/topic details if applicable
            if (kind == "ServiceBus")
            {
                PopulateServiceBusParts(action, sp.Address);
            }

            return action;
        }

        /// <summary>
        /// Creates a workflow for content-based routing scenarios.
        /// Generates a workflow with a Switch action that routes to different send ports based on promoted property value.
        /// </summary>
        /// <param name="receiveLocation">The receive location that triggers the workflow.</param>
        /// <param name="cbrGroup">The CBR group containing routing information.</param>
        /// <param name="workflowName">The name for the generated workflow.</param>
        /// <returns>A workflow with Switch-based routing logic.</returns>
        private static LogicAppWorkflowMap CreateCbrWorkflow(
            BindingReceiveLocation receiveLocation,
            ContentBasedRoutingGroup cbrGroup,
            string workflowName)
        {
            var workflow = new LogicAppWorkflowMap { Name = workflowName };
            
            // Create trigger from receive location
            var trigger = CreateTriggerFromReceiveLocation(receiveLocation);
            workflow.Triggers.Add(trigger);
            
            // Add EDI decode if needed
            AddEdiDecodeIfNeeded(workflow, trigger);
            
            // Create a Switch action for routing
            var switchAction = new LogicAppAction
            {
                Name = "Route_By_" + SafeName(cbrGroup.RoutingProperty.Split('.').Last()),
                Type = "Switch",
                Details = cbrGroup.RoutingProperty,
                Sequence = 0
            };
            
            // Add cases for each routing value
            int caseSeq = 0;
            foreach (var route in cbrGroup.RoutesByValue.OrderBy(kvp => kvp.Key))
            {
                var caseScope = new LogicAppAction
                {
                    Name = "Case_" + SafeName(route.Key),
                    Type = "Scope",
                    Details = "Route to: " + string.Join(", ", route.Value.Select(sp => sp.Name)),
                    Sequence = caseSeq++,
                    IsBranchContainer = true
                };
                
                // Add send port actions for this case
                int sendSeq = 0;
                foreach (var sendPort in route.Value)
                {
                    var sendAction = CreateActionFromSendPort(sendPort, sendSeq++);
                    caseScope.Children.Add(sendAction);
                }
                
                switchAction.Children.Add(caseScope);
            }
            
            // Add default case (optional - for unmatched values)
            var defaultCase = new LogicAppAction
            {
                Name = "Default_Route",
                Type = "Scope",
                Details = "No routing match - log or handle error",
                Sequence = caseSeq,
                IsBranchContainer = true
            };
            
            var logAction = new LogicAppAction
            {
                Name = "Log_Unmatched_Route",
                Type = "Compose",
                Details = "Unmatched routing value for " + cbrGroup.RoutingProperty,
                Sequence = 0
            };
            defaultCase.Children.Add(logAction);
            switchAction.Children.Add(defaultCase);
            
            workflow.Actions.Add(switchAction);
            
            return workflow;
        }

        /// <summary>
        /// Maps a BizTalk orchestration and its bindings to a Logic Apps workflow structure.
        /// </summary>
        /// <param name="orchestration">The BizTalk orchestration model containing shapes and metadata.</param>
        /// <param name="binding">The binding snapshot containing receive locations and send ports.</param>
        /// <param name="isCallable">If true, forces Request trigger for nested workflow compatibility.</param>
        /// <returns>A LogicAppWorkflowMap containing triggers, actions, and variable declarations.</returns>
        /// <exception cref="ArgumentNullException">Thrown when orchestration or binding is null.</exception>
        public static LogicAppWorkflowMap MapToLogicApp(OrchestrationModel orchestration, BindingSnapshot binding, bool isCallable = false)
        {
            if (orchestration == null) throw new ArgumentNullException(nameof(orchestration));
            if (binding == null) throw new ArgumentNullException(nameof(binding));

            // Set current orchestration context for self-recursion detection
            _currentOrchestrationName.Value = orchestration.Name;
            _currentOrchestrationFullName.Value = orchestration.FullName;

            var map = new LogicAppWorkflowMap { Name = orchestration.FullName };

            // Collect variable names from orchestration for expression mapping context
            CollectVariableNamesRecursive(orchestration.Shapes, map.VariableNames);

            var trigger = SelectTrigger(binding, isCallable);
            map.Triggers.Add(trigger);
            AddEdiDecodeIfNeeded(map, trigger);

            // FIRST PASS: Collect all VariableDeclarations from anywhere in the shape tree
            // Logic Apps requires InitializeVariable actions at workflow root level (not nested in scopes)
            var variableActions = new List<LogicAppAction>();
            CollectVariableDeclarationsRecursive(orchestration.Shapes, variableActions);
            
            // Check for any variables in VariableNames that don't have declarations (undeclared but used)
            // Create InitializeVariable for all variables found in expressions (loop conditions, assignments, etc.)
            var declaredVarNames = new HashSet<string>(variableActions.Select(v => v.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var varName in map.VariableNames)
            {
                if (!declaredVarNames.Contains(varName))
                {
                    // Determine variable type based on naming or context
                    string varType = InferVariableType(varName);
                    
                    // Create InitializeVariable action for undeclared variable
                    var undeclaredVarAction = new LogicAppAction
                    {
                        Name = SafeName(varName),
                        Type = "InitializeVariable",
                        Details = varType,
                        Sequence = -1 // Will be reassigned
                    };
                    variableActions.Add(undeclaredVarAction);
                }
            }
            
            // Assign sequence numbers to variable initialization actions (must be FIRST)
            int varSeq = 0;
            foreach (var varAction in variableActions)
            {
                varAction.Sequence = varSeq++;
                map.Actions.Add(varAction);
            }

            // SECOND PASS: Convert remaining shapes into actions
            int nextSeq = varSeq; // Start after variable initializations
            foreach (var root in orchestration.Shapes.Where(s => s.Parent == null).OrderBy(s => s.Sequence))
            {
                if (root is ReceiveShapeModel r && r.Activate) continue;
                // Skip VariableDeclarations - already processed in first pass
                if (root is VariableDeclarationShapeModel) continue;
                var action = ConvertShape(root);
                if (action != null)
                {
                    action.Sequence = nextSeq++;
                    map.Actions.Add(action);
                }
            }

            foreach (var sp in binding.SendPorts)
            {
                // Use HostAppsSubType if available for HostApps transport
                var kind = InferKind(sp.TransportType, sp.Address, sp.HostAppsSubType);
                var send = new LogicAppAction
                {
                    Name = SafeName(sp.Name),
                    Type = "SendConnector",
                    ConnectorKind = kind,
                    TargetAddress = sp.Address,
                    Sequence = NextSeq(map),
                    ConnectionString = sp.ConnectionString,
                    UserName = sp.UserName,
                    Password = sp.Password,

                    // Additional bindings properties
                    PrimaryTransport = sp.PrimaryTransport,
                    Endpoint = sp.Endpoint,
                    SecurityMode = sp.SecurityMode,
                    
                    // WCF-specific metadata for send ports
                    MessageClientCredentialType = sp.MessageClientCredentialType,
                    TransportClientCredentialType = sp.TransportClientCredentialType,
                    MessageEncoding = sp.MessageEncoding,
                    AlgorithmSuite = sp.AlgorithmSuite,
                    MaxReceivedMessageSize = sp.MaxReceivedMessageSize,
                    MaxConcurrentCalls = sp.MaxConcurrentCalls,
                    OpenTimeout = sp.OpenTimeout,
                    CloseTimeout = sp.CloseTimeout,
                    SendTimeout = sp.SendTimeout,
                    EstablishSecurityContext = sp.EstablishSecurityContext,
                    NegotiateServiceCredential = sp.NegotiateServiceCredential
                };
                if (kind == "ServiceBus")
                    PopulateServiceBusParts(send, sp.Address);
                map.Actions.Add(send);
            }

            int seq = 0;
            foreach (var a in map.Actions.OrderBy(a => a.Sequence))
                a.Sequence = seq++;

            // Clear orchestration context after processing
            _currentOrchestrationName.Value = null;
            _currentOrchestrationFullName.Value = null;

            return map;
        }

        /// <summary>
        /// Recursively collects VariableDeclaration shapes from anywhere in the orchestration tree
        /// and converts them to InitializeVariable actions at workflow root level.
        /// This is necessary because Logic Apps requires InitializeVariable actions to be at root,
        /// but BizTalk allows VariableDeclarations to be nested inside Scopes.
        /// </summary>
        private static void CollectVariableDeclarationsRecursive(IEnumerable<ShapeModel> shapes, List<LogicAppAction> actions)
        {
            foreach (var shape in shapes)
            {
                // Convert VariableDeclaration shapes to InitializeVariable actions
                if (shape is VariableDeclarationShapeModel varDecl)
                {
                    var action = ConvertShape(varDecl);
                    if (action != null)
                    {
                        // Add to root-level actions list (flattening)
                        actions.Add(action);
                    }
                }

                // Recurse into children to find nested VariableDeclarations
                if (shape.Children.Count > 0)
                {
                    CollectVariableDeclarationsRecursive(shape.Children, actions);
                }

                // Check Decide branches
                if (shape is DecideShapeModel decide)
                {
                    CollectVariableDeclarationsRecursive(decide.TrueBranch, actions);
                    CollectVariableDeclarationsRecursive(decide.FalseBranch, actions);
                }

                // Check Construct inner shapes
                if (shape is ConstructShapeModel construct)
                {
                    CollectVariableDeclarationsRecursive(construct.InnerShapes, actions);
                }
            }
        }

        /// <summary>
        /// Recursively collects variable names from VariableDeclaration shapes AND from assignment/usage expressions.
        /// Case-insensitive duplicate prevention to avoid multiple initializations in Logic Apps.
        /// Also extracts variables used in Decide conditions to catch undeclared variables.
        /// </summary>
        private static void CollectVariableNamesRecursive(IEnumerable<ShapeModel> shapes, List<string> variableNames)
        {
            foreach (var shape in shapes)
            {
                // Collect from VariableDeclaration shapes
                if (shape is VariableDeclarationShapeModel varDecl)
                {
                    // Case-insensitive duplicate check
                    if (!string.IsNullOrEmpty(varDecl.Name) &&
                        !variableNames.Any(v => v.Equals(varDecl.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        variableNames.Add(varDecl.Name);
                    }
                }

                // Extract variable names from VariableAssignment expressions
                if (shape is VariableAssignmentShapeModel varAssign)
                {
                    var variables = ExtractVariableNamesFromExpression(varAssign.Expression);
                    foreach (var varName in variables)
                    {
                        // Case-insensitive duplicate check
                        if (!string.IsNullOrEmpty(varName) &&
                            !variableNames.Any(v => v.Equals(varName, StringComparison.OrdinalIgnoreCase)))
                        {
                            variableNames.Add(varName);
                        }
                    }
                }

                // Extract variable names from MessageAssignment expressions
                if (shape is MessageAssignmentShapeModel msgAssign)
                {
                    var variables = ExtractVariableNamesFromExpression(msgAssign.Expression);
                    foreach (var varName in variables)
                    {
                        // Case-insensitive duplicate check
                        if (!string.IsNullOrEmpty(varName) &&
                            !variableNames.Any(v => v.Equals(varName, StringComparison.OrdinalIgnoreCase)))
                        {
                            variableNames.Add(varName);
                        }
                    }
                }

                // Extract variable names from Decide condition expressions (catch undeclared variables)
                if (shape is DecideShapeModel decide && !string.IsNullOrWhiteSpace(decide.Expression))
                {
                    // Extract variables from the condition expression (e.g., "lbCifrasGeneradoOk == false")
                    var variables = ExtractVariableNamesFromCondition(decide.Expression);
                    foreach (var varName in variables)
                    {
                        // Case-insensitive duplicate check
                        if (!string.IsNullOrEmpty(varName) &&
                            !variableNames.Any(v => v.Equals(varName, StringComparison.OrdinalIgnoreCase)))
                        {
                            variableNames.Add(varName);
                        }
                    }
                }

                // Check direct children
                if (shape.Children.Count > 0)
                {
                    CollectVariableNamesRecursive(shape.Children, variableNames);
                }

                // Check Decide branches
                if (shape is DecideShapeModel decide2)
                {
                    CollectVariableNamesRecursive(decide2.TrueBranch, variableNames);
                    CollectVariableNamesRecursive(decide2.FalseBranch, variableNames);
                }

                // Check Construct inner shapes
                if (shape is ConstructShapeModel construct)
                {
                    CollectVariableNamesRecursive(construct.InnerShapes, variableNames);
                }
            }
        }

        /// <summary>
        /// Extracts variable names from C# assignment expressions in BizTalk orchestrations.
        /// Parses expressions to identify assignment targets (left side of '=').
        /// </summary>
        /// <param name="expression">The C# expression containing variable assignments.</param>
        /// <returns>A list of variable names found in the expression (e.g., ["recepcionArchivos", "reproceso"]).</returns>
        /// <example>
        /// Input: "recepcionArchivos = new X(); reproceso = false;"
        /// Output: ["recepcionArchivos", "reproceso"]
        /// </example>
        private static List<string> ExtractVariableNamesFromExpression(string expression)
        {
            var variables = new List<string>();

            if (string.IsNullOrWhiteSpace(expression))
                return variables;

            // Pattern: variable_name = anything; (captures assignment targets)
            var assignmentPattern = new Regex(@"(\w+)\s*=\s*[^=]", RegexOptions.Multiline);
            var matches = assignmentPattern.Matches(expression);

            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    var varName = match.Groups[1].Value.Trim();

                    // Filter out type names and keywords
                    if (!IsKeywordOrTypeName(varName))
                    {
                        // Case-insensitive comparison to avoid duplicates
                        if (!variables.Any(v => v.Equals(varName, StringComparison.OrdinalIgnoreCase)))
                        {
                            variables.Add(varName);
                        }
                    }
                }
            }

            return variables;
        }

        /// <summary>
        /// Extracts variable names from condition expressions (e.g., Decide shape conditions).
        /// Identifies variables used in comparisons like "lbCifrasGeneradoOk == false" or "counter < 10".
        /// Filters out property accesses (Object.Property) and common false positives.
        /// </summary>
        /// <param name="condition">The condition expression to parse.</param>
        /// <returns>A list of variable names found in the condition.</returns>
        /// <example>
        /// Input: "lbCifrasGeneradoOk == false"
        /// Output: ["lbCifrasGeneradoOk"]
        /// </example>
        private static List<string> ExtractVariableNamesFromCondition(string condition)
        {
            var variables = new List<string>();

            if (string.IsNullOrWhiteSpace(condition))
                return variables;

            // Remove comments first (// ... and /* ... */)
            var cleaned = Regex.Replace(condition, @"//.*?$", "", RegexOptions.Multiline);
            cleaned = Regex.Replace(cleaned, @"/\*.*?\*/", "", RegexOptions.Singleline);

            // Pattern: identifier (alphanumeric + underscore, starting with letter or underscore)
            // But NOT preceded by '.' and NOT followed by '.' (to exclude property accesses)
            var identifierPattern = new Regex(@"(?<!\.)(\b[a-zA-Z_][a-zA-Z0-9_]*\b)(?!\s*\.)");
            var matches = identifierPattern.Matches(cleaned);

            foreach (Match match in matches)
            {
                var identifier = match.Groups[1].Value;

                // Filter out keywords and type names
                if (!IsKeywordOrTypeName(identifier))
                {
                    // Likely a variable (camelCase pattern with lowercase first letter)
                    if (char.IsLower(identifier[0]) || identifier[0] == '_')
                    {
                        // Additional filters for common false positives
                        if (identifier.Length > 2) // Skip single/double letter words from comments
                        {
                            // Case-insensitive comparison to avoid duplicates
                            if (!variables.Any(v => v.Equals(identifier, StringComparison.OrdinalIgnoreCase)))
                            {
                                variables.Add(identifier);
                            }
                        }
                    }
                }
            }

            return variables;
        }

        /// <summary>
        /// Determines if an identifier is a C# keyword or common type name rather than a variable.
        /// Used to filter out false positives when extracting variable names from expressions.
        /// </summary>
        /// <param name="identifier">The identifier to check.</param>
        /// <returns>True if the identifier is a keyword or type name; false if it appears to be a variable.</returns>
        private static bool IsKeywordOrTypeName(string identifier)
        {
            // C# keywords - Create HashSet with comparer in constructor
            var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "new", "null", "true", "false", "var", "if", "else", "while", "for", "foreach",
                "return", "break", "continue", "switch", "case", "default", "try", "catch",
                "finally", "throw", "using", "namespace", "class", "struct", "interface",
                "enum", "public", "private", "protected", "internal", "static", "readonly",
                "const", "virtual", "override", "abstract", "sealed", "partial", "async",
                "await", "yield", "base", "this", "typeof", "sizeof", "nameof", "is", "as"
            };

            // Use Contains with only one argument - comparer is already set in constructor
            if (keywords.Contains(identifier))
                return true;

            // Filter out PascalCase type names (likely classes, not variables)
            // Variables in BizTalk typically use camelCase
            if (identifier.Length > 1 && char.IsUpper(identifier[0]) && char.IsUpper(identifier[1]))
                return true;

            return false;
        }

        /// <summary>
        /// Infers the Logic Apps variable type from a BizTalk variable name.
        /// Uses naming conventions and common patterns to determine the appropriate type.
        /// Priority order: boolean → integer → array → object → string
        /// </summary>
        /// <param name="varName">The variable name to analyze.</param>
        /// <returns>The inferred Logic Apps variable type ("string", "integer", "boolean", "object", "array").</returns>
        private static string InferVariableType(string varName)
        {
            if (string.IsNullOrWhiteSpace(varName))
                return "string";

            var lowerName = varName.ToLowerInvariant();

            // Boolean indicators (highest priority - most specific)
            if (lowerName.StartsWith("lb") || lowerName.StartsWith("is") || lowerName.StartsWith("has") ||
                lowerName.StartsWith("can") || lowerName.StartsWith("should") || 
                lowerName.Contains("flag") || lowerName.Contains("enabled") || lowerName.Contains("active") ||
                lowerName.Contains("complete") || lowerName.Contains("done") || lowerName.Contains("success"))
            {
                return "boolean";
            }

            // Integer/number indicators (check before generic patterns)
            // "number" is a strong indicator of integer type - prioritize it
            if (lowerName.StartsWith("li") || lowerName.StartsWith("ln") || lowerName.StartsWith("int") ||
                lowerName.Contains("count") || lowerName.Contains("number") || 
                lowerName.Contains("total") || lowerName.Contains("index") ||
                lowerName.Contains("counter") || lowerName.Contains("size") || 
                lowerName.Contains("length") || lowerName.Contains("quantity") ||
                lowerName.EndsWith("count"))
            {
                return "integer";
            }

            // Array/collection indicators
            if (lowerName.StartsWith("la") || lowerName.Contains("list") || 
                lowerName.Contains("array") || lowerName.Contains("collection") ||
                lowerName.Contains("items") || lowerName.EndsWith("list"))
            {
                return "array";
            }

            // Object indicators
            if (lowerName.StartsWith("lo") || lowerName.Contains("enumerator") ||
                lowerName.Contains("iterator") || lowerName.EndsWith("object") ||
                lowerName.Contains("document") || 
                (lowerName.Contains("message") && !lowerName.Contains("number"))) // Message but not "NumberOfMessages"
            {
                return "object";
            }

            // String indicators (default for text-based content)
            if (lowerName.StartsWith("ls") || lowerName.StartsWith("str") ||
                lowerName.Contains("name") || lowerName.Contains("text") || 
                lowerName.Contains("description") || lowerName.Contains("error"))
            {
                return "string";
            }

            // Default to string for unknown patterns
            return "string";
        }

        /// <summary>
        /// Parses MessageAssignment expressions to extract property assignments for message construction.
        /// Identifies patterns like "MessageName.PropertyName = value" and captures property-value pairs.
        /// </summary>
        /// <param name="expression">The MessageAssignment expression containing property assignments.</param>
        /// <returns>A dictionary mapping property names to their assigned values.</returns>
        /// <example>
        /// Input: "UpdateResponseMsg.Status = \"Received\";"
        /// Output: {"Status": "Received"}
        /// </example>
        private static Dictionary<string, string> ParseMessagePropertyAssignments(string expression)
        {
            var assignments = new Dictionary<string, string>();

            if (string.IsNullOrWhiteSpace(expression))
                return assignments;

            // Pattern: MessageName.PropertyName = "value" or MessageName.PropertyName = value
            var pattern = new Regex(@"(\w+)\.(\w+)\s*=\s*(.+?);", RegexOptions.Multiline);
            var matches = pattern.Matches(expression);

            foreach (Match match in matches)
            {
                if (match.Groups.Count >= 4)
                {
                    var propertyName = match.Groups[2].Value.Trim();
                    var value = match.Groups[3].Value.Trim();

                    // Remove quotes if present
                    if (value.StartsWith("\"") && value.EndsWith("\""))
                        value = value.Substring(1, value.Length - 2);

                    assignments[propertyName] = value;
                }
            }

            return assignments;
        }

        /// <summary>
        /// Selects the appropriate trigger from the binding snapshot.
        /// Prioritizes enabled receive locations; falls back to HTTP Request trigger if none found.
        /// For callable workflows (invoked via Call/Start), forces Request trigger to comply with Azure Logic Apps requirements.
        /// </summary>
        /// <param name="binding">The binding snapshot containing receive locations.</param>
        /// <param name="isCallable">If true, forces Request trigger regardless of binding configuration (required for nested workflows).</param>
        /// <returns>A LogicAppTrigger configured based on the receive location or a default HTTP trigger.</returns>
        private static LogicAppTrigger SelectTrigger(BindingSnapshot binding, bool isCallable = false)
        {
            // Force Request trigger for callable workflows (Azure requirement)
            if (isCallable)
            {
                return new LogicAppTrigger
                {
                    Name = "When_called_from_parent_workflow",
                    Kind = "Request",
                    TransportType = "HTTP",
                    Sequence = 0
                };
            }

            var rl = binding.ReceiveLocations.FirstOrDefault(r => r.Enabled) ?? binding.ReceiveLocations.FirstOrDefault();
            if (rl == null)
            {
                return new LogicAppTrigger
                {
                    Name = "When_an_HTTP_request_is_received",
                    Kind = "Request",
                    TransportType = "HTTP",
                    Sequence = -1
                };
            }

            // Use HostAppsSubType if available for HostApps transport
            var kind = InferKind(rl.TransportType, rl.Address, rl.HostAppsSubType);
            
            return new LogicAppTrigger
            {
                Name = SafeName(rl.Name),
                Kind = kind,
                TransportType = rl.TransportType,
                Address = rl.Address,
                FolderPath = rl.FolderPath,
                FileMask = rl.FileMask,
                PollingIntervalSeconds = rl.PollingIntervalSeconds,
                UserName = rl.UserName,
                Password = rl.Password,
                ConnectionString = rl.ConnectionString,

                // Additional bindings properties
                PrimaryTransport = rl.PrimaryTransport,
                Endpoint = rl.Endpoint,
                SecurityMode = rl.SecurityMode,
                
                // WCF-specific metadata
                MessageClientCredentialType = rl.MessageClientCredentialType,
                TransportClientCredentialType = rl.TransportClientCredentialType,
                MessageEncoding = rl.MessageEncoding,
                AlgorithmSuite = rl.AlgorithmSuite,
                MaxReceivedMessageSize = rl.MaxReceivedMessageSize,
                MaxConcurrentCalls = rl.MaxConcurrentCalls,
                OpenTimeout = rl.OpenTimeout,
                CloseTimeout = rl.CloseTimeout,
                SendTimeout = rl.SendTimeout,
                EstablishSecurityContext = rl.EstablishSecurityContext,
                NegotiateServiceCredential = rl.NegotiateServiceCredential,
                IncludeExceptionDetailInFaults = rl.IncludeExceptionDetailInFaults,
                UseSSO = rl.UseSSO,
                SuspendMessageOnFailure = rl.SuspendMessageOnFailure,

                Sequence = 0
            };
        }

        /// <summary>
        /// Adds EDI decode actions (X12 or EDIFACT) to the workflow if the trigger address indicates EDI processing.
        /// </summary>
        /// <param name="map">The workflow map to add decode actions to.</param>
        /// <param name="trigger">The trigger to inspect for EDI indicators.</param>
        private static void AddEdiDecodeIfNeeded(LogicAppWorkflowMap map, LogicAppTrigger trigger)
        {
            if (trigger == null) return;
            var addr = (trigger.Address ?? "").ToLowerInvariant();
            if (addr.Contains("x12"))
            {
                map.Actions.Add(new LogicAppAction
                {
                    Name = "X12Decode",
                    Type = "X12Decode",
                    Details = "Decode X12 message",
                    Sequence = 0
                });
            }
            else if (addr.Contains("edifact"))
            {
                map.Actions.Add(new LogicAppAction
                {
                    Name = "EdifactDecode",
                    Type = "EdifactDecode",
                    Details = "Decode EDIFACT message",
                    Sequence = 0
                });
            }
        }

        /// <summary>
        /// Recursively converts a BizTalk orchestration shape to a Logic Apps action.
        /// Handles all shape types including Parallel, Loop, Decide, Construct, Transform, Send, etc.
        /// </summary>
        /// <param name="shape">The BizTalk shape to convert.</param>
        /// <returns>A LogicAppAction representing the converted shape, or null if the shape should be skipped.</returns>
        private static LogicAppAction ConvertShape(ShapeModel shape)
        {
            LogicAppAction action = null;

            if (shape is ParallelShapeModel parallel)
            {
                action = new LogicAppAction
                {
                    Name = SafeName(parallel.Name ?? "Parallel"),
                    Type = "ParallelContainer",
                    Details = "Parallel branches",
                    Sequence = parallel.Sequence
                };

                int idx = 0;
                foreach (var branch in parallel.Children.OrderBy(c => c.Sequence))
                {
                    if (branch.ShapeType == "ParallelBranch")
                    {
                        var branchName = branch.Name;
                        if (string.IsNullOrWhiteSpace(branchName))
                        {
                            branchName = "ParallelBranch_" + (idx + 1);
                        }

                        var branchScope = new LogicAppAction
                        {
                            Name = SafeName(branchName),
                            Type = "Scope",
                            Details = "ParallelBranch",
                            Sequence = branch.Sequence,
                            IsBranchContainer = true
                        };

                        foreach (var child in branch.Children.OrderBy(c => c.Sequence))
                        {
                            if (child is ReceiveShapeModel r && r.Activate) continue;
                            var mapped = ConvertShape(child);
                            if (mapped != null) branchScope.Children.Add(mapped);
                        }

                        action.Children.Add(branchScope);
                    }
                    else
                    {
                        // ✅ FIX: Non-ParallelBranch children (e.g., Group, Scope) need to be converted AND their children processed
                        // Previous code only converted the wrapper shape but didn't recurse into children
                        
                        var branchScope = new LogicAppAction
                        {
                            Name = SafeName(branch.Name ?? ("ParallelBranch_" + (idx + 1))),
                            Type = "Scope",
                            Details = "ParallelBranch (from " + branch.ShapeType + ")",
                            Sequence = branch.Sequence,
                            IsBranchContainer = true
                        };

                        // ✅ CRITICAL FIX: Process ALL children of the non-ParallelBranch shape
                        // This handles Group shapes that contain Decide/Listen/etc.
                        foreach (var child in branch.Children.OrderBy(c => c.Sequence))
                        {
                            if (child is ReceiveShapeModel r && r.Activate) continue;
                            var mapped = ConvertShape(child);
                            if (mapped != null) branchScope.Children.Add(mapped);
                        }

                        action.Children.Add(branchScope);
                    }
                    idx++;
                }
                return action;
            }

            if (shape is ListenShapeModel listen)
            {
                action = new LogicAppAction
                {
                    Name = SafeName(listen.Name ?? "Listen"),
                    Type = "ParallelContainer",
                    Details = "Listen - first branch to complete wins",
                    Sequence = listen.Sequence
                };

                int branchIdx = 0;
                foreach (var branch in listen.Children.OrderBy(c => c.Sequence))
                {
                    var branchScope = new LogicAppAction
                    {
                        Name = "ListenBranch" + (++branchIdx),
                        Type = "Scope",
                        Details = "Listen branch " + branchIdx,
                        Sequence = branch.Sequence,
                        IsBranchContainer = true
                    };

                    // ✅ FIX: Handle Task and ListenBranch the same way - unwrap and process children
                    if (branch.ShapeType == "Task" || branch.ShapeType == "ListenBranch")
                    {
                        // Wrapper container - process all children inside (don't convert the wrapper itself)
                        foreach (var child in branch.Children.OrderBy(c => c.Sequence))
                        {
                            if (child is ReceiveShapeModel r && r.Activate) continue;
                            var mapped = ConvertShape(child);
                            if (mapped != null) branchScope.Children.Add(mapped);
                        }
                    }
                    else
                    {
                        // Direct shape (not a wrapper) - convert it and its children
                        if (!(branch is ReceiveShapeModel r && r.Activate))
                        {
                            var mapped = ConvertShape(branch);
                            if (mapped != null) branchScope.Children.Add(mapped);
                        }
                        
                        // Process any subsequent shapes in the branch
                        foreach (var sibling in branch.Children.OrderBy(c => c.Sequence))
                        {
                            if (sibling is ReceiveShapeModel r2 && r2.Activate) continue;
                            var mappedSibling = ConvertShape(sibling);
                            if (mappedSibling != null) branchScope.Children.Add(mappedSibling);
                        }
                    }

                    action.Children.Add(branchScope);
                }

                return action;
            }

            // Handle Decide/If shape
            if (shape is DecideShapeModel decide)
            {
                // Detect unmigrateable exception type-checking expressions
                // Logic Apps doesn't support typeof() comparisons or exception introspection
                bool isExceptionTypeCheck = !string.IsNullOrWhiteSpace(decide.Expression) &&
                    (decide.Expression.Contains("typeof(") || 
                     decide.Expression.Contains(".GetType()") &&
                     (decide.Expression.Contains("Exception") || decide.Expression.Contains("exception")));

                if (isExceptionTypeCheck)
                {
                    // Cannot migrate exception type checking - convert to Compose with migration comment
                    var composeAction = new LogicAppAction
                    {
                        Type = "Compose",
                        Name = SafeName(decide.Name + "_" + decide.UniqueId.Substring(0, 8)),
                        Details = "// BizTalk exception type checking cannot be migrated to Logic Apps.\n" +
                                  "// Original expression: " + decide.Expression + "\n" +
                                  "// Logic Apps Scope actions cannot introspect exception types at runtime.\n" +
                                  "// Manual implementation required: Use runAfter conditions or status checks.",
                        Sequence = decide.Sequence
                    };
                    
                    // Process children from both branches as sequential actions (no type filtering)
                    foreach (var trueShape in decide.TrueBranch)
                    {
                        if (string.IsNullOrEmpty(trueShape.UniqueId))
                        {
                            trueShape.UniqueId = Guid.NewGuid().ToString("N").Substring(0, 8);
                        }
                        
                        var mapped = ConvertShape(trueShape);
                        if (mapped != null)
                        {
                            if (!mapped.Name.Contains(trueShape.UniqueId.Substring(0, 8)))
                            {
                                mapped.Name = SafeName(mapped.Name + "_" + trueShape.UniqueId.Substring(0, 8));
                            }
                            // Add as child instead of true branch
                            composeAction.Children.Add(mapped);
                        }
                    }
                    
                    return composeAction;
                }

                var ifAction = new LogicAppAction
                {
                    Type = "If",
                    Name = SafeName(decide.Name + "_" + decide.UniqueId.Substring(0, 8)),
                    Details = decide.Expression,
                    Sequence = decide.Sequence
                };

                // Process TrueBranch shapes
                foreach (var trueShape in decide.TrueBranch)
                {
                    if (string.IsNullOrEmpty(trueShape.UniqueId))
                    {
                        trueShape.UniqueId = Guid.NewGuid().ToString("N").Substring(0, 8);
                    }

                    var mapped = ConvertShape(trueShape);
                    if (mapped != null)
                    {
                        if (!mapped.Name.Contains(trueShape.UniqueId.Substring(0, 8)))
                        {
                            mapped.Name = SafeName(mapped.Name + "_" + trueShape.UniqueId.Substring(0, 8));
                        }
                        ifAction.TrueBranch.Add(mapped);
                    }
                }

                // Process FalseBranch shapes
                foreach (var falseShape in decide.FalseBranch)
                {
                    if (string.IsNullOrEmpty(falseShape.UniqueId))
                    {
                        falseShape.UniqueId = Guid.NewGuid().ToString("N").Substring(0, 8);
                    }

                    var mapped = ConvertShape(falseShape);
                    if (mapped != null)
                    {
                        if (!mapped.Name.Contains(falseShape.UniqueId.Substring(0, 8)))
                        {
                            mapped.Name = SafeName(mapped.Name + "_" + falseShape.UniqueId.Substring(0, 8));
                        }
                        ifAction.FalseBranch.Add(mapped);
                    }
                }

                return ifAction;
            }

            switch (shape.ShapeType)
            {
                case "MessageAssignment":
                    var msgAssignShape = shape as MessageAssignmentShapeModel;

                    // Check if this is inside a Construct block
                    if (shape.Parent != null && shape.Parent.ShapeType == "Construct")
                    {
                        // Will be handled by Construct block processing
                        return null;
                    }
                    else
                    {
                        // Standalone message assignment - convert to Compose with warning
                        action = Simple(shape, "Compose", msgAssignShape?.Expression ?? "Message assignment");
                    }
                    break;

                case "VariableAssignment":
                    action = Simple(shape, "Compose", ((VariableAssignmentShapeModel)shape).Expression);
                    break;

                case "Construct":
                    var constructShape = shape as ConstructShapeModel;

                    if (constructShape != null && constructShape.InnerShapes.Count == 1)
                    {
                        var innerShape = constructShape.InnerShapes[0];
                        action = ConvertShape(innerShape);

                        if (action != null)
                        {
                            var uniqueSuffix = !string.IsNullOrEmpty(shape.UniqueId)
                                ? shape.UniqueId.Substring(0, 8)
                                : shape.Sequence.ToString();
                            action.Name = SafeName(shape.Name + "_" + uniqueSuffix);
                            action.Sequence = shape.Sequence;
                        }
                        return action;
                    }
                    else if (constructShape != null && constructShape.InnerShapes.Count > 1)
                    {
                        var uniqueSuffix = !string.IsNullOrEmpty(shape.UniqueId)
                            ? shape.UniqueId.Substring(0, 8)
                            : shape.Sequence.ToString();

                        // Separate transforms and message assignments
                        var transforms = constructShape.InnerShapes
                            .Where(s => s.ShapeType == "Transform")
                            .ToList();

                        var messageAssignments = constructShape.InnerShapes
                            .Where(s => s.ShapeType == "MessageAssignment")
                            .Cast<MessageAssignmentShapeModel>()
                            .ToList();

                        // If there's exactly one transform and message assignments, merge them
                        if (transforms.Count == 1 && messageAssignments.Count > 0)
                        {
                            action = ConvertShape(transforms[0]);

                            if (action != null)
                            {
                                action.Name = SafeName(shape.Name + "_" + uniqueSuffix);
                                action.Sequence = shape.Sequence;

                                // Collect all message property assignments
                                foreach (var msgAssign in messageAssignments)
                                {
                                    var assignments = ParseMessagePropertyAssignments(msgAssign.Expression);
                                    foreach (var kvp in assignments)
                                    {
                                        action.MessagePropertyAssignments[kvp.Key] = kvp.Value;
                                    }
                                }
                            }

                            return action;
                        }
                        // Otherwise, create a scope with all inner shapes
                        else
                        {
                            action = new LogicAppAction
                            {
                                Name = SafeName((shape.Name ?? "Construct") + "_" + uniqueSuffix),
                                Type = "Scope",
                                Details = "Construct Message",
                                Sequence = shape.Sequence
                            };

                            // Process transforms first
                            foreach (var transform in transforms)
                            {
                                var innerAction = ConvertShape(transform);
                                if (innerAction != null) action.Children.Add(innerAction);
                            }

                            // Process message assignments as Compose actions with property assignments
                            foreach (var msgAssign in messageAssignments)
                            {
                                var assignments = ParseMessagePropertyAssignments(msgAssign.Expression);

                                if (assignments.Count > 0)
                                {
                                    var composeAction = new LogicAppAction
                                    {
                                        Name = SafeName(msgAssign.Name),
                                        Type = "Compose",
                                        Details = "Set message properties",
                                        Sequence = msgAssign.Sequence,
                                        MessagePropertyAssignments = new Dictionary<string, string>(assignments)
                                    };
                                    action.Children.Add(composeAction);
                                }
                                else
                                {
                                    // Fallback for complex expressions
                                    var innerAction = ConvertShape(msgAssign);
                                    if (innerAction != null) action.Children.Add(innerAction);
                                }
                            }

                            return action;
                        }
                    }
                    else
                    {
                        action = Simple(shape, "Compose", "Construct Message");
                    }
                    break;

                case "Transform":
                    var transformShape = shape as TransformShapeModel;
                    action = new LogicAppAction
                    {
                        Name = SafeName(shape.Name),
                        Type = "Xslt",
                        Details = transformShape?.ClassName,
                        TransformClassName = transformShape?.ClassName,  // Store for JSON generator
                        Sequence = shape.Sequence
                    };
                    break;

                case "XmlParse":
                case "ParseXml":
                    action = new LogicAppAction
                    {
                        Name = SafeName(shape.Name ?? "Parse_XML"),
                        Type = "XmlParse",
                        Details = "Parse XML with schema validation - Schema: {{SCHEMA_NAME}}",
                        Sequence = shape.Sequence
                    };
                    break;

                case "XmlCompose":
                case "ComposeXml":
                    action = new LogicAppAction
                    {
                        Name = SafeName(shape.Name ?? "Compose_XML"),
                        Type = "XmlCompose",
                        Details = "Compose XML with schema - Schema: {{SCHEMA_NAME}}",
                        Sequence = shape.Sequence
                    };
                    break;

                case "XmlValidation":
                case "ValidateXml":
                    action = new LogicAppAction
                    {
                        Name = SafeName(shape.Name ?? "Validate_XML"),
                        Type = "XmlValidation",
                        Details = "Validate XML against schema - Schema: {{SCHEMA_NAME}}",
                        Sequence = shape.Sequence
                    };
                    break;

                case "FlatFileDecoding":
                case "DecodeFlatFile":
                    action = new LogicAppAction
                    {
                        Name = SafeName(shape.Name ?? "Decode_Flat_File"),
                        Type = "FlatFileDecoding",
                        Details = "Decode flat file to XML - Schema: {{FLAT_FILE_SCHEMA_NAME}}",
                        Sequence = shape.Sequence
                    };
                    break;

                case "FlatFileEncoding":
                case "EncodeFlatFile":
                    action = new LogicAppAction
                    {
                        Name = SafeName(shape.Name ?? "Encode_Flat_File"),
                        Type = "FlatFileEncoding",
                        Details = "Encode XML to flat file - Schema: {{FLAT_FILE_SCHEMA_NAME}}",
                        Sequence = shape.Sequence
                    };
                    break;

                case "SwiftMTDecode":
                case "DecodeSwift":
                    action = new LogicAppAction
                    {
                        Name = SafeName(shape.Name ?? "Decode_SWIFT_MT"),
                        Type = "SwiftMTDecode",
                        Details = "Decode SWIFT MT message - Validation: Enable",
                        Sequence = shape.Sequence
                    };
                    break;

                case "SwiftMTEncode":
                case "EncodeSwift":
                    action = new LogicAppAction
                    {
                        Name = SafeName(shape.Name ?? "Encode_SWIFT_MT"),
                        Type = "SwiftMTEncode",
                        Details = "Encode SWIFT MT message - Validation: Enable",
                        Sequence = shape.Sequence
                    };
                    break;

                case "Loop":
                case "ForEach":
                    var loopShape = shape as LoopShapeModel;
                    action = new LogicAppAction
                    {
                        Name = SafeName(shape.Name ?? "ForEach"),
                        Type = "Foreach",
                        Details = loopShape?.CollectionExpression ?? "@triggerBody()?['items']",
                        Sequence = shape.Sequence
                    };

                    foreach (var child in shape.Children.OrderBy(c => c.Sequence))
                    {
                        if (child is ReceiveShapeModel r && r.Activate) continue;
                        var mapped = ConvertShape(child);
                        if (mapped != null) action.Children.Add(mapped);
                    }
                    break;

                case "Catch":
                case "CatchException":
                    var catchName = shape.Name;
                    if (string.IsNullOrWhiteSpace(catchName))
                    {
                        catchName = "CatchException_" + shape.Sequence;
                    }

                    action = new LogicAppAction
                    {
                        Name = SafeName(catchName),
                        Type = "Scope",
                        Details = "Exception handler",
                        Sequence = shape.Sequence
                    };

                    // Use shape.Children instead of catchShape.ExceptionHandlers
                    foreach (var child in shape.Children.OrderBy(c => c.Sequence))
                    {
                        if (child is ReceiveShapeModel r && r.Activate) continue;
                        var mapped = ConvertShape(child);
                        if (mapped != null) action.Children.Add(mapped);
                    }

                    return action;

                case "Send":
                    var sendShape = shape as SendShapeModel;
                    action = new LogicAppAction
                    {
                        Name = SafeName(shape.Name ?? "Send"),
                        Type = "SendConnector",
                        ConnectorKind = "Http",
                        Details = "Send to " + (sendShape?.PortName ?? "Port"),
                        Sequence = shape.Sequence
                    };
                    break;

                case "While":
                    var whileExpr = ((WhileShapeModel)shape).Expression;
                    var invertedExpr = InvertCondition(whileExpr);
                    action = new LogicAppAction
                    {
                        Name = "Until",
                        Type = "Until",
                        Details = invertedExpr,
                        LoopThreshold = ExtractThreshold(whileExpr),
                        Sequence = shape.Sequence
                    };
                    break;

                case "Terminate":
                case "Suspend":
                case "Throw":
                    action = Simple(shape, "Terminate", (shape as TerminateShapeModel)?.ErrorMessage ?? "Terminated");
                    break;

                case "Delay":
                    var delayShape = shape as DelayShapeModel;
                    action = new LogicAppAction
                    {
                        Name = SafeName(shape.Name ?? "Delay"),
                        Type = "Delay",
                        Details = delayShape?.DelayExpression ?? "PT1M",
                        Sequence = shape.Sequence
                    };
                    break;

                case "VariableDeclaration":
                    var varDeclShape = shape as VariableDeclarationShapeModel;
                    action = new LogicAppAction
                    {
                        Name = SafeName(shape.Name ?? "Variable"),
                        Type = "InitializeVariable",
                        Details = varDeclShape?.VarType ?? "string",
                        Sequence = shape.Sequence
                    };
                    break;

                case "Until":
                    var untilExpr = ((UntilShapeModel)shape).Expression;
                    action = new LogicAppAction
                    {
                        Name = "Until",
                        Type = "Until",
                        Details = untilExpr,
                        LoopThreshold = ExtractThreshold(untilExpr),
                        Sequence = shape.Sequence
                    };
                    break;

                case "Call":
                    var callShape = shape as CallShapeModel;
                    var invokee = callShape?.Invokee ?? "ChildWorkflow";
                    
                    // Check for self-recursion (workflow calling itself)
                    // Convert to loop pattern instead of nested workflow call
                    if (IsSelfRecursiveCall(invokee))
                    {
                        action = new LogicAppAction
                        {
                            Name = SafeName(shape.Name ?? "RetryLoop"),
                            Type = "Until",
                            Details = "@equals(variables('retryComplete'), true)",
                            Sequence = shape.Sequence
                        };
                        
                        // Add comment about conversion
                        action.Details = "// WARNING: Self-recursive call detected. Converted to Until loop.\r\n// Original call: " + invokee + "\r\n" + action.Details;
                    }
                    else
                    {
                        action = Simple(shape, "Workflow", invokee);
                    }
                    break;

                case "Task":
                    var taskName = shape.Name;
                    if (string.IsNullOrWhiteSpace(taskName))
                    {
                        taskName = "TaskScope_" + shape.Sequence;
                    }

                    action = new LogicAppAction
                    {
                        Name = SafeName(taskName),
                        Type = "Scope",
                        Details = "Sequential task",
                        Sequence = shape.Sequence,
                        IsBranchContainer = true  // ✅ Mark as branch container for flattening
                    };

                    foreach (var child in shape.Children.OrderBy(c => c.Sequence))
                    {
                        if (child is ReceiveShapeModel r && r.Activate) continue;
                        var mapped = ConvertShape(child);
                        if (mapped != null) action.Children.Add(mapped);
                    }

                    return action;

                case "Group":
                    // ✅ NEW: Handle Group shapes (logical organization in BizTalk Designer)
                    // Groups don't translate to Logic Apps actions - they're visual containers
                    // Flatten their children into the parent sequence
                    var groupName = shape.Name;
                    if (string.IsNullOrWhiteSpace(groupName))
                    {
                        groupName = "Group_" + shape.Sequence;
                    }

                    action = new LogicAppAction
                    {
                        Name = SafeName(groupName),
                        Type = "Scope",
                        Details = "Group (logical container)",
                        Sequence = shape.Sequence,
                        IsBranchContainer = true  // ✅ Mark as branch container for flattening
                    };

                    // Process all children - Groups can contain ANY shape (Decide, Listen, Parallel, etc.)
                    foreach (var child in shape.Children.OrderBy(c => c.Sequence))
                    {
                        if (child is ReceiveShapeModel r && r.Activate) continue;
                        var mapped = ConvertShape(child);
                        if (mapped != null) action.Children.Add(mapped);
                    }

                    return action;

                case "ListenBranch":
                    // ✅ NEW: Handle ListenBranch shapes (children of Listen shapes)
                    // These should be treated as branch containers that get flattened
                    // Similar to Task and ParallelBranch handling
                    var listenBranchName = shape.Name;
                    if (string.IsNullOrWhiteSpace(listenBranchName))
                    {
                        listenBranchName = "ListenBranch_" + shape.Sequence;
                    }

                    action = new LogicAppAction
                    {
                        Name = SafeName(listenBranchName),
                        Type = "Scope",
                        Details = "Listen branch",
                        Sequence = shape.Sequence,
                        IsBranchContainer = true  // ✅ CRITICAL: Mark as branch container for flattening
                    };

                    // Process all children shapes in the branch
                    foreach (var child in shape.Children.OrderBy(c => c.Sequence))
                    {
                        if (child is ReceiveShapeModel r && r.Activate) continue;
                        var mapped = ConvertShape(child);
                        if (mapped != null) action.Children.Add(mapped);
                    }

                    return action;

                case "Start":
                case "StartOrchestration":
                case "Exec":
                    var startShape = shape as StartShapeModel;
                    var startInvokee = startShape?.Invokee ?? "ChildWorkflow";
                    
                    // Check for self-recursion (workflow calling itself)
                    if (IsSelfRecursiveCall(startInvokee))
                    {
                        action = new LogicAppAction
                        {
                            Name = SafeName(shape.Name ?? "RetryLoop"),
                            Type = "Until",
                            Details = "@equals(variables('retryComplete'), true)",
                            Sequence = shape.Sequence
                        };
                        
                        // Add comment about conversion
                        action.Details = "// WARNING: Self-recursive Start detected. Converted to Until loop.\r\n// Original Start: " + startInvokee + "\r\n" + action.Details;
                    }
                    else
                    {
                        action = Simple(shape, "Workflow", startInvokee);
                    }
                    break;

                case "CallRules":
                case "CallPolicy":
                    action = Simple(shape, "RuleExecute", (shape as CallRulesShapeModel)?.PolicyName ?? "Ruleset");
                    break;

                case "Scope":
                case "Compensation":
                case "AtomicTransaction":
                case "LongRunningTransaction":
                    var scopeName = shape.Name;
                    if (string.IsNullOrWhiteSpace(scopeName))
                    {
                        var prefix = shape.ShapeType == "AtomicTransaction" ? "AtomicTxn" :
                                     shape.ShapeType == "LongRunningTransaction" ? "LongRunningTxn" :
                                     shape.ShapeType == "Compensation" ? "Compensation" :
                                     "Scope";
                        scopeName = prefix + "_" + shape.Sequence;
                    }

                    action = new LogicAppAction
                    {
                        Name = SafeName(scopeName),
                        Type = "Scope",
                        Details = shape.ShapeType,
                        Sequence = shape.Sequence
                    };

                    var hasCatchBlocks = shape.Children.Any(c =>
                        c.ShapeType != null && (
                            c.ShapeType.Equals("Catch", StringComparison.OrdinalIgnoreCase) ||
                            c.ShapeType.Equals("CatchException", StringComparison.OrdinalIgnoreCase)));

                    if (hasCatchBlocks)
                    {
                        action.Details = "Scope with exception handling";
                    }

                    if (shape.ShapeType == "AtomicTransaction" || shape.ShapeType == "LongRunningTransaction")
                    {
                        action.Details += " - Transaction";
                    }

                    // Identify and separate compensation blocks from regular children
                    // C# 7.3 compatible - removed LINQ where it caused issues
                    var compensationBlocks = new List<ShapeModel>();
                    foreach (var c in shape.Children)
                    {
                        if (c.ShapeType != null &&
                            (c.ShapeType.Equals("Compensation", StringComparison.OrdinalIgnoreCase) ||
                             c.ShapeType.Equals("Compensate", StringComparison.OrdinalIgnoreCase) ||
                             // Named compensation patterns (check Name property only)
                             (c.Name != null && (
                                 c.Name.IndexOf("Undo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 c.Name.IndexOf("Compensat", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 c.Name.IndexOf("Rollback", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 c.Name.IndexOf("Revert", StringComparison.OrdinalIgnoreCase) >= 0
                             ))
                        ))
                        {
                            compensationBlocks.Add(c);
                        }
                    }

                    // Process compensation blocks as nested scopes with special naming
                    foreach (var compBlock in compensationBlocks)
                    {
                        var compAction = new LogicAppAction
                        {
                            Name = SafeName(compBlock.Name ?? "CompensationBlock"),
                            Type = "Scope",
                            Details = "Compensation logic",
                            Sequence = compBlock.Sequence
                        };

                        // Process children of compensation block
                        foreach (var compChild in compBlock.Children.OrderBy(c => c.Sequence))
                        {
                            if (compChild is ReceiveShapeModel r && r.Activate) continue;
                            var mappedComp = ConvertShape(compChild);
                            if (mappedComp != null) 
                            {
                                compAction.Children.Add(mappedComp);
                            }
                        }

                        action.Children.Add(compAction);
                    }

                    // ✅ FIX: Process normal children (including Parallel shapes) BEFORE break
                    // This ensures Parallel shapes inside Scopes are properly converted
                    // Previously, this logic was in the fallback code at lines ~2050-2100,
                    // but that code only ran if Children.Count == 0, which was false after
                    // compensation blocks were added, causing Parallel shapes to be skipped.
                    var normalChildren = shape.Children
                        .Where(c => !(c is CatchShapeModel) &&
                                   !(c is VariableDeclarationShapeModel) &&
                                   !c.ShapeType.Equals("Catch", StringComparison.OrdinalIgnoreCase) &&
                                   !c.ShapeType.Equals("CatchException", StringComparison.OrdinalIgnoreCase) &&
                                   !c.ShapeType.Equals("Compensation", StringComparison.OrdinalIgnoreCase) &&
                                   !c.ShapeType.Equals("Compensate", StringComparison.OrdinalIgnoreCase) &&
                                   // Only filter empty transaction markers, keep scopes with children
                                   !(c.ShapeType.Equals("LongRunningTransaction", StringComparison.OrdinalIgnoreCase) && c.Children.Count == 0) &&
                                   !(c.ShapeType.Equals("AtomicTransaction", StringComparison.OrdinalIgnoreCase) && c.Children.Count == 0))
                        .OrderBy(c => c.Sequence);

                    foreach (var child in normalChildren)
                    {
                        if (child is ReceiveShapeModel r && r.Activate) continue;
                        var mapped = ConvertShape(child);
                        if (mapped != null) action.Children.Add(mapped);
                    }

                    // Process catch blocks last
                    var catchBlocks = shape.Children
                        .Where(c => c is CatchShapeModel ||
                                   c.ShapeType.Equals("Catch", StringComparison.OrdinalIgnoreCase) ||
                                   c.ShapeType.Equals("CatchException", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(c => c.Sequence);

                    foreach (var catchBlock in catchBlocks)
                    {
                        var mapped = ConvertShape(catchBlock);
                        if (mapped != null) action.Children.Add(mapped);
                    }

                    break;

                case "Compensate":
                    action = Simple(shape, "Compose", "Compensate: " + ((CompensateShapeModel)shape).Target);
                    break;

                case "Receive":
                    var receiveShape = shape as ReceiveShapeModel;
                    
                    // Check if this is a non-activating receive (correlation pattern)
                    if (receiveShape != null && !receiveShape.Activate)
                    {
                        // Create HTTP Response action for request-response patterns
                        // Common in Listen shapes (timeout vs response) and correlation scenarios
                        action = new LogicAppAction
                        {
                            Name = SafeName(shape.Name ?? "Receive_Response"),
                            Type = "Response",
                            ConnectorKind = "Http",
                            Details = "Receive response from " + (receiveShape.PortName ?? "correlated port") + 
                                      " (BizTalk correlation pattern - requires manual configuration)",
                            Sequence = shape.Sequence
                        };
                    }
                    else
                    {
                        // Fallback for edge cases (shouldn't normally reach here)
                        action = Simple(shape, "Compose", "Receive (non-activating)");
                    }
                    break;
            }

            // Handle ALL Expression shapes (not just those with "if" in name)
            if (action == null && shape.ShapeType.Equals("Expression", StringComparison.OrdinalIgnoreCase))
            {
                var exprShape = shape as ExpressionShapeModel;
                var expression = exprShape?.Expression ?? "";

                // If it looks like a condition, treat as "If", otherwise as "Compose"
                if (shape.Name.ToLowerInvariant().Contains("if") ||
                    shape.Name.Contains("?") ||
                    expression.Contains("==") ||
                    expression.Contains("!="))
                {
                    action = Simple(shape, "If", expression);
                }
                else
                {
                    action = Simple(shape, "Compose", expression);
                }
            }

            if (action != null && (action.Type == "Scope" || action.Type == "Until" || action.Type == "Foreach"))
            {
                if (action.Children.Count == 0)
                {
                    var normalChildren = shape.Children
                        .Where(c => !(c is CatchShapeModel) &&
                                   !(c is VariableDeclarationShapeModel) &&
                                   !c.ShapeType.Equals("Catch", StringComparison.OrdinalIgnoreCase) &&
                                   !c.ShapeType.Equals("CatchException", StringComparison.OrdinalIgnoreCase) &&
                                   !c.ShapeType.Equals("Compensation", StringComparison.OrdinalIgnoreCase) &&
                                   // Only filter empty transaction markers, keep scopes with children
                                   !(c.ShapeType.Equals("LongRunningTransaction", StringComparison.OrdinalIgnoreCase) && c.Children.Count == 0) &&
                                   !(c.ShapeType.Equals("AtomicTransaction", StringComparison.OrdinalIgnoreCase) && c.Children.Count == 0))
                        .OrderBy(c => c.Sequence);

                    var catchBlocks = shape.Children
                        .Where(c => c is CatchShapeModel ||
                                   c.ShapeType.Equals("Catch", StringComparison.OrdinalIgnoreCase) ||
                                   c.ShapeType.Equals("CatchException", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(c => c.Sequence);

                    var compensationBlocks = shape.Children
                        .Where(c => c.ShapeType != null && c.ShapeType.Equals("Compensation", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(c => c.Sequence);

                    foreach (var child in normalChildren)
                    {
                        if (child is ReceiveShapeModel r && r.Activate) continue;
                        var mapped = ConvertShape(child);
                        if (mapped != null) action.Children.Add(mapped);
                    }

                    foreach (var catchBlock in catchBlocks)
                    {
                        var mapped = ConvertShape(catchBlock);
                        if (mapped != null) action.Children.Add(mapped);
                    }

                    // Process compensation blocks properly
                    foreach (var compBlock in compensationBlocks)
                    {
                        var compScope = new LogicAppAction
                        {
                            Name = SafeName(compBlock.Name ?? "Compensation_" + compBlock.Sequence),
                            Type = "Scope",
                            Details = "Compensation logic",
                            Sequence = compBlock.Sequence
                        };

                        // Recursively process compensation children
                        foreach (var compChild in compBlock.Children.OrderBy(c => c.Sequence))
                        {
                            if (compChild is ReceiveShapeModel r && r.Activate) continue;

                            var mapped = ConvertShape(compChild);
                            if (mapped != null) 
                            {
                                compScope.Children.Add(mapped);
                            }
                        }

                        // Only add if there are actual actions
                        if (compScope.Children.Count > 0)
                        {
                            action.Children.Add(compScope);
                        }
                        else
                        {
                            // Add placeholder note
                            var compensationNote = new LogicAppAction
                            {
                                Name = SafeName("CompensationNote_" + compBlock.Sequence),
                                Type = "Compose",
                                Details = "// Note: BizTalk compensation logic requires manual implementation in Logic Apps",
                                Sequence = compBlock.Sequence
                            };
                            action.Children.Add(compensationNote);
                        }
                    }
                }
            }

            // Catch-all: Handle unhandled shapes as Compose
            if (action == null)
            {
                action = new LogicAppAction
                {
                    Name = SafeName(shape.Name),
                    Type = "Compose",
                    Details = $"// Unhandled shape type: {shape.ShapeType}",
                    Sequence = shape.Sequence
                };
            }

            return action;
        }

        /// <summary>
        /// Creates a simple Logic Apps action from a BizTalk shape with the specified type and details.
        /// </summary>
        /// <param name="shape">The source BizTalk shape.</param>
        /// <param name="type">The Logic Apps action type (e.g., "Compose", "Terminate").</param>
        /// <param name="details">Additional details or expression for the action.</param>
        /// <returns>A basic LogicAppAction with name, type, details, and sequence.</returns>
        private static LogicAppAction Simple(ShapeModel shape, string type, string details) =>
            new LogicAppAction
            {
                Name = SafeName(shape.Name),
                Type = type,
                Details = details,
                Sequence = shape.Sequence
            };

        /// <summary>
        /// Checks if an invokee (Call/Start target) refers to the current orchestration being processed.
        /// This detects self-recursive workflow calls, which are allowed in BizTalk but not supported in Azure Logic Apps.
        /// Compares against both simple name and fully-qualified name to handle different call patterns.
        /// Thread-safe: Uses ThreadLocal storage to access current orchestration context.
        /// </summary>
        /// <param name="invokee">The target orchestration name from Call/Start shape.</param>
        /// <returns>True if the invokee matches the current orchestration (self-recursion detected); false otherwise.</returns>
        private static bool IsSelfRecursiveCall(string invokee)
        {
            if (string.IsNullOrEmpty(invokee))
                return false;

            if (string.IsNullOrEmpty(_currentOrchestrationName.Value) && string.IsNullOrEmpty(_currentOrchestrationFullName.Value))
                return false;

            // Compare with simple name (case-insensitive)
            if (!string.IsNullOrEmpty(_currentOrchestrationName.Value) &&
                invokee.Equals(_currentOrchestrationName.Value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Compare with fully-qualified name (case-insensitive)
            if (!string.IsNullOrEmpty(_currentOrchestrationFullName.Value) &&
                invokee.Equals(_currentOrchestrationFullName.Value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Check if invokee is the last segment of fully-qualified name
            // (e.g., "Reprocesamiento" matches "Sat.Scade.PagosCore.Procesos.Reprocesamiento")
            if (!string.IsNullOrEmpty(_currentOrchestrationFullName.Value) &&
                _currentOrchestrationFullName.Value.EndsWith("." + invokee, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Check if current orchestration simple name is the last segment of invokee
            // (e.g., current="Reprocesamiento", invokee="Sat.Scade.PagosCore.Procesos.Reprocesamiento")
            if (!string.IsNullOrEmpty(_currentOrchestrationName.Value) &&
                invokee.EndsWith("." + _currentOrchestrationName.Value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Sanitizes a name to make it valid for Logic Apps action names.
        /// Removes invalid characters and ensures the name doesn't start with a digit.
        /// </summary>
        /// <param name="name">The original name to sanitize.</param>
        /// <returns>A sanitized name safe for use in Logic Apps workflows.</returns>
        private static string SafeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Unnamed";

            // Allow more characters: letters, digits, underscore, hyphen, space
            var cleaned = new string(name.Where(c =>
                char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ' ').ToArray());

            if (cleaned.Length == 0) return "Item";

            // Replace spaces with underscores
            cleaned = cleaned.Replace(' ', '_');

            if (char.IsDigit(cleaned[0]))
            {
                cleaned = "Action_" + cleaned;
            }

            return cleaned;
        }

        /// <summary>
        /// Calculates the next available sequence number for actions in the workflow.
        /// </summary>
        /// <param name="map">The workflow map to analyze.</param>
        /// <returns>The next sequence number (max existing sequence + 1, or 0 if no actions).</returns>
        private static int NextSeq(LogicAppWorkflowMap map) =>
            map.Actions.Count == 0 ? 0 : map.Actions.Max(a => a.Sequence) + 1;

        /// <summary>
        /// Extracts a numeric threshold from a While loop expression for Until action configuration.
        /// Parses expressions like "while (counter <= 100)" to extract "100".
        /// </summary>
        /// <param name="expr">The While loop expression.</param>
        /// <returns>The extracted threshold value, or null if not found.</returns>
        private static int? ExtractThreshold(string expr)
        {
            if (string.IsNullOrWhiteSpace(expr)) return null;
            var t = expr.Trim();
            if (t.StartsWith("while", StringComparison.OrdinalIgnoreCase))
            {
                int o = t.IndexOf('(');
                int c = t.LastIndexOf(')');
                if (o >= 0 && c > o) t = t.Substring(o + 1, c - o - 1);
            }
            var m = Regex.Match(t, @"<\s*=?\s*(\d+)");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var v)) return v;
            return null;
        }

        /// <summary>
        /// Inverts a condition expression to convert BizTalk While loops to Logic Apps Until actions.
        /// Logic Apps only has Until (runs UNTIL condition is true), so While (runs WHILE true) requires inversion.
        /// Examples: "&lt;" becomes "&gt;=", "==" becomes "!=", complex expressions wrapped with "!()".
        /// ✅ FIXED: Now strips outer parentheses BEFORE operator inversion to avoid malformed variable names.
        /// </summary>
        /// <param name="expression">The While condition expression to invert.</param>
        /// <returns>The inverted expression suitable for Logic Apps Until action.</returns>
        private static string InvertCondition(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return expression;

            var expr = expression.Trim();

            // ✅ FIX: Strip outer parentheses BEFORE operator inversion
            // BizTalk While expressions like "(TotalNumberOfMessages < 2)" have outer parens
            // If we split on "<" before stripping, we get "(TotalNumberOfMessages" and "2)" - MALFORMED!
            while (expr.StartsWith("(") && expr.EndsWith(")"))
            {
                // Extract inner content
                var innerContent = expr.Substring(1, expr.Length - 2);
                
                // Check if the inner content is balanced (avoid stripping function call parens)
                int openCount = 0;
                bool balanced = true;
                foreach (char c in innerContent)
                {
                    if (c == '(') openCount++;
                    else if (c == ')') openCount--;
                    
                    if (openCount < 0)
                    {
                        balanced = false;
                        break;
                    }
                }
                
                // Only strip if inner content is balanced and complete
                if (balanced && openCount == 0)
                {
                    expr = innerContent.Trim();
                }
                else
                {
                    break;
                }
            }

            // Handle compound conditions by wrapping with NOT
            if (expr.Contains("&&") || expr.Contains("||"))
                return "!(" + expr + ")";

            // Simple inversions for comparison operators
            // Order matters - check longer operators first to avoid partial replacements
            if (expr.Contains("<="))
                return expr.Replace("<=", ">");
            if (expr.Contains(">="))
                return expr.Replace(">=", "<");
            if (expr.Contains("=="))
                return expr.Replace("==", "!=");
            if (expr.Contains("!="))
                return expr.Replace("!=", "==");
            if (expr.Contains("<"))
                return expr.Replace("<", ">=");
            if (expr.Contains(">"))
                return expr.Replace(">", "<=");

            // Fallback: wrap with NOT for complex expressions
            return "!(" + expr + ")";
        }

        /// <summary>
        /// Infers the Logic Apps connector kind from BizTalk transport type and address.
        /// Maps BizTalk adapters (FILE, FTP, SQL, etc.) to corresponding Logic Apps connectors.
        /// ✅ NEW: Supports HostApps transport with subtype detection (CICS, IMS, VSAM).
        /// </summary>
        /// <param name="transport">The BizTalk transport type (e.g., "FILE", "WCF-BasicHttp", "HostApps").</param>
        /// <param name="address">The transport address (e.g., "C:\\Files\\*.xml").</param>
        /// <param name="hostAppsSubType">For HostApps transport, the detected subtype ("Cics", "Ims", "Vsam", "HostFile").</param>
        /// <returns>The corresponding Logic Apps connector kind (e.g., "FileSystem", "Http", "Sql", "Cics").</returns>
        private static string InferKind(string transport, string address, string hostAppsSubType = null)
        {
            var t = (transport ?? "").ToLowerInvariant();
            var a = (address ?? "").ToLowerInvariant();

            // Handle HostApps transport with subtype detection
            if (t.Contains("hostapp"))
            {
                if (!string.IsNullOrEmpty(hostAppsSubType))
                {
                    // Use the detected subtype from AssemblyMappings
                    return hostAppsSubType; // "Cics", "Ims", "Vsam", or "HostFile"
                }
                
                // Fallback: Try to detect from address if subtype not available
                if (a.Contains("cics://")) return "Cics";
                if (a.Contains("ims://")) return "Ims";
                if (a.Contains("vsam://")) return "Vsam";
                
                // Default to HostFile if can't determine
                return "HostFile";
            }

            if (t.Contains("file")) return "FileSystem";
            if (t.Contains("ftp") && !t.Contains("sftp")) return "Ftp";
            if (t.Contains("sftp")) return "Sftp";
            if (t.Contains("sql")) return "Sql";
            if (t.Contains("as2")) return "AS2";
            if (a.Contains("x12")) return "X12";
            if (a.Contains("edifact")) return "Edifact";
            if (t.Contains("mllp") || a.StartsWith("mllp://")) return "Mllp";
            if (t.Contains("sb") || t.Contains("servicebus") || t.Contains("msmq") || t.Contains("netmsmq")) return "ServiceBus";
            if (t.Contains("eventhub")) return "EventHub";
            if (t.Contains("mqseries") || t.Contains("ibmmq") || t.Contains("mq")) return "IbmMq";

            if (t.Contains("db2") || t.Contains("db2oledb") || a.Contains("db2://")) return "Db2";
            if (t.Contains("cics") || a.Contains("cics://")) return "Cics";
            if (t.Contains("ims") || a.Contains("ims://")) return "Ims";
            if (t.Contains("vsam") || a.Contains("vsam://")) return "Vsam";
            if (t.Contains("informix")) return "Informix";
            if (t.Contains("hostfile") || t.Contains("host-files")) return "HostFile";
            if (t.Contains("sap") || a.Contains("sap://")) return "SapEcc";

            if (t.Contains("smtp") || t.Contains("pop3") || a.Contains("gmail.com") || a.Contains("outlook") || a.Contains("office365") || a.Contains("exchange"))
            {
                //if (a.Contains("gmail.com")) return "Smtp";
                //if (a.Contains("exchange")) return "Smtp";
                //if (t.Contains("smtp")) return "Smtp";
                return "Smtp";
            }

            if (t.Contains("wcf") || t.Contains("soap") || t.Contains("webhttp") || t.Contains("rest") || t.Contains("custom"))
                return "Http";

            return "Http";
        }

        /// <summary>
        /// Parses a Service Bus address to extract queue/topic name and subscription information.
        /// Populates the action with IsTopic, QueueOrTopicName, HasSubscription, and SubscriptionName.
        /// </summary>
        /// <param name="action">The Logic Apps action to populate with Service Bus details.</param>
        /// <param name="address">The Service Bus address to parse (e.g., "sb://namespace.servicebus.windows.net/mytopic/subscriptions/mysub").</param>
        private static void PopulateServiceBusParts(LogicAppAction action, string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return;
            var path = address.ToLowerInvariant();
            var segments = address.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            int hostIdx = segments.FindIndex(s => s.EndsWith(".servicebus.windows.net", StringComparison.OrdinalIgnoreCase));
            if (hostIdx >= 0 && hostIdx + 1 < segments.Count)
            {
                var entity = segments[hostIdx + 1];
                action.QueueOrTopicName = entity;
                if (segments.Skip(hostIdx + 2).Any(s => s.Equals("subscriptions", StringComparison.OrdinalIgnoreCase)))
                {
                    action.IsTopic = true;
                    action.HasSubscription = true;
                    int subIdx = segments.FindIndex(hostIdx + 2, s => s.Equals("subscriptions", StringComparison.OrdinalIgnoreCase));
                    if (subIdx >= 0 && subIdx + 1 < segments.Count)
                        action.SubscriptionName = segments[subIdx + 1];
                }
                else
                {
                    action.IsTopic = path.Contains("/topic") || path.Contains("topic=");
                }
            }
        }
    }
}