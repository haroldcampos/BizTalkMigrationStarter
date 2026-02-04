// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
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
    /// - Fallback to built-in connector schemas when external registry is unavailable
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
                            DisplayName = connectorDef["DisplayName"]?.ToString()
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
                                    Parameters = triggerDef["Parameters"]?.ToObject<List<string>>() ?? new List<string>()
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
                                schema.Actions[action.Name] = new OperationSchema
                                {
                                    OperationId = actionDef["OperationId"]?.ToString(),
                                    IsTrigger = false,
                                    Parameters = actionDef["Parameters"]?.ToObject<List<string>>() ?? new List<string>()
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
        /// Adds common BizTalk adapter aliases to the registry for easier connector lookup.
        /// </summary>
        /// <param name="registry">The registry to add aliases to.</param>
        /// <param name="schema">The connector schema to create aliases for.</param>
        /// <remarks>
        /// Creates case-insensitive aliases for common BizTalk adapter names:
        /// - FileSystem ? FILE
        /// - Ftp ? FTP
        /// - Sftp ? SFTP
        /// - Sql ? SQL
        /// - SapOData ? SAP
        /// - SapErp ? SAPERP, BAPI
        /// - IbmMq ? MQ
        /// - ConfluentKafka ? Kafka
        /// This allows lookups using either the Logic Apps connector name or the BizTalk adapter name.
        /// </remarks>
        private static void AddCommonAliases(ConnectorSchemaRegistry registry, ConnectorSchema schema)
        {
            if (schema == null || string.IsNullOrEmpty(schema.Name))
            {
                return;
            }

            // Add uppercase alias for common patterns
            var upperName = schema.Name.ToUpperInvariant();
            
            switch (schema.Name)
            {
                case "FileSystem":
                    registry.connectors["FILE"] = schema;
                    break;
                case "Ftp":
                    registry.connectors["FTP"] = schema;
                    break;
                case "Sftp":
                    registry.connectors["SFTP"] = schema;
                    break;
                case "Sql":
                    registry.connectors["SQL"] = schema;
                    break;
                case "SapOData":
                    registry.connectors["SAP"] = schema;
                    break;
                case "SapErp":
                    registry.connectors["SAPERP"] = schema;
                    registry.connectors["BAPI"] = schema;
                    break;
                case "IbmMq":
                    registry.connectors["MQ"] = schema;
                    break;
                case "ConfluentKafka":
                    registry.connectors["Kafka"] = schema;
                    break;
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
        
        /// <summary>Gets or sets the list of parameter names required by this operation.</summary>
        /// <remarks>
        /// Parameter names match Logic Apps workflow definition schema.
        /// Examples: "folderPath", "fileMask", "queueName", "message", "body"
        /// </remarks>
        public List<string> Parameters { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="OperationSchema"/> class with an empty parameter list.
        /// </summary>
        public OperationSchema()
        {
            this.Parameters = new List<string>();
        }
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