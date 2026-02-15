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
            if (pipeline == null)
            {
                Console.WriteLine("Pipeline is null");
                return;
            }

            var defaultPipelineInfo = DefaultPipelineInfo.DetectDefaultPipeline(pipeline);

            Console.WriteLine("=== BizTalk Pipeline Information ===");
            Console.WriteLine($"Pipeline Type: {pipeline.GetPipelineType()}");
            Console.WriteLine($"Pipeline Pattern: {defaultPipelineInfo.Name}");
            
            if (defaultPipelineInfo.Type != DefaultPipelineType.Unknown)
            {
                Console.WriteLine($"Assembly: {defaultPipelineInfo.Assembly}");
                Console.WriteLine($"Pattern Description: {defaultPipelineInfo.Description}");
                
                if (defaultPipelineInfo.Type == DefaultPipelineType.PassThruReceive || 
                    defaultPipelineInfo.Type == DefaultPipelineType.PassThruTransmit ||
                    defaultPipelineInfo.Type == DefaultPipelineType.XMLReceive ||
                    defaultPipelineInfo.Type == DefaultPipelineType.XMLTransmit)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"[DEFAULT] DEFAULT PIPELINE DETECTED");
                    Console.ResetColor();
                }
                else if (defaultPipelineInfo.Type == DefaultPipelineType.ReceiveTemplate ||
                         defaultPipelineInfo.Type == DefaultPipelineType.TransmitTemplate)
                {
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.WriteLine($"[TEMPLATE] PIPELINE TEMPLATE DETECTED");
                    Console.ResetColor();
                    if (!string.IsNullOrEmpty(defaultPipelineInfo.TemplateFile))
                    {
                        Console.WriteLine($"Template File: {defaultPipelineInfo.TemplateFile}");
                    }
                }
                else if (defaultPipelineInfo.Type == DefaultPipelineType.Custom)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"[CUSTOM] CUSTOM PIPELINE");
                    Console.ResetColor();
                    if (!string.IsNullOrEmpty(defaultPipelineInfo.TemplateFile))
                    {
                        Console.WriteLine($"Based on: {defaultPipelineInfo.TemplateFile}");
                    }
                }
                
                Console.WriteLine($"Use Cases: {defaultPipelineInfo.UseCases}");
                
                if (!string.IsNullOrEmpty(defaultPipelineInfo.Limitations))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[!] Limitations: {defaultPipelineInfo.Limitations}");
                    Console.ResetColor();
                }
            }
            
            Console.WriteLine($"Policy File: {pipeline.PolicyFilePath}");
            Console.WriteLine($"Version: {pipeline.MajorVersion}.{pipeline.MinorVersion}");
            
            if (!string.IsNullOrEmpty(pipeline.CategoryId))
                Console.WriteLine($"Category ID: {pipeline.CategoryId}");
            
            if (!string.IsNullOrEmpty(pipeline.FriendlyName))
                Console.WriteLine($"Friendly Name: {pipeline.FriendlyName}");
            
            Console.WriteLine($"Description: {pipeline.Description ?? "(none)"}");
            Console.WriteLine();

            Console.WriteLine($"Total Stages: {pipeline.Stages.Count}");
            Console.WriteLine();

            foreach (var stage in pipeline.Stages)
            {
                var metadata = stage.GetMetadata();
                var category = ComponentCategory.GetCategory(stage.CategoryId);
                
                Console.WriteLine($"Stage: {metadata.Name} (CategoryId: {stage.CategoryId})");
                Console.WriteLine($"  Category: {category.Name}");
                Console.WriteLine($"  Purpose: {metadata.Purpose}");
                Console.WriteLine($"  Execution Mode: {metadata.ExecutionMode} (Read-Only: {metadata.IsExecutionModeReadOnly})");
                Console.WriteLine($"  Note: {metadata.ExecutionModeNote}");
                Console.WriteLine($"  Behavior: {metadata.Behavior}");
                Console.WriteLine($"  Components: {stage.Components.Count} (Min: {metadata.MinOccurs}, Max: {metadata.MaxOccurs})");

                foreach (var component in stage.Components)
                {
                    var compMetadata = component.GetMetadata();
                    
                    Console.WriteLine($"    - {component.ComponentName}");
                    Console.WriteLine($"      Type: {component.Name}");
                    Console.WriteLine($"      Component Type: {compMetadata.Type}");
                    Console.WriteLine($"      Message Flow: {compMetadata.MessageFlow}");
                    Console.WriteLine($"      Supports Probing: {compMetadata.SupportsProbing}");
                    Console.WriteLine($"      Version: {component.Version}");
                    Console.WriteLine($"      Description: {component.Description}");
                    Console.WriteLine($"      Behavior: {compMetadata.Behavior}");
                    
                    if (component.Properties.Count > 0)
                    {
                        Console.WriteLine($"      Properties:");
                        foreach (var prop in component.Properties)
                        {
                            Console.WriteLine($"        {prop.Name}: {prop.Value.Text} ({prop.Value.Type})");
                        }
                    }
                    Console.WriteLine();
                }
            }
        }
    }
}
