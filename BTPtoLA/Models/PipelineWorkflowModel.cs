using System.Collections.Generic;

namespace BizTalktoLogicApps.BTPtoLA.Models
{
    /// <summary>
    /// Represents a complete Azure Logic Apps workflow generated from a BizTalk pipeline.
    /// This is the intermediate model used for mapping BizTalk pipelines to Logic Apps.
    /// Simplified from ODXtoWFMigrator's LogicAppWorkflowMap to include only pipeline-relevant properties.
    /// </summary>
    public sealed class PipelineWorkflowModel
    {
        /// <summary>
        /// Gets or sets the workflow name (typically the pipeline name).
        /// </summary>
        public string Name { get; set; }
        
        /// <summary>
        /// Gets the collection of workflow triggers (typically one per workflow).
        /// </summary>
        public List<PipelineWorkflowTrigger> Triggers { get; } = new List<PipelineWorkflowTrigger>();
        
        /// <summary>
        /// Gets the collection of workflow actions in execution sequence.
        /// </summary>
        public List<PipelineWorkflowAction> Actions { get; } = new List<PipelineWorkflowAction>();
    }

    /// <summary>
    /// Represents a Logic Apps workflow trigger for pipeline processing.
    /// Simplified from ODXtoWFMigrator's LogicAppTrigger - pipelines don't have transport configuration.
    /// </summary>
    public sealed class PipelineWorkflowTrigger
    {
        /// <summary>Gets or sets the trigger name.</summary>
        public string Name { get; set; }
        
        /// <summary>Gets or sets the trigger kind (e.g., "Request", "HTTP").</summary>
        public string Kind { get; set; }
        
        /// <summary>Gets or sets the transport type (always "HTTP" for pipelines).</summary>
        public string TransportType { get; set; }
        
        /// <summary>Gets or sets the sequence number.</summary>
        public int Sequence { get; set; }
    }

    /// <summary>
    /// Represents a Logic Apps workflow action for pipeline component processing.
    /// Simplified from ODXtoWFMigrator's LogicAppAction to include only pipeline-relevant properties.
    /// </summary>
    public sealed class PipelineWorkflowAction
    {
        /// <summary>Gets or sets the action name.</summary>
        public string Name { get; set; }
        
        /// <summary>Gets or sets the action type (e.g., "Compose", "Foreach", "Scope").</summary>
        public string Type { get; set; }
        
        /// <summary>Gets or sets the action details (typically component metadata as comments).</summary>
        public string Details { get; set; }
        
        /// <summary>Gets or sets the sequence number in the workflow.</summary>
        public int Sequence { get; set; }
        
        /// <summary>Gets or sets the parent action name (used for @items() references in Foreach children).</summary>
        public string ParentActionName { get; set; }
        
        /// <summary>Gets the child actions (for Scope, Foreach, etc.).</summary>
        public List<PipelineWorkflowAction> Children { get; } = new List<PipelineWorkflowAction>();
        
        /// <summary>
        /// Gets or sets the component properties preserved from the pipeline.
        /// Key-value pairs of component configuration settings.
        /// </summary>
        public Dictionary<string, string> ComponentProperties { get; set; } = new Dictionary<string, string>();
    }
}
