using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BizTalktoLogicApps.BTPtoLA.Models;
using BizTalktoLogicApps.BTPtoLA.Services;

namespace BizTalktoLogicApps.BTPtoLA.Services
{
    /// <summary>
    /// Maps BizTalk pipeline documents to Azure Logic Apps workflow models.
    /// This is the core mapper that converts PipelineDocument (from BTPtoLA parser)
    /// to PipelineWorkflowModel (intermediate model for JSON generation).
    /// </summary>
    public static class PipelineWorkflowMapper
    {
        /// <summary>
        /// Maps a parsed BizTalk pipeline to a Logic Apps workflow model.
        /// </summary>
        /// <param name="pipeline">The parsed pipeline document.</param>
        /// <param name="workflowName">Optional custom workflow name (defaults to pipeline FriendlyName or "Pipeline").</param>
        /// <returns>A workflow model ready for JSON generation.</returns>
        /// <exception cref="ArgumentNullException">Thrown when pipeline is null.</exception>
        public static PipelineWorkflowModel MapPipelineToWorkflow(
            PipelineDocument pipeline, 
            string workflowName = null)
        {
            if (pipeline == null)
                throw new ArgumentNullException(nameof(pipeline));
            
            // Priority: 1) Explicitly provided workflowName, 2) Pipeline FriendlyName, 3) Default "Pipeline"
            var finalName = !string.IsNullOrWhiteSpace(workflowName) 
                ? workflowName 
                : (pipeline.FriendlyName ?? "Pipeline");
            
            var workflow = new PipelineWorkflowModel
            {
                Name = SanitizeName(finalName)
            };
            
            // Add trigger (always Request for pipelines)
            workflow.Triggers.Add(CreateTrigger(pipeline));
            
            // Detect PassThru optimization
            var defaultInfo = DefaultPipelineInfo.DetectDefaultPipeline(pipeline);
            if (IsPassThru(defaultInfo))
            {
                workflow.Actions.Add(CreatePassThruNote(pipeline, defaultInfo));
                return workflow;
            }
            
            // Get pipeline type to determine stage processing order
            var pipelineType = pipeline.GetPipelineType();
            
            // Convert stages to actions based on pipeline type
            int seq = 0;
            
            if (pipelineType == "Receive")
            {
                // Receive pipeline stages (in order):
                // 1. Decode (e.g., MIME/SMIME Decoder)
                // 2. Disassemble (e.g., XML Disassembler)
                // 3. Validate (e.g., XML Validator)
                // 4. ResolveParty (e.g., Party Resolution)
                
                MapReceivePipelineStages(pipeline, workflow, ref seq);
            }
            else
            {
                // Send pipeline stages (in order):
                // 1. PreAssemble (rarely used)
                // 2. Assemble (e.g., XML Assembler)
                // 3. Encode (e.g., MIME/SMIME Encoder)
                
                MapSendPipelineStages(pipeline, workflow, ref seq);
            }
            
            return workflow;
        }
        
        /// <summary>
        /// Maps Receive pipeline stages to workflow actions.
        /// Receive pipelines have 4 stages: Decode, Disassemble, Validate, ResolveParty.
        /// </summary>
        private static void MapReceivePipelineStages(
            PipelineDocument pipeline, 
            PipelineWorkflowModel workflow, 
            ref int seq)
        {
            // Standard receive pipeline stage order
            var stageOrder = new[]
            {
                "Decode",        // Stage 1: MIME/SMIME decoding, decompression
                "Disassemble",   // Stage 2: XML/Flat File disassembly, debatching
                "Validate",      // Stage 3: Schema validation
                "ResolveParty"   // Stage 4: Party resolution for trading partners
            };
            
            foreach (var expectedStageName in stageOrder)
            {
                var stage = pipeline.Stages.FirstOrDefault(s => 
                    s.GetMetadata().Name.Equals(expectedStageName, StringComparison.OrdinalIgnoreCase));
                
                if (stage != null && stage.Components.Count > 0)
                {
                    var stageScope = CreateStageScope(stage, seq++);
                    
                    foreach (var component in stage.Components)
                    {
                        var action = MapComponentToAction(component, stage, seq++);
                        if (action != null)
                        {
                            stageScope.Children.Add(action);
                        }
                    }
                    
                    workflow.Actions.Add(stageScope);
                }
            }
        }
        
        /// <summary>
        /// Maps Send pipeline stages to workflow actions.
        /// Send pipelines have 3 stages: PreAssemble, Assemble, Encode.
        /// </summary>
        private static void MapSendPipelineStages(
            PipelineDocument pipeline, 
            PipelineWorkflowModel workflow, 
            ref int seq)
        {
            // Standard send pipeline stage order
            var stageOrder = new[]
            {
                "PreAssemble",   // Stage 1: Pre-assembly processing (rarely used)
                "Assemble",      // Stage 2: XML/Flat File assembly, enveloping
                "Encode"         // Stage 3: MIME/SMIME encoding, compression
            };
            
            foreach (var expectedStageName in stageOrder)
            {
                var stage = pipeline.Stages.FirstOrDefault(s => 
                    s.GetMetadata().Name.Equals(expectedStageName, StringComparison.OrdinalIgnoreCase));
                
                if (stage != null && stage.Components.Count > 0)
                {
                    var stageScope = CreateStageScope(stage, seq++);
                    
                    foreach (var component in stage.Components)
                    {
                        var action = MapComponentToAction(component, stage, seq++);
                        if (action != null)
                        {
                            stageScope.Children.Add(action);
                        }
                    }
                    
                    workflow.Actions.Add(stageScope);
                }
            }
        }
        
        /// <summary>
        /// Creates a trigger based on pipeline type (Receive vs Send).
        /// Both types use HTTP Request triggers with standardized naming.
        /// </summary>
        private static PipelineWorkflowTrigger CreateTrigger(PipelineDocument pipeline)
        {
            var pipelineType = pipeline.GetPipelineType();
            
            return new PipelineWorkflowTrigger
            {
                Name = pipelineType == "Receive" 
                    ? "pipeline_receive" 
                    : "pipeline_send",
                Kind = "Request",
                TransportType = "HTTP",
                Sequence = 0
            };
        }
        
        /// <summary>
        /// Maps a pipeline component to a Logic Apps action.
        /// Uses component metadata to determine action type and preserve configuration.
        /// </summary>
        private static PipelineWorkflowAction MapComponentToAction(
            PipelineComponent component,
            PipelineStage stage,
            int sequence)
        {
            if (component == null)
                return null;
                
            var metadata = component.GetMetadata();
            var stageMetadata = stage?.GetMetadata();
            
            var action = new PipelineWorkflowAction
            {
                Name = SanitizeName(component.ComponentName ?? component.Name),
                Sequence = sequence
            };
            
            // Determine action type based on component metadata
            var componentType = metadata?.Type ?? ComponentType.General;
            
            switch (componentType)
            {
                case ComponentType.Disassembling:
                    action.Type = "Foreach"; // Disassemblers produce 0-N messages
                    action.Details = BuildComponentDetails(component, metadata);
                    
                    // Add child action based on disassembler type
                    if (metadata != null && metadata.Name != null)
                    {
                        if (metadata.Name.Contains("XML"))
                        {
                            // XML Disassembler ? XmlParse with schema
                            action.Children.Add(new PipelineWorkflowAction
                            {
                                Name = "Parse_XML_with_Schema",
                                Type = "XmlParse",
                                Details = "// Use 'Parse XML with schema' action\n" +
                                         "// ACTION REQUIRED:\n" +
                                         "// 1. Upload XSD schema to Logic App artifacts\n" +
                                         "// 2. Select schema in Parse XML action\n" +
                                         "// 3. Configure XPath expressions for property extraction\n" +
                                         "// 4. Map promoted properties from BizTalk schema annotations",
                                Sequence = 0
                            });
                        }
                        else if (metadata.Name.Contains("Flat File") || metadata.Name.Contains("FF"))
                        {
                            // Flat File Disassembler ? Flat File Decoding
                            action.Children.Add(new PipelineWorkflowAction
                            {
                                Name = "Flat_File_Decoding",
                                Type = "FlatFileDecoding",
                                Details = "// Use 'Flat file decoding' action\n" +
                                         "// ACTION REQUIRED:\n" +
                                         "// 1. Export flat file schema from BizTalk\n" +
                                         "// 2. Upload to Integration Account\n" +
                                         "// 3. Select schema in Flat File Decoding action\n" +
                                         "// 4. Schema name from property: " + (component.Properties?.FirstOrDefault(p => p.Name == "DocumentSpecName")?.Value?.Text ?? "SCHEMA_NAME_HERE"),
                                Sequence = 0
                            });
                        }
                    }
                    break;
                
                case ComponentType.Assembling:
                    // Determine assembler type and use appropriate action
                    if (metadata != null && metadata.Name != null)
                    {
                        if (metadata.Name.Contains("XML"))
                        {
                            // XML Assembler ? XmlCompose with schema
                            action.Type = "XmlCompose";
                            action.Details = BuildComponentDetails(component, metadata);
                            action.Details += "\n// Use 'Compose XML with schema' action";
                            action.Details += "\n// ACTION REQUIRED:";
                            action.Details += "\n// 1. Upload XSD schema to Logic App artifacts";
                            action.Details += "\n// 2. Select schema in Compose XML action";
                            action.Details += "\n// 3. Map input data to schema structure (as JSON object)";
                            action.Details += "\n// 4. Configure envelope if needed";
                        }
                        else if (metadata.Name.Contains("Flat File") || metadata.Name.Contains("FF"))
                        {
                            // Flat File Assembler ? Flat File Encoding
                            action.Type = "FlatFileEncoding";
                            action.Details = BuildComponentDetails(component, metadata);
                            action.Details += "\n// Use 'Flat file encoding' action";
                            action.Details += "\n// ACTION REQUIRED:";
                            action.Details += "\n// 1. Export flat file schema from BizTalk";
                            action.Details += "\n// 2. Upload to Logic App artifacts";
                            action.Details += "\n// 3. Select schema in Flat File Encoding action";
                            action.Details += "\n// 4. Ensure input is XML matching schema structure";
                        }
                        else
                        {
                            action.Type = "Compose";
                            action.Details = BuildComponentDetails(component, metadata);
                        }
                    }
                    else
                    {
                        action.Type = "Compose";
                        action.Details = BuildComponentDetails(component, metadata);
                    }
                    break;
                
                case ComponentType.General:
                default:
                    action.Type = "Compose";
                    action.Details = BuildComponentDetails(component, metadata);
                    
                    // Add migration warnings and use specific action types for known components
                    if (metadata != null && metadata.Name != null)
                    {
                        if (metadata.Name.Contains("MIME"))
                        {
                            // Use InvokeFunction for MIME components
                            action.Type = "InvokeFunction";
                            action.Details = BuildComponentDetails(component, metadata);
                            action.Details += "\n// ?? WARNING: MIME processing requires Azure Functions";
                            action.Details += "\n// IMPLEMENTATION: Deploy Azure Function with MimeKit library";
                            action.Details += "\n// FUNCTION NAME: " + (metadata.Name.Contains("Decoder") ? "DecodeMimeSmimeMessage" : "EncodeMimeSmimeMessage");
                            
                            // Add function parameters
                            action.ComponentProperties["functionName"] = metadata.Name.Contains("Decoder") 
                                ? "DecodeMimeSmimeMessage" 
                                : "EncodeMimeSmimeMessage";
                            action.ComponentProperties["messageContent"] = "@triggerBody()?['$content']";
                            
                            if (metadata.Name.Contains("Decoder"))
                            {
                                action.ComponentProperties["validateSignature"] = "true";
                                action.ComponentProperties["decryptMessage"] = "true";
                            }
                            else
                            {
                                action.ComponentProperties["encryptMessage"] = "true";
                                action.ComponentProperties["signMessage"] = "true";
                            }
                        }
                        else if (metadata.Name.Contains("Party Resolution"))
                        {
                            // Use InvokeFunction for Party Resolution
                            action.Type = "InvokeFunction";
                            action.Details = BuildComponentDetails(component, metadata);
                            action.Details += "\n// MIGRATION: Implement via Azure Functions with data store lookup";
                            action.Details += "\n// FUNCTION NAME: ResolvePartner";
                            action.Details += "\n// DATA STORE: Azure Table Storage or SQL Database";
                            
                            // Add function parameters
                            action.ComponentProperties["functionName"] = "ResolvePartner";
                            action.ComponentProperties["certificateThumbprint"] = "@triggerBody()?['CertificateThumbprint']";
                            action.ComponentProperties["windowsSID"] = "@triggerBody()?['WindowsSID']";
                            action.ComponentProperties["resolutionMode"] = "CertificateThenSID";
                        }
                        else if (metadata.Name.Contains("XslTransform") || metadata.Name.Contains("XSL Transform") || 
                                 component.Name.Contains("XslTransform"))
                        {
                            // Use Xslt action for XSLT transformations
                            action.Type = "Xslt";
                            action.Details = BuildComponentDetails(component, metadata);
                            action.Details += "\n// Use 'Transform XML' action with XSLT";
                            action.Details += "\n// ACTION REQUIRED:";
                            action.Details += "\n// 1. Export XSLT file from BizTalk pipeline component";
                            action.Details += "\n// 2. Upload XSLT to Logic App artifacts/Maps folder";
                            action.Details += "\n// 3. Test transformation with sample XML";
                        }
                    }
                    break;
            }
            
            // Add stage execution mode context
            if (stageMetadata != null)
            {
                var executionMode = stageMetadata.ExecutionMode.ToString();
                action.Details += "\n// Stage: " + (stageMetadata.Name ?? "Unknown") + " (" + executionMode + ")";
            }
            
            // Preserve component properties
            if (component.Properties != null && component.Properties.Count > 0)
            {
                foreach (var prop in component.Properties)
                {
                    if (prop != null && prop.Value != null && !string.IsNullOrEmpty(prop.Name))
                    {
                        action.ComponentProperties[prop.Name] = prop.Value.Text ?? "";
                    }
                }
            }
            
            return action;
        }
        
        /// <summary>
        /// Builds detailed component documentation for the action Details property.
        /// </summary>
        private static string BuildComponentDetails(
            PipelineComponent component, 
            ComponentMetadata metadata)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// Component: " + (metadata?.Name ?? "Unknown"));
            sb.AppendLine("// Description: " + (metadata?.Description ?? "No description"));
            sb.AppendLine("// Behavior: " + (metadata?.Behavior ?? "Unknown behavior"));
            sb.AppendLine("// Message Flow: " + (metadata?.MessageFlow ?? "Unknown flow"));
            
            if (component != null && component.Properties != null && component.Properties.Count > 0)
            {
                sb.AppendLine("// Properties:");
                foreach (var prop in component.Properties)
                {
                    if (prop != null && prop.Value != null)
                    {
                        sb.AppendLine("//   " + prop.Name + " = " + (prop.Value.Text ?? ""));
                    }
                }
            }
            
            return sb.ToString().TrimEnd();
        }
        
        /// <summary>
        /// Creates a Scope action to group components within a pipeline stage.
        /// </summary>
        private static PipelineWorkflowAction CreateStageScope(
            PipelineStage stage, 
            int sequence)
        {
            var metadata = stage.GetMetadata();
            
            return new PipelineWorkflowAction
            {
                Name = metadata.Name,
                Type = "Scope",
                Details = "// Pipeline Stage: " + metadata.Name + "\n" +
                         "// Execution Mode: " + metadata.ExecutionMode + "\n" +
                         "// Description: " + metadata.Description,
                Sequence = sequence
            };
        }
        
        /// <summary>
        /// Checks if the pipeline is a PassThru (no processing) pipeline.
        /// </summary>
        private static bool IsPassThru(DefaultPipelineInfo info)
        {
            return info.Type == DefaultPipelineType.PassThruReceive ||
                   info.Type == DefaultPipelineType.PassThruTransmit;
        }
        
        /// <summary>
        /// Creates a note action for PassThru pipelines (optimization).
        /// </summary>
        private static PipelineWorkflowAction CreatePassThruNote(
            PipelineDocument pipeline, 
            DefaultPipelineInfo info)
        {
            return new PipelineWorkflowAction
            {
                Name = "PassThru_Pipeline_Note",
                Type = "Compose",
                Details = "// This is a PassThru pipeline - no message processing\n" +
                         "// Original: " + (pipeline.FriendlyName ?? info.Name) + "\n" +
                         "// Description: " + info.Description,
                Sequence = 0
            };
        }
        
        /// <summary>
        /// Sanitizes a name to make it valid for Logic Apps action names.
        /// Removes invalid characters and ensures the name doesn't start with a digit.
        /// </summary>
        private static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Unnamed";
            
            var cleaned = new string(name.Where(c =>
                char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
            
            if (string.IsNullOrEmpty(cleaned)) return "Item";
            
            if (char.IsDigit(cleaned[0]))
            {
                cleaned = "Action_" + cleaned;
            }
            
            return cleaned;
        }
    }
}
