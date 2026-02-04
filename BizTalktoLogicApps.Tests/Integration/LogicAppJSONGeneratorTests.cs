// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using BizTalktoLogicApps.ODXtoWFMigrator;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Collections.Generic;

namespace BizTalktoLogicApps.Tests.Integration
{
    /// <summary>
    /// Integration tests for LogicAppJSONGenerator workflow generation.
    /// Tests JSON output format, action structures, and Logic Apps Standard compatibility.
    /// </summary>
    [TestClass]
    public class LogicAppJSONGeneratorTests
    {
        [TestMethod]
        public void GenerateStandardWorkflow_WorkflowAction_UsesCorrectIdFormat()
        {
            // Arrange
            var workflowMap = new LogicAppWorkflowMap
            {
                Name = "ParentWorkflow"
            };
            workflowMap.Triggers.Add(new LogicAppTrigger
            {
                Name = "When_an_HTTP_request_is_received",
                Kind = "Request",
                Sequence = 0
            });
            workflowMap.Actions.Add(new LogicAppAction
            {
                Name = "Call_ChildWorkflow",
                Type = "Workflow",
                Details = "ChildWorkflow",
                Sequence = 0
            });

            // Act
            var jsonResult = LogicAppJSONGenerator.GenerateStandardWorkflow(map: workflowMap);
            var workflow = JObject.Parse(jsonResult);

            // Assert
            var workflowId = workflow["definition"]["actions"]["Call_ChildWorkflow"]["inputs"]["host"]["workflow"]["id"].ToString();

            Assert.AreEqual(
                expected: "ChildWorkflow",
                actual: workflowId,
                message: "Workflow ID should be just the workflow name without '/workflows/' prefix");
            Assert.IsFalse(
                condition: workflowId.Contains("/"),
                message: "Workflow ID should not contain forward slashes");
        }
        
        
        [TestMethod]
        public void GenerateStandardWorkflow_CallableWorkflow_HasRequestTrigger()
        {
            // Arrange
            var workflowMap = new LogicAppWorkflowMap
            {
                Name = "CallableOrchestration"
            };
            workflowMap.Triggers.Add(new LogicAppTrigger
            {
                Name = "When_called_from_parent_workflow",
                Kind = "Request",
                Sequence = 0
            });

            // Act
            var jsonResult = LogicAppJSONGenerator.GenerateStandardWorkflow(map: workflowMap);
            var workflow = JObject.Parse(jsonResult);

            // Assert
            var triggerType = workflow["definition"]["triggers"]?.First?.First?["type"]?.ToString();
            Assert.AreEqual(
                expected: "Request",
                actual: triggerType,
                message: "Callable workflow should have Request trigger");
        }

        [TestMethod]
        public void GenerateStandardWorkflow_ProducesValidJSON()
        {
            // Arrange
            var workflowMap = this.CreateSampleWorkflowMap();

            // Act
            var jsonResult = LogicAppJSONGenerator.GenerateStandardWorkflow(map: workflowMap);

            // Assert
            Assert.IsFalse(
                condition: string.IsNullOrEmpty(jsonResult),
                message: "Generated JSON should not be empty");

            // Validate JSON structure
            var workflow = JObject.Parse(jsonResult);
            Assert.IsNotNull(
                value: workflow["definition"],
                message: "Should have definition");
            Assert.IsNotNull(
                value: workflow["definition"]["$schema"],
                message: "Should have $schema");
            Assert.IsNotNull(
                value: workflow["definition"]["actions"],
                message: "Should have actions");
            Assert.IsNotNull(
                value: workflow["definition"]["triggers"],
                message: "Should have triggers");
        }

        private LogicAppWorkflowMap CreateSampleWorkflowMap()
        {
            var workflowMap = new LogicAppWorkflowMap
            {
                Name = "TestWorkflow"
            };
            workflowMap.Triggers.Add(new LogicAppTrigger
            {
                Name = "When_an_HTTP_request_is_received",
                Kind = "Request",
                Sequence = 0
            });
            workflowMap.Actions.Add(new LogicAppAction
            {
                Name = "Initialize_Variable",
                Type = "InitializeVariable",
                Details = "string",
                Sequence = 0
            });
            return workflowMap;
        }
    }
}
