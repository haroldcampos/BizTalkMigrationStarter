// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BizTalktoLogicApps.BTPtoLA.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BizTalktoLogicApps.Tests.Unit.BTPtoLA
{
    /// <summary>
    /// Unit tests for PipelineConnectorRegistry.
    /// Verifies that all three JSON sections (components, edifactComponents, as2Components)
    /// are loaded into the registry and resolved correctly by GetMapping().
    /// </summary>
    [TestClass]
    public class PipelineConnectorRegistryTests
    {
        private static string RegistryJson => BuildRegistryJson();

        /// <summary>
        /// Builds a minimal but representative registry JSON covering all three sections
        /// and the customComponents fallback pattern, without depending on the file system.
        /// </summary>
        private static string BuildRegistryJson()
        {
            var obj = new JObject
            {
                ["components"] = new JObject
                {
                    ["Microsoft.BizTalk.Component.XmlDasmComp"] = new JObject
                    {
                        ["displayName"] = "XML Disassembler",
                        ["category"] = "Disassembling",
                        ["complexity"] = "Medium",
                        ["migrationNotes"] = new JArray("XML note"),
                        ["requiredResources"] = new JArray("XSD schema"),
                        ["logicAppsAction"] = new JObject
                        {
                            ["type"] = "Foreach",
                            ["description"] = "XML disassembly"
                        }
                    }
                },
                ["edifactComponents"] = new JObject
                {
                    ["Microsoft.BizTalk.Edi.DefaultPipelines.EdiReceive"] = new JObject
                    {
                        ["displayName"] = "EDI Receive Pipeline",
                        ["complexity"] = "Medium",
                        ["migrationNotes"] = new JArray("EDI note"),
                        ["requiredResources"] = new JArray("Integration Account"),
                        ["logicAppsAction"] = new JObject
                        {
                            ["type"] = "EdifactDecode",
                            ["description"] = "Decodes EDIFACT messages"
                        }
                    },
                    ["Microsoft.BizTalk.Edi.DefaultPipelines.EdiSend"] = new JObject
                    {
                        ["displayName"] = "EDI Send Pipeline",
                        ["complexity"] = "Medium",
                        ["migrationNotes"] = new JArray("EDI send note"),
                        ["requiredResources"] = new JArray("Integration Account"),
                        ["logicAppsAction"] = new JObject
                        {
                            ["type"] = "EdifactEncode",
                            ["description"] = "Encodes EDIFACT messages"
                        }
                    }
                },
                ["as2Components"] = new JObject
                {
                    ["Microsoft.BizTalk.DefaultPipelines.AS2Receive"] = new JObject
                    {
                        ["displayName"] = "AS2 Receive",
                        ["complexity"] = "Medium",
                        ["migrationNotes"] = new JArray("AS2 note"),
                        ["requiredResources"] = new JArray("Integration Account"),
                        ["logicAppsAction"] = new JObject
                        {
                            ["type"] = "AS2Decode",
                            ["description"] = "Decodes AS2 messages with MDN handling"
                        }
                    }
                },
                ["customComponents"] = new JObject
                {
                    ["pattern"] = new JObject
                    {
                        ["displayName"] = "Custom Pipeline Component",
                        ["complexity"] = "Variable",
                        ["migrationNotes"] = new JArray("Manual assessment required"),
                        ["logicAppsAction"] = new JObject
                        {
                            ["type"] = "Compose",
                            ["description"] = "Custom component - requires analysis"
                        }
                    }
                },
                ["metadata"] = new JObject
                {
                    ["complexityLevels"] = new JObject
                    {
                        ["Low"] = "Direct mapping available, minimal configuration",
                        ["Medium"] = "Native connector exists, requires setup",
                        ["High"] = "Custom development required, no direct equivalent"
                    },
                    ["requiredServices"] = new JObject
                    {
                        ["IntegrationAccount"] = "Required for B2B, schemas, maps, agreements"
                    }
                }
            };

            return obj.ToString(Formatting.Indented);
        }

        /// <summary>
        /// Writes the registry JSON to a temp file, loads a registry from it via reflection
        /// into a fresh instance (bypassing the singleton), and returns it.
        /// </summary>
        private static PipelineConnectorRegistry LoadFromJson(string json)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
            try
            {
                File.WriteAllText(tempPath, json);
                return PipelineConnectorRegistry.LoadFromFile(tempPath);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        // ?? Section loading counts ????????????????????????????????????????????

        [TestMethod]
        public void LoadFromFile_AllSections_LoadsCorrectTotalCount()
        {
            var registry = LoadFromJson(RegistryJson);

            // 1 component + 2 edifact + 1 as2 = 4
            Assert.AreEqual(4, registry.GetAllMappings().Count(),
                "Registry should contain entries from all three sections");
        }

        // ?? components section ????????????????????????????????????????????????

        [TestMethod]
        public void GetMapping_ComponentsSection_ReturnsCorrectActionType()
        {
            var registry = LoadFromJson(RegistryJson);

            var mapping = registry.GetMapping("Microsoft.BizTalk.Component.XmlDasmComp");

            Assert.IsNotNull(mapping, "XmlDasmComp mapping should be found");
            Assert.AreEqual("Foreach", mapping.ActionType);
            Assert.AreEqual("XML Disassembler", mapping.DisplayName);
        }

        // ?? edifactComponents section ?????????????????????????????????????????

        [TestMethod]
        public void GetMapping_EdiReceive_ReturnsEdifactDecodeActionType()
        {
            var registry = LoadFromJson(RegistryJson);

            var mapping = registry.GetMapping("Microsoft.BizTalk.Edi.DefaultPipelines.EdiReceive");

            Assert.IsNotNull(mapping, "EdiReceive mapping should be found — edifactComponents section was not loaded");
            Assert.AreEqual("EdifactDecode", mapping.ActionType);
        }

        [TestMethod]
        public void GetMapping_EdiSend_ReturnsEdifactEncodeActionType()
        {
            var registry = LoadFromJson(RegistryJson);

            var mapping = registry.GetMapping("Microsoft.BizTalk.Edi.DefaultPipelines.EdiSend");

            Assert.IsNotNull(mapping, "EdiSend mapping should be found — edifactComponents section was not loaded");
            Assert.AreEqual("EdifactEncode", mapping.ActionType);
        }

        [TestMethod]
        public void GetMapping_EdiReceive_HasRequiredResources()
        {
            var registry = LoadFromJson(RegistryJson);

            var mapping = registry.GetMapping("Microsoft.BizTalk.Edi.DefaultPipelines.EdiReceive");

            Assert.IsNotNull(mapping);
            CollectionAssert.Contains(mapping.RequiredResources, "Integration Account");
        }

        // ?? as2Components section ?????????????????????????????????????????????

        [TestMethod]
        public void GetMapping_AS2Receive_ReturnsAS2DecodeActionType()
        {
            var registry = LoadFromJson(RegistryJson);

            var mapping = registry.GetMapping("Microsoft.BizTalk.DefaultPipelines.AS2Receive");

            Assert.IsNotNull(mapping, "AS2Receive mapping should be found — as2Components section was not loaded");
            Assert.AreEqual("AS2Decode", mapping.ActionType);
        }

        [TestMethod]
        public void GetMapping_AS2Receive_HasMigrationNotes()
        {
            var registry = LoadFromJson(RegistryJson);

            var mapping = registry.GetMapping("Microsoft.BizTalk.DefaultPipelines.AS2Receive");

            Assert.IsNotNull(mapping);
            Assert.IsTrue(mapping.MigrationNotes.Count > 0, "AS2Receive should have migration notes");
        }

        // ?? unknown component falls back to custom pattern ????????????????????

        [TestMethod]
        public void GetMapping_UnknownComponent_ReturnsCustomFallback()
        {
            var registry = LoadFromJson(RegistryJson);

            var mapping = registry.GetMapping("Some.Unknown.CustomComponent");

            Assert.IsNotNull(mapping, "Unknown component should return custom fallback, not null");
            Assert.AreEqual("Compose", mapping.ActionType,
                "Unknown component fallback should use Compose action type");
            Assert.AreEqual("Variable", mapping.Complexity);
        }

        // ?? GetAllMappings does not include EDI/AS2 duplicates ????????????????

        [TestMethod]
        public void GetAllMappings_ReturnsDistinctEntries_NoSectionDuplicates()
        {
            var registry = LoadFromJson(RegistryJson);

            var all = registry.GetAllMappings().ToList();
            var distinctNames = all.Select(m => m.ComponentName)
                                   .Distinct(System.StringComparer.OrdinalIgnoreCase)
                                   .Count();

            Assert.AreEqual(all.Count, distinctNames,
                "GetAllMappings should not return duplicate entries");
        }

        // ?? metadata helpers ??????????????????????????????????????????????????

        [TestMethod]
        public void GetComplexityDescription_Medium_ReturnsDescription()
        {
            var registry = LoadFromJson(RegistryJson);

            var description = registry.GetComplexityDescription("Medium");

            Assert.IsFalse(string.IsNullOrEmpty(description),
                "Medium complexity should have a description");
            Assert.AreNotEqual("Unknown complexity level", description);
        }

        [TestMethod]
        public void GetServiceDescription_IntegrationAccount_ReturnsDescription()
        {
            var registry = LoadFromJson(RegistryJson);

            var description = registry.GetServiceDescription("IntegrationAccount");

            Assert.IsFalse(string.IsNullOrEmpty(description),
                "IntegrationAccount should have a service description");
            Assert.AreNotEqual("No description available", description);
        }
    }
}
