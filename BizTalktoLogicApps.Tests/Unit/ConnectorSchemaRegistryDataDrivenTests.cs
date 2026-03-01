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

        // ?? Phase 1: ParameterSchema model ?????????????????????????????????????

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

        // ?? Phase 1: Dual-format parser ????????????????????????????????????????

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
    }
}
