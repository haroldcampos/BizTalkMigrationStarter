using System;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using BizTalktoLogicApps.BTPtoLA.Models;

namespace BizTalktoLogicApps.BTPtoLA.Services
{
    public class PipelineParser
    {
        public PipelineDocument ParsePipelineFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Pipeline file not found: {filePath}");
            }

            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(PipelineDocument));
                
                using (StreamReader reader = new StreamReader(filePath))
                {
                    PipelineDocument pipeline = (PipelineDocument)serializer.Deserialize(reader);
                    return pipeline;
                }
            }
            catch (Exception ex)
            {
                var innerMsg = ex.InnerException != null ? " Inner: " + ex.InnerException.Message : "";
                throw new Exception($"Failed to parse pipeline file: {ex.Message}{innerMsg}", ex);
            }
        }

        public PipelineDocument ParsePipelineXml(string xmlContent)
        {
            if (string.IsNullOrEmpty(xmlContent))
            {
                throw new ArgumentException("XML content cannot be null or empty", nameof(xmlContent));
            }

            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(PipelineDocument));
                
                using (StringReader reader = new StringReader(xmlContent))
                {
                    PipelineDocument pipeline = (PipelineDocument)serializer.Deserialize(reader);
                    return pipeline;
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to parse pipeline XML: {ex.Message}", ex);
            }
        }

        public void DisplayPipelineInfo(PipelineDocument pipeline)
        {
            DisplayPipelineInfo(pipeline, Console.Out);
        }

        public void DisplayPipelineInfo(PipelineDocument pipeline, TextWriter writer)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));

            if (pipeline == null)
            {
                writer.WriteLine("Pipeline is null");
                return;
            }

            var defaultPipelineInfo = DefaultPipelineInfo.DetectDefaultPipeline(pipeline);

            writer.WriteLine("=== BizTalk Pipeline Information ===");
            writer.WriteLine($"Pipeline Type: {pipeline.GetPipelineType()}");
            writer.WriteLine($"Pipeline Pattern: {defaultPipelineInfo.Name}");

            if (defaultPipelineInfo.Type != DefaultPipelineType.Unknown)
            {
                writer.WriteLine($"Assembly: {defaultPipelineInfo.Assembly}");
                writer.WriteLine($"Pattern Description: {defaultPipelineInfo.Description}");

                if (defaultPipelineInfo.Type == DefaultPipelineType.PassThruReceive ||
                    defaultPipelineInfo.Type == DefaultPipelineType.PassThruTransmit ||
                    defaultPipelineInfo.Type == DefaultPipelineType.XMLReceive ||
                    defaultPipelineInfo.Type == DefaultPipelineType.XMLTransmit)
                {
                    writer.WriteLine("[DEFAULT] DEFAULT PIPELINE DETECTED");
                }
                else if (defaultPipelineInfo.Type == DefaultPipelineType.ReceiveTemplate ||
                         defaultPipelineInfo.Type == DefaultPipelineType.TransmitTemplate)
                {
                    writer.WriteLine("[TEMPLATE] PIPELINE TEMPLATE DETECTED");
                    if (!string.IsNullOrEmpty(defaultPipelineInfo.TemplateFile))
                    {
                        writer.WriteLine($"Template File: {defaultPipelineInfo.TemplateFile}");
                    }
                }
                else if (defaultPipelineInfo.Type == DefaultPipelineType.Custom)
                {
                    writer.WriteLine("[CUSTOM] CUSTOM PIPELINE");
                    if (!string.IsNullOrEmpty(defaultPipelineInfo.TemplateFile))
                    {
                        writer.WriteLine($"Based on: {defaultPipelineInfo.TemplateFile}");
                    }
                }

                writer.WriteLine($"Use Cases: {defaultPipelineInfo.UseCases}");

                if (!string.IsNullOrEmpty(defaultPipelineInfo.Limitations))
                {
                    writer.WriteLine($"[!] Limitations: {defaultPipelineInfo.Limitations}");
                }
            }

            writer.WriteLine($"Policy File: {pipeline.PolicyFilePath}");
            writer.WriteLine($"Version: {pipeline.MajorVersion}.{pipeline.MinorVersion}");

            if (!string.IsNullOrEmpty(pipeline.CategoryId))
                writer.WriteLine($"Category ID: {pipeline.CategoryId}");

            if (!string.IsNullOrEmpty(pipeline.FriendlyName))
                writer.WriteLine($"Friendly Name: {pipeline.FriendlyName}");

            writer.WriteLine($"Description: {pipeline.Description ?? "(none)"}");
            writer.WriteLine();

            writer.WriteLine($"Total Stages: {pipeline.Stages.Count}");
            writer.WriteLine();

            foreach (var stage in pipeline.Stages)
            {
                var metadata = stage.GetMetadata();
                var category = ComponentCategory.GetCategory(stage.CategoryId);

                writer.WriteLine($"Stage: {metadata.Name} (CategoryId: {stage.CategoryId})");
                writer.WriteLine($"  Category: {category.Name}");
                writer.WriteLine($"  Purpose: {metadata.Purpose}");
                writer.WriteLine($"  Execution Mode: {metadata.ExecutionMode} (Read-Only: {metadata.IsExecutionModeReadOnly})");
                writer.WriteLine($"  Note: {metadata.ExecutionModeNote}");
                writer.WriteLine($"  Behavior: {metadata.Behavior}");
                writer.WriteLine($"  Components: {stage.Components.Count} (Min: {metadata.MinOccurs}, Max: {metadata.MaxOccurs})");

                foreach (var component in stage.Components)
                {
                    var compMetadata = component.GetMetadata();

                    writer.WriteLine($"    - {component.ComponentName}");
                    writer.WriteLine($"      Type: {component.Name}");
                    writer.WriteLine($"      Component Type: {compMetadata.Type}");
                    writer.WriteLine($"      Message Flow: {compMetadata.MessageFlow}");
                    writer.WriteLine($"      Supports Probing: {compMetadata.SupportsProbing}");
                    writer.WriteLine($"      Version: {component.Version}");
                    writer.WriteLine($"      Description: {component.Description}");
                    writer.WriteLine($"      Behavior: {compMetadata.Behavior}");

                    if (component.Properties.Count > 0)
                    {
                        writer.WriteLine("      Properties:");
                        foreach (var prop in component.Properties)
                        {
                            writer.WriteLine($"        {prop.Name}: {prop.Value.Text} ({prop.Value.Type})");
                        }
                    }
                    writer.WriteLine();
                }
            }
        }
    }
}
