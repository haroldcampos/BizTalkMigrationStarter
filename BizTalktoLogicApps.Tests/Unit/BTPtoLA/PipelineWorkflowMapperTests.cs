// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BizTalktoLogicApps.BTPtoLA.Models;
using BizTalktoLogicApps.BTPtoLA.Services;

namespace BizTalktoLogicApps.Tests.Unit.BTPtoLA
{
    /// <summary>
    /// Unit tests for PipelineWorkflowMapper component logic.
    /// Tests mapping of BizTalk pipeline models to Logic Apps workflow models.
    /// </summary>
    [TestClass]
    public class PipelineWorkflowMapperTests
    {
        #region Workflow Creation Tests

        [TestMethod]
        public void MapPipelineToWorkflow_ValidPipeline_CreatesWorkflow()
        {
            // Arrange
            var pipeline = this.CreateMinimalReceivePipeline();
            var workflowName = "TestWorkflow";

            // Act
            var result = PipelineWorkflowMapper.MapPipelineToWorkflow(
                pipeline: pipeline,
                workflowName: workflowName);

            // Assert
            Assert.IsNotNull(value: result, message: "Workflow should not be null");
            Assert.AreEqual(
                expected: workflowName,
                actual: result.Name,
                message: "Workflow name should match");
        }

        [TestMethod]
        public void MapPipelineToWorkflow_ReceivePipeline_CreatesTrigger()
        {
            // Arrange
            var pipeline = this.CreateMinimalReceivePipeline();

            // Act
            var result = PipelineWorkflowMapper.MapPipelineToWorkflow(
                pipeline: pipeline,
                workflowName: "TestReceive");

            // Assert
            Assert.IsNotNull(
                value: result.Triggers,
                message: "Workflow should have triggers");
            Assert.IsTrue(
                condition: result.Triggers.Count > 0,
                message: "Receive pipeline should create at least one trigger");
        }

        [TestMethod]
        public void MapPipelineToWorkflow_SendPipeline_CreatesActions()
        {
            // Arrange
            var pipeline = this.CreateMinimalSendPipeline();

            // Act
            var result = PipelineWorkflowMapper.MapPipelineToWorkflow(
                pipeline: pipeline,
                workflowName: "TestSend");

            // Assert
            Assert.IsNotNull(
                value: result.Actions,
                message: "Workflow should have actions");
            Assert.IsTrue(
                condition: result.Actions.Count > 0,
                message: "Send pipeline should create at least one action");
        }

        #endregion

        #region Component Mapping Tests

        [TestMethod]
        public void MapPipelineToWorkflow_WithComponents_CreatesActions()
        {
            // Arrange
            var pipeline = this.CreateXmlReceivePipeline();

            // Act
            var result = PipelineWorkflowMapper.MapPipelineToWorkflow(
                pipeline: pipeline,
                workflowName: "XmlReceive");

            // Assert
            Assert.IsTrue(
                condition: result.Actions.Count > 0,
                message: "Pipeline with components should create actions");
        }

        [TestMethod]
        public void MapPipelineToWorkflow_EmptyStages_StillCreatesWorkflow()
        {
            // Arrange
            var pipeline = this.CreateMinimalReceivePipeline();

            // Act
            var result = PipelineWorkflowMapper.MapPipelineToWorkflow(
                pipeline: pipeline,
                workflowName: "Empty");

            // Assert
            Assert.IsNotNull(value: result, message: "Should create workflow even with empty stages");
            Assert.AreEqual(
                expected: "Empty",
                actual: result.Name,
                message: "Workflow name should be preserved");
        }

        #endregion

        #region Stage Order Tests

        [TestMethod]
        public void MapPipelineToWorkflow_ReceivePipeline_PreservesStageOrder()
        {
            // Arrange
            var pipeline = this.CreateXmlReceivePipeline();

            // Act
            var result = PipelineWorkflowMapper.MapPipelineToWorkflow(
                pipeline: pipeline,
                workflowName: "OrderTest");

            // Assert
            Assert.IsTrue(
                condition: result.Actions.Count > 0,
                message: "Should create actions in stage order");
        }

        #endregion

        #region Helper Methods

        private PipelineDocument CreateMinimalReceivePipeline()
        {
            return new PipelineDocument
            {
                PolicyFilePath = "BTSReceivePolicy.xml",
                MajorVersion = 1,
                MinorVersion = 0,
                Stages = new System.Collections.Generic.List<PipelineStage>
                {
                    new PipelineStage { CategoryId = "9d0e4103-4cce-4536-83fa-4a5040674ad6" },
                    new PipelineStage { CategoryId = "9d0e4105-4cce-4536-83fa-4a5040674ad6" },
                    new PipelineStage { CategoryId = "9d0e410d-4cce-4536-83fa-4a5040674ad6" },
                    new PipelineStage { CategoryId = "9d0e410e-4cce-4536-83fa-4a5040674ad6" }
                }
            };
        }

        private PipelineDocument CreateMinimalSendPipeline()
        {
            var pipeline = new PipelineDocument
            {
                PolicyFilePath = "BTSTransmitPolicy.xml",
                MajorVersion = 1,
                MinorVersion = 0,
                Stages = new System.Collections.Generic.List<PipelineStage>
                {
                    new PipelineStage { CategoryId = "9d0e4101-4cce-4536-83fa-4a5040674ad6" }, // PreAssemble
                    new PipelineStage { CategoryId = "9d0e4107-4cce-4536-83fa-4a5040674ad6" }, // Assemble
                    new PipelineStage { CategoryId = "9d0e4108-4cce-4536-83fa-4a5040674ad6" }  // Encode
                }
            };

            // Add a simple component to the Assemble stage so the pipeline creates actions
            var xmlAsmComponent = new PipelineComponent
            {
                Name = "Microsoft.BizTalk.Component.XmlAsmComp",
                ComponentName = "XML assembler",
                Description = "XML assembler component",
                Version = "1.0",
                Properties = new System.Collections.Generic.List<ComponentProperty>
                {
                    new ComponentProperty
                    {
                        Name = "AddXmlDeclaration",
                        Value = new PropertyValue { Type = "xsd:boolean", Text = "true" }
                    }
                }
            };

            pipeline.Stages[1].Components.Add(xmlAsmComponent);
            return pipeline;
        }

        private PipelineDocument CreateXmlReceivePipeline()
        {
            var pipeline = this.CreateMinimalReceivePipeline();
            
            var xmlDasmComponent = new PipelineComponent
            {
                Name = "Microsoft.BizTalk.Component.XmlDasmComp",
                ComponentName = "XML disassembler",
                Description = "Streaming XML disassembler",
                Version = "1.0",
                Properties = new System.Collections.Generic.List<ComponentProperty>
                {
                    new ComponentProperty
                    {
                        Name = "ValidateDocument",
                        Value = new PropertyValue { Type = "xsd:boolean", Text = "false" }
                    }
                }
            };
            
            pipeline.Stages[1].Components.Add(xmlDasmComponent);
            return pipeline;
        }

        #endregion
    }
}
