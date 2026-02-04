using System;

namespace BizTalktoLogicApps.BTPtoLA.Models
{
    public enum DefaultPipelineType
    {
        PassThruReceive,
        PassThruTransmit,
        XMLReceive,
        XMLTransmit,
        ReceiveTemplate,
        TransmitTemplate,
        Custom,
        Unknown
    }

    public class DefaultPipelineInfo
    {
        public DefaultPipelineType Type { get; set; }
        public string Name { get; set; }
        public string Assembly { get; set; }
        public string Description { get; set; }
        public string UseCases { get; set; }
        public string Limitations { get; set; }
        public string TemplateFile { get; set; }
        public string PolicyFile { get; set; }

        public static DefaultPipelineInfo DetectDefaultPipeline(PipelineDocument pipeline)
        {
            if (pipeline == null)
                return CreateUnknown();

            string pipelineType = pipeline.GetPipelineType();
            string policyFile = pipeline.PolicyFilePath ?? string.Empty;
            
            bool hasComponents = false;
            bool hasXmlDisassembler = false;
            bool hasXmlAssembler = false;
            bool hasPartyResolution = false;

            foreach (var stage in pipeline.Stages)
            {
                if (stage.Components.Count > 0)
                {
                    hasComponents = true;
                    
                    foreach (var component in stage.Components)
                    {
                        if (component.Name != null && component.Name.Contains("XmlDasmComp"))
                            hasXmlDisassembler = true;
                        if (component.Name != null && component.Name.Contains("XmlAsmComp"))
                            hasXmlAssembler = true;
                        if (component.Name != null && component.Name.Contains("PartyRes"))
                            hasPartyResolution = true;
                    }
                }
            }

            // Check for Template patterns (empty pipelines that follow template structure)
            if (!hasComponents)
            {
                // Receive Template: Empty receive pipeline with BTSReceivePolicy.xml
                if (pipelineType == "Receive" && policyFile.Contains("Receive"))
                {
                    return new DefaultPipelineInfo
                    {
                        Type = DefaultPipelineType.ReceiveTemplate,
                        Name = "Receive Pipeline Template",
                        Assembly = "Custom (from template)",
                        Description = "Empty receive pipeline template from BTSReceivePipeline.btp",
                        UseCases = "Starting point for creating custom receive pipelines. Modify in Pipeline Designer to add components.",
                        Limitations = "Template must be customized before use. Cannot route to orchestrations or promote properties without components.",
                        TemplateFile = "BTSReceivePipeline.btp",
                        PolicyFile = "BTSReceivePolicy.xml"
                    };
                }

                // Transmit Template: Empty send pipeline with BTSTransmitPolicy.xml
                if (pipelineType == "Send" && policyFile.Contains("Transmit"))
                {
                    return new DefaultPipelineInfo
                    {
                        Type = DefaultPipelineType.TransmitTemplate,
                        Name = "Send Pipeline Template",
                        Assembly = "Custom (from template)",
                        Description = "Empty send pipeline template from BTSTransmitPipeline.btp",
                        UseCases = "Starting point for creating custom send pipelines. Modify in Pipeline Designer to add components.",
                        Limitations = "Template must be customized before use. No message processing without components.",
                        TemplateFile = "BTSTransmitPipeline.btp",
                        PolicyFile = "BTSTransmitPolicy.xml"
                    };
                }
            }

            // PassThruReceive: Receive pipeline with no components
            if (pipelineType == "Receive" && !hasComponents)
            {
                return new DefaultPipelineInfo
                {
                    Type = DefaultPipelineType.PassThruReceive,
                    Name = "PassThruReceive",
                    Assembly = "Microsoft.BizTalk.DefaultPipelines",
                    Description = "Pass-through receive pipeline with no components for simple pass-through scenarios",
                    UseCases = "When source and destination are known, no validation/encoding/disassembling needed. Commonly used with PassThruTransmit.",
                    Limitations = "Cannot route messages to orchestrations (no disassembler). Does not support property promotion.",
                    TemplateFile = null,
                    PolicyFile = "BTSReceivePolicy.xml"
                };
            }

            // PassThruTransmit: Send pipeline with no components
            if (pipelineType == "Send" && !hasComponents)
            {
                return new DefaultPipelineInfo
                {
                    Type = DefaultPipelineType.PassThruTransmit,
                    Name = "PassThruTransmit",
                    Assembly = "Microsoft.BizTalk.DefaultPipelines",
                    Description = "Pass-through send pipeline with no components",
                    UseCases = "When no document processing is necessary before sending the message to destination",
                    Limitations = "No message transformation or encoding",
                    TemplateFile = null,
                    PolicyFile = "BTSTransmitPolicy.xml"
                };
            }

            // XMLReceive: Receive pipeline with XML Disassembler and Party Resolution
            if (pipelineType == "Receive" && hasXmlDisassembler)
            {
                return new DefaultPipelineInfo
                {
                    Type = DefaultPipelineType.XMLReceive,
                    Name = "XMLReceive",
                    Assembly = "Microsoft.BizTalk.DefaultPipelines",
                    Description = "XML receive pipeline with XML Disassembler in Disassemble stage and Party Resolution in ResolveParty stage",
                    UseCases = "Processing XML messages, disassembling envelopes, resolving parties from certificates or security IDs",
                    Limitations = "Does not support XML documents larger than 4 gigabytes",
                    TemplateFile = null,
                    PolicyFile = "BTSReceivePolicy.xml"
                };
            }

            // XMLTransmit: Send pipeline with XML Assembler
            if (pipelineType == "Send" && hasXmlAssembler)
            {
                return new DefaultPipelineInfo
                {
                    Type = DefaultPipelineType.XMLTransmit,
                    Name = "XMLTransmit",
                    Assembly = "Microsoft.BizTalk.DefaultPipelines",
                    Description = "XML send pipeline with XML Assembler in Assemble stage",
                    UseCases = "Assembling XML messages with envelopes before sending",
                    Limitations = "Does not support XML documents larger than 4 gigabytes",
                    TemplateFile = null,
                    PolicyFile = "BTSTransmitPolicy.xml"
                };
            }

            // Custom pipeline
            if (hasComponents)
            {
                return new DefaultPipelineInfo
                {
                    Type = DefaultPipelineType.Custom,
                    Name = "Custom Pipeline",
                    Assembly = "Custom",
                    Description = "Custom pipeline with specific components configured",
                    UseCases = "Specialized message processing requirements",
                    Limitations = "Varies based on components used",
                    TemplateFile = "Created from BTSReceivePipeline.btp or BTSTransmitPipeline.btp template",
                    PolicyFile = policyFile
                };
            }

            return CreateUnknown();
        }

        private static DefaultPipelineInfo CreateUnknown()
        {
            return new DefaultPipelineInfo
            {
                Type = DefaultPipelineType.Unknown,
                Name = "Unknown",
                Assembly = "Unknown",
                Description = "Unknown pipeline type",
                UseCases = "Unknown",
                Limitations = "Unknown",
                TemplateFile = null,
                PolicyFile = null
            };
        }
    }
}
