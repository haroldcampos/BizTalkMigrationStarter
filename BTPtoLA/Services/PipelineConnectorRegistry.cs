using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BizTalktoLogicApps.BTPtoLA.Services
{
    /// <summary>
    /// Registry for mapping BizTalk pipeline components to Logic Apps actions.
    /// Loads connector definitions from pipeline-connector-registry.json.
    /// </summary>
    public class PipelineConnectorRegistry
    {
        private static PipelineConnectorRegistry _instance;
        private static readonly object _lock = new object();
        
        private JObject _registry;
        private Dictionary<string, ComponentMapping> _componentMappings;
        
        /// <summary>
        /// Gets the singleton instance of the connector registry.
        /// </summary>
        public static PipelineConnectorRegistry Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new PipelineConnectorRegistry();
                        }
                    }
                }
                return _instance;
            }
        }
        
        private PipelineConnectorRegistry()
        {
            LoadRegistry();
        }
        
        /// <summary>
        /// Loads the connector registry from the embedded JSON file.
        /// </summary>
        private void LoadRegistry()
        {
            try
            {
                // Try to load from file system first
                var registryPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Schemas",
                    "Connectors",
                    "pipeline-connector-registry.json");
                
                string jsonContent;
                
                if (File.Exists(registryPath))
                {
                    jsonContent = File.ReadAllText(registryPath);
                }
                else
                {
                    // Fallback: Try relative path from current directory
                    var relativePath = Path.Combine("BTPtoLA", "Schemas", "Connectors", "pipeline-connector-registry.json");
                    if (File.Exists(relativePath))
                    {
                        jsonContent = File.ReadAllText(relativePath);
                    }
                    else
                    {
                        Console.WriteLine($"WARNING: Connector registry not found at {registryPath}");
                        Console.WriteLine("Using fallback empty registry");
                        jsonContent = "{ \"components\": {}, \"metadata\": {} }";
                    }
                }
                
                _registry = JObject.Parse(jsonContent);
                _componentMappings = new Dictionary<string, ComponentMapping>(StringComparer.OrdinalIgnoreCase);
                
                // Parse component mappings
                ParseComponentMappings();
                
                Console.WriteLine($"[REGISTRY] Loaded {_componentMappings.Count} component mappings");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[REGISTRY] ERROR loading connector registry: {ex.Message}");
                _registry = new JObject();
                _componentMappings = new Dictionary<string, ComponentMapping>();
            }
        }
        
        /// <summary>
        /// Parses component mappings from the registry JSON.
        /// </summary>
        private void ParseComponentMappings()
        {
            var components = _registry["components"] as JObject;
            if (components == null) return;
            
            foreach (var component in components.Properties())
            {
                try
                {
                    var mapping = new ComponentMapping
                    {
                        ComponentName = component.Name,
                        DisplayName = component.Value["displayName"]?.ToString(),
                        Category = component.Value["category"]?.ToString(),
                        ActionType = component.Value["logicAppsAction"]?["type"]?.ToString(),
                        Description = component.Value["logicAppsAction"]?["description"]?.ToString(),
                        MigrationNotes = component.Value["migrationNotes"]?.ToObject<List<string>>() ?? new List<string>(),
                        RequiredResources = component.Value["requiredResources"]?.ToObject<List<string>>() ?? new List<string>(),
                        Complexity = component.Value["complexity"]?.ToString() ?? "Medium",
                        CustomCodeRequired = component.Value["customCodeRequired"]?.ToObject<bool>() ?? false,
                        ActionTemplate = component.Value["logicAppsAction"] as JObject
                    };
                    
                    _componentMappings[component.Name] = mapping;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[REGISTRY] Error parsing component {component.Name}: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Gets the Logic Apps action mapping for a BizTalk component.
        /// </summary>
        /// <param name="componentName">Full name of the BizTalk component (e.g., Microsoft.BizTalk.Component.XmlDasmComp)</param>
        /// <returns>Component mapping or null if not found</returns>
        public ComponentMapping GetMapping(string componentName)
        {
            if (string.IsNullOrEmpty(componentName))
                return null;
            
            // Try exact match first
            if (_componentMappings.TryGetValue(componentName, out var mapping))
                return mapping;
            
            // Try partial match (e.g., "XmlDasmComp" matches "Microsoft.BizTalk.Component.XmlDasmComp")
            var partialMatch = _componentMappings.Values.FirstOrDefault(m =>
                m.ComponentName.EndsWith(componentName, StringComparison.OrdinalIgnoreCase) ||
                componentName.EndsWith(m.ComponentName.Split('.').Last(), StringComparison.OrdinalIgnoreCase));
            
            if (partialMatch != null)
                return partialMatch;
            
            // Return custom component template
            return GetCustomComponentMapping(componentName);
        }
        
        /// <summary>
        /// Gets a default mapping for unknown custom components.
        /// </summary>
        private ComponentMapping GetCustomComponentMapping(string componentName)
        {
            var customPattern = _registry["customComponents"]?["pattern"] as JObject;
            if (customPattern == null)
            {
                return new ComponentMapping
                {
                    ComponentName = componentName,
                    DisplayName = "Unknown Component",
                    ActionType = "Compose",
                    Description = "Custom component - requires manual migration",
                    Complexity = "Variable",
                    MigrationNotes = new List<string> { "Custom component detected", "Manual assessment required" }
                };
            }
            
            return new ComponentMapping
            {
                ComponentName = componentName,
                DisplayName = customPattern["displayName"]?.ToString() ?? "Custom Component",
                ActionType = customPattern["logicAppsAction"]?["type"]?.ToString() ?? "Compose",
                Description = customPattern["logicAppsAction"]?["description"]?.ToString()?.Replace("{{COMPONENT_NAME}}", componentName),
                MigrationNotes = customPattern["migrationNotes"]?.ToObject<List<string>>() ?? new List<string>(),
                Complexity = customPattern["complexity"]?.ToString() ?? "Variable",
                ActionTemplate = customPattern["logicAppsAction"] as JObject
            };
        }
        
        /// <summary>
        /// Gets all registered component mappings.
        /// </summary>
        public IEnumerable<ComponentMapping> GetAllMappings()
        {
            return _componentMappings.Values;
        }
        
        /// <summary>
        /// Gets complexity description.
        /// </summary>
        public string GetComplexityDescription(string complexity)
        {
            return _registry["metadata"]?["complexityLevels"]?[complexity]?.ToString() 
                   ?? "Unknown complexity level";
        }
        
        /// <summary>
        /// Gets required service description.
        /// </summary>
        public string GetServiceDescription(string serviceName)
        {
            return _registry["metadata"]?["requiredServices"]?[serviceName]?.ToString()
                   ?? "No description available";
        }
    }
    
    /// <summary>
    /// Represents a mapping from a BizTalk pipeline component to a Logic Apps action.
    /// </summary>
    public class ComponentMapping
    {
        public string ComponentName { get; set; }
        public string DisplayName { get; set; }
        public string Category { get; set; }
        public string ActionType { get; set; }
        public string Description { get; set; }
        public List<string> MigrationNotes { get; set; }
        public List<string> RequiredResources { get; set; }
        public string Complexity { get; set; }
        public bool CustomCodeRequired { get; set; }
        public JObject ActionTemplate { get; set; }
        
        public ComponentMapping()
        {
            MigrationNotes = new List<string>();
            RequiredResources = new List<string>();
        }
        
        /// <summary>
        /// Gets formatted migration notes for display in generated workflow.
        /// </summary>
        public string GetFormattedNotes()
        {
            var notes = new System.Text.StringBuilder();
            
            if (!string.IsNullOrEmpty(Description))
                notes.AppendLine("// " + Description);
            
            if (MigrationNotes.Count > 0)
            {
                notes.AppendLine("//");
                notes.AppendLine("// MIGRATION NOTES:");
                foreach (var note in MigrationNotes)
                    notes.AppendLine("// " + note);
            }
            
            if (RequiredResources.Count > 0)
            {
                notes.AppendLine("//");
                notes.AppendLine("// REQUIRED RESOURCES:");
                foreach (var resource in RequiredResources)
                    notes.AppendLine("//   - " + resource);
            }
            
            if (CustomCodeRequired)
            {
                notes.AppendLine("//");
                notes.AppendLine("// ?? WARNING: This component requires custom code development");
            }
            
            notes.AppendLine("//");
            notes.AppendLine($"// Migration Complexity: {Complexity}");
            
            return notes.ToString().TrimEnd();
        }
    }
}
