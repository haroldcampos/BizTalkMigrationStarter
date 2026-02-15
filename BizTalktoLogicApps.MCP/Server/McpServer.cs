// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BizTalktoLogicApps.MCP.Models;
using BizTalktoLogicApps.MCP.Server.ToolHandlers;
using BizTalktoLogicApps.ODXtoWFMigrator;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BizTalktoLogicApps.MCP.Server
{
    public class McpServer
    {
        private readonly ToolRegistry _toolRegistry;
        private readonly TextWriter _output;
        private readonly TextReader _input;
        private bool _initialized;

        public McpServer(TextReader input, TextWriter output)
        {
            _input = input ?? Console.In;
            _output = output ?? Console.Out;
            _toolRegistry = new ToolRegistry();
            _initialized = false;

            RegisterAllTools();
        }

        private void RegisterAllTools()
        {
            var analysisHandler = new AnalysisToolHandler();
            var conversionHandler = new ConversionToolHandler();
            var mappingHandler = new MappingToolHandler();
            var configHandler = new ConfigurationToolHandler();
            var mapConversionHandler = new MapConversionToolHandler();
            var pipelineHandler = new PipelineToolHandler();

            _toolRegistry.RegisterAllTools(analysisHandler, conversionHandler, mappingHandler, configHandler, mapConversionHandler, pipelineHandler);
        }

        public void Start()
        {
            try
            {
                LogDebug("MCP Server starting...");

                while (true)
                {
                    var line = _input.ReadLine();
                    if (line == null)
                    {
                        LogDebug("Input stream closed, exiting...");
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    try
                    {
                        var message = JsonConvert.DeserializeObject<McpMessage>(line);
                        HandleMessage(message);
                    }
                    catch (JsonException ex)
                    {
                        LogDebug($"Invalid JSON received: {ex.Message}");
                        SendError(null, -32700, "Parse error", ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Server error: {ex.Message}");
                LogDebug(ex.StackTrace);
            }
        }

        private void HandleMessage(McpMessage message)
        {
            LogDebug($"Received method: {message.Method}");

            switch (message.Method)
            {
                case "initialize":
                    HandleInitialize(message);
                    break;

                case "tools/list":
                    HandleToolsList(message);
                    break;

                case "tools/call":
                    HandleToolsCall(message);
                    break;

                case "resources/list":
                    HandleResourcesList(message);
                    break;

                case "prompts/list":
                    HandlePromptsList(message);
                    break;

                default:
                    SendError(message.Id, -32601, "Method not found", $"Unknown method: {message.Method}");
                    break;
            }
        }

        private void HandleInitialize(McpMessage message)
        {
            var result = new InitializeResult
            {
                ProtocolVersion = "2024-11-05",
                ServerInfo = new ServerInfo
                {
                    Name = "BizTalk to Logic Apps Migration Server",
                    Version = "1.0.0"
                },
                Capabilities = new ServerCapabilities
                {
                    Tools = new ToolsCapability { ListChanged = false },
                    Resources = new ResourcesCapability { Subscribe = false, ListChanged = false },
                    Prompts = new PromptsCapability { ListChanged = false }
                }
            };

            _initialized = true;
            SendResponse(message.Id, result);
        }

        private void HandleToolsList(McpMessage message)
        {
            if (!_initialized)
            {
                SendError(message.Id, -32002, "Not initialized", "Server must be initialized first");
                return;
            }

            var tools = _toolRegistry.GetAllTools();
            var result = new { tools };

            SendResponse(message.Id, result);
        }

        private void HandleToolsCall(McpMessage message)
        {
            if (!_initialized)
            {
                SendError(message.Id, -32002, "Not initialized", "Server must be initialized first");
                return;
            }

            try
            {
                var toolName = message.Params?["name"]?.ToString();
                var arguments = message.Params?["arguments"] as JObject;

                if (string.IsNullOrEmpty(toolName))
                {
                    SendError(message.Id, -32602, "Invalid params", "Tool name is required");
                    return;
                }

                if (!_toolRegistry.TryGetHandler(toolName, out var handler))
                {
                    SendError(message.Id, -32602, "Invalid params", $"Unknown tool: {toolName}");
                    return;
                }

                LogDebug($"Calling tool: {toolName}");

                var result = handler(arguments ?? new JObject());

                SendResponse(message.Id, result);
            }
            catch (Exception ex)
            {
                LogDebug($"Tool execution error: {ex.Message}");
                SendError(message.Id, -32603, "Internal error", ex.Message);
            }
        }

        private void HandleResourcesList(McpMessage message)
        {
            if (!_initialized)
            {
                SendError(message.Id, -32002, "Not initialized", "Server must be initialized first");
                return;
            }

            var resources = new List<Resource>
            {
                new Resource
                {
                    Uri = "biztalk://orchestration/{name}",
                    Name = "BizTalk Orchestration",
                    Description = "BizTalk orchestration ODX file",
                    MimeType = "application/xml"
                },
                new Resource
                {
                    Uri = "biztalk://binding/{name}",
                    Name = "BizTalk Binding File",
                    Description = "BizTalk binding configuration XML",
                    MimeType = "application/xml"
                },
                new Resource
                {
                    Uri = "biztalk://map/{name}",
                    Name = "BizTalk Map File",
                    Description = "BizTalk map BTM file",
                    MimeType = "application/xml"
                },
                new Resource
                {
                    Uri = "logicapp://definition/{name}",
                    Name = "Logic Apps Workflow Definition",
                    Description = "Generated Logic Apps workflow JSON",
                    MimeType = "application/json"
                },
                new Resource
                {
                    Uri = "logicapp://LMLmap/{name}",
                    Name = "Logic Apps LML Map",
                    Description = "Generated Logic Apps mapping LML file",
                    MimeType = "text/plain"
                }
            };

            var result = new { resources };
            SendResponse(message.Id, result);
        }

        private void HandlePromptsList(McpMessage message)
        {
            if (!_initialized)
            {
                SendError(message.Id, -32002, "Not initialized", "Server must be initialized first");
                return;
            }

            var prompts = new List<Prompt>
            {
                new Prompt
                {
                    Name = "analyze-migration-complexity",
                    Description = "Assess BizTalk to Logic Apps migration effort and complexity",
                    Arguments = new[]
                    {
                        new PromptArgument { Name = "odxPath", Description = "Path to orchestration file", Required = true }
                    }
                },
                new Prompt
                {
                    Name = "suggest-connector-mappings",
                    Description = "Recommend Logic Apps connector alternatives for BizTalk adapters",
                    Arguments = new[]
                    {
                        new PromptArgument { Name = "adapterType", Description = "BizTalk adapter type", Required = true }
                    }
                },
                new Prompt
                {
                    Name = "generate-migration-checklist",
                    Description = "Create step-by-step migration plan for BizTalk orchestration",
                    Arguments = new[]
                    {
                        new PromptArgument { Name = "orchestrationName", Description = "Orchestration name", Required = true }
                    }
                },
                new Prompt
                {
                    Name = "explain-conversion-differences",
                    Description = "Document BizTalk vs Logic Apps implementation differences",
                    Arguments = new[]
                    {
                        new PromptArgument { Name = "feature", Description = "BizTalk feature to explain", Required = true }
                    }
                },
                new Prompt
                {
                    Name = "convert-biztalk-map",
                    Description = "Convert BizTalk map (BTM) to Logic Apps mapping (LML) with guidance",
                    Arguments = new[]
                    {
                        new PromptArgument { Name = "btmPath", Description = "Path to BTM map file", Required = true },
                        new PromptArgument { Name = "sourceSchemaPath", Description = "Path to source XSD schema", Required = false },
                        new PromptArgument { Name = "targetSchemaPath", Description = "Path to target XSD schema", Required = false }
                    }
                }
            };

            var result = new { prompts };
            SendResponse(message.Id, result);
        }

        private void SendResponse(object id, object result)
        {
            var response = new McpResponse
            {
                Id = id,
                Result = result
            };

            var json = JsonConvert.SerializeObject(response);
            _output.WriteLine(json);
            _output.Flush();
            LogDebug($"Sent response for id: {id}");
        }

        private void SendError(object id, int code, string message, object data = null)
        {
            var response = new McpResponse
            {
                Id = id,
                Error = new McpError
                {
                    Code = code,
                    Message = message,
                    Data = data
                }
            };

            var json = JsonConvert.SerializeObject(response);
            _output.WriteLine(json);
            _output.Flush();
            LogDebug($"Sent error for id: {id}, code: {code}, message: {message}");
        }

        private void LogDebug(string message)
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BizTalkToLogicApps.MCP",
                "debug.log"
            );

            try
            {
                var logDir = Path.GetDirectoryName(logPath);
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
            catch (Exception ex) when (!ex.IsFatal())
            {
                // Non-fatal logging failures are intentionally suppressed
            }
        }
    }
}
