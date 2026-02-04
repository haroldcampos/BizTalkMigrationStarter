using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using BizTalktoLogicApps.BTPtoLA.Models;

namespace BizTalktoLogicApps.BTPtoLA.Services
{
    /// <summary>
    /// Generates Azure Logic Apps Standard workflow JSON definitions from pipeline workflow models.
    /// Simplified version for pipeline processing - removed orchestration-specific features.
    /// Converts triggers, actions (Scope, Compose, Foreach) to Logic Apps workflow definition language.
    /// </summary>
    public static class PipelineJSONGenerator
    {
        private const string SchemaVersion = "2016-06-01";

        /// <summary>
        /// Generates a complete Logic Apps Standard workflow JSON from a pipeline workflow model.
        /// </summary>
        /// <param name="workflow">The pipeline workflow model containing triggers and actions.</param>
        /// <param name="workflowKind">The workflow kind ("Stateful" or "Stateless"). Defaults to "Stateful".</param>
        /// <returns>A JSON string representing the complete Logic Apps workflow definition.</returns>
        /// <exception cref="ArgumentNullException">Thrown when workflow is null.</exception>
        public static string GenerateWorkflowJSON(PipelineWorkflowModel workflow, string workflowKind = "Stateful")
        {
            if (workflow == null) throw new ArgumentNullException(nameof(workflow));

            JObject definition = BuildDefinition(workflow);

            var root = new JObject();
            root["kind"] = workflowKind;
            root["definition"] = definition;
            
            return JsonConvert.SerializeObject(root, Formatting.Indented);
        }

        /// <summary>
        /// Builds the workflow definition object containing schema, triggers, actions, and outputs.
        /// </summary>
        /// <param name="workflow">The pipeline workflow model to convert.</param>
        /// <returns>A JObject containing the complete workflow definition.</returns>
        private static JObject BuildDefinition(PipelineWorkflowModel workflow)
        {
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Get trigger (pipelines always have exactly one Request trigger)
            var trigger = workflow.Triggers.FirstOrDefault()
                          ?? new PipelineWorkflowTrigger { Name = "When_a_message_is_received", Kind = "Request" };

            string triggerName = AllocateName(NormalizeName(trigger.Name ?? "Trigger"), usedNames);
            var triggersObj = new JObject();
            triggersObj[triggerName] = BuildTrigger(trigger);

            // Build actions sequentially
            var actionsObj = BuildActions(workflow.Actions.OrderBy(a => a.Sequence), usedNames);

            var def = new JObject();
            def["$schema"] = string.Format(
                "https://schema.management.azure.com/providers/Microsoft.Logic/schemas/{0}/workflowdefinition.json#",
                SchemaVersion);
            def["contentVersion"] = "1.0.0.0";
            def["triggers"] = triggersObj;
            def["actions"] = actionsObj;
            def["outputs"] = new JObject();
            
            return def;
        }

        /// <summary>
        /// Builds a Logic Apps trigger JSON object from a pipeline trigger model.
        /// Pipelines always use HTTP Request triggers.
        /// </summary>
        /// <param name="trigger">The trigger model to convert.</param>
        /// <returns>A JObject representing the trigger configuration.</returns>
        private static JObject BuildTrigger(PipelineWorkflowTrigger trigger)
        {
            var req = new JObject();
            req["type"] = "Request";
            req["kind"] = "Http";
            return req;
        }

        /// <summary>
        /// Builds the actions object for the workflow with proper sequencing.
        /// Processes actions in sequence with runAfter dependencies.
        /// </summary>
        /// <param name="ordered">The ordered collection of actions to process.</param>
        /// <param name="used">A set of already-used action names to prevent duplicates.</param>
        /// <returns>A JObject containing all workflow actions with proper sequencing.</returns>
        private static JObject BuildActions(IEnumerable<PipelineWorkflowAction> ordered, HashSet<string> used)
        {
            var actionsObj = new JObject();
            string lastLinear = null;

            foreach (var act in ordered)
            {
                var actionName = AllocateName(NormalizeName(act.Name), used);
                var actionObj = BuildAction(act, used);

                // Skip null actions (empty scopes, etc.)
                if (actionObj == null)
                {
                    Console.WriteLine("[GENERATOR] Skipping null action: " + act.Name);
                    continue;
                }

                // Set runAfter to depend on previous action
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
        /// Builds a Logic Apps action JSON object from a pipeline action model.
        /// Handles Compose, Foreach, and Scope action types.
        /// </summary>
        /// <param name="act">The action model to convert.</param>
        /// <param name="usedNames">Set of used action names to prevent duplicates.</param>
        /// <returns>A JObject representing the action, or null if the action is empty/should be skipped.</returns>
        private static JObject BuildAction(PipelineWorkflowAction act, HashSet<string> usedNames)
        {
            switch (act.Type)
            {
                case "Compose":
                    var compose = new JObject();
                    compose["type"] = "Compose";
                    
                    // Use Details as the input - it contains component metadata as comments
                    compose["inputs"] = act.Details ?? "@triggerBody()";
                    
                    return compose;

                case "XmlParse":
                    // Parse XML with schema action (correct type for Logic Apps)
                    var xmlParse = new JObject();
                    xmlParse["type"] = "XmlParse";
                    
                    var parseInputs = new JObject();
                    
                    // Use parent action name for @items() reference if available
                    var parseItemsRef = !string.IsNullOrEmpty(act.ParentActionName)
                        ? $"@items('{NormalizeName(act.ParentActionName)}')?['$content']"
                        : "@items('Parse_XML_with_Schema')?['$content']";
                    
                    parseInputs["content"] = parseItemsRef;
                    
                    // Schema reference - Logic Apps Standard format
                    var parseSchema = new JObject();
                    parseSchema["source"] = "LogicApp";
                    parseSchema["name"] = "SCHEMA_NAME_HERE"; // Placeholder
                    parseInputs["schema"] = parseSchema;
                    
                    // XML reader settings
                    var xmlReaderSettings = new JObject();
                    xmlReaderSettings["dtdProcessing"] = "Prohibit";
                    xmlReaderSettings["xmlNormalization"] = true;
                    xmlReaderSettings["ignoreWhitespace"] = true;
                    xmlReaderSettings["ignoreProcessingInstructions"] = true;
                    parseInputs["xmlReaderSettings"] = xmlReaderSettings;
                    
                    // JSON writer settings (for converting XML to JSON output)
                    var jsonWriterSettings = new JObject();
                    jsonWriterSettings["ignoreAttributes"] = false; // Keep attributes for property extraction
                    jsonWriterSettings["useFullyQualifiedNames"] = false;
                    parseInputs["jsonWriterSettings"] = jsonWriterSettings;
                    
                    xmlParse["inputs"] = parseInputs;
                    
                    // Add metadata comment
                    xmlParse["metadata"] = new JObject
                    {
                        ["comment"] = act.Details ?? "Parse XML with Logic App schema"
                    };
                    
                    return xmlParse;

                case "XmlCompose":
                    // Compose XML with schema action (correct type for Logic Apps)
                    var xmlCompose = new JObject();
                    xmlCompose["type"] = "XmlCompose";
                    
                    var composeInputs = new JObject();
                    
                    // Schema reference - Logic Apps Standard format (schema first)
                    var composeSchema = new JObject();
                    composeSchema["source"] = "LogicApp";
                    composeSchema["name"] = "SCHEMA_NAME_HERE"; // Placeholder
                    composeInputs["schema"] = composeSchema;
                    
                    // Content - should be JSON object matching schema structure
                    composeInputs["content"] = "@triggerBody()"; // Input data as JSON
                    
                    xmlCompose["inputs"] = composeInputs;
                    
                    // Add metadata comment
                    xmlCompose["metadata"] = new JObject
                    {
                        ["comment"] = act.Details ?? "Compose XML with Logic App schema"
                    };
                    
                    return xmlCompose;

                case "InvokeFunction":
                    // Azure Functions invocation for custom components
                    var invokeFunction = new JObject();
                    invokeFunction["type"] = "InvokeFunction";
                    
                    var functionInputs = new JObject();
                    
                    // Extract function name from component properties or use default
                    var functionName = act.ComponentProperties.ContainsKey("functionName")
                        ? act.ComponentProperties["functionName"]
                        : act.Name + "_Function";
                    
                    functionInputs["functionName"] = functionName;
                    
                    // Build parameters object from component properties
                    var parameters = new JObject();
                    foreach (var prop in act.ComponentProperties)
                    {
                        if (prop.Key != "functionName") // Skip functionName as it's used above
                        {
                            // Keep all values as strings (Logic Apps will handle type conversion)
                            parameters[prop.Key] = prop.Value;
                        }
                    }
                    
                    // If no parameters, add defaults based on component
                    if (parameters.Count == 0)
                    {
                        parameters["messageContent"] = "@triggerBody()?['$content']";
                    }
                    
                    functionInputs["parameters"] = parameters;
                    
                    invokeFunction["inputs"] = functionInputs;
                    
                    // Add metadata with migration notes
                    if (!string.IsNullOrEmpty(act.Details))
                    {
                        invokeFunction["metadata"] = new JObject
                        {
                            ["comment"] = act.Details
                        };
                    }
                    
                    return invokeFunction;

                case "FlatFileDecoding":
                    // NEW: Flat File Decoding action
                    var flatFileDecode = new JObject();
                    flatFileDecode["type"] = "FlatFileDecoding";
                    
                    var decodeInputs = new JObject();
                    
                    // Use parent action name for @items() reference if available
                    var decodeItemsRef = !string.IsNullOrEmpty(act.ParentActionName)
                        ? $"@items('{NormalizeName(act.ParentActionName)}')?['$content']"
                        : "@items('Flat_File_Decoding')?['$content']";
                    
                    decodeInputs["content"] = decodeItemsRef;
                    
                    // Schema reference - Logic Apps Standard format
                    var decodeSchema = new JObject();
                    decodeSchema["source"] = "LogicApp";
                    decodeSchema["name"] = "FLAT_FILE_SCHEMA_NAME_HERE"; // Placeholder
                    decodeInputs["schema"] = decodeSchema;
                    
                    flatFileDecode["inputs"] = decodeInputs;
                    
                    // Add metadata comment
                    if (!string.IsNullOrEmpty(act.Details))
                    {
                        flatFileDecode["metadata"] = new JObject
                        {
                            ["comment"] = act.Details
                        };
                    }
                    
                    return flatFileDecode;

                case "FlatFileEncoding":
                    // NEW: Flat File Encoding action
                    var flatFileEncode = new JObject();
                    flatFileEncode["type"] = "FlatFileEncoding";
                    
                    var encodeInputs = new JObject();
                    encodeInputs["content"] = "@triggerBody()"; // Input XML data
                    
                    // Schema reference - Logic Apps Standard format
                    var encodeSchema = new JObject();
                    encodeSchema["source"] = "LogicApp";
                    encodeSchema["name"] = "FLAT_FILE_SCHEMA_NAME_HERE"; // Placeholder
                    encodeInputs["schema"] = encodeSchema;
                    
                    flatFileEncode["inputs"] = encodeInputs;
                    
                    // Add metadata comment
                    if (!string.IsNullOrEmpty(act.Details))
                    {
                        flatFileEncode["metadata"] = new JObject
                        {
                            ["comment"] = act.Details
                        };
                    }
                    
                    return flatFileEncode;

                case "XmlValidation":
                    // NEW: XML Validation action
                    var xmlValidation = new JObject();
                    xmlValidation["type"] = "XmlValidation";
                    
                    var validationInputs = new JObject();
                    validationInputs["content"] = "@triggerBody()?['$content']"; // Input XML
                    
                    // Schema reference - Logic Apps Standard format
                    var validationSchema = new JObject();
                    validationSchema["source"] = "LogicApp";
                    validationSchema["name"] = "SCHEMA_NAME_HERE"; // Placeholder
                    validationInputs["schema"] = validationSchema;
                    
                    xmlValidation["inputs"] = validationInputs;
                    
                    // Add metadata comment
                    if (!string.IsNullOrEmpty(act.Details))
                    {
                        xmlValidation["metadata"] = new JObject
                        {
                            ["comment"] = act.Details
                        };
                    }
                    
                    return xmlValidation;

                case "Xslt":
                    // Transform XML using XSLT map
                    var xslt = new JObject();
                    xslt["type"] = "Xslt";
                    
                    var xsltInputs = new JObject();
                    xsltInputs["content"] = "@triggerBody()?['$content']"; // Input XML
                    
                    // Map reference - Logic Apps Standard format
                    var xsltMap = new JObject();
                    xsltMap["source"] = "LogicApp";
                    
                    // Try to extract map name from component properties
                    var mapName = "XSLT_MAP_NAME_HERE"; // Default placeholder
                    if (act.ComponentProperties.ContainsKey("XsltFilePath"))
                    {
                        mapName = act.ComponentProperties["XsltFilePath"];
                    }
                    
                    xsltMap["name"] = mapName;
                    xsltInputs["map"] = xsltMap;
                    
                    xslt["inputs"] = xsltInputs;
                    
                    // Add metadata comment
                    if (!string.IsNullOrEmpty(act.Details))
                    {
                        xslt["metadata"] = new JObject
                        {
                            ["comment"] = act.Details
                        };
                    }
                    
                    return xslt;

                case "Foreach":
                    var foreachAction = new JObject();
                    foreachAction["type"] = "Foreach";

                    // Default collection for disassemblers
                    foreachAction["foreach"] = "@triggerBody()?['items']";

                    // Build child actions
                    var foreachActions = new JObject();
                    string prevForeach = null;
                    
                    foreach (var child in act.Children.OrderBy(c => c.Sequence))
                    {
                        string childName = AllocateName(NormalizeName(child.Name), usedNames);
                        
                        // Pass parent action name to child for @items() reference
                        child.ParentActionName = act.Name;
                        
                        var childAction = BuildAction(child, usedNames);
                        
                        if (childAction == null) continue;
                        
                        childAction["runAfter"] = prevForeach == null
                            ? new JObject()
                            : new JObject { [prevForeach] = new JArray("SUCCEEDED") };
                        
                        foreachActions[childName] = childAction;
                        prevForeach = childName;
                    }
                    
                    foreachAction["actions"] = foreachActions;

                    // Set concurrency for performance
                    var runtimeConfig = new JObject();
                    runtimeConfig["concurrency"] = new JObject
                    {
                        ["repetitions"] = 20
                    };
                    foreachAction["runtimeConfiguration"] = runtimeConfig;

                    return foreachAction;

                case "Scope":
                    var scope = new JObject();
                    scope["type"] = "Scope";

                    var childObj = new JObject();
                    string prev = null;

                    foreach (var child in act.Children.OrderBy(c => c.Sequence))
                    {
                        string childName = AllocateName(NormalizeName(child.Name), usedNames);
                        var childJson = BuildAction(child, usedNames);

                        // Skip null actions
                        if (childJson == null)
                        {
                            Console.WriteLine("[GENERATOR] Skipping null child action: " + child.Name);
                            continue;
                        }

                        childJson["runAfter"] = prev == null
                            ? new JObject()
                            : new JObject { [prev] = new JArray("SUCCEEDED") };
                        
                        childObj[childName] = childJson;
                        prev = childName;
                    }
                    
                    scope["actions"] = childObj;

                    // Return null if the scope is completely empty
                    if (childObj.Count == 0)
                    {
                        Console.WriteLine("[GENERATOR] Returning null for empty scope: " + act.Name);
                        return null;
                    }

                    return scope;

                default:
                    // Unmapped action types - create placeholder
                    var unmapped = new JObject();
                    unmapped["type"] = "Compose";
                    unmapped["inputs"] = "// Unmapped pipeline action: " + act.Type + 
                                        (string.IsNullOrEmpty(act.Details) ? "" : "\n" + act.Details);
                    return unmapped;
            }
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
        /// Truncates long names (>80 chars) for Logic Apps compatibility.
        /// </summary>
        /// <param name="name">The name to normalize.</param>
        /// <returns>A normalized name safe for use in Logic Apps workflow definitions.</returns>
        private static string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Item";
            
            var chars = name.Where(char.IsLetterOrDigit).ToArray();
            var cleaned = chars.Length == 0 ? "Item" : new string(chars);
            
            // Truncate if too long (Logic Apps max name length is 80)
            if (cleaned.Length > 80)
            {
                cleaned = cleaned.Substring(0, 80);
            }
            
            return cleaned;
        }
    }
}
