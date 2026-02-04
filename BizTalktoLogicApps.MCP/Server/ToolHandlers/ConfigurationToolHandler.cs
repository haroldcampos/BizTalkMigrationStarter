// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BizTalktoLogicApps.ODXtoWFMigrator;
using BizTalktoLogicApps.MCP.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BizTalktoLogicApps.MCP.Server.ToolHandlers
{
    public class ConfigurationToolHandler
    {
        public void RegisterTools(ToolRegistry registry)
        {
            RegisterLoadConnectorRegistry(registry);
            RegisterListAvailableConnectors(registry);
            RegisterValidateWorkflow(registry);
        }

        private void RegisterLoadConnectorRegistry(ToolRegistry registry)
        {
            var tool = new Tool
            {
                Name = "load_connector_registry",
                Description = "Loads and validates a custom connector schema registry from JSON file",
                InputSchema = ToolSchemas.CreateObjectSchema(
                    new Dictionary<string, object>
                    {
                        ["registryPath"] = ToolSchemas.StringProperty("Path to connector registry JSON file"),
                        ["validate"] = ToolSchemas.BoolProperty("Validate registry structure", true)
                    },
                    new[] { "registryPath" }
                )
            };

            registry.RegisterTool(tool, args =>
            {
                try
                {
                    var registryPath = args["registryPath"]?.ToString();
                    var validate = args["validate"]?.ToObject<bool>() ?? true;

                    if (string.IsNullOrEmpty(registryPath) || !File.Exists(registryPath))
                    {
                        return CreateErrorResult($"Registry file not found: {registryPath}");
                    }

                    var connectorRegistry = ConnectorSchemaRegistry.LoadFromFile(registryPath);

                    var result = new JObject
                    {
                        ["success"] = true,
                        ["registryPath"] = registryPath,
                        ["connectorCount"] = connectorRegistry.ConnectorCount,
                        ["message"] = $"Successfully loaded {connectorRegistry.ConnectorCount} connector(s)"
                    };

                    return new ToolCallResult
                    {
                        Content = new[]
                        {
                            new ToolContent
                            {
                                Type = "text",
                                Text = result.ToString(Newtonsoft.Json.Formatting.Indented)
                            }
                        }
                    };
                }
                catch (Exception ex)
                {
                    return CreateErrorResult($"Failed to load connector registry: {ex.Message}");
                }
            });
        }

        private void RegisterListAvailableConnectors(ToolRegistry registry)
        {
            var tool = new Tool
            {
                Name = "list_available_connectors",
                Description = "Lists all available Logic Apps connectors and their BizTalk adapter mappings",
                InputSchema = ToolSchemas.CreateObjectSchema(
                    new Dictionary<string, object>
                    {
                        ["registryPath"] = ToolSchemas.StringProperty("Optional path to custom connector registry"),
                        ["filterByType"] = ToolSchemas.StringProperty("Optional filter by connector type/category")
                    },
                    new string[] { }
                )
            };

            registry.RegisterTool(tool, args =>
            {
                try
                {
                    var registryPath = args["registryPath"]?.ToString();

                    ConnectorSchemaRegistry connectorRegistry;
                    
                    if (!string.IsNullOrEmpty(registryPath) && File.Exists(registryPath))
                    {
                        connectorRegistry = ConnectorSchemaRegistry.LoadFromFile(registryPath);
                    }
                    else
                    {
                        connectorRegistry = BizTalkOrchestrationParser.TryLoadConnectorRegistry();
                    }

                    if (connectorRegistry == null)
                    {
                        return CreateErrorResult("Connector registry not found. Please ensure connector-registry.json exists in the Schemas/Connectors folder.");
                    }

                    var connectors = new JArray();
                    
                    var knownAdapterTypes = new[] { "Http", "FileSystem", "FILE", "Ftp", "FTP", "Sql", "SQL", "ServiceBus" };
                    
                    foreach (var adapterType in knownAdapterTypes)
                    {
                        if (connectorRegistry.HasConnector(adapterType))
                        {
                            var connector = connectorRegistry.GetConnector(adapterType);
                            connectors.Add(new JObject
                            {
                                ["name"] = connector.Name,
                                ["serviceProviderId"] = connector.ServiceProviderId,
                                ["displayName"] = connector.DisplayName,
                                ["triggerCount"] = connector.Triggers.Count,
                                ["actionCount"] = connector.Actions.Count
                            });
                        }
                    }

                    var result = new JObject
                    {
                        ["totalConnectors"] = connectors.Count,
                        ["source"] = string.IsNullOrEmpty(registryPath) ? "default" : "custom",
                        ["connectors"] = connectors
                    };

                    return new ToolCallResult
                    {
                        Content = new[]
                        {
                            new ToolContent
                            {
                                Type = "text",
                                Text = result.ToString(Newtonsoft.Json.Formatting.Indented)
                            }
                        }
                    };
                }
                catch (Exception ex)
                {
                    return CreateErrorResult($"Failed to list connectors: {ex.Message}");
                }
            });
        }

        private void RegisterValidateWorkflow(ToolRegistry registry)
        {
            var tool = new Tool
            {
                Name = "validate_workflow",
                Description = "Validates a Logic Apps workflow JSON file for schema compliance and best practices",
                InputSchema = ToolSchemas.CreateObjectSchema(
                    new Dictionary<string, object>
                    {
                        ["workflowPath"] = ToolSchemas.StringProperty("Path to workflow.json file"),
                        ["strictMode"] = ToolSchemas.BoolProperty("Enable strict validation (fail on warnings)", false)
                    },
                    new[] { "workflowPath" }
                )
            };

            registry.RegisterTool(tool, args =>
            {
                try
                {
                    var workflowPath = args["workflowPath"]?.ToString();
                    var strictMode = args["strictMode"]?.ToObject<bool>() ?? false;

                    if (string.IsNullOrEmpty(workflowPath) || !File.Exists(workflowPath))
                    {
                        return CreateErrorResult($"Workflow file not found: {workflowPath}");
                    }

                    var json = File.ReadAllText(workflowPath);
                    var validator = new WorkflowValidator();
                    var validationResult = validator.Validate(json);

                    var result = new JObject
                    {
                        ["valid"] = !validationResult.HasErrors,
                        ["hasWarnings"] = validationResult.HasWarnings,
                        ["hasErrors"] = validationResult.HasErrors,
                        ["errorCount"] = validationResult.Issues.Count(i => i.Severity == IssueSeverity.Error),
                        ["warningCount"] = validationResult.Issues.Count(i => i.Severity == IssueSeverity.Warning),
                        ["errors"] = JArray.FromObject(validationResult.Issues.Where(i => i.Severity == IssueSeverity.Error)),
                        ["warnings"] = JArray.FromObject(validationResult.Issues.Where(i => i.Severity == IssueSeverity.Warning)),
                        ["summary"] = validationResult.GetSummary()
                    };

                    if (strictMode && validationResult.HasWarnings)
                    {
                        result["valid"] = false;
                        result["message"] = "Validation failed in strict mode due to warnings";
                    }

                    return new ToolCallResult
                    {
                        Content = new[]
                        {
                            new ToolContent
                            {
                                Type = "text",
                                Text = result.ToString(Newtonsoft.Json.Formatting.Indented)
                            }
                        }
                    };
                }
                catch (Exception ex)
                {
                    return CreateErrorResult($"Validation failed: {ex.Message}");
                }
            });
        }

        private ToolCallResult CreateErrorResult(string message)
        {
            return new ToolCallResult
            {
                Content = new[]
                {
                    new ToolContent
                    {
                        Type = "text",
                        Text = message
                    }
                },
                IsError = true
            };
        }
    }
}
