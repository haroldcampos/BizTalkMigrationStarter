// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BizTalktoLogicApps.ODXtoWFMigrator
{
    /// <summary>
    /// Registry for BizTalk adapter to Logic Apps connector schema mappings.
    /// Provides connector definitions with triggers, actions, and parameters for accurate workflow generation.
    /// </summary>
    /// <remarks>
    /// This registry enables:
    /// - Dynamic connector lookup by BizTalk adapter name (FILE, FTP, SQL, ServiceBus, etc.)
    /// - Extensible connector definitions via JSON configuration files
    /// - Accurate parameter mapping for triggers and actions during ODX to workflow conversion
    /// Used by BizTalkOrchestrationParser and LogicAppJSONGenerator for connector-aware workflow generation.
    /// </remarks>
    public class ConnectorSchemaRegistry
    {
        private Dictionary<string, ConnectorSchema> connectors;

        /// <summary>
        /// Gets the total number of registered connectors in the registry.
        /// </summary>
        public int ConnectorCount => this.connectors?.Count ?? 0;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConnectorSchemaRegistry"/> class with an empty connector dictionary.
        /// </summary>
        public ConnectorSchemaRegistry()
        {
            this.connectors = new Dictionary<string, ConnectorSchema>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Loads the connector registry from a JSON configuration file.
        /// </summary>
        /// <param name="filePath">Path to the connector registry JSON file.</param>
        /// <returns>A ConnectorSchemaRegistry populated with connectors from the JSON file.</returns>
        /// <exception cref="ArgumentNullException">Thrown when filePath is null or empty.</exception>
        /// <exception cref="FileNotFoundException">Thrown when the specified file does not exist.</exception>
        /// <exception cref="JsonException">Thrown when the JSON file is malformed or cannot be parsed.</exception>
        /// <remarks>
        /// <para>
        /// This is the ONLY way to load connectors. There is no fallback CreateDefault() method.
        /// The connector-registry.json file is the single source of truth for all connector definitions.
        /// </para>
        /// <para>Expected JSON structure:</para>
        /// <code>
        /// {
        ///   "Connectors": {
        ///     "FileSystem": {
        ///       "Name": "FileSystem",
        ///       "ServiceProviderId": "/serviceProviders/fileSystem",
        ///       "DisplayName": "File System",
        ///       "Triggers": {
        ///         "whenFilesAreAdded": {
        ///           "OperationId": "whenFilesAreAdded",
        ///           "IsTrigger": true,
        ///           "Kind": "Polling",
        ///           "Parameters": ["folderPath", "fileMask"]
        ///         }
        ///       },
        ///       "Actions": {
        ///         "createFile": {
        ///           "OperationId": "createFile",
        ///           "Parameters": ["filePath", "body"]
        ///         }
        ///       },
        ///       "ConnectionParameters": {
        ///         "RootFolder": {
        ///           "Name": "RootFolder",
        ///           "Type": "StringType",
        ///           "Required": true
        ///         }
        ///       }
        ///     }
        ///   }
        /// }
        /// </code>
        /// <para>
        /// Connectors are stored with case-insensitive keys for flexible lookup.
        /// BizTalk adapter aliases (e.g., FILE, FTP, SQL) are automatically created from connector names.
        /// </para>
        /// </remarks>
        public static ConnectorSchemaRegistry LoadFromFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                throw new ArgumentNullException(nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Connector registry file not found: {filePath}");
            }

            string json = File.ReadAllText(filePath);
            var registry = new ConnectorSchemaRegistry();
            
            var jsonObject = JObject.Parse(json);
            var connectorsSection = jsonObject["Connectors"] as JObject;
            
            if (connectorsSection != null)
            {
                foreach (var connector in connectorsSection.Children<JProperty>())
                {
                    var connectorDef = connector.Value as JObject;
                    if (connectorDef != null)
                    {
                        var schema = new ConnectorSchema
                        {
                            Name = connectorDef["Name"]?.ToString(),
                            ServiceProviderId = connectorDef["ServiceProviderId"]?.ToString(),
                            DisplayName = connectorDef["DisplayName"]?.ToString(),
                            DefaultTrigger = connectorDef["DefaultTrigger"]?.ToString(),
                            DefaultAction = connectorDef["DefaultAction"]?.ToString(),
                            DeploymentScope = connectorDef["DeploymentScope"]?.ToString(),
                            MessagingCategory = connectorDef["MessagingCategory"]?.ToString(),
                            BizTalkAdapters = connectorDef["BizTalkAdapters"]?.ToObject<List<string>>() ?? new List<string>()
                        };

                        // Parse triggers
                        var triggers = connectorDef["Triggers"] as JObject;
                        if (triggers != null)
                        {
                            foreach (var trigger in triggers.Children<JProperty>())
                            {
                                var triggerDef = trigger.Value as JObject;
                                schema.Triggers[trigger.Name] = new OperationSchema
                                {
                                    OperationId = triggerDef["OperationId"]?.ToString(),
                                    IsTrigger = triggerDef["IsTrigger"]?.ToObject<bool>() ?? true,
                                    Kind = triggerDef["Kind"]?.ToString(),
                                    Parameters = ParseParameterSchemas(triggerDef["Parameters"] as JArray)
                                };
                            }
                        }

                        // Parse actions
                        var actions = connectorDef["Actions"] as JObject;
                        if (actions != null)
                        {
                            foreach (var action in actions.Children<JProperty>())
                            {
                                var actionDef = action.Value as JObject;
                                var operationId = actionDef["OperationId"]?.ToString();
                                var inputsTemplate = ParseInputsTemplate(
                                    actionDef["Inputs"] ?? actionDef["inputs"]);
                                var actionType = actionDef["ActionType"]?.ToString()
                                    ?? (inputsTemplate != null ? operationId : null);

                                schema.Actions[action.Name] = new OperationSchema
                                {
                                    OperationId = operationId,
                                    IsTrigger = false,
                                    Parameters = ParseParameterSchemas(actionDef["Parameters"] as JArray),
                                    InputsTemplate = inputsTemplate,
                                    ActionType = actionType
                                };
                            }
                        }

                        // Parse connection parameters
                        var connectionParams = connectorDef["ConnectionParameters"] as JObject;
                        if (connectionParams != null)
                        {
                            foreach (var param in connectionParams.Children<JProperty>())
                            {
                                var paramDef = param.Value as JObject;
                                if (paramDef != null)
                                {
                                    schema.ConnectionParameters[param.Name] = new ConnectionParameter
                                    {
                                        Name = paramDef["Name"]?.ToString(),
                                        Type = paramDef["Type"]?.ToString(),
                                        Required = paramDef["Required"]?.ToObject<bool>() ?? false
                                    };
                                }
                            }
                        }

                        registry.connectors[schema.Name] = schema;

                        // Add common BizTalk adapter aliases for easier lookup
                        AddCommonAliases(registry, schema);
                    }
                }
            }

            return registry;
        }

        /// <summary>
        /// Parses the <c>"Inputs"</c> (or <c>"inputs"</c>) token from an action definition
        /// into a <see cref="JObject"/> inputs template.
        /// <list type="bullet">
        ///   <item>JObject — returned as-is (already a structured template).</item>
        ///   <item>JArray of strings — converted to a JObject whose keys are those strings
        ///         and whose values are empty strings, ready for placeholder resolution.</item>
        ///   <item>Anything else / null — returns null (ServiceProvider architecture).</item>
        /// </list>
        /// </summary>
        private static JObject ParseInputsTemplate(JToken raw)
        {
            if (raw == null)
            {
                return null;
            }

            if (raw.Type == JTokenType.Object)
            {
                return (JObject)raw;
            }

            if (raw.Type == JTokenType.Array)
            {
                var result = new JObject();
                foreach (var item in (JArray)raw)
                {
                    if (item.Type == JTokenType.String)
                    {
                        result[item.ToString()] = string.Empty;
                    }
                }
                return result;
            }

            return null;
        }

        /// <summary>
        /// Parses a JSON parameter array that may contain either plain strings (legacy format)
        /// or objects with Name, ValueSource, and DefaultValue fields (data-driven format).
        /// Both formats may coexist in the same array to allow incremental migration.
        /// </summary>
        /// <param name="rawParams">The raw JArray from the JSON file, or null.</param>
        /// <returns>A list of <see cref="ParameterSchema"/> instances.</returns>
        private static List<ParameterSchema> ParseParameterSchemas(JArray rawParams)
        {
            var result = new List<ParameterSchema>();
            if (rawParams == null)
            {
                return result;
            }

            foreach (var token in rawParams)
            {
                if (token.Type == JTokenType.String)
                {
                    result.Add(new ParameterSchema
                    {
                        Name = token.ToString(),
                        ValueSource = "Literal",
                        DefaultValue = ""
                    });
                }
                else if (token.Type == JTokenType.Object)
                {
                    var obj = (JObject)token;
                    result.Add(new ParameterSchema
                    {
                        Name = obj["Name"]?.ToString(),
                        ValueSource = obj["ValueSource"]?.ToString() ?? "Literal",
                        DefaultValue = obj["DefaultValue"]?.ToString() ?? ""
                    });
                }
            }

            return result;
        }

        /// <summary>
        /// Registers every entry in <see cref="ConnectorSchema.BizTalkAdapters"/> as a
        /// case-insensitive alias that resolves to the same schema instance.
        /// </summary>
        /// <param name="registry">The registry to add aliases to.</param>
        /// <param name="schema">The connector schema whose BizTalkAdapters list supplies the alias names.</param>
        /// <remarks>
        /// Driven entirely by the BizTalkAdapters array in connector-registry.json.
        /// No hardcoded connector names — adding or changing adapters requires only a JSON edit.
        /// </remarks>
        private static void AddCommonAliases(ConnectorSchemaRegistry registry, ConnectorSchema schema)
        {
            if (schema == null || string.IsNullOrEmpty(schema.Name))
            {
                return;
            }

            foreach (var alias in schema.BizTalkAdapters)
            {
                if (!string.IsNullOrEmpty(alias))
                {
                    registry.connectors[alias] = schema;
                }
            }
        }

        /// <summary>
        /// Retrieves a connector schema by name or BizTalk adapter alias.
        /// </summary>
        /// <param name="name">The connector name or BizTalk adapter name (e.g., "FileSystem", "FILE", "FTP").</param>
        /// <returns>The ConnectorSchema if found; otherwise null.</returns>
        /// <remarks>
        /// Lookup is case-insensitive. Supports BizTalk adapter aliases (FILE?FileSystem, FTP?Ftp, SQL?Sql).
        /// Returns null for unknown connector names or null/empty input.
        /// </remarks>
        public ConnectorSchema GetConnector(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            this.connectors.TryGetValue(name, out var connector);
            return connector;
        }

        /// <summary>
        /// Checks whether a connector with the specified name exists in the registry.
        /// </summary>
        /// <param name="name">The connector name or BizTalk adapter name to check.</param>
        /// <returns>True if the connector exists; otherwise false.</returns>
        /// <remarks>
        /// Lookup is case-insensitive. Returns false for null or empty names.
        /// Used to determine whether to use registry-based connector generation or legacy fallback.
        /// </remarks>
        public bool HasConnector(string name)
        {
            return !string.IsNullOrEmpty(name) && this.connectors.ContainsKey(name);
        }

        /// <summary>
        /// Finds an <see cref="OperationSchema"/> whose <see cref="OperationSchema.ActionType"/>
        /// matches <paramref name="actionType"/>. Used by <c>BuildAction</c> to locate the
        /// Inputs-architecture template for built-in action types such as
        /// <c>"XmlParse"</c>, <c>"SwiftMTDecode"</c>, <c>"FlatFileDecoding"</c>, etc.
        /// </summary>
        /// <param name="actionType">The Logic Apps action type string (e.g., <c>"XmlParse"</c>).</param>
        /// <returns>
        /// The matching <see cref="OperationSchema"/> if found; otherwise <c>null</c>.
        /// Only operations that have a non-null <see cref="OperationSchema.InputsTemplate"/> are
        /// considered — ServiceProvider operations that happen to share a name are ignored.
        /// </returns>
        /// <remarks>
        /// Scans every distinct connector (alias duplicates are skipped via a name-based
        /// <see cref="HashSet{T}"/>) and returns the first matching operation.
        /// Comparison is case-insensitive to tolerate minor casing differences.
        /// </remarks>
        public OperationSchema GetOperationByActionType(string actionType)
        {
            if (string.IsNullOrEmpty(actionType))
            {
                return null;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var schema in this.connectors.Values)
            {
                if (string.IsNullOrEmpty(schema.Name) || !seen.Add(schema.Name))
                {
                    continue;
                }

                foreach (var op in schema.Actions.Values)
                {
                    if (op.InputsTemplate != null &&
                        string.Equals(op.ActionType, actionType, StringComparison.OrdinalIgnoreCase))
                    {
                        return op;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Determines whether a connector is compatible with the specified deployment target.
        /// </summary>
        /// <param name="name">The connector name or BizTalk adapter alias.</param>
        /// <param name="deploymentTarget">The deployment target string: "Cloud" or "OnPremises".</param>
        /// <returns>
        /// <c>true</c> when the connector's <see cref="ConnectorSchema.DeploymentScope"/> is "Any",
        /// matches the target, or is unknown/unset. <c>false</c> only when the scope explicitly
        /// excludes the target (e.g., "CloudOnly" with "OnPremises").
        /// </returns>
        /// <remarks>
        /// Accepts a plain string for <paramref name="deploymentTarget"/> so this method has no
        /// dependency on the <c>Refactoring</c> namespace. Callers convert
        /// <c>DeploymentTarget.ToString()</c> before calling.
        /// </remarks>
        public bool IsCompatibleWith(string name, string deploymentTarget)
        {
            var schema = GetConnector(name);
            if (schema == null || string.IsNullOrEmpty(schema.DeploymentScope))
            {
                return true;
            }

            if (string.Equals(schema.DeploymentScope, "Any", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(schema.DeploymentScope, "CloudOnly", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(deploymentTarget, "Cloud", StringComparison.OrdinalIgnoreCase);
            }

            if (string.Equals(schema.DeploymentScope, "OnPremOnly", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(deploymentTarget, "OnPremises", StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }

        /// <summary>
        /// Returns all distinct connectors in the registry, deduplicated by canonical
        /// connector name (excludes BizTalk adapter aliases that resolve to the same schema).
        /// </summary>
        /// <returns>
        /// A read-only list of distinct <see cref="ConnectorSchema"/> instances.
        /// External callers can use this to enumerate every registered connector without
        /// needing to know the internal alias structure.
        /// </returns>
        /// <remarks>
        /// Designed for external tool consumption: an AI agent or workflow generator can
        /// call this method to discover all available connectors, their operation IDs,
        /// parameter schemas, service provider IDs, and deployment constraints.
        /// </remarks>
        public IReadOnlyList<ConnectorSchema> GetAllConnectors()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var results = new List<ConnectorSchema>();

            foreach (var schema in this.connectors.Values)
            {
                if (!string.IsNullOrEmpty(schema.Name) && seen.Add(schema.Name))
                {
                    results.Add(schema);
                }
            }

            return results;
        }

        /// <summary>
        /// Resolves a BizTalk transport type and address to a Logic Apps connector kind
        /// using the registry's <see cref="ConnectorSchema.BizTalkAdapters"/> aliases.
        /// </summary>
        /// <param name="transport">The BizTalk transport type string (e.g., "FILE", "WCF-BasicHttp", "HostApps").</param>
        /// <param name="address">The transport address (e.g., "C:\\Files\\*.xml", "sb://ns.servicebus.windows.net/queue").</param>
        /// <param name="hostAppsSubType">For HostApps transport, the detected subtype ("Cics", "Ims", "Vsam", "HostFile").</param>
        /// <returns>
        /// The canonical <see cref="ConnectorSchema.Name"/> if a matching connector is found;
        /// otherwise <c>null</c>, signalling the caller to fall back to its own heuristics.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Resolution order:
        /// 1. If <paramref name="hostAppsSubType"/> is set and matches a registered connector, return it.
        /// 2. Try an exact match of <paramref name="transport"/> against registered aliases.
        /// 3. Try a case-insensitive substring match of each alias against <paramref name="transport"/>.
        /// 4. Try a case-insensitive substring match of each alias against <paramref name="address"/>.
        /// 5. Return <c>null</c> if no match is found.
        /// </para>
        /// <para>
        /// This method externalises the adapter-to-connector mapping that was previously
        /// hardcoded in <c>LogicAppsMapper.InferKind()</c>. Adding a new adapter requires
        /// only a JSON edit to the <c>BizTalkAdapters</c> array — no C# change needed.
        /// </para>
        /// </remarks>
        public string ResolveConnectorKind(string transport, string address, string hostAppsSubType = null)
        {
            // Priority 1: HostApps subtype direct lookup
            if (!string.IsNullOrEmpty(hostAppsSubType))
            {
                if (this.connectors.ContainsKey(hostAppsSubType))
                {
                    return this.connectors[hostAppsSubType].Name;
                }
            }

            // Priority 2: Exact match on transport string
            if (!string.IsNullOrEmpty(transport))
            {
                if (this.connectors.TryGetValue(transport, out var exact))
                {
                    return exact.Name;
                }

                // Priority 3: Substring match — check if any registered alias appears in the transport
                var lowerTransport = transport.ToLowerInvariant();
                foreach (var kvp in this.connectors)
                {
                    if (lowerTransport.Contains(kvp.Key.ToLowerInvariant()))
                    {
                        return kvp.Value.Name;
                    }
                }
            }

            // Priority 4: Address-based detection (e.g., "oracledb://", "sap://")
            if (!string.IsNullOrEmpty(address))
            {
                var lowerAddress = address.ToLowerInvariant();
                foreach (var schema in this.connectors.Values)
                {
                    foreach (var alias in schema.BizTalkAdapters)
                    {
                        if (!string.IsNullOrEmpty(alias) &&
                            lowerAddress.Contains(alias.ToLowerInvariant() + "://"))
                        {
                            return schema.Name;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Returns all connectors in the specified messaging category that are compatible
        /// with the given deployment target, deduplicated by canonical connector name.
        /// </summary>
        /// <param name="category">The <see cref="ConnectorSchema.MessagingCategory"/> value to match
        /// (e.g., "Messaging", "Storage", "Database").</param>
        /// <param name="deploymentTarget">The deployment target string: "Cloud" or "OnPremises".</param>
        /// <returns>
        /// A sequence of distinct <see cref="ConnectorSchema"/> instances whose category matches
        /// and which pass <see cref="IsCompatibleWith"/>. Returns an empty sequence when no
        /// compatible alternatives exist or when <paramref name="category"/> is null or empty.
        /// </returns>
        public IEnumerable<ConnectorSchema> FindAlternatives(string category, string deploymentTarget)
        {
            if (string.IsNullOrEmpty(category))
            {
                return Enumerable.Empty<ConnectorSchema>();
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var results = new List<ConnectorSchema>();

            foreach (var schema in this.connectors.Values)
            {
                if (!string.Equals(schema.MessagingCategory, category, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!IsCompatibleWith(schema.Name, deploymentTarget))
                {
                    continue;
                }

                if (seen.Add(schema.Name))
                {
                    results.Add(schema);
                }
            }

            return results;
        }
    }

    /// <summary>
    /// Represents a Logic Apps connector schema with triggers, actions, and connection parameters.
    /// </summary>
    /// <remarks>
    /// Defines the complete connector metadata needed for accurate workflow generation:
    /// - Name: Connector identifier (e.g., "FileSystem", "ServiceBus")
    /// - ServiceProviderId: Logic Apps service provider path (e.g., "/serviceProviders/fileSystem")
    /// - Triggers: Available trigger operations with parameters
    /// - Actions: Available action operations with parameters
    /// - ConnectionParameters: Connection configuration requirements
    /// </remarks>
    public class ConnectorSchema
    {
        /// <summary>Gets or sets the connector name (e.g., "FileSystem", "ServiceBus").</summary>
        public string Name { get; set; }
        
        /// <summary>Gets or sets the Logic Apps service provider ID (e.g., "/serviceProviders/fileSystem").</summary>
        public string ServiceProviderId { get; set; }
        
        /// <summary>Gets or sets the human-readable display name (e.g., "File System", "Azure Service Bus").</summary>
        public string DisplayName { get; set; }

        /// <summary>Gets or sets the default trigger operation ID for this connector (e.g., "whenFilesAreAdded").</summary>
        /// <remarks>Null for connectors that have no triggers. Used by workflow generation to select
        /// the preferred trigger without hardcoded connector name checks.</remarks>
        public string DefaultTrigger { get; set; }

        /// <summary>Gets or sets the default action operation ID for this connector (e.g., "createFile").</summary>
        /// <remarks>Used by workflow generation to select the preferred action without hardcoded
        /// connector name checks.</remarks>
        public string DefaultAction { get; set; }

        /// <summary>Gets or sets the deployment scope: "Any", "CloudOnly", or "OnPremOnly".</summary>
        /// <remarks>Drives <see cref="ConnectorSchemaRegistry.IsCompatibleWith"/> and
        /// <see cref="ConnectorSchemaRegistry.FindAlternatives"/> without hardcoded cloud-only
        /// connector name lists in <c>ConnectorOptimizer</c>.</remarks>
        public string DeploymentScope { get; set; }

        /// <summary>Gets or sets the functional category (e.g., "Messaging", "Storage", "Database").</summary>
        /// <remarks>Used by <see cref="ConnectorSchemaRegistry.FindAlternatives"/> to locate
        /// same-category replacements when a connector is incompatible with the deployment target.</remarks>
        public string MessagingCategory { get; set; }

        /// <summary>Gets or sets the BizTalk adapter names that map to this connector.</summary>
        /// <remarks>Every entry is registered as a case-insensitive alias by
        /// <c>AddCommonAliases</c> during <see cref="ConnectorSchemaRegistry.LoadFromFile"/>.
        /// Adding a new alias requires only a JSON edit — no C# change needed.</remarks>
        public List<string> BizTalkAdapters { get; set; }

        /// <summary>Gets or sets the dictionary of trigger operations keyed by operation ID.</summary>
        /// <remarks>Keys are operation IDs like "whenFilesAreAdded", "receiveQueueMessage".</remarks>
        public Dictionary<string, OperationSchema> Triggers { get; set; }
        
        /// <summary>Gets or sets the dictionary of action operations keyed by operation ID.</summary>
        /// <remarks>Keys are operation IDs like "createFile", "sendMessage", "executeQuery".</remarks>
        public Dictionary<string, OperationSchema> Actions { get; set; }
        
        /// <summary>Gets or sets the connection parameters required for this connector.</summary>
        /// <remarks>Defines authentication and configuration parameters (connectionString, account, etc.).</remarks>
        public Dictionary<string, ConnectionParameter> ConnectionParameters { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConnectorSchema"/> class with empty operation dictionaries.
        /// </summary>
        public ConnectorSchema()
        {
            this.Triggers = new Dictionary<string, OperationSchema>(StringComparer.OrdinalIgnoreCase);
            this.Actions = new Dictionary<string, OperationSchema>(StringComparer.OrdinalIgnoreCase);
            this.ConnectionParameters = new Dictionary<string, ConnectionParameter>(StringComparer.OrdinalIgnoreCase);
            this.BizTalkAdapters = new List<string>();
        }
    }

    /// <summary>
    /// Represents a trigger or action operation schema with parameters.
    /// </summary>
    /// <remarks>
    /// Defines the operation metadata used for workflow JSON generation:
    /// - OperationId: Unique identifier for the operation (e.g., "createFile", "sendMessage")
    /// - IsTrigger: Whether this is a trigger (true) or action (false)
    /// - Kind: Trigger type (Polling, Push, Http) - only used for triggers
    /// - Parameters: List of parameter names required by the operation
    /// </remarks>
    public class OperationSchema
    {
        /// <summary>Gets or sets the unique operation identifier (e.g., "createFile", "whenFilesAreAdded").</summary>
        public string OperationId { get; set; }
        
        /// <summary>Gets or sets whether this operation is a trigger (true) or action (false).</summary>
        public bool IsTrigger { get; set; }
        
        /// <summary>Gets or sets the trigger kind: "Polling", "Push", or "Http". Only applicable for triggers.</summary>
        /// <remarks>
        /// Trigger kinds:
        /// - Polling: Periodically checks for new data (FILE, FTP, SQL)
        /// - Push: Event-driven activation (Service Bus, Event Hub)
        /// - Http: HTTP request trigger (manual, webhook)
        /// </remarks>
        public string Kind { get; set; }
        
        /// <summary>Gets or sets the list of parameter schemas required by this operation.</summary>
        /// <remarks>
        /// Each entry carries the parameter name, the value-source token, and a default value.
        /// Loaded from JSON by <see cref="ConnectorSchemaRegistry.LoadFromFile"/> which accepts
        /// both the legacy plain-string format and the new object format.
        /// </remarks>
        public List<ParameterSchema> Parameters { get; set; }

        /// <summary>
        /// Gets or sets the inputs template for built-in (non-ServiceProvider) actions.
        /// When non-null, the operation uses the Inputs architecture: the template is deep-cloned
        /// and resolved at generation time to produce the action's <c>inputs</c> object directly.
        /// When null, the operation uses the Parameters/ServiceProvider architecture.
        /// </summary>
        /// <remarks>
        /// Populated by <see cref="ConnectorSchemaRegistry.LoadFromFile"/> from the <c>"Inputs"</c>
        /// (or <c>"inputs"</c>) key in the connector JSON.  A JArray of strings is automatically
        /// converted to a JObject whose keys are those strings and whose values are empty strings,
        /// ready for <c>ResolveInputsPlaceholders</c> to fill at generation time.
        /// </remarks>
        public JObject InputsTemplate { get; set; }

        /// <summary>
        /// Gets or sets the Logic Apps action <c>type</c> string for built-in actions
        /// (e.g., <c>"XmlParse"</c>, <c>"SwiftMTDecode"</c>, <c>"FlatFileDecoding"</c>).
        /// When null, the action type is <c>"ServiceProvider"</c>.
        /// Defaults to <see cref="OperationId"/> when <see cref="InputsTemplate"/> is non-null
        /// and no explicit value is provided in the JSON.
        /// </summary>
        public string ActionType { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="OperationSchema"/> class with an empty parameter list.
        /// </summary>
        public OperationSchema()
        {
            this.Parameters = new List<ParameterSchema>();
        }
    }

    /// <summary>
    /// Describes a single parameter within an <see cref="OperationSchema"/>.
    /// Carries the parameter name, a value-source token that tells the generator
    /// where to read the runtime value from, and a fallback default value.
    /// </summary>
    /// <remarks>
    /// Supported <see cref="ValueSource"/> tokens:
    /// <list type="bullet">
    /// <item>TargetAddress — <c>LogicAppAction.TargetAddress</c> / <c>LogicAppTrigger.Address</c></item>
    /// <item>MessageBody — resolved via <c>ResolveMessageBodyExpression</c> for actions, or <c>@triggerBody()</c> for triggers</item>
    /// <item>QueueName — <c>LogicAppAction.QueueOrTopicName</c> or inferred from address</item>
    /// <item>TopicName — same as QueueName</item>
    /// <item>SubscriptionName — <c>LogicAppAction.SubscriptionName</c></item>
    /// <item>FolderPath — folder portion of TargetAddress / <c>LogicAppTrigger.FolderPath</c></item>
    /// <item>FileName — file-name portion of TargetAddress</item>
    /// <item>TableName — inferred from address via table= query parameter</item>
    /// <item>EventHubName — <c>LogicAppAction.QueueOrTopicName</c> or inferred from address</item>
    /// <item>Endpoint — TargetAddress / Address</item>
    /// <item>FileMask — <c>LogicAppTrigger.FileMask</c></item>
    /// <item>Literal — write <see cref="DefaultValue"/> verbatim (no action/trigger property read)</item>
    /// </list>
    /// </remarks>
    public class ParameterSchema
    {
        /// <summary>Gets or sets the Logic Apps parameter name (e.g., "filePath", "queueName").</summary>
        public string Name { get; set; }

        /// <summary>Gets or sets the value-source token used by the generator to resolve the runtime value.</summary>
        public string ValueSource { get; set; }

        /// <summary>Gets or sets the fallback value used when the source property is null or empty.</summary>
        public string DefaultValue { get; set; }
    }

    /// <summary>
    /// Represents a connection parameter definition for a connector.
    /// </summary>
    /// <remarks>
    /// Defines connection configuration parameters such as:
    /// - Connection strings (SQL, Service Bus)
    /// - Account names and keys (Storage, Cosmos DB)
    /// - Authentication credentials (username, password)
    /// - Endpoint URLs (custom APIs)
    /// Used for connector authentication and configuration in Logic Apps.
    /// </remarks>
    public class ConnectionParameter
    {
        /// <summary>Gets or sets the parameter name (e.g., "connectionString", "accountName", "apiKey").</summary>
        public string Name { get; set; }
        
        /// <summary>Gets or sets the parameter data type: "StringType", "SecureString", "IntType", or "BoolType".</summary>
        /// <remarks>
        /// Parameter types:
        /// - StringType: Plain text values (URLs, account names)
        /// - SecureString: Sensitive values (passwords, API keys, connection strings)
        /// - IntType: Numeric values (timeouts, retry counts)
        /// - BoolType: Boolean flags (enable/disable options)
        /// </remarks>
        public string Type { get; set; }
        
        /// <summary>Gets or sets whether this parameter is required for connector authentication.</summary>
        public bool Required { get; set; }
    }
}