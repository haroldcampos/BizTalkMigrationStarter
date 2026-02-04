// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BizTalktoLogicApps.BTPtoLA.Models;
using BizTalktoLogicApps.BTPtoLA.Services;

namespace BizTalktoLogicApps.Tests.Unit.BTPtoLA
{
    /// <summary>
    /// Unit tests for PipelineJSONGenerator component logic.
    /// Tests Logic Apps workflow JSON generation from pipeline workflow models.
    /// </summary>
    [TestClass]
    public class PipelineJSONGeneratorTests
    {
        #region JSON Generation Tests

        [TestMethod]
        public void GenerateWorkflowJSON_ValidWorkflow_ReturnsJson()
        {
            // Arrange
            var workflow = this.CreateMinimalWorkflow();

            // Act
            var result = PipelineJSONGenerator.GenerateWorkflowJSON(
                workflow: workflow,
                workflowKind: "Stateful");

            // Assert
            Assert.IsFalse(
                condition: string.IsNullOrEmpty(result),
                message: "Generated JSON should not be empty");
        }

        [TestMethod]
        public void GenerateWorkflowJSON_ValidWorkflow_ContainsDefinitionSection()
        {
            // Arrange
            var workflow = this.CreateMinimalWorkflow();

            // Act
            var result = PipelineJSONGenerator.GenerateWorkflowJSON(
                workflow: workflow,
                workflowKind: "Stateful");

            // Assert
            Assert.IsTrue(
                condition: result.Contains("\"definition\""),
                message: "JSON should contain definition section");
        }

        [TestMethod]
        public void GenerateWorkflowJSON_ValidWorkflow_ContainsTriggersSection()
        {
            // Arrange
            var workflow = this.CreateMinimalWorkflow();

            // Act
            var result = PipelineJSONGenerator.GenerateWorkflowJSON(
                workflow: workflow,
                workflowKind: "Stateful");

            // Assert
            Assert.IsTrue(
                condition: result.Contains("\"triggers\""),
                message: "JSON should contain triggers section");
        }

        [TestMethod]
        public void GenerateWorkflowJSON_ValidWorkflow_ContainsActionsSection()
        {
            // Arrange
            var workflow = this.CreateMinimalWorkflow();

            // Act
            var result = PipelineJSONGenerator.GenerateWorkflowJSON(
                workflow: workflow,
                workflowKind: "Stateful");

            // Assert
            Assert.IsTrue(
                condition: result.Contains("\"actions\""),
                message: "JSON should contain actions section");
        }

        [TestMethod]
        public void GenerateWorkflowJSON_StatefulWorkflow_ContainsKindStateful()
        {
            // Arrange
            var workflow = this.CreateMinimalWorkflow();

            // Act
            var result = PipelineJSONGenerator.GenerateWorkflowJSON(
                workflow: workflow,
                workflowKind: "Stateful");

            // Assert
            Assert.IsTrue(
                condition: result.Contains("\"kind\"") && result.Contains("Stateful"),
                message: "Stateful workflow should contain kind: Stateful");
        }

        [TestMethod]
        public void GenerateWorkflowJSON_StatelessWorkflow_ContainsKindStateless()
        {
            // Arrange
            var workflow = this.CreateMinimalWorkflow();

            // Act
            var result = PipelineJSONGenerator.GenerateWorkflowJSON(
                workflow: workflow,
                workflowKind: "Stateless");

            // Assert
            Assert.IsTrue(
                condition: result.Contains("\"kind\"") && result.Contains("Stateless"),
                message: "Stateless workflow should contain kind: Stateless");
        }

        #endregion

        #region JSON Validity Tests

        [TestMethod]
        public void GenerateWorkflowJSON_ValidWorkflow_ProducesValidJson()
        {
            // Arrange
            var workflow = this.CreateMinimalWorkflow();

            // Act
            var result = PipelineJSONGenerator.GenerateWorkflowJSON(
                workflow: workflow,
                workflowKind: "Stateful");

            // Assert - Try to parse as JSON to validate
            try
            {
                Newtonsoft.Json.Linq.JObject.Parse(result);
                Assert.IsTrue(condition: true, message: "JSON should be valid");
            }
            catch (Newtonsoft.Json.JsonException)
            {
                Assert.Fail(message: "Generated JSON is not valid");
            }
        }

        [TestMethod]
        public void GenerateWorkflowJSON_WithActions_IncludesActionDefinitions()
        {
            // Arrange
            var workflow = this.CreateWorkflowWithActions();

            // Act
            var result = PipelineJSONGenerator.GenerateWorkflowJSON(
                workflow: workflow,
                workflowKind: "Stateful");

            // Assert
            Assert.IsTrue(
                condition: result.Contains("TestAction"),
                message: "JSON should include action names");
        }

        #endregion

        #region Formatting Tests

        [TestMethod]
        public void GenerateWorkflowJSON_ValidWorkflow_IsFormatted()
        {
            // Arrange
            var workflow = this.CreateMinimalWorkflow();

            // Act
            var result = PipelineJSONGenerator.GenerateWorkflowJSON(
                workflow: workflow,
                workflowKind: "Stateful");

            // Assert
            Assert.IsTrue(
                condition: result.Contains(Environment.NewLine) || result.Contains("\n"),
                message: "JSON should be formatted with line breaks");
        }

        #endregion

        #region Helper Methods

        private PipelineWorkflowModel CreateMinimalWorkflow()
        {
            var workflow = new PipelineWorkflowModel
            {
                Name = "TestWorkflow"
            };
            
            workflow.Triggers.Add(new PipelineWorkflowTrigger
            {
                Name = "manual",
                Kind = "Request",
                Sequence = 0
            });
            
            workflow.Actions.Add(new PipelineWorkflowAction
            {
                Name = "Response",
                Type = "Compose",
                Sequence = 0
            });
            
            return workflow;
        }

        private PipelineWorkflowModel CreateWorkflowWithActions()
        {
            var workflow = this.CreateMinimalWorkflow();
            
            workflow.Actions.Add(new PipelineWorkflowAction
            {
                Name = "TestAction",
                Type = "Compose",
                Details = "@triggerBody()",
                Sequence = 1
            });
            
            return workflow;
        }

        #endregion
    }
}
