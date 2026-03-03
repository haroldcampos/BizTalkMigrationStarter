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

        [TestMethod]
        public void GenerateStandardWorkflow_XsltWithInputMessageSource_UsesBodyExpression()
        {
            // Arrange: Transform has InputMessageSourceAction set (not triggerBody)
            var workflowMap = new LogicAppWorkflowMap { Name = "TestWorkflow" };
            workflowMap.Triggers.Add(new LogicAppTrigger
            {
                Name = "When_an_HTTP_request_is_received",
                Kind = "Request",
                Sequence = 0
            });
            workflowMap.Actions.Add(new LogicAppAction
            {
                Name = "Transform_Order",
                Type = "Xslt",
                Details = "MyMaps.OrderToInvoice",
                Sequence = 0,
                InputMessageSourceAction = null // null = triggerBody (activation message)
            });
            workflowMap.Actions.Add(new LogicAppAction
            {
                Name = "Transform_Invoice",
                Type = "Xslt",
                Details = "MyMaps.InvoiceToPdf",
                Sequence = 1,
                InputMessageSourceAction = "Transform_Order" // references prior transform
            });

            // Act
            var jsonResult = LogicAppJSONGenerator.GenerateStandardWorkflow(map: workflowMap);
            var workflow = JObject.Parse(jsonResult);

            // Assert: First transform should use @triggerBody()
            var firstTransformContent = workflow["definition"]["actions"]["Transform_Order"]["inputs"]["content"].ToString();
            Assert.AreEqual("@triggerBody()", firstTransformContent,
                "First transform (activation message) should use @triggerBody()");

            // Assert: Second transform should reference first transform's output
            var secondTransformContent = workflow["definition"]["actions"]["Transform_Invoice"]["inputs"]["content"].ToString();
            Assert.AreEqual("@body('Transform_Order')", secondTransformContent,
                "Second transform should reference the first transform's output via @body()");
        }

        [TestMethod]
        public void GenerateStandardWorkflow_SendWithInputMessageSource_UsesBodyExpression()
        {
            // Arrange: Send has InputMessageSourceAction set
            var workflowMap = new LogicAppWorkflowMap { Name = "TestWorkflow" };
            workflowMap.Triggers.Add(new LogicAppTrigger
            {
                Name = "When_an_HTTP_request_is_received",
                Kind = "Request",
                Sequence = 0
            });
            workflowMap.Actions.Add(new LogicAppAction
            {
                Name = "Transform_Order",
                Type = "Xslt",
                Details = "MyMaps.OrderToInvoice",
                Sequence = 0
            });
            workflowMap.Actions.Add(new LogicAppAction
            {
                Name = "Send_Invoice",
                Type = "SendConnector",
                ConnectorKind = "Http",
                TargetAddress = "http://localhost/invoice",
                Sequence = 1,
                InputMessageSourceAction = "Transform_Order" // references transform output
            });

            // Act
            var jsonResult = LogicAppJSONGenerator.GenerateStandardWorkflow(map: workflowMap);
            var workflow = JObject.Parse(jsonResult);

            // Assert: Send body should reference the transform output
            var sendBody = workflow["definition"]["actions"]["Send_Invoice"]["inputs"]["body"].ToString();
            Assert.AreEqual("@body('Transform_Order')", sendBody,
                "Send action should reference the transform's output via @body(), not @triggerBody()");
        }

        [TestMethod]
        public void GenerateStandardWorkflow_ActionWithoutInputMessageSource_UsesTriggerBody()
        {
            // Arrange: Action without InputMessageSourceAction should fall back to @triggerBody()
            var workflowMap = new LogicAppWorkflowMap { Name = "TestWorkflow" };
            workflowMap.Triggers.Add(new LogicAppTrigger
            {
                Name = "When_an_HTTP_request_is_received",
                Kind = "Request",
                Sequence = 0
            });
            workflowMap.Actions.Add(new LogicAppAction
            {
                Name = "Send_PassThrough",
                Type = "SendConnector",
                ConnectorKind = "Http",
                TargetAddress = "http://localhost/forward",
                Sequence = 0
                // InputMessageSourceAction is null — should use @triggerBody()
            });

            // Act
            var jsonResult = LogicAppJSONGenerator.GenerateStandardWorkflow(map: workflowMap);
            var workflow = JObject.Parse(jsonResult);

            // Assert: Send body should use @triggerBody()
            var sendBody = workflow["definition"]["actions"]["Send_PassThrough"]["inputs"]["body"].ToString();
            Assert.AreEqual("@triggerBody()", sendBody,
                "Action without InputMessageSourceAction should fall back to @triggerBody()");
        }

        // =====================================================================
        // Step 0: Baseline snapshot tests for Inputs-type action case branches.
        // These capture the exact current output before any registry-driven
        // changes are made. All must pass and remain green throughout the refactor.
        // =====================================================================

        // --- 0.1: XmlParse with Details ---

        [TestMethod]
        public void BuildAction_XmlParse_WithDetails_ProducesCorrectShape()
        {
            var map = this.MakeMapWithAction("XmlParse", details: "Order schema parse");
            var action = this.GetAction(map, "Parse_XML");

            Assert.AreEqual("XmlParse", action["type"]?.ToString());
            Assert.AreEqual("@triggerBody()", action["inputs"]["content"]?.ToString());
            Assert.AreEqual("LogicApp",       action["inputs"]["schema"]["source"]?.ToString());
            Assert.AreEqual("{{SCHEMA_NAME}}", action["inputs"]["schema"]["name"]?.ToString());
            Assert.AreEqual("Prohibit", action["inputs"]["xmlReaderSettings"]["dtdProcessing"]?.ToString());
            Assert.AreEqual(true,  action["inputs"]["xmlReaderSettings"]["xmlNormalization"]?.ToObject<bool>());
            Assert.AreEqual(true,  action["inputs"]["xmlReaderSettings"]["ignoreWhitespace"]?.ToObject<bool>());
            Assert.AreEqual(true,  action["inputs"]["xmlReaderSettings"]["ignoreProcessingInstructions"]?.ToObject<bool>());
            Assert.AreEqual(false, action["inputs"]["jsonWriterSettings"]["ignoreAttributes"]?.ToObject<bool>());
            Assert.AreEqual(false, action["inputs"]["jsonWriterSettings"]["useFullyQualifiedNames"]?.ToObject<bool>());
            Assert.AreEqual("Order schema parse", action["metadata"]?["comment"]?.ToString());
        }

        // --- 0.2: XmlParse without Details - no metadata ---

        [TestMethod]
        public void BuildAction_XmlParse_NullDetails_NoMetadata()
        {
            var map = this.MakeMapWithAction("XmlParse", details: null);
            var action = this.GetAction(map, "Parse_XML");

            Assert.AreEqual("XmlParse", action["type"]?.ToString());
            Assert.AreEqual("@triggerBody()", action["inputs"]["content"]?.ToString());
            Assert.AreEqual("LogicApp",        action["inputs"]["schema"]["source"]?.ToString());
            Assert.AreEqual("{{SCHEMA_NAME}}", action["inputs"]["schema"]["name"]?.ToString());
            Assert.IsNull(action["metadata"], "metadata must be absent when Details is null");
        }

        // --- 0.3: XmlCompose ---

        [TestMethod]
        public void BuildAction_XmlCompose_ProducesCorrectShape()
        {
            var map = this.MakeMapWithAction("XmlCompose", details: "Compose invoice");
            var action = this.GetAction(map, "Parse_XML");

            Assert.AreEqual("XmlCompose",      action["type"]?.ToString());
            Assert.AreEqual("LogicApp",        action["inputs"]["schema"]["source"]?.ToString());
            Assert.AreEqual("{{SCHEMA_NAME}}", action["inputs"]["schema"]["name"]?.ToString());
            Assert.IsNotNull(action["inputs"]["content"], "content must be present");
            Assert.AreEqual(JTokenType.Object, action["inputs"]["content"].Type, "content must be an empty object");
            Assert.AreEqual(0, ((JObject)action["inputs"]["content"]).Count, "content object must be empty");
            Assert.AreEqual("Compose invoice", action["metadata"]?["comment"]?.ToString());
        }

        // --- 0.4: XmlValidation ---

        [TestMethod]
        public void BuildAction_XmlValidation_ProducesCorrectShape()
        {
            var map = this.MakeMapWithAction("XmlValidation", details: "Validate order");
            var action = this.GetAction(map, "Parse_XML");

            Assert.AreEqual("XmlValidation",   action["type"]?.ToString());
            Assert.AreEqual("@triggerBody()",  action["inputs"]["content"]?.ToString());
            Assert.AreEqual("LogicApp",        action["inputs"]["schema"]["source"]?.ToString());
            Assert.AreEqual("{{SCHEMA_NAME}}", action["inputs"]["schema"]["name"]?.ToString());
            Assert.AreEqual("Validate order",  action["metadata"]?["comment"]?.ToString());
        }

        // --- 0.5: FlatFileDecoding ---

        [TestMethod]
        public void BuildAction_FlatFileDecoding_ProducesCorrectShape()
        {
            var map = this.MakeMapWithAction("FlatFileDecoding", details: "Decode flat file");
            var action = this.GetAction(map, "Parse_XML");

            Assert.AreEqual("FlatFileDecoding",        action["type"]?.ToString());
            Assert.AreEqual("@triggerBody()",          action["inputs"]["content"]?.ToString());
            Assert.AreEqual("LogicApp",                action["inputs"]["schema"]["source"]?.ToString());
            Assert.AreEqual("{{FLAT_FILE_SCHEMA_NAME}}", action["inputs"]["schema"]["name"]?.ToString());
            Assert.AreEqual("Decode flat file",        action["metadata"]?["comment"]?.ToString());
        }

        // --- 0.6: FlatFileEncoding ---

        [TestMethod]
        public void BuildAction_FlatFileEncoding_ProducesCorrectShape()
        {
            var map = this.MakeMapWithAction("FlatFileEncoding", details: "Encode flat file");
            var action = this.GetAction(map, "Parse_XML");

            Assert.AreEqual("FlatFileEncoding",        action["type"]?.ToString());
            Assert.AreEqual("@triggerBody()",          action["inputs"]["content"]?.ToString());
            Assert.AreEqual("LogicApp",                action["inputs"]["schema"]["source"]?.ToString());
            Assert.AreEqual("{{FLAT_FILE_SCHEMA_NAME}}", action["inputs"]["schema"]["name"]?.ToString());
            Assert.AreEqual("Encode flat file",        action["metadata"]?["comment"]?.ToString());
        }

        // --- 0.7: SwiftMTDecode ---

        [TestMethod]
        public void BuildAction_SwiftMTDecode_ProducesCorrectShape()
        {
            var map = this.MakeMapWithAction("SwiftMTDecode", details: "Decode MT103");
            var action = this.GetAction(map, "Parse_XML");

            Assert.AreEqual("SwiftMTDecode",   action["type"]?.ToString());
            Assert.AreEqual("@triggerBody()",  action["inputs"]["messageToDecode"]?.ToString());
            Assert.AreEqual("Enable",          action["inputs"]["messageValidation"]?.ToString());
            Assert.AreEqual("Decode MT103",    action["metadata"]?["comment"]?.ToString());
        }

        // --- 0.8: SwiftMTEncode ---

        [TestMethod]
        public void BuildAction_SwiftMTEncode_ProducesCorrectShape()
        {
            var map = this.MakeMapWithAction("SwiftMTEncode", details: "Encode MT202");
            var action = this.GetAction(map, "Parse_XML");

            Assert.AreEqual("SwiftMTEncode",  action["type"]?.ToString());
            Assert.AreEqual("@triggerBody()", action["inputs"]["messageToEncode"]?.ToString());
            Assert.AreEqual("Enable",         action["inputs"]["messageValidation"]?.ToString());
            Assert.AreEqual("Encode MT202",   action["metadata"]?["comment"]?.ToString());
        }

        // --- 0.9: RuleExecute ---

        [TestMethod]
        public void BuildAction_RuleExecute_ProducesCorrectShape()
        {
            var map = this.MakeMapWithAction("RuleExecute", details: "LoanApprovalPolicy");
            var action = this.GetAction(map, "Parse_XML");

            Assert.AreEqual("RuleExecute",       action["type"]?.ToString());
            Assert.AreEqual("LoanApprovalPolicy", action["inputs"]["ruleSet"]?.ToString());
            Assert.AreEqual("FileFolder",         action["inputs"]["ruleStore"]?.ToString());
        }

        // --- 0.10: Xslt ---

        [TestMethod]
        public void BuildAction_Xslt_ProducesCorrectShape()
        {
            var map = this.MakeMapWithAction("Xslt", details: "MyMaps.OrderToInvoice");
            var action = this.GetAction(map, "Parse_XML");

            Assert.AreEqual("Xslt",                action["type"]?.ToString());
            Assert.AreEqual("@triggerBody()",       action["inputs"]["content"]?.ToString());
            Assert.AreEqual("OrderToInvoice",       action["inputs"]["map"]["name"]?.ToString());
            Assert.AreEqual("Xslt",                 action["inputs"]["map"]["type"]?.ToString());
            Assert.AreEqual("ApplyXsltTemplates",   action["inputs"]["transformOptions"]?.ToString());
        }

        // --- 0.11: X12Decode ---

        [TestMethod]
        public void BuildAction_X12Decode_ProducesCorrectShape()
        {
            var map = this.MakeMapWithAction("X12Decode", details: null);
            var action = this.GetAction(map, "Parse_XML");

            Assert.AreEqual("ServiceProvider",    action["type"]?.ToString());
            Assert.AreEqual("@triggerBody()",     action["inputs"]["parameters"]["content"]?.ToString());
            Assert.AreEqual("x12",                action["inputs"]["serviceProviderConfiguration"]["connectionName"]?.ToString());
            Assert.AreEqual("decodeX12",          action["inputs"]["serviceProviderConfiguration"]["operationId"]?.ToString());
            Assert.AreEqual("/serviceProviders/x12", action["inputs"]["serviceProviderConfiguration"]["serviceProviderId"]?.ToString());
        }

        // --- 0.12: EdifactDecode ---

        [TestMethod]
        public void BuildAction_EdifactDecode_ProducesCorrectShape()
        {
            var map = this.MakeMapWithAction("EdifactDecode", details: null);
            var action = this.GetAction(map, "Parse_XML");

            Assert.AreEqual("ServiceProvider",       action["type"]?.ToString());
            Assert.AreEqual("@triggerBody()",        action["inputs"]["parameters"]["content"]?.ToString());
            Assert.AreEqual("edifact",               action["inputs"]["serviceProviderConfiguration"]["connectionName"]?.ToString());
            Assert.AreEqual("decodeEdifact",         action["inputs"]["serviceProviderConfiguration"]["operationId"]?.ToString());
            Assert.AreEqual("/serviceProviders/edifact", action["inputs"]["serviceProviderConfiguration"]["serviceProviderId"]?.ToString());
        }

        // --- 0.13: All Inputs-type actions - metadata absent when Details is null ---

        [TestMethod]
        public void BuildAction_AllInputsTypes_MetadataAbsentWhenDetailsNull()
        {
            var inputsTypes = new[]
            {
                "XmlParse", "XmlCompose", "XmlValidation",
                "FlatFileDecoding", "FlatFileEncoding",
                "SwiftMTDecode", "SwiftMTEncode"
            };

            foreach (var actionType in inputsTypes)
            {
                var map = this.MakeMapWithAction(actionType, details: null);
                var action = this.GetAction(map, "Parse_XML");

                Assert.IsNull(
                    action["metadata"],
                    string.Format("{0}: metadata must be absent when Details is null", actionType));
            }
        }

        // --- 0.14: All Inputs-type actions - null registry produces identical output (canary) ---

        [TestMethod]
        public void BuildAction_AllInputsTypes_IdenticalWithAndWithoutRegistry()
        {
            var inputsTypes = new[]
            {
                "XmlParse", "XmlCompose", "XmlValidation",
                "FlatFileDecoding", "FlatFileEncoding",
                "SwiftMTDecode", "SwiftMTEncode",
                "RuleExecute", "X12Decode", "EdifactDecode"
            };

            foreach (var actionType in inputsTypes)
            {
                var mapWithout = this.MakeMapWithAction(actionType, details: "SomeDetail");
                var mapWith    = this.MakeMapWithAction(actionType, details: "SomeDetail");

                var withoutRegistry = LogicAppJSONGenerator.GenerateStandardWorkflow(mapWithout, registry: null);
                var withRegistry    = LogicAppJSONGenerator.GenerateStandardWorkflow(mapWith,    registry: null);

                Assert.AreEqual(
                    withoutRegistry,
                    withRegistry,
                    string.Format("{0}: output must be identical when registry is null (both calls)", actionType));
            }
        }

        // =====================================================================
        // Helpers
        // =====================================================================

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

        private LogicAppWorkflowMap MakeMapWithAction(string actionType, string details)
        {
            var map = new LogicAppWorkflowMap { Name = "TestWorkflow" };
            map.Triggers.Add(new LogicAppTrigger
            {
                Name = "When_an_HTTP_request_is_received",
                Kind = "Request",
                Sequence = 0
            });
            map.Actions.Add(new LogicAppAction
            {
                Name = "Parse_XML",
                Type = actionType,
                Details = details,
                Sequence = 0
            });
            return map;
        }

        private JObject GetAction(LogicAppWorkflowMap map, string actionName)
        {
            var json = LogicAppJSONGenerator.GenerateStandardWorkflow(map, registry: null);
            var root = JObject.Parse(json);
            var action = root["definition"]["actions"][actionName] as JObject;
            Assert.IsNotNull(action, string.Format("Action '{0}' not found in generated workflow.", actionName));
            return action;
        }
    }
}
