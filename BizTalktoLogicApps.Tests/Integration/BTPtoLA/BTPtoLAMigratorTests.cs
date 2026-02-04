// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BizTalktoLogicApps.BTPtoLA.Models;
using BizTalktoLogicApps.BTPtoLA.Services;

namespace BizTalktoLogicApps.Tests.Integration.BTPtoLA
{
    /// <summary>
    /// Integration tests for the BizTalk Pipeline to Logic Apps migration.
    /// Tests end-to-end conversion of BizTalk Pipelines (.btp) to Logic Apps workflows.
    /// Supports dynamic discovery - customers can add their own pipeline files to the test data directory.
    /// </summary>
    [TestClass]
    public class BTPtoLAMigratorTests
    {
        private string testDataDirectory;
        private string pipelinesDirectory;
        private string outputDirectory;

        [TestInitialize]
        public void Setup()
        {
            var assemblyLocation = Assembly.GetExecutingAssembly().Location;
            var assemblyDirectory = Path.GetDirectoryName(assemblyLocation);

            // Use test project directory for test data (2 levels up from bin\Debug)
            this.testDataDirectory = Path.Combine(assemblyDirectory, "..", "..", "Data");
            this.pipelinesDirectory = Path.Combine(this.testDataDirectory, "BizTalk", "Pipelines");
            this.outputDirectory = Path.Combine(this.testDataDirectory, "LogicApps", "Pipelines");
            
            Directory.CreateDirectory(this.pipelinesDirectory);
            Directory.CreateDirectory(this.outputDirectory);
        }

        [TestCleanup]
        public void Cleanup()
        {
            // Do not delete the output directory - keep generated workflows for reference
        }

        #region Argument Validation Tests

        [TestMethod]
        [ExpectedException(typeof(FileNotFoundException))]
        public void ParsePipelineFile_NonExistentFile_ThrowsFileNotFoundException()
        {
            // Arrange
            var nonExistentFile = Path.Combine(this.pipelinesDirectory, "NonExistent.btp");
            var parser = new PipelineParser();

            // Act
            parser.ParsePipelineFile(filePath: nonExistentFile);
        }

        #endregion

        #region Sample Pipeline Tests

        [TestMethod]
        public void GenerateWorkflow_PassThruReceive_CreatesWorkflow()
        {
            // Arrange
            var pipelineXml = this.CreatePassThruReceivePipelineXml();
            var pipelineName = "PassThruReceive";
            var pipelineFile = Path.Combine(this.pipelinesDirectory, $"{pipelineName}.btp");
            File.WriteAllText(pipelineFile, pipelineXml);

            var parser = new PipelineParser();

            // Act
            var pipeline = parser.ParsePipelineFile(filePath: pipelineFile);
            var workflow = PipelineWorkflowMapper.MapPipelineToWorkflow(
                pipeline: pipeline,
                workflowName: pipelineName);
            var json = PipelineJSONGenerator.GenerateWorkflowJSON(
                workflow: workflow,
                workflowKind: "Stateful");
            var outputFile = Path.Combine(this.outputDirectory, $"{pipelineName}_workflow.json");
            File.WriteAllText(outputFile, json);

            // Assert
            Assert.IsTrue(condition: File.Exists(outputFile), message: "Workflow file should be created");
            Assert.IsFalse(condition: string.IsNullOrEmpty(json), message: "Workflow JSON should not be empty");
        }

        [TestMethod]
        public void GenerateWorkflow_XmlReceive_CreatesWorkflow()
        {
            // Arrange
            var pipelineXml = this.CreateXmlReceivePipelineXml();
            var pipelineName = "XMLReceive";
            var pipelineFile = Path.Combine(this.pipelinesDirectory, $"{pipelineName}.btp");
            File.WriteAllText(pipelineFile, pipelineXml);

            var parser = new PipelineParser();

            // Act
            var pipeline = parser.ParsePipelineFile(filePath: pipelineFile);
            var workflow = PipelineWorkflowMapper.MapPipelineToWorkflow(
                pipeline: pipeline,
                workflowName: pipelineName);
            var json = PipelineJSONGenerator.GenerateWorkflowJSON(
                workflow: workflow,
                workflowKind: "Stateful");
            var outputFile = Path.Combine(this.outputDirectory, $"{pipelineName}_workflow.json");
            File.WriteAllText(outputFile, json);

            // Assert
            Assert.IsTrue(condition: File.Exists(outputFile), message: "Workflow file should be created");
            Assert.IsTrue(condition: json.Contains("\"triggers\""), message: "Workflow should have triggers");
        }

        [TestMethod]
        public void GenerateWorkflow_PassThruTransmit_CreatesWorkflow()
        {
            // Arrange
            var pipelineXml = this.CreatePassThruTransmitPipelineXml();
            var pipelineName = "PassThruTransmit";
            var pipelineFile = Path.Combine(this.pipelinesDirectory, $"{pipelineName}.btp");
            File.WriteAllText(pipelineFile, pipelineXml);

            var parser = new PipelineParser();

            // Act
            var pipeline = parser.ParsePipelineFile(filePath: pipelineFile);
            var workflow = PipelineWorkflowMapper.MapPipelineToWorkflow(
                pipeline: pipeline,
                workflowName: pipelineName);
            var json = PipelineJSONGenerator.GenerateWorkflowJSON(
                workflow: workflow,
                workflowKind: "Stateful");
            var outputFile = Path.Combine(this.outputDirectory, $"{pipelineName}_workflow.json");
            File.WriteAllText(outputFile, json);

            // Assert
            Assert.IsTrue(condition: File.Exists(outputFile), message: "Workflow file should be created");
            Assert.IsFalse(condition: string.IsNullOrEmpty(json), message: "Workflow JSON should not be empty");
        }

        [TestMethod]
        public void GenerateWorkflow_XmlTransmit_CreatesWorkflow()
        {
            // Arrange
            var pipelineXml = this.CreateXmlTransmitPipelineXml();
            var pipelineName = "XMLTransmit";
            var pipelineFile = Path.Combine(this.pipelinesDirectory, $"{pipelineName}.btp");
            File.WriteAllText(pipelineFile, pipelineXml);

            var parser = new PipelineParser();

            // Act
            var pipeline = parser.ParsePipelineFile(filePath: pipelineFile);
            var workflow = PipelineWorkflowMapper.MapPipelineToWorkflow(
                pipeline: pipeline,
                workflowName: pipelineName);
            var json = PipelineJSONGenerator.GenerateWorkflowJSON(
                workflow: workflow,
                workflowKind: "Stateful");
            var outputFile = Path.Combine(this.outputDirectory, $"{pipelineName}_workflow.json");
            File.WriteAllText(outputFile, json);

            // Assert
            Assert.IsTrue(condition: File.Exists(outputFile), message: "Workflow file should be created");
            Assert.IsTrue(condition: json.Contains("\"actions\""), message: "Workflow should have actions");
        }

        #endregion

        #region Dynamic Discovery - Process All Pipeline Files

        [TestMethod]
        public void ConvertAllPipelineFiles_GeneratesWorkflows()
        {
            // Arrange - Create sample pipelines first
            this.CreateSamplePipelineFiles();

            // Get all BTP files from the Data/BizTalk/Pipelines directory
            var btpFiles = Directory.GetFiles(this.pipelinesDirectory, "*.btp");
            
            if (btpFiles.Length == 0)
            {
                Assert.Inconclusive(message: "No .btp files found in test data directory. Add pipeline files to test dynamic discovery.");
                return;
            }

            var successCount = 0;
            var failureCount = 0;
            var results = new System.Text.StringBuilder();

            results.AppendLine($"Processing {btpFiles.Length} BTP files from {this.pipelinesDirectory}");
            results.AppendLine(new string('-', 80));

            // Act - Process each BTP file
            foreach (var btpPath in btpFiles)
            {
                var btpFileName = Path.GetFileName(btpPath);
                var baseName = Path.GetFileNameWithoutExtension(btpPath);
                var outputFileName = $"{baseName}_workflow.json";
                var outputPath = Path.Combine(this.outputDirectory, outputFileName);

                results.AppendLine($"\nProcessing: {btpFileName}");

                try
                {
                    var parser = new PipelineParser();
                    var pipeline = parser.ParsePipelineFile(filePath: btpPath);
                    
                    results.AppendLine($"  Pipeline Type: {pipeline.GetPipelineType()}");
                    results.AppendLine($"  Stages: {pipeline.Stages.Count}");
                    
                    var totalComponents = pipeline.Stages.Sum(s => s.Components.Count);
                    results.AppendLine($"  Components: {totalComponents}");

                    var workflow = PipelineWorkflowMapper.MapPipelineToWorkflow(
                        pipeline: pipeline,
                        workflowName: baseName);
                    
                    var json = PipelineJSONGenerator.GenerateWorkflowJSON(
                        workflow: workflow,
                        workflowKind: "Stateful");

                    File.WriteAllText(outputPath, json);

                    Assert.IsTrue(condition: File.Exists(outputPath), message: $"Output file should exist: {outputFileName}");
                    Assert.IsFalse(condition: string.IsNullOrEmpty(json), message: $"Workflow JSON should not be empty for {btpFileName}");

                    results.AppendLine($"  ? SUCCESS: Generated {outputFileName}");
                    var fileInfo = new FileInfo(outputPath);
                    results.AppendLine($"  ? Size: {fileInfo.Length} bytes");

                    successCount++;
                }
                catch (Exception ex)
                {
                    results.AppendLine($"  ? FAILED: {ex.Message}");
                    failureCount++;
                }
            }

            // Assert - Report summary
            results.AppendLine();
            results.AppendLine(new string('=', 80));
            results.AppendLine($"SUMMARY:");
            results.AppendLine($"  Total BTP files: {btpFiles.Length}");
            results.AppendLine($"  Successful: {successCount}");
            results.AppendLine($"  Failed: {failureCount}");
            results.AppendLine(new string('=', 80));

            Console.WriteLine(results.ToString());

            Assert.IsTrue(condition: successCount > 0, message: "At least one BTP file should be converted successfully");
        }

        #endregion

        #region Custom Pipeline Component Support

        [TestMethod]
        public void ConvertPipeline_WithCustomComponents_GeneratesWorkflow()
        {
            // Arrange
            var customPipelineXml = this.CreateCustomPipelineWithUnknownComponent();
            var pipelineName = "CustomPipeline";
            var pipelineFile = Path.Combine(this.pipelinesDirectory, $"{pipelineName}.btp");
            File.WriteAllText(pipelineFile, customPipelineXml);

            var parser = new PipelineParser();

            // Act
            var pipeline = parser.ParsePipelineFile(filePath: pipelineFile);
            var workflow = PipelineWorkflowMapper.MapPipelineToWorkflow(
                pipeline: pipeline,
                workflowName: pipelineName);
            var json = PipelineJSONGenerator.GenerateWorkflowJSON(
                workflow: workflow,
                workflowKind: "Stateful");
            var outputFile = Path.Combine(this.outputDirectory, $"{pipelineName}_workflow.json");
            File.WriteAllText(outputFile, json);

            // Assert
            Assert.IsTrue(condition: File.Exists(outputFile), message: "Workflow file should be created for custom components");
            Assert.IsFalse(condition: string.IsNullOrEmpty(json), message: "Workflow JSON should not be empty");
            
            Console.WriteLine("Custom Pipeline Workflow Generated:");
            Console.WriteLine(json);
        }

        #endregion

        #region Helper Methods

        private void CreateSamplePipelineFiles()
        {
            var samplePipelines = new[]
            {
                ("PassThruReceive.btp", this.CreatePassThruReceivePipelineXml()),
                ("XMLReceive.btp", this.CreateXmlReceivePipelineXml()),
                ("PassThruTransmit.btp", this.CreatePassThruTransmitPipelineXml()),
                ("XMLTransmit.btp", this.CreateXmlTransmitPipelineXml())
            };

            foreach (var (fileName, content) in samplePipelines)
            {
                var filePath = Path.Combine(this.pipelinesDirectory, fileName);
                if (!File.Exists(filePath))
                {
                    File.WriteAllText(filePath, content);
                }
            }
        }

        private string CreatePassThruReceivePipelineXml()
        {
            return @"<?xml version=""1.0"" encoding=""utf-16""?>
<Document xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" PolicyFilePath=""BTSReceivePolicy.xml"" MajorVersion=""1"" MinorVersion=""0"">
  <Description />
  <Stages>
    <Stage CategoryId=""9d0e4103-4cce-4536-83fa-4a5040674ad6"">
      <Components />
    </Stage>
    <Stage CategoryId=""9d0e4105-4cce-4536-83fa-4a5040674ad6"">
      <Components />
    </Stage>
    <Stage CategoryId=""9d0e410d-4cce-4536-83fa-4a5040674ad6"">
      <Components />
    </Stage>
    <Stage CategoryId=""9d0e410e-4cce-4536-83fa-4a5040674ad6"">
      <Components />
    </Stage>
  </Stages>
</Document>";
        }

        private string CreateXmlReceivePipelineXml()
        {
            return @"<?xml version=""1.0"" encoding=""utf-16""?>
<Document xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" PolicyFilePath=""BTSReceivePolicy.xml"" MajorVersion=""1"" MinorVersion=""0"">
  <Description />
  <Stages>
    <Stage CategoryId=""9d0e4103-4cce-4536-83fa-4a5040674ad6"">
      <Components>
        <Component>
          <Name>Microsoft.BizTalk.Component.MIME_SMIME_Decoder</Name>
          <ComponentName>MIME/SMIME decoder</ComponentName>
          <Description>MIME/SMIME decoder component.</Description>
          <Version>1.0</Version>
          <Properties>
            <Property Name=""AllowNonMIMEMessage"">
              <Value xsi:type=""xsd:boolean"">false</Value>
            </Property>
          </Properties>
          <CachedDisplayName>MIME/SMIME decoder</CachedDisplayName>
          <CachedIsManaged>true</CachedIsManaged>
        </Component>
      </Components>
    </Stage>
    <Stage CategoryId=""9d0e4105-4cce-4536-83fa-4a5040674ad6"">
      <Components>
        <Component>
          <Name>Microsoft.BizTalk.Component.XmlDasmComp</Name>
          <ComponentName>XML disassembler</ComponentName>
          <Description>Streaming XML disassembler</Description>
          <Version>1.0</Version>
          <Properties>
            <Property Name=""ValidateDocument"">
              <Value xsi:type=""xsd:boolean"">false</Value>
            </Property>
            <Property Name=""AllowUnrecognizedMessage"">
              <Value xsi:type=""xsd:boolean"">false</Value>
            </Property>
          </Properties>
          <CachedDisplayName>XML disassembler</CachedDisplayName>
          <CachedIsManaged>true</CachedIsManaged>
        </Component>
      </Components>
    </Stage>
    <Stage CategoryId=""9d0e410d-4cce-4536-83fa-4a5040674ad6"">
      <Components />
    </Stage>
    <Stage CategoryId=""9d0e410e-4cce-4536-83fa-4a5040674ad6"">
      <Components>
        <Component>
          <Name>Microsoft.BizTalk.Component.PartyRes</Name>
          <ComponentName>Party resolution</ComponentName>
          <Description>Party resolution component.</Description>
          <Version>1.0</Version>
          <Properties>
            <Property Name=""AllowBySID"">
              <Value xsi:type=""xsd:boolean"">false</Value>
            </Property>
          </Properties>
          <CachedDisplayName>Party resolution</CachedDisplayName>
          <CachedIsManaged>true</CachedIsManaged>
        </Component>
      </Components>
    </Stage>
  </Stages>
</Document>";
        }

        private string CreatePassThruTransmitPipelineXml()
        {
            return @"<?xml version=""1.0"" encoding=""utf-16""?>
<Document xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" PolicyFilePath=""BTSTransmitPolicy.xml"" MajorVersion=""1"" MinorVersion=""0"">
  <Description />
  <Stages>
    <Stage CategoryId=""9d0e4101-4cce-4536-83fa-4a5040674ad6"">
      <Components />
    </Stage>
    <Stage CategoryId=""9d0e4107-4cce-4536-83fa-4a5040674ad6"">
      <Components />
    </Stage>
    <Stage CategoryId=""9d0e4108-4cce-4536-83fa-4a5040674ad6"">
      <Components />
    </Stage>
  </Stages>
</Document>";
        }

        private string CreateXmlTransmitPipelineXml()
        {
            return @"<?xml version=""1.0"" encoding=""utf-16""?>
<Document xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" PolicyFilePath=""BTSTransmitPolicy.xml"" MajorVersion=""1"" MinorVersion=""0"">
  <Description />
  <Stages>
    <Stage CategoryId=""9d0e4101-4cce-4536-83fa-4a5040674ad6"">
      <Components />
    </Stage>
    <Stage CategoryId=""9d0e4107-4cce-4536-83fa-4a5040674ad6"">
      <Components>
        <Component>
          <Name>Microsoft.BizTalk.Component.XmlAsmComp</Name>
          <ComponentName>XML assembler</ComponentName>
          <Description>XML assembler component.</Description>
          <Version>1.0</Version>
          <Properties>
            <Property Name=""AddXmlDeclaration"">
              <Value xsi:type=""xsd:boolean"">true</Value>
            </Property>
          </Properties>
          <CachedDisplayName>XML assembler</CachedDisplayName>
          <CachedIsManaged>true</CachedIsManaged>
        </Component>
      </Components>
    </Stage>
    <Stage CategoryId=""9d0e4108-4cce-4536-83fa-4a5040674ad6"">
      <Components>
        <Component>
          <Name>Microsoft.BizTalk.Component.MIME_SMIME_Encoder</Name>
          <ComponentName>MIME/SMIME encoder</ComponentName>
          <Description>MIME/SMIME encoder component.</Description>
          <Version>1.0</Version>
          <Properties>
            <Property Name=""EnableEncryption"">
              <Value xsi:type=""xsd:boolean"">false</Value>
            </Property>
          </Properties>
          <CachedDisplayName>MIME/SMIME encoder</CachedDisplayName>
          <CachedIsManaged>true</CachedIsManaged>
        </Component>
      </Components>
    </Stage>
  </Stages>
</Document>";
        }

        private string CreateCustomPipelineWithUnknownComponent()
        {
            return @"<?xml version=""1.0"" encoding=""utf-16""?>
<Document xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" PolicyFilePath=""BTSReceivePolicy.xml"" MajorVersion=""1"" MinorVersion=""0"">
  <Description>Pipeline with custom third-party component</Description>
  <Stages>
    <Stage CategoryId=""9d0e4103-4cce-4536-83fa-4a5040674ad6"">
      <Components />
    </Stage>
    <Stage CategoryId=""9d0e4105-4cce-4536-83fa-4a5040674ad6"">
      <Components>
        <Component>
          <Name>CustomCompany.CustomPipeline.CustomDeserializer</Name>
          <ComponentName>Custom Deserializer</ComponentName>
          <Description>Third-party custom deserializer component</Description>
          <Version>2.0</Version>
          <Properties>
            <Property Name=""CustomProperty1"">
              <Value xsi:type=""xsd:string"">CustomValue</Value>
            </Property>
          </Properties>
          <CachedDisplayName>Custom Deserializer</CachedDisplayName>
          <CachedIsManaged>true</CachedIsManaged>
        </Component>
      </Components>
    </Stage>
    <Stage CategoryId=""9d0e410d-4cce-4536-83fa-4a5040674ad6"">
      <Components />
    </Stage>
    <Stage CategoryId=""9d0e410e-4cce-4536-83fa-4a5040674ad6"">
      <Components />
    </Stage>
  </Stages>
</Document>";
        }

        #endregion
    }
}


