// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BizTalktoLogicApps.BTPtoLA.Models;
using BizTalktoLogicApps.BTPtoLA.Services;

namespace BizTalktoLogicApps.Tests.Unit.BTPtoLA
{
    /// <summary>
    /// Unit tests for PipelineParser component logic.
    /// Tests BizTalk pipeline (.btp) file parsing, stage detection, and component extraction.
    /// </summary>
    [TestClass]
    public class PipelineParserTests
    {
        #region Basic Parsing Tests

        [TestMethod]
        public void ParsePipelineXml_ValidXml_ReturnsPipelineDocument()
        {
            // Arrange
            var parser = new PipelineParser();
            var xml = this.CreateMinimalReceivePipelineXml();

            // Act
            var result = parser.ParsePipelineXml(xmlContent: xml);

            // Assert
            Assert.IsNotNull(value: result, message: "Pipeline document should not be null");
            Assert.IsTrue(condition: result.Stages.Count > 0, message: "Pipeline should have at least one stage");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void ParsePipelineXml_NullXml_ThrowsArgumentException()
        {
            // Arrange
            var parser = new PipelineParser();

            // Act
            parser.ParsePipelineXml(xmlContent: null);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void ParsePipelineXml_EmptyXml_ThrowsArgumentException()
        {
            // Arrange
            var parser = new PipelineParser();

            // Act
            parser.ParsePipelineXml(xmlContent: string.Empty);
        }

        [TestMethod]
        [ExpectedException(typeof(Exception))]
        public void ParsePipelineXml_InvalidXml_ThrowsException()
        {
            // Arrange
            var parser = new PipelineParser();
            var invalidXml = "This is not valid XML";

            // Act
            parser.ParsePipelineXml(xmlContent: invalidXml);
        }

        #endregion

        #region Pipeline Type Detection Tests

        [TestMethod]
        public void ParsePipelineXml_ReceivePipeline_DetectsReceiveType()
        {
            // Arrange
            var parser = new PipelineParser();
            var xml = this.CreateMinimalReceivePipelineXml();

            // Act
            var result = parser.ParsePipelineXml(xmlContent: xml);

            // Assert
            Assert.AreEqual(
                expected: "Receive",
                actual: result.GetPipelineType(),
                message: "Should detect Receive pipeline type");
        }

        [TestMethod]
        public void ParsePipelineXml_SendPipeline_DetectsSendType()
        {
            // Arrange
            var parser = new PipelineParser();
            var xml = this.CreateMinimalSendPipelineXml();

            // Act
            var result = parser.ParsePipelineXml(xmlContent: xml);

            // Assert
            Assert.AreEqual(
                expected: "Send",
                actual: result.GetPipelineType(),
                message: "Should detect Send pipeline type");
        }

        #endregion

        #region Stage Parsing Tests

        [TestMethod]
        public void ParsePipelineXml_ReceivePipeline_HasFourStages()
        {
            // Arrange
            var parser = new PipelineParser();
            var xml = this.CreateMinimalReceivePipelineXml();

            // Act
            var result = parser.ParsePipelineXml(xmlContent: xml);

            // Assert
            Assert.AreEqual(
                expected: 4,
                actual: result.Stages.Count,
                message: "Receive pipeline should have 4 stages");
        }

        [TestMethod]
        public void ParsePipelineXml_SendPipeline_HasThreeStages()
        {
            // Arrange
            var parser = new PipelineParser();
            var xml = this.CreateMinimalSendPipelineXml();

            // Act
            var result = parser.ParsePipelineXml(xmlContent: xml);

            // Assert
            Assert.AreEqual(
                expected: 3,
                actual: result.Stages.Count,
                message: "Send pipeline should have 3 stages");
        }

        [TestMethod]
        public void ParsePipelineXml_WithDecodeStage_ParsesCategoryId()
        {
            // Arrange
            var parser = new PipelineParser();
            var xml = this.CreateMinimalReceivePipelineXml();

            // Act
            var result = parser.ParsePipelineXml(xmlContent: xml);

            // Assert
            Assert.AreEqual(
                expected: "9d0e4103-4cce-4536-83fa-4a5040674ad6",
                actual: result.Stages[0].CategoryId,
                message: "First stage should be Decode stage");
        }

        #endregion

        #region Component Parsing Tests

        [TestMethod]
        public void ParsePipelineXml_WithComponents_ParsesComponentList()
        {
            // Arrange
            var parser = new PipelineParser();
            var xml = this.CreateXmlReceivePipelineXml();

            // Act
            var result = parser.ParsePipelineXml(xmlContent: xml);

            // Assert
            var disassembleStage = result.Stages[1]; // Disassemble stage
            Assert.IsTrue(
                condition: disassembleStage.Components.Count > 0,
                message: "Disassemble stage should have components");
        }

        [TestMethod]
        public void ParsePipelineXml_XmlDisassembler_ParsesComponentProperties()
        {
            // Arrange
            var parser = new PipelineParser();
            var xml = this.CreateXmlReceivePipelineXml();

            // Act
            var result = parser.ParsePipelineXml(xmlContent: xml);

            // Assert
            var disassembleStage = result.Stages[1];
            var component = disassembleStage.Components[0];
            
            Assert.AreEqual(
                expected: "Microsoft.BizTalk.Component.XmlDasmComp",
                actual: component.Name,
                message: "Component should be XML disassembler");
            Assert.IsTrue(
                condition: component.Properties.Count > 0,
                message: "Component should have properties");
        }

        [TestMethod]
        public void ParsePipelineXml_ComponentProperty_ParsesNameAndValue()
        {
            // Arrange
            var parser = new PipelineParser();
            var xml = this.CreateXmlReceivePipelineXml();

            // Act
            var result = parser.ParsePipelineXml(xmlContent: xml);

            // Assert
            var component = result.Stages[1].Components[0];
            var validateDocProperty = component.Properties.Find(p => 
                string.Equals(p.Name, "ValidateDocument", StringComparison.Ordinal));
            
            Assert.IsNotNull(
                value: validateDocProperty,
                message: "Should find ValidateDocument property");
            Assert.AreEqual(
                expected: "xsd:boolean",
                actual: validateDocProperty.Value.Type,
                message: "Property should have boolean type");
        }

        #endregion

        #region Default Pipeline Detection Tests

        [TestMethod]
        public void ParsePipelineXml_PassThruReceive_IsDetectedAsDefault()
        {
            // Arrange
            var parser = new PipelineParser();
            var xml = this.CreateMinimalReceivePipelineXml();

            // Act
            var result = parser.ParsePipelineXml(xmlContent: xml);
            var defaultInfo = DefaultPipelineInfo.DetectDefaultPipeline(pipeline: result);

            // Assert
            Assert.IsTrue(
                condition: defaultInfo.Type == DefaultPipelineType.PassThruReceive ||
                          defaultInfo.Type == DefaultPipelineType.ReceiveTemplate,
                message: "Empty receive pipeline should be PassThruReceive or template");
        }

        [TestMethod]
        public void ParsePipelineXml_XmlReceive_IsDetectedAsDefault()
        {
            // Arrange
            var parser = new PipelineParser();
            var xml = this.CreateXmlReceivePipelineXml();

            // Act
            var result = parser.ParsePipelineXml(xmlContent: xml);
            var defaultInfo = DefaultPipelineInfo.DetectDefaultPipeline(pipeline: result);

            // Assert
            Assert.IsTrue(
                condition: defaultInfo.Type == DefaultPipelineType.XMLReceive ||
                          defaultInfo.Type == DefaultPipelineType.Custom,
                message: "XML receive pipeline should be XMLReceive or custom");
        }

        #endregion

        #region Helper Methods

        private string CreateMinimalReceivePipelineXml()
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

        private string CreateMinimalSendPipelineXml()
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

        private string CreateXmlReceivePipelineXml()
        {
            return @"<?xml version=""1.0"" encoding=""utf-16""?>
<Document xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" PolicyFilePath=""BTSReceivePolicy.xml"" MajorVersion=""1"" MinorVersion=""0"">
  <Description />
  <Stages>
    <Stage CategoryId=""9d0e4103-4cce-4536-83fa-4a5040674ad6"">
      <Components />
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
      <Components />
    </Stage>
  </Stages>
</Document>";
        }

        #endregion
    }
}
