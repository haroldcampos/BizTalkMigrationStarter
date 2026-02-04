// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BizTalktoLogicApps.ODXtoWFMigrator;
using BizTalktoLogicApps.MCP.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;

namespace BizTalktoLogicApps.MCP.Server.ToolHandlers
{
    public class MappingToolHandler
    {
        public void RegisterTools(ToolRegistry registry)
        {
            RegisterMapBizTalkExpression(registry);
            RegisterResolveConnectorSchema(registry);
            RegisterParseBindingFile(registry);
        }

        private void RegisterMapBizTalkExpression(ToolRegistry registry)
        {
            var tool = new Tool
            {
                Name = "map_biztalk_expression",
                Description = "Translates BizTalk XLANG expressions to Azure Logic Apps workflow definition language expressions",
                InputSchema = ToolSchemas.CreateObjectSchema(
                    new Dictionary<string, object>
                    {
                        ["expression"] = ToolSchemas.StringProperty("BizTalk XLANG expression to translate"),
                        ["context"] = ToolSchemas.StringProperty("Optional expression context (variable, message, port)"),
                        ["targetVersion"] = ToolSchemas.EnumProperty("Target Logic Apps version", "Standard", "Consumption")
                    },
                    new[] { "expression" }
                )
            };

            registry.RegisterTool(tool, args =>
            {
                try
                {
                    var expression = args["expression"]?.ToString();
                    var context = args["context"]?.ToString();

                    if (string.IsNullOrEmpty(expression))
                    {
                        return CreateErrorResult("Expression cannot be empty");
                    }

                    var mappedExpression = ExpressionMapper.MapExpression(expression);

                    var result = new JObject
                    {
                        ["originalExpression"] = expression,
                        ["mappedExpression"] = mappedExpression,
                        ["context"] = context ?? "general",
                        ["notes"] = GetExpressionMappingNotes(expression, mappedExpression)
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
                    return CreateErrorResult($"Expression mapping failed: {ex.Message}");
                }
            });
        }

        private void RegisterResolveConnectorSchema(ToolRegistry registry)
        {
            var tool = new Tool
            {
                Name = "resolve_connector_schema",
                Description = "Resolves BizTalk adapter types to Azure Logic Apps connector schemas with operation details",
                InputSchema = ToolSchemas.CreateObjectSchema(
                    new Dictionary<string, object>
                    {
                        ["adapterType"] = ToolSchemas.StringProperty("BizTalk adapter type (FILE, FTP, HTTP, SQL, WCF-*, etc.)"),
                        ["registryPath"] = ToolSchemas.StringProperty("Optional path to custom connector registry JSON"),
                        ["operationType"] = ToolSchemas.EnumProperty("Operation type", "trigger", "action", "both")
                    },
                    new[] { "adapterType" }
                )
            };

            registry.RegisterTool(tool, args =>
            {
                try
                {
                    var adapterType = args["adapterType"]?.ToString();
                    var registryPath = args["registryPath"]?.ToString();
                    var operationType = args["operationType"]?.ToString() ?? "both";

                    if (string.IsNullOrEmpty(adapterType))
                    {
                        return CreateErrorResult("Adapter type cannot be empty");
                    }

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

                    var connector = connectorRegistry.GetConnector(adapterType);

                    if (connector == null)
                    {
                        return CreateErrorResult($"No connector mapping found for adapter type: {adapterType}");
                    }

                    var result = new JObject
                    {
                        ["adapterType"] = adapterType,
                        ["connectorName"] = connector.Name,
                        ["serviceProviderId"] = connector.ServiceProviderId,
                        ["displayName"] = connector.DisplayName
                    };

                    if (operationType == "trigger" || operationType == "both")
                    {
                        var triggers = new JArray();
                        foreach (var trigger in connector.Triggers)
                        {
                            triggers.Add(new JObject
                            {
                                ["name"] = trigger.Key,
                                ["operationId"] = trigger.Value.OperationId,
                                ["kind"] = trigger.Value.Kind,
                                ["parameters"] = JArray.FromObject(trigger.Value.Parameters)
                            });
                        }
                        result["triggers"] = triggers;
                    }

                    if (operationType == "action" || operationType == "both")
                    {
                        var actions = new JArray();
                        foreach (var action in connector.Actions)
                        {
                            actions.Add(new JObject
                            {
                                ["name"] = action.Key,
                                ["operationId"] = action.Value.OperationId,
                                ["parameters"] = JArray.FromObject(action.Value.Parameters)
                            });
                        }
                        result["actions"] = actions;
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
                    return CreateErrorResult($"Connector resolution failed: {ex.Message}");
                }
            });
        }

        private void RegisterParseBindingFile(ToolRegistry registry)
        {
            var tool = new Tool
            {
                Name = "parse_binding_file",
                Description = "Parses BizTalk binding XML files to extract receive locations, send ports, and adapter configurations",
                InputSchema = ToolSchemas.CreateObjectSchema(
                    new Dictionary<string, object>
                    {
                        ["bindingFilePath"] = ToolSchemas.StringProperty("Path to BizTalk binding XML file"),
                        ["extractFilters"] = ToolSchemas.BoolProperty("Extract send port filter expressions", true),
                        ["extractTransportConfig"] = ToolSchemas.BoolProperty("Extract detailed transport configuration", true)
                    },
                    new[] { "bindingFilePath" }
                )
            };

            registry.RegisterTool(tool, args =>
            {
                try
                {
                    var bindingPath = args["bindingFilePath"]?.ToString();
                    var extractFilters = args["extractFilters"]?.ToObject<bool>() ?? true;
                    var extractTransportConfig = args["extractTransportConfig"]?.ToObject<bool>() ?? true;

                    if (string.IsNullOrEmpty(bindingPath) || !File.Exists(bindingPath))
                    {
                        return CreateErrorResult($"Binding file not found: {bindingPath}");
                    }

                    var bindings = BindingSnapshot.Parse(bindingPath);

                    var receiveLocations = new JArray();
                    foreach (var rl in bindings.ReceiveLocations)
                    {
                        var rlObj = new JObject
                        {
                            ["name"] = rl.Name,
                            ["receivePortName"] = rl.ReceivePortName,
                            ["address"] = rl.Address,
                            ["transportType"] = rl.TransportType,
                            ["enabled"] = rl.Enabled
                        };

                        receiveLocations.Add(rlObj);
                    }

                    var sendPorts = new JArray();
                    foreach (var sp in bindings.SendPorts)
                    {
                        var spObj = new JObject
                        {
                            ["name"] = sp.Name,
                            ["address"] = sp.Address,
                            ["transportType"] = sp.TransportType
                        };

                        sendPorts.Add(spObj);
                    }

                    var result = new JObject
                    {
                        ["receiveLocationCount"] = bindings.ReceiveLocations.Count,
                        ["sendPortCount"] = bindings.SendPorts.Count,
                        ["receiveLocations"] = receiveLocations,
                        ["sendPorts"] = sendPorts
                    };

                    // Note: Parse warnings functionality not yet implemented in BindingSnapshot
                    // Future enhancement: Add ParseWarnings property for validation messages

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
                    return CreateErrorResult($"Binding file parsing failed: {ex.Message}");
                }
            });
        }

        private string GetExpressionMappingNotes(string original, string mapped)
        {
            if (original == mapped)
            {
                return "Expression wrapped as literal string (complex pattern not directly mappable)";
            }

            if (mapped.Contains("@"))
            {
                return "Successfully mapped to Logic Apps expression syntax";
            }

            return "Expression mapped with best effort";
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
