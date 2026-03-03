// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BizTalktoLogicApps.ODXtoWFMigrator;
using Newtonsoft.Json.Linq;

namespace BizTalktoLogicApps.Tests.Unit
{
    /// <summary>
    /// Tests for the fully data-driven parameter resolution introduced in Phase 1-3:
    /// - ParameterSchema dual-format parsing (plain strings and objects)
    /// - ResolveValue routing for every ValueSource token
    /// - End-to-end workflow generation using both legacy and data-driven JSON entries
    /// </summary>
    [TestClass]
    public class ConnectorSchemaRegistryDataDrivenTests
    {
        // ?? helpers ????????????????????????????????????????????????????????????

        private static string RegistryPath =>
            Path.Combine(
                System.AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "..", "ODXtoWFMigrator",
                "Schemas", "Connectors", "connector-registry.json");

        private static ConnectorSchemaRegistry LoadRegistry()
        {
            var path = Path.GetFullPath(RegistryPath);
            Assert.IsTrue(File.Exists(path), $"Registry file not found at: {path}");
            return ConnectorSchemaRegistry.LoadFromFile(path);
        }

        private static LogicAppWorkflowMap MakeSendMap(
            string connectorKind,
            string targetAddress = null,
            string queueOrTopicName = null,
            string subscriptionName = null)
        {
            var map = new LogicAppWorkflowMap { Name = "Test" };
            map.Triggers.Add(new LogicAppTrigger { Name = "Trigger", Kind = "Request", Sequence = 0 });
            map.Actions.Add(new LogicAppAction
            {
                Name = "Send",
                Type = "SendConnector",
                ConnectorKind = connectorKind,
                TargetAddress = targetAddress,
                QueueOrTopicName = queueOrTopicName,
                SubscriptionName = subscriptionName,
                Sequence = 0
            });
            return map;
        }

        private static JObject GenerateAndGetAction(LogicAppWorkflowMap map, ConnectorSchemaRegistry registry, string actionName = "Send")
        {
            var json = LogicAppJSONGenerator.GenerateStandardWorkflow(map, registry: registry);
            var root = JObject.Parse(json);
            var action = root["definition"]["actions"][actionName];
            Assert.IsNotNull(action, $"Action '{actionName}' not found in generated workflow.");
            return (JObject)action;
        }

        // -- Phase 1: ParameterSchema model --

        [TestMethod]
        public void OperationSchema_Parameters_IsListOfParameterSchema()
        {
            var op = new OperationSchema();
            op.Parameters.Add(new ParameterSchema { Name = "foo", ValueSource = "Literal", DefaultValue = "bar" });
            Assert.AreEqual(1, op.Parameters.Count);
            Assert.AreEqual("foo", op.Parameters[0].Name);
            Assert.AreEqual("Literal", op.Parameters[0].ValueSource);
            Assert.AreEqual("bar", op.Parameters[0].DefaultValue);
        }

        // -- Step 1: InputsTemplate and ActionType model properties --

        [TestMethod]
        public void OperationSchema_InputsTemplate_DefaultsToNull()
        {
            var op = new OperationSchema();
            Assert.IsNull(op.InputsTemplate, "InputsTemplate must default to null");
        }

        [TestMethod]
        public void OperationSchema_ActionType_DefaultsToNull()
        {
            var op = new OperationSchema();
            Assert.IsNull(op.ActionType, "ActionType must default to null");
        }

        [TestMethod]
        public void OperationSchema_InputsTemplate_CanBeAssigned()
        {
            var op = new OperationSchema();
            var template = new JObject();
            template["content"] = "";
            op.InputsTemplate = template;
            op.ActionType = "XmlParse";

            Assert.IsNotNull(op.InputsTemplate);
            Assert.AreEqual("XmlParse", op.ActionType);
            Assert.IsTrue(op.InputsTemplate.ContainsKey("content"));
        }



        [TestMethod]
        public void LoadFromFile_LegacyStringParameters_ParsedAsLiteralSource()
        {
            // Sql connector still uses plain-string format for most parameters
            var registry = LoadRegistry();
            var sql = registry.GetConnector("Sql");
            Assert.IsNotNull(sql, "Sql connector should exist");

            // executeQuery has plain-string parameters
            Assert.IsTrue(sql.Actions.ContainsKey("executeQuery"));
            var op = sql.Actions["executeQuery"];
            Assert.IsTrue(op.Parameters.Count > 0, "executeQuery should have parameters");
            // Legacy plain strings are normalised to ValueSource=Literal
            foreach (var p in op.Parameters)
            {
                Assert.AreEqual("Literal", p.ValueSource,
                    $"Legacy parameter '{p.Name}' should have ValueSource=Literal");
                Assert.IsNotNull(p.Name, "Parameter name must not be null");
            }
        }

        [TestMethod]
        public void LoadFromFile_ObjectParameters_ParsedWithCorrectSourceAndDefault()
        {
            var registry = LoadRegistry();

            // FileSystem.createFile is migrated to object format
            var fs = registry.GetConnector("FileSystem");
            Assert.IsNotNull(fs);
            var createFile = fs.Actions["createFile"];

            Assert.AreEqual(2, createFile.Parameters.Count);

            var filePath = createFile.Parameters[0];
            Assert.AreEqual("filePath",       filePath.Name);
            Assert.AreEqual("TargetAddress",  filePath.ValueSource);
            Assert.AreEqual("/output/file.txt", filePath.DefaultValue);

            var body = createFile.Parameters[1];
            Assert.AreEqual("body",          body.Name);
            Assert.AreEqual("MessageBody",   body.ValueSource);
            Assert.AreEqual("@triggerBody()", body.DefaultValue);
        }

        [TestMethod]
        public void LoadFromFile_MixedFormatInSameConnector_BothParsedCorrectly()
        {
            var registry = LoadRegistry();
            var fs = registry.GetConnector("FileSystem");
            Assert.IsNotNull(fs);

            // getFileContent still uses plain strings
            var get = fs.Actions["getFileContent"];
            Assert.IsTrue(get.Parameters.Count > 0);
            Assert.AreEqual("Literal", get.Parameters[0].ValueSource);

            // createFile uses object format
            var create = fs.Actions["createFile"];
            Assert.AreEqual("TargetAddress", create.Parameters[0].ValueSource);
        }

        // ?? Phase 3: ResolveValue — TargetAddress ??????????????????????????????

        [TestMethod]
        public void ResolveParameters_TargetAddress_UsesActionTargetAddress()
        {
            var registry = LoadRegistry();
            var map = MakeSendMap("FileSystem", targetAddress: @"\\server\share\out.xml");
            var action = GenerateAndGetAction(map, registry);

            var filePath = action["inputs"]["parameters"]["filePath"]?.ToString();
            Assert.AreEqual(@"\\server\share\out.xml", filePath,
                "filePath should come from act.TargetAddress");
        }

        [TestMethod]
        public void ResolveParameters_TargetAddress_FallsBackToDefault_WhenNull()
        {
            var registry = LoadRegistry();
            var map = MakeSendMap("FileSystem", targetAddress: null);
            var action = GenerateAndGetAction(map, registry);

            var filePath = action["inputs"]["parameters"]["filePath"]?.ToString();
            Assert.AreEqual("/output/file.txt", filePath,
                "filePath should fall back to DefaultValue when TargetAddress is null");
        }

        // ?? Phase 3: ResolveValue — MessageBody ???????????????????????????????

        [TestMethod]
        public void ResolveParameters_MessageBody_UsesTriggerBodyWhenNoSource()
        {
            var registry = LoadRegistry();
            var map = MakeSendMap("FileSystem", targetAddress: "/out/file.txt");
            var action = GenerateAndGetAction(map, registry);

            var body = action["inputs"]["parameters"]["body"]?.ToString();
            Assert.AreEqual("@triggerBody()", body,
                "body should be @triggerBody() when InputMessageSourceAction is null");
        }

        [TestMethod]
        public void ResolveParameters_MessageBody_UsesBodyExpressionWhenSourceSet()
        {
            var registry = LoadRegistry();
            var map = new LogicAppWorkflowMap { Name = "Test" };
            map.Triggers.Add(new LogicAppTrigger { Name = "Trigger", Kind = "Request", Sequence = 0 });
            map.Actions.Add(new LogicAppAction
            {
                Name = "Send",
                Type = "SendConnector",
                ConnectorKind = "FileSystem",
                TargetAddress = "/out/file.txt",
                InputMessageSourceAction = "Transform",
                Sequence = 0
            });

            var action = GenerateAndGetAction(map, registry);
            var body = action["inputs"]["parameters"]["body"]?.ToString();
            Assert.AreEqual("@body('Transform')", body,
                "body should reference the source action via @body()");
        }

        // ?? Phase 3: ResolveValue — QueueName ?????????????????????????????????

        [TestMethod]
        public void ResolveParameters_QueueName_UsesQueueOrTopicName()
        {
            var registry = LoadRegistry();
            var map = MakeSendMap("ServiceBus", queueOrTopicName: "my-orders-queue");
            var action = GenerateAndGetAction(map, registry);

            var entityName = action["inputs"]["parameters"]["entityName"]?.ToString();
            Assert.AreEqual("my-orders-queue", entityName,
                "entityName should come from act.QueueOrTopicName");
        }

        [TestMethod]
        public void ResolveParameters_QueueName_FallsBackToDefault_WhenNull()
        {
            var registry = LoadRegistry();
            var map = MakeSendMap("ServiceBus", queueOrTopicName: null);
            var action = GenerateAndGetAction(map, registry);

            var entityName = action["inputs"]["parameters"]["entityName"]?.ToString();
            Assert.AreEqual("queue", entityName,
                "entityName should fall back to DefaultValue 'queue'");
        }

        // ?? Phase 3: ResolveValue — EventHubName ?????????????????????????????

        [TestMethod]
        public void ResolveParameters_EventHubName_UsesQueueOrTopicName()
        {
            var registry = LoadRegistry();
            var map = MakeSendMap("EventHub", queueOrTopicName: "telemetry-hub");
            var action = GenerateAndGetAction(map, registry);

            var hubName = action["inputs"]["parameters"]["eventHubName"]?.ToString();
            Assert.AreEqual("telemetry-hub", hubName,
                "eventHubName should come from act.QueueOrTopicName");
        }

        // ?? Phase 3: ResolveValue — Literal ???????????????????????????????????

        [TestMethod]
        public void ResolveParameters_Literal_WriteDefaultValueVerbatim()
        {
            var registry = LoadRegistry();
            var map = MakeSendMap("ServiceBus", queueOrTopicName: "orders");
            var action = GenerateAndGetAction(map, registry);

            // contentType is Literal with DefaultValue "application/json"
            var contentType = action["inputs"]["parameters"]["contentType"]?.ToString();
            Assert.AreEqual("application/json", contentType,
                "Literal parameter should write DefaultValue verbatim");
        }

        // ?? Phase 3: Trigger parameter resolution ?????????????????????????????

        [TestMethod]
        public void BuildTriggerParameters_FolderPath_FromTriggerFolderPath()
        {
            var registry = LoadRegistry();
            var map = new LogicAppWorkflowMap { Name = "Test" };
            map.Triggers.Add(new LogicAppTrigger
            {
                Name = "FileSystem_Trigger",
                Kind = "FileSystem",
                FolderPath = @"\\nas\inbound",
                Sequence = 0
            });

            var json = LogicAppJSONGenerator.GenerateStandardWorkflow(map, registry: registry);
            var root = JObject.Parse(json);
            var trigger = root["definition"]["triggers"]["FileSystem_Trigger"];
            Assert.IsNotNull(trigger);

            var folderPath = trigger["inputs"]["parameters"]["folderPath"]?.ToString();
            Assert.AreEqual(@"\\nas\inbound", folderPath,
                "folderPath should come from trigger.FolderPath");
        }

        [TestMethod]
        public void BuildTriggerParameters_FolderPath_FallsBackToDefault_WhenNull()
        {
            var registry = LoadRegistry();
            var map = new LogicAppWorkflowMap { Name = "Test" };
            map.Triggers.Add(new LogicAppTrigger
            {
                Name = "FileSystem_Trigger",
                Kind = "FileSystem",
                FolderPath = null,
                Sequence = 0
            });

            var json = LogicAppJSONGenerator.GenerateStandardWorkflow(map, registry: registry);
            var root = JObject.Parse(json);
            var folderPath = root["definition"]["triggers"]["FileSystem_Trigger"]["inputs"]["parameters"]["folderPath"]?.ToString();
            Assert.AreEqual("/", folderPath,
                "folderPath should fall back to DefaultValue '/' when FolderPath is null");
        }

        [TestMethod]
        public void BuildTriggerParameters_QueueName_InferredFromTriggerAddress()
        {
            var registry = LoadRegistry();
            var map = new LogicAppWorkflowMap { Name = "Test" };
            map.Triggers.Add(new LogicAppTrigger
            {
                Name = "SB_Trigger",
                Kind = "ServiceBus",
                Address = "sb://myns.servicebus.windows.net/invoices",
                Sequence = 0
            });

            var json = LogicAppJSONGenerator.GenerateStandardWorkflow(map, registry: registry);
            var root = JObject.Parse(json);
            var queueName = root["definition"]["triggers"]["SB_Trigger"]["inputs"]["parameters"]["queueName"]?.ToString();
            Assert.AreEqual("invoices", queueName,
                "queueName should be inferred as the last path segment of the trigger address");
        }

        // ?? Phase 3: Connector service provider ID ?????????????????????????????

        [TestMethod]
        public void BuildAction_ServiceProviderAction_UsesRegistryServiceProviderId()
        {
            var registry = LoadRegistry();
            var map = MakeSendMap("FileSystem", targetAddress: "/out/file.txt");
            var action = GenerateAndGetAction(map, registry);

            var spId = action["inputs"]["serviceProviderConfiguration"]["serviceProviderId"]?.ToString();
            Assert.AreEqual("/serviceProviders/fileSystem", spId,
                "serviceProviderId should come from the registry, not be hardcoded");
        }

        [TestMethod]
        public void BuildAction_ServiceProviderAction_UsesRegistryOperationId()
        {
            var registry = LoadRegistry();
            var map = MakeSendMap("FileSystem", targetAddress: "/out/file.txt");
            var action = GenerateAndGetAction(map, registry);

            var opId = action["inputs"]["serviceProviderConfiguration"]["operationId"]?.ToString();
            Assert.AreEqual("createFile", opId,
                "operationId should come from the registry DefaultAction");
        }

        // ?? Phase 3: FTP data-driven parameters ????????????????????????????????

        [TestMethod]
        public void BuildAction_Ftp_CreateFile_DataDrivenParameters()
        {
            var registry = LoadRegistry();
            var map = MakeSendMap("Ftp", targetAddress: "/upload/orders.xml");
            var action = GenerateAndGetAction(map, registry);

            var filePath    = action["inputs"]["parameters"]["filePath"]?.ToString();
            var fileContent = action["inputs"]["parameters"]["fileContent"]?.ToString();

            Assert.AreEqual("/upload/orders.xml", filePath,
                "filePath should come from act.TargetAddress");
            Assert.AreEqual("@triggerBody()", fileContent,
                "fileContent should resolve to @triggerBody() via MessageBody source");
        }

        // ?? Backward compat: legacy connectors still work ?????????????????????

        [TestMethod]
        public void BuildAction_UnknownConnector_FallsBackToLegacyPath()
        {
            var registry = LoadRegistry();
            var map = MakeSendMap("UNKNOWNLEGACY", targetAddress: "http://x");
            var action = GenerateAndGetAction(map, registry);

            // Legacy path produces a ServiceProvider action with type ServiceProvider
            Assert.AreEqual("ServiceProvider", action["type"]?.ToString(),
                "Unknown connector should fall back to legacy ServiceProvider generation");
        }

        [TestMethod]
        public void BuildAction_NullRegistry_LegacyPathUsed()
        {
            var map = MakeSendMap("FileSystem", targetAddress: @"\\server\out.txt");
            var action = GenerateAndGetAction(map, registry: null);

            // Legacy path still produces a ServiceProvider action
            Assert.AreEqual("ServiceProvider", action["type"]?.ToString(),
                "Null registry should fall back to legacy generation");
        }

        // -- Step 2: LoadFromFile parser reads Inputs/inputs and ActionType --

        [TestMethod]
        public void LoadFromFile_XmlParse_InputsTemplate_IsJObject()
        {
            var registry = LoadRegistry();
            var connector = registry.GetConnector("XmlOperations");
            Assert.IsNotNull(connector, "XmlOperations connector must exist");

            Assert.IsTrue(connector.Actions.ContainsKey("XmlParse"), "XmlParse action must exist");
            var op = connector.Actions["XmlParse"];

            Assert.IsNotNull(op.InputsTemplate, "XmlParse must have a non-null InputsTemplate");
            Assert.IsTrue(op.InputsTemplate.ContainsKey("content"), "InputsTemplate must have 'content' key");
            Assert.IsTrue(op.InputsTemplate.ContainsKey("schema"), "InputsTemplate must have 'schema' key");
            Assert.IsTrue(op.InputsTemplate.ContainsKey("xmlReaderSettings"), "InputsTemplate must have 'xmlReaderSettings' key");
            Assert.IsTrue(op.InputsTemplate.ContainsKey("jsonWriterSettings"), "InputsTemplate must have 'jsonWriterSettings' key");
        }

        [TestMethod]
        public void LoadFromFile_XmlParse_ActionType_EqualsOperationId()
        {
            var registry = LoadRegistry();
            var op = registry.GetConnector("XmlOperations").Actions["XmlParse"];

            Assert.AreEqual("XmlParse", op.ActionType,
                "ActionType must default to OperationId when InputsTemplate is present");
        }

        [TestMethod]
        public void LoadFromFile_SwiftMTDecode_InputsTemplate_HasCorrectKeys()
        {
            var registry = LoadRegistry();
            var connector = registry.GetConnector("SWIFT");
            Assert.IsNotNull(connector, "SWIFT connector must exist");

            var op = connector.Actions["SwiftMTDecode"];
            Assert.IsNotNull(op.InputsTemplate, "SwiftMTDecode must have InputsTemplate");
            Assert.IsTrue(op.InputsTemplate.ContainsKey("messageToDecode"), "InputsTemplate must have 'messageToDecode'");
            Assert.IsTrue(op.InputsTemplate.ContainsKey("messageValidation"), "InputsTemplate must have 'messageValidation'");
            Assert.AreEqual("Enable", op.InputsTemplate["messageValidation"]?.ToString(),
                "messageValidation default value must be 'Enable'");
        }

        [TestMethod]
        public void LoadFromFile_HL7Decode_InputsArrayConverted_ToJObject()
        {
            var registry = LoadRegistry();
            var connector = registry.GetConnector("HL7");
            Assert.IsNotNull(connector, "HL7 connector must exist");

            var op = connector.Actions["HL7Decode"];
            Assert.IsNotNull(op.InputsTemplate, "HL7Decode must have InputsTemplate");
            Assert.AreEqual(JTokenType.Object, op.InputsTemplate.Type,
                "JArray inputs must be converted to JObject");
            Assert.IsTrue(op.InputsTemplate.ContainsKey("messageToDecode"),
                "Converted template must have 'messageToDecode' key");
        }

        [TestMethod]
        public void LoadFromFile_AS2Encode_LowercaseInputs_Parsed()
        {
            var registry = LoadRegistry();
            var connector = registry.GetConnector("AS2");
            Assert.IsNotNull(connector, "AS2 connector must exist");

            var op = connector.Actions["AS2Encode"];
            Assert.IsNotNull(op.InputsTemplate,
                "AS2Encode must have InputsTemplate (lowercase 'inputs' key must be handled)");
            Assert.IsTrue(op.InputsTemplate.ContainsKey("messageToEncode"),
                "Template must contain 'messageToEncode' key");
        }

        [TestMethod]
        public void LoadFromFile_X12Encode_InputsArray_Converted()
        {
            var registry = LoadRegistry();
            var connector = registry.GetConnector("X12");
            Assert.IsNotNull(connector, "X12 connector must exist");

            var op = connector.Actions["X12Encode"];
            Assert.IsNotNull(op.InputsTemplate, "X12Encode must have InputsTemplate");
            Assert.AreEqual(JTokenType.Object, op.InputsTemplate.Type,
                "JArray inputs must be converted to JObject");
        }

        [TestMethod]
        public void LoadFromFile_FileSystem_CreateFile_InputsTemplate_IsNull()
        {
            var registry = LoadRegistry();
            var op = registry.GetConnector("FileSystem").Actions["createFile"];

            Assert.IsNull(op.InputsTemplate,
                "FileSystem.createFile uses Parameters — InputsTemplate must be null");
            Assert.IsNull(op.ActionType,
                "FileSystem.createFile has no InputsTemplate — ActionType must be null");
        }

        [TestMethod]
        public void LoadFromFile_Xslt_InputsTemplate_HasContentAndMapKeys()
        {
            var registry = LoadRegistry();
            var op = registry.GetConnector("XmlOperations").Actions["Xslt"];

            Assert.IsNotNull(op.InputsTemplate, "Xslt must have InputsTemplate");
            Assert.IsTrue(op.InputsTemplate.ContainsKey("content"), "InputsTemplate must have 'content' key");
            Assert.IsTrue(op.InputsTemplate.ContainsKey("map"), "InputsTemplate must have 'map' key");
            Assert.AreEqual("Xslt", op.ActionType);
        }

        [TestMethod]
        public void LoadFromFile_RuleExecute_InputsArray_HasRuleSetKey()
        {
            var registry = LoadRegistry();
            var connector = registry.GetConnector("Rules");
            Assert.IsNotNull(connector, "Rules connector must exist");

            var op = connector.Actions["RuleExecute"];
            Assert.IsNotNull(op.InputsTemplate, "RuleExecute must have InputsTemplate");
            Assert.IsTrue(op.InputsTemplate.ContainsKey("ruleSet"),
                "Converted template must contain 'ruleSet' key");
        }

        [TestMethod]
        public void LoadFromFile_ExistingParameterParsing_Unchanged()
        {
            // Verify the pre-existing parameter tests still hold after parser changes.
            // FileSystem.createFile: object-format parameters untouched.
            var registry = LoadRegistry();
            var op = registry.GetConnector("FileSystem").Actions["createFile"];

            Assert.AreEqual(2, op.Parameters.Count);
            Assert.AreEqual("TargetAddress", op.Parameters[0].ValueSource);
            Assert.AreEqual("MessageBody",   op.Parameters[1].ValueSource);
        }

        // -- Step 3: GetOperationByActionType --

        [TestMethod]
        public void GetOperationByActionType_XmlParse_ReturnsOperation()
        {
            var registry = LoadRegistry();
            var op = registry.GetOperationByActionType("XmlParse");

            Assert.IsNotNull(op, "XmlParse must be found");
            Assert.IsNotNull(op.InputsTemplate, "InputsTemplate must be non-null");
            Assert.AreEqual("XmlParse", op.ActionType);
        }

        [TestMethod]
        public void GetOperationByActionType_SwiftMTDecode_ReturnsOperation()
        {
            var registry = LoadRegistry();
            var op = registry.GetOperationByActionType("SwiftMTDecode");

            Assert.IsNotNull(op, "SwiftMTDecode must be found");
            Assert.IsNotNull(op.InputsTemplate);
            Assert.AreEqual("SwiftMTDecode", op.ActionType);
        }

        [TestMethod]
        public void GetOperationByActionType_FlatFileDecoding_ReturnsOperation()
        {
            var registry = LoadRegistry();
            var op = registry.GetOperationByActionType("FlatFileDecoding");

            Assert.IsNotNull(op, "FlatFileDecoding must be found");
            Assert.IsNotNull(op.InputsTemplate);
            Assert.AreEqual("FlatFileDecoding", op.ActionType);
        }

        [TestMethod]
        public void GetOperationByActionType_RuleExecute_ReturnsOperationWithRuleSetKey()
        {
            var registry = LoadRegistry();
            var op = registry.GetOperationByActionType("RuleExecute");

            Assert.IsNotNull(op, "RuleExecute must be found");
            Assert.IsNotNull(op.InputsTemplate);
            Assert.IsTrue(op.InputsTemplate.ContainsKey("ruleSet"),
                "InputsTemplate must contain 'ruleSet' key");
        }

        [TestMethod]
        public void GetOperationByActionType_Unknown_ReturnsNull()
        {
            var registry = LoadRegistry();
            var op = registry.GetOperationByActionType("NonExistentActionType");

            Assert.IsNull(op, "Unknown action type must return null");
        }

        [TestMethod]
        public void GetOperationByActionType_NullInput_ReturnsNull()
        {
            var registry = LoadRegistry();
            var op = registry.GetOperationByActionType(null);

            Assert.IsNull(op, "Null input must return null");
        }

        [TestMethod]
        public void GetOperationByActionType_ServiceProviderAction_ReturnsNull()
        {
            // createFile is a Parameters-based operation — no ActionType, no InputsTemplate.
            // GetOperationByActionType must not return it even if the name happens to match.
            var registry = LoadRegistry();
            var op = registry.GetOperationByActionType("createFile");

            Assert.IsNull(op,
                "createFile is a ServiceProvider operation and must not be returned");
        }

        // -- Step 4: BuildActionFromRegistry wiring --

        private static LogicAppWorkflowMap MakeBuiltInActionMap(
            string actionType,
            string details = null,
            string inputMessageSourceAction = null)
        {
            var map = new LogicAppWorkflowMap { Name = "Test" };
            map.Triggers.Add(new LogicAppTrigger { Name = "Trigger", Kind = "Request", Sequence = 0 });
            map.Actions.Add(new LogicAppAction
            {
                Name = "Action",
                Type = actionType,
                Details = details,
                InputMessageSourceAction = inputMessageSourceAction,
                Sequence = 0
            });
            return map;
        }

        private static JObject GenerateAndGetBuiltInAction(
            LogicAppWorkflowMap map,
            ConnectorSchemaRegistry registry,
            string actionName = "Action")
        {
            var json = LogicAppJSONGenerator.GenerateStandardWorkflow(map, registry: registry);
            var root = JObject.Parse(json);
            var action = root["definition"]["actions"][actionName];
            Assert.IsNotNull(action, $"Action '{actionName}' not found in generated workflow.");
            return (JObject)action;
        }

        [TestMethod]
        public void BuildAction_XmlParse_WithRegistry_UsesRegistryPath()
        {
            var registry = LoadRegistry();
            var map = MakeBuiltInActionMap("XmlParse");
            var action = GenerateAndGetBuiltInAction(map, registry);

            Assert.AreEqual("XmlParse", action["type"]?.ToString(),
                "type must be XmlParse from the registry ActionType");
            Assert.IsNotNull(action["inputs"],
                "inputs must be populated from the registry InputsTemplate");
        }

        [TestMethod]
        public void BuildAction_XmlParse_WithNullRegistry_FallsBackToSwitch()
        {
            var map = MakeBuiltInActionMap("XmlParse");
            var action = GenerateAndGetBuiltInAction(map, registry: null);

            // The hardcoded switch still produces XmlParse
            Assert.AreEqual("XmlParse", action["type"]?.ToString(),
                "type must still be XmlParse via the hardcoded switch fallback");
        }

        [TestMethod]
        public void BuildAction_FlatFileDecoding_WithRegistry_UsesRegistryPath()
        {
            var registry = LoadRegistry();
            var map = MakeBuiltInActionMap("FlatFileDecoding");
            var action = GenerateAndGetBuiltInAction(map, registry);

            Assert.AreEqual("FlatFileDecoding", action["type"]?.ToString());
            Assert.IsNotNull(action["inputs"]["content"],
                "inputs.content must be present");
            Assert.IsNotNull(action["inputs"]["schema"],
                "inputs.schema must be present");
        }

        [TestMethod]
        public void BuildAction_SwiftMTDecode_WithRegistry_UsesRegistryPath()
        {
            var registry = LoadRegistry();
            var map = MakeBuiltInActionMap("SwiftMTDecode");
            var action = GenerateAndGetBuiltInAction(map, registry);

            Assert.AreEqual("SwiftMTDecode", action["type"]?.ToString());
            Assert.IsNotNull(action["inputs"]["messageToDecode"],
                "inputs.messageToDecode must be present");
        }

        [TestMethod]
        public void BuildAction_RuleExecute_WithRegistry_UsesRegistryPath()
        {
            var registry = LoadRegistry();
            var map = MakeBuiltInActionMap("RuleExecute", details: "MyRuleSet");
            var action = GenerateAndGetBuiltInAction(map, registry);

            Assert.AreEqual("RuleExecute", action["type"]?.ToString());
            Assert.IsNotNull(action["inputs"]["ruleSet"],
                "inputs.ruleSet must be present");
        }

        [TestMethod]
        public void BuildAction_Xslt_WithRegistry_UsesRegistryPath()
        {
            var registry = LoadRegistry();
            var map = MakeBuiltInActionMap("Xslt", details: "MyNs.MyMap");
            var action = GenerateAndGetBuiltInAction(map, registry);

            Assert.AreEqual("Xslt", action["type"]?.ToString());
            Assert.IsNotNull(action["inputs"]["content"],
                "inputs.content must be present");
            Assert.IsNotNull(action["inputs"]["map"],
                "inputs.map must be present");
        }

        [TestMethod]
        public void BuildAction_UnknownType_NotInRegistry_FallsThrough()
        {
            var registry = LoadRegistry();
            // "Wait" is not in any registry InputsTemplate; switch handles it
            var map = MakeBuiltInActionMap("Wait", details: "new System.TimeSpan(0, 5, 0)");
            var action = GenerateAndGetBuiltInAction(map, registry);

            Assert.AreEqual("Wait", action["type"]?.ToString(),
                "Wait must still be handled by the switch fallback");
        }

        // -- Step 5: ResolveInputsPlaceholders resolution rules --

        [TestMethod]
        public void ResolveInputsPlaceholders_Content_ResolvedToTriggerBody()
        {
            var registry = LoadRegistry();
            var map = MakeBuiltInActionMap("XmlParse", inputMessageSourceAction: null);
            var action = GenerateAndGetBuiltInAction(map, registry);

            Assert.AreEqual("@triggerBody()", action["inputs"]["content"]?.ToString(),
                "content must resolve to @triggerBody() when no InputMessageSourceAction");
        }

        [TestMethod]
        public void ResolveInputsPlaceholders_Content_ResolvedToBodyExpression_WhenSourceActionSet()
        {
            var registry = LoadRegistry();
            var map = MakeBuiltInActionMap("XmlParse", inputMessageSourceAction: "Transform");
            var action = GenerateAndGetBuiltInAction(map, registry);

            Assert.AreEqual("@body('Transform')", action["inputs"]["content"]?.ToString(),
                "content must resolve to @body('Transform') when InputMessageSourceAction is set");
        }

        [TestMethod]
        public void ResolveInputsPlaceholders_MessageToDecode_ResolvedToTriggerBody()
        {
            var registry = LoadRegistry();
            var map = MakeBuiltInActionMap("SwiftMTDecode");
            var action = GenerateAndGetBuiltInAction(map, registry);

            Assert.AreEqual("@triggerBody()", action["inputs"]["messageToDecode"]?.ToString(),
                "messageToDecode must resolve to @triggerBody()");
        }

        [TestMethod]
        public void ResolveInputsPlaceholders_MessageToEncode_ResolvedToTriggerBody()
        {
            var registry = LoadRegistry();
            var map = MakeBuiltInActionMap("SwiftMTEncode");
            var action = GenerateAndGetBuiltInAction(map, registry);

            Assert.AreEqual("@triggerBody()", action["inputs"]["messageToEncode"]?.ToString(),
                "messageToEncode must resolve to @triggerBody()");
        }

        [TestMethod]
        public void ResolveInputsPlaceholders_BatchMessage_ResolvedToTriggerBody()
        {
            var registry = LoadRegistry();
            var map = MakeBuiltInActionMap("X12BatchEncode");
            var action = GenerateAndGetBuiltInAction(map, registry);

            Assert.AreEqual("@triggerBody()", action["inputs"]["batchMessage"]?.ToString(),
                "batchMessage must resolve to @triggerBody()");
        }

        [TestMethod]
        public void ResolveInputsPlaceholders_StaticValue_KeptVerbatim()
        {
            var registry = LoadRegistry();
            var map = MakeBuiltInActionMap("SwiftMTDecode");
            var action = GenerateAndGetBuiltInAction(map, registry);

            Assert.AreEqual("Enable", action["inputs"]["messageValidation"]?.ToString(),
                "messageValidation static value 'Enable' must be kept verbatim");
        }

        [TestMethod]
        public void ResolveInputsPlaceholders_TransformOptions_KeptVerbatim()
        {
            var registry = LoadRegistry();
            var map = MakeBuiltInActionMap("Xslt", details: "MyNs.MyMap");
            var action = GenerateAndGetBuiltInAction(map, registry);

            Assert.AreEqual("ApplyXsltTemplates", action["inputs"]["transformOptions"]?.ToString(),
                "transformOptions static value must be kept verbatim from the registry template");
        }

        [TestMethod]
        public void ResolveInputsPlaceholders_SchemaName_ResolvedFromDetails()
        {
            var registry = LoadRegistry();
            var map = MakeBuiltInActionMap("XmlParse", details: "MySchemaName");
            var action = GenerateAndGetBuiltInAction(map, registry);

            var schemaName = action["inputs"]["schema"]["name"]?.ToString();
            Assert.AreEqual("MySchemaName", schemaName,
                "schema.name must be resolved from act.Details");
        }

        [TestMethod]
        public void ResolveInputsPlaceholders_SchemaName_LeftAsPlaceholder_WhenDetailsNull()
        {
            var registry = LoadRegistry();
            var map = MakeBuiltInActionMap("XmlParse", details: null);
            var action = GenerateAndGetBuiltInAction(map, registry);

            var schemaName = action["inputs"]["schema"]["name"]?.ToString();
            Assert.AreEqual("{{SCHEMA_NAME}}", schemaName,
                "schema.name must be left as {{SCHEMA_NAME}} when act.Details is null");
        }

        [TestMethod]
        public void ResolveInputsPlaceholders_FlatFileSchemaName_ResolvedFromDetails()
        {
            var registry = LoadRegistry();
            var map = MakeBuiltInActionMap("FlatFileDecoding", details: "MyFlatFileSchema");
            var action = GenerateAndGetBuiltInAction(map, registry);

            var schemaName = action["inputs"]["schema"]["name"]?.ToString();
            Assert.AreEqual("MyFlatFileSchema", schemaName,
                "schema.name must be resolved from act.Details for FlatFileDecoding");
        }

        [TestMethod]
        public void ResolveInputsPlaceholders_XsltMapName_ResolvedFromDetails()
        {
            var registry = LoadRegistry();
            var map = MakeBuiltInActionMap("Xslt", details: "MyNs.MyProject.OrderMap");
            var action = GenerateAndGetBuiltInAction(map, registry);

            var mapName = action["inputs"]["map"]["name"]?.ToString();
            Assert.AreEqual("OrderMap", mapName,
                "map.name must be the short name extracted from act.Details");
        }

        [TestMethod]
        public void ResolveInputsPlaceholders_XsltMapName_FallsBackToMapName_WhenDetailsNull()
        {
            var registry = LoadRegistry();
            var map = MakeBuiltInActionMap("Xslt", details: null);
            var action = GenerateAndGetBuiltInAction(map, registry);

            var mapName = action["inputs"]["map"]["name"]?.ToString();
            Assert.AreEqual("MapName", mapName,
                "map.name must fall back to 'MapName' when act.Details is null");
        }

        [TestMethod]
        public void ResolveInputsPlaceholders_RuleSet_ResolvedFromDetails()
        {
            var registry = LoadRegistry();
            var map = MakeBuiltInActionMap("RuleExecute", details: "LoanApprovalPolicy");
            var action = GenerateAndGetBuiltInAction(map, registry);

            Assert.AreEqual("LoanApprovalPolicy", action["inputs"]["ruleSet"]?.ToString(),
                "ruleSet must be resolved from act.Details");
        }

        [TestMethod]
        public void ResolveInputsPlaceholders_RuleSet_DefaultsToRuleset_WhenDetailsNull()
        {
            var registry = LoadRegistry();
            var map = MakeBuiltInActionMap("RuleExecute", details: null);
            var action = GenerateAndGetBuiltInAction(map, registry);

            Assert.AreEqual("Ruleset", action["inputs"]["ruleSet"]?.ToString(),
                "ruleSet must default to 'Ruleset' when act.Details is null");
        }

        [TestMethod]
        public void ResolveInputsPlaceholders_RuleStore_AlwaysFileFolder()
        {
            var registry = LoadRegistry();
            var map = MakeBuiltInActionMap("RuleExecute", details: "SomePolicy");
            var action = GenerateAndGetBuiltInAction(map, registry);

            Assert.AreEqual("FileFolder", action["inputs"]["ruleStore"]?.ToString(),
                "ruleStore must always resolve to 'FileFolder'");
        }

        [TestMethod]
        public void ResolveInputsPlaceholders_MetadataComment_PopulatedFromDetails()
        {
            var registry = LoadRegistry();
            var map = MakeBuiltInActionMap("XmlParse", details: "MySchemaName");
            var action = GenerateAndGetBuiltInAction(map, registry);

            var comment = action["metadata"]?["comment"]?.ToString();
            Assert.AreEqual("MySchemaName", comment,
                "metadata.comment must be set to act.Details when Details is non-empty");
        }

        [TestMethod]
        public void ResolveInputsPlaceholders_MetadataComment_AbsentWhenDetailsNull()
        {
            var registry = LoadRegistry();
            var map = MakeBuiltInActionMap("XmlParse", details: null);
            var action = GenerateAndGetBuiltInAction(map, registry);

            Assert.IsNull(action["metadata"],
                "metadata must be absent when act.Details is null");
        }

        [TestMethod]
        public void ResolveInputsPlaceholders_TemplateDeepCloned_RegistryNotMutated()
        {
            var registry = LoadRegistry();

            // Generate the same action type twice with different Details values
            var map1 = MakeBuiltInActionMap("XmlParse", details: "Schema1");
            var map2 = MakeBuiltInActionMap("XmlParse", details: "Schema2");

            var action1 = GenerateAndGetBuiltInAction(map1, registry);
            var action2 = GenerateAndGetBuiltInAction(map2, registry);

            Assert.AreEqual("Schema1", action1["inputs"]["schema"]["name"]?.ToString(),
                "First generation must use Schema1");
            Assert.AreEqual("Schema2", action2["inputs"]["schema"]["name"]?.ToString(),
                "Second generation must use Schema2 — registry template must not be mutated");
        }

        [TestMethod]
        public void ResolveInputsPlaceholders_Xslt_XmlReaderSettingsPreserved()
        {
            var registry = LoadRegistry();
            var map = MakeBuiltInActionMap("XmlParse", details: "MySchema");
            var action = GenerateAndGetBuiltInAction(map, registry);

            var xmlReaderSettings = action["inputs"]["xmlReaderSettings"];
            Assert.IsNotNull(xmlReaderSettings,
                "xmlReaderSettings nested object must be preserved");
            Assert.AreEqual("Prohibit", xmlReaderSettings["dtdProcessing"]?.ToString(),
                "dtdProcessing static value must be kept verbatim");
            Assert.AreEqual("True", xmlReaderSettings["ignoreWhitespace"]?.ToString(),
                "ignoreWhitespace boolean must be preserved");
        }

        [TestMethod]
        public void ResolveInputsPlaceholders_HL7Decode_MessageToDecodeResolved()
        {
            var registry = LoadRegistry();
            var map = MakeBuiltInActionMap("HL7Decode");
            var action = GenerateAndGetBuiltInAction(map, registry);

            Assert.AreEqual("HL7Decode", action["type"]?.ToString());
            Assert.AreEqual("@triggerBody()", action["inputs"]["messageToDecode"]?.ToString(),
                "messageToDecode must resolve to @triggerBody()");
        }

        [TestMethod]
        public void ResolveInputsPlaceholders_FlatFileEncoding_ContentResolved()
        {
            var registry = LoadRegistry();
            var map = MakeBuiltInActionMap("FlatFileEncoding", details: "FlatSchema", inputMessageSourceAction: "ParseStep");
            var action = GenerateAndGetBuiltInAction(map, registry);

            Assert.AreEqual("FlatFileEncoding", action["type"]?.ToString());
            Assert.AreEqual("@body('ParseStep')", action["inputs"]["content"]?.ToString(),
                "content must resolve to @body('ParseStep') via InputMessageSourceAction");
            Assert.AreEqual("FlatSchema", action["inputs"]["schema"]["name"]?.ToString(),
                "schema.name must resolve from act.Details");
        }
    }
}
