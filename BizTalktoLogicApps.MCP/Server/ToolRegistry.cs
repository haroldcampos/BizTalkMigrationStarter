// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BizTalktoLogicApps.MCP.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace BizTalktoLogicApps.MCP.Server
{
    public class ToolRegistry
    {
        private readonly Dictionary<string, Tool> _tools;
        private readonly Dictionary<string, Func<JObject, ToolCallResult>> _handlers;

        public ToolRegistry()
        {
            _tools = new Dictionary<string, Tool>();
            _handlers = new Dictionary<string, Func<JObject, ToolCallResult>>();
        }

        public void RegisterTool(Tool tool, Func<JObject, ToolCallResult> handler)
        {
            _tools[tool.Name] = tool;
            _handlers[tool.Name] = handler;
        }

        public Tool[] GetAllTools()
        {
            var tools = new Tool[_tools.Count];
            _tools.Values.CopyTo(tools, 0);
            return tools;
        }

        public bool TryGetHandler(string toolName, out Func<JObject, ToolCallResult> handler)
        {
            return _handlers.TryGetValue(toolName, out handler);
        }

        public void RegisterAllTools(
            ToolHandlers.AnalysisToolHandler analysisHandler,
            ToolHandlers.ConversionToolHandler conversionHandler,
            ToolHandlers.MappingToolHandler mappingHandler,
            ToolHandlers.ConfigurationToolHandler configHandler,
            ToolHandlers.MapConversionToolHandler mapConversionHandler,
            ToolHandlers.PipelineToolHandler pipelineHandler)
        {
            analysisHandler.RegisterTools(this);
            conversionHandler.RegisterTools(this);
            mappingHandler.RegisterTools(this);
            configHandler.RegisterTools(this);
            mapConversionHandler.RegisterTools(this);
            pipelineHandler.RegisterTools(this);
        }
    }
}
