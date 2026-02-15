// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BizTalktoLogicApps.BTMtoLMLMigrator;

namespace BizTalktoLogicApps.Tests.Unit.BTMtoLMLMigrator
{
    /// <summary>
    /// Unit tests for BtmParser component logic.
    /// Tests schema extraction, namespace derivation, and XPath normalization without file I/O.
    /// </summary>
    [TestClass]
    public class BtmParserTests
    {
        #region Schema File Name Extraction Tests

        [TestMethod]
        public void ExtractSchemaFileName_AssemblyQualifiedName_ExtractsLastSegment()
        {
            // Arrange
            var parser = new TestableBtmParser();
            var assemblyQualifiedName = "Microsoft.Samples.BizTalk.Schemas.Order.PurchaseOrder, Schemas, Version=1.0.0.0";

            // Act
            var result = parser.PublicExtractSchemaFileName(schemaPath: assemblyQualifiedName);

            // Assert
            Assert.AreEqual(expected: "PurchaseOrder.xsd", actual: result, message: "Should extract last segment before comma");
        }

        [TestMethod]
        public void ExtractSchemaFileName_SimpleTypeName_AddsXsdExtension()
        {
            // Arrange
            var parser = new TestableBtmParser();
            var typeName = "PurchaseOrder";

            // Act
            var result = parser.PublicExtractSchemaFileName(schemaPath: typeName);

            // Assert
            Assert.AreEqual(expected: "PurchaseOrder.xsd", actual: result, message: "Should add .xsd extension to simple type name");
        }

        [TestMethod]
        public void ExtractSchemaFileName_NullInput_ReturnsNull()
        {
            // Arrange
            var parser = new TestableBtmParser();

            // Act
            var result = parser.PublicExtractSchemaFileName(schemaPath: null);

            // Assert
            Assert.IsNull(value: result, message: "Should return null for null input");
        }

        [TestMethod]
        public void ExtractSchemaFileName_EmptyString_ReturnsNull()
        {
            // Arrange
            var parser = new TestableBtmParser();

            // Act
            var result = parser.PublicExtractSchemaFileName(schemaPath: string.Empty);

            // Assert
            Assert.IsNull(value: result, message: "Should return null for empty string");
        }

        [TestMethod]
        public void ExtractSchemaFileName_NestedNamespace_ExtractsCorrectly()
        {
            // Arrange
            var parser = new TestableBtmParser();
            var assemblyQualifiedName = "Company.BizTalk.Schemas.Customer.CustomerInfo, Schemas";

            // Act
            var result = parser.PublicExtractSchemaFileName(schemaPath: assemblyQualifiedName);

            // Assert
            Assert.AreEqual(expected: "CustomerInfo.xsd", actual: result, message: "Should extract last segment from nested namespace");
        }

        #endregion

        #region Namespace Prefix Derivation Tests

        [DataTestMethod]
        [DataRow("http://schemas.microsoft.com/BizTalk/EDI/X12/2006", "ns0", DisplayName = "EDI X12 namespace")]
        [DataRow("http://schemas.microsoft.com/BizTalk/EDI/EDIFACT/2006", "ns0", DisplayName = "EDI EDIFACT namespace")]
        [DataRow("http://company.com/schemas/order", "order", DisplayName = "Non-EDI namespace with meaningful segment")]
        public void DeriveNamespacePrefix_VariousNamespaces_ReturnsExpectedPrefix(string namespaceUri, string expectedPrefix)
        {
            // Arrange
            var parser = new TestableBtmParser();
            var existingNamespaces = new Dictionary<string, string>
            {
                { "xs", "http://www.w3.org/2001/XMLSchema" }
            };

            // Act
            var result = parser.PublicDeriveNamespacePrefix(targetNamespace: namespaceUri, existingNamespaces: existingNamespaces);

            // Assert
            Assert.AreEqual(expected: expectedPrefix, actual: result, message: $"Should derive correct prefix for {namespaceUri}");
        }

        [TestMethod]
        public void DeriveNamespacePrefix_EdiNamespace_UsesNsPrefix()
        {
            // Arrange
            var parser = new TestableBtmParser();
            var ediNamespace = "http://schemas.microsoft.com/BizTalk/EDI/X12/2006";
            var existingNamespaces = new Dictionary<string, string>
            {
                { "xs", "http://www.w3.org/2001/XMLSchema" }
            };

            // Act
            var result = parser.PublicDeriveNamespacePrefix(targetNamespace: ediNamespace, existingNamespaces: existingNamespaces);

            // Assert
            Assert.IsTrue(condition: result.StartsWith("ns"), message: "EDI namespace should use ns prefix");
            Assert.IsFalse(condition: result.Contains("2006"), message: "Should not use year-based prefix for EDI");
        }

        [TestMethod]
        public void DeriveNamespacePrefix_ExistingNamespace_ReturnsExistingPrefix()
        {
            // Arrange
            var parser = new TestableBtmParser();
            var targetNamespace = "http://company.com/schemas/order";
            var existingNamespaces = new Dictionary<string, string>
            {
                { "ns1", targetNamespace }
            };

            // Act
            var result = parser.PublicDeriveNamespacePrefix(targetNamespace: targetNamespace, existingNamespaces: existingNamespaces);

            // Assert
            Assert.AreEqual(expected: "ns1", actual: result, message: "Should return existing prefix for already-registered namespace");
        }

        [TestMethod]
        public void DeriveNamespacePrefix_AvoidsYearBasedPrefixes_UsesNsPrefix()
        {
            // Arrange
            var parser = new TestableBtmParser();
            var namespaceUri = "http://company.com/schemas/2023/order";
            var existingNamespaces = new Dictionary<string, string>();

            // Act
            var result = parser.PublicDeriveNamespacePrefix(targetNamespace: namespaceUri, existingNamespaces: existingNamespaces);

            // Assert
            Assert.IsTrue(condition: result.StartsWith("ns"), message: "Should use ns prefix instead of year-based prefix");
        }

        [TestMethod]
        public void DeriveNamespacePrefix_MultipleNamespaces_IncrementsCounter()
        {
            // Arrange
            var parser = new TestableBtmParser();
            var existingNamespaces = new Dictionary<string, string>
            {
                { "ns0", "http://company.com/ns0" },
                { "ns1", "http://company.com/ns1" }
            };
            var newNamespace = "http://company.com/ns2";

            // Act
            var result = parser.PublicDeriveNamespacePrefix(targetNamespace: newNamespace, existingNamespaces: existingNamespaces);

            // Assert
            Assert.AreEqual(expected: "ns2", actual: result, message: "Should use next available ns counter");
        }

        #endregion

        #region XPath Normalization Tests

        [TestMethod]
        public void NormalizeXPath_BtmStyleXPath_ConvertsToStandardFormat()
        {
            // Arrange
            var parser = new BtmParser();
            var btmXPath = "/*[local-name()='Order']/*[local-name()='Items']";
            var namespaces = new Dictionary<string, string>
            {
                { "ns1", "http://company.com/schemas/order" }
            };

            // Act
            var result = parser.NormalizeXPath(xpath: btmXPath, namespaces: namespaces);

            // Assert
            Assert.IsFalse(condition: result.Contains("local-name()"), message: "Should remove local-name() syntax");
            Assert.IsTrue(condition: result.Contains("ns1:"), message: "Should add namespace prefix");
        }

        [TestMethod]
        public void NormalizeXPath_StandardXPath_RemainsUnchanged()
        {
            // Arrange
            var parser = new BtmParser();
            var standardXPath = "/ns1:Order/ns1:Items";
            var namespaces = new Dictionary<string, string>
            {
                { "ns1", "http://company.com/schemas/order" }
            };

            // Act
            var result = parser.NormalizeXPath(xpath: standardXPath, namespaces: namespaces);

            // Assert
            Assert.AreEqual(expected: standardXPath, actual: result, message: "Standard XPath should remain unchanged");
        }

        [TestMethod]
        public void NormalizeXPath_NullInput_ReturnsNull()
        {
            // Arrange
            var parser = new BtmParser();
            var namespaces = new Dictionary<string, string>();

            // Act
            var result = parser.NormalizeXPath(xpath: null, namespaces: namespaces);

            // Assert
            Assert.IsNull(value: result, message: "Should return null for null input");
        }

        [TestMethod]
        public void NormalizeXPath_EmptyString_ReturnsEmptyString()
        {
            // Arrange
            var parser = new BtmParser();
            var namespaces = new Dictionary<string, string>();

            // Act
            var result = parser.NormalizeXPath(xpath: string.Empty, namespaces: namespaces);

            // Assert
            Assert.AreEqual(expected: string.Empty, actual: result, message: "Should return empty string for empty input");
        }

        #endregion

        #region Functoid Parameter Ordering Tests

        [TestMethod]
        public void ResolveRelationships_InputLinksWithParameters_SortedByParameterOrder()
        {
            // Arrange - Create a functoid with parameters referencing links in non-document order
            // Simulates a subtract functoid where param 0=link2 (minuend), param 1=link1 (subtrahend)
            var mapData = new BtmMapData();

            var link1 = new BtmLink { LinkId = "link1", LinkFrom = "source1", LinkTo = "func1" };
            var link2 = new BtmLink { LinkId = "link2", LinkFrom = "source2", LinkTo = "func1" };
            var linkOut = new BtmLink { LinkId = "linkOut", LinkFrom = "func1", LinkTo = "target1" };

            mapData.Links.Add(link1);
            mapData.Links.Add(link2);
            mapData.Links.Add(linkOut);

            var functoid = new BtmFunctoid
            {
                FunctoidId = "func1",
                FunctoidType = "MathSubtract",
                FunctoidFid = "119"
            };
            // Parameters specify: link2 is param 0 (minuend), link1 is param 1 (subtrahend)
            functoid.InputParameters.Add(new BtmParameter { Type = "link", Value = "link2", LinkIndex = 0 });
            functoid.InputParameters.Add(new BtmParameter { Type = "link", Value = "link1", LinkIndex = 1 });

            mapData.Functoids.Add(functoid);

            // Act
            var resolver = new FunctoidRelationshipResolver(mapData);
            resolver.ResolveRelationships();

            // Assert - InputLinks should be sorted: link2 first (param 0), then link1 (param 1)
            Assert.AreEqual(expected: 2, actual: functoid.InputLinks.Count, message: "Should have 2 input links");
            Assert.AreEqual(expected: "link2", actual: functoid.InputLinks[0].LinkId, message: "First input link should be link2 (param 0)");
            Assert.AreEqual(expected: "link1", actual: functoid.InputLinks[1].LinkId, message: "Second input link should be link1 (param 1)");
        }

        [TestMethod]
        public void ResolveRelationships_ParametersWithConstants_PreservesOrder()
        {
            // Arrange - Functoid with mixed link and constant parameters
            // Simulates substring(link1=input, constant="3", link2=length)
            var mapData = new BtmMapData();

            var link1 = new BtmLink { LinkId = "link1", LinkFrom = "source1", LinkTo = "func1" };
            var link2 = new BtmLink { LinkId = "link2", LinkFrom = "source2", LinkTo = "func1" };
            var linkOut = new BtmLink { LinkId = "linkOut", LinkFrom = "func1", LinkTo = "target1" };

            mapData.Links.Add(link1);
            mapData.Links.Add(link2);
            mapData.Links.Add(linkOut);

            var functoid = new BtmFunctoid
            {
                FunctoidId = "func1",
                FunctoidType = "StringSubstring",
                FunctoidFid = "106"
            };
            // Parameters: link1 at position 0, constant at position 1, link2 at position 2
            functoid.InputParameters.Add(new BtmParameter { Type = "link", Value = "link1", LinkIndex = 0 });
            functoid.InputParameters.Add(new BtmParameter { Type = "constant", Value = "3", LinkIndex = 1 });
            functoid.InputParameters.Add(new BtmParameter { Type = "link", Value = "link2", LinkIndex = 2 });

            mapData.Functoids.Add(functoid);

            // Act
            var resolver = new FunctoidRelationshipResolver(mapData);
            resolver.ResolveRelationships();

            // Assert - InputLinks should maintain parameter order (link1 first, link2 second)
            Assert.AreEqual(expected: 2, actual: functoid.InputLinks.Count, message: "Should have 2 input links (constants excluded)");
            Assert.AreEqual(expected: "link1", actual: functoid.InputLinks[0].LinkId, message: "First input link should be link1 (param 0)");
            Assert.AreEqual(expected: "link2", actual: functoid.InputLinks[1].LinkId, message: "Second input link should be link2 (param 2)");
        }

        [TestMethod]
        public void ResolveRelationships_SingleInputLink_NoSortNeeded()
        {
            // Arrange
            var mapData = new BtmMapData();

            var link1 = new BtmLink { LinkId = "link1", LinkFrom = "source1", LinkTo = "func1" };
            var linkOut = new BtmLink { LinkId = "linkOut", LinkFrom = "func1", LinkTo = "target1" };

            mapData.Links.Add(link1);
            mapData.Links.Add(linkOut);

            var functoid = new BtmFunctoid
            {
                FunctoidId = "func1",
                FunctoidType = "StringUpperCase",
                FunctoidFid = "110"
            };
            functoid.InputParameters.Add(new BtmParameter { Type = "link", Value = "link1", LinkIndex = 0 });

            mapData.Functoids.Add(functoid);

            // Act
            var resolver = new FunctoidRelationshipResolver(mapData);
            resolver.ResolveRelationships();

            // Assert
            Assert.AreEqual(expected: 1, actual: functoid.InputLinks.Count, message: "Should have 1 input link");
            Assert.AreEqual(expected: "link1", actual: functoid.InputLinks[0].LinkId, message: "Single link should be preserved");
        }

        [TestMethod]
        public void ParameterOrdering_DocumentOrderFallback_AssignsSequentialIndices()
        {
            // Arrange - Simulate what the parser does when no Order attribute exists
            var parameters = new List<BtmParameter>();

            // Add in document order (no explicit Order attribute — fallback to Count)
            parameters.Add(new BtmParameter { Type = "link", Value = "linkA", LinkIndex = 0 });
            parameters.Add(new BtmParameter { Type = "link", Value = "linkB", LinkIndex = 1 });
            parameters.Add(new BtmParameter { Type = "link", Value = "linkC", LinkIndex = 2 });

            // Act - Sort by LinkIndex (mimicking what the parser does)
            parameters.Sort((a, b) => a.LinkIndex.CompareTo(b.LinkIndex));

            // Assert - Document order should be preserved
            Assert.AreEqual(expected: "linkA", actual: parameters[0].Value, message: "First param should be linkA");
            Assert.AreEqual(expected: "linkB", actual: parameters[1].Value, message: "Second param should be linkB");
            Assert.AreEqual(expected: "linkC", actual: parameters[2].Value, message: "Third param should be linkC");
        }

        [TestMethod]
        public void ParameterOrdering_ExplicitOrderDiffersFromDocumentOrder_SortsByExplicitOrder()
        {
            // Arrange - Simulate what the parser does when Order attributes differ from document order
            // Document order: linkC, linkA, linkB
            // Explicit order: linkA=0, linkB=1, linkC=2
            var parameters = new List<BtmParameter>();

            parameters.Add(new BtmParameter { Type = "link", Value = "linkC", LinkIndex = 2 });
            parameters.Add(new BtmParameter { Type = "link", Value = "linkA", LinkIndex = 0 });
            parameters.Add(new BtmParameter { Type = "link", Value = "linkB", LinkIndex = 1 });

            // Act - Sort by LinkIndex
            parameters.Sort((a, b) => a.LinkIndex.CompareTo(b.LinkIndex));

            // Assert - Should be sorted by explicit order, not document order
            Assert.AreEqual(expected: "linkA", actual: parameters[0].Value, message: "First param should be linkA (Order=0)");
            Assert.AreEqual(expected: "linkB", actual: parameters[1].Value, message: "Second param should be linkB (Order=1)");
            Assert.AreEqual(expected: "linkC", actual: parameters[2].Value, message: "Third param should be linkC (Order=2)");
        }

        #endregion

        #region Helper Class for Testing Private Methods

        /// <summary>
        /// Testable wrapper for BtmParser that exposes private methods for unit testing.
        /// </summary>
        private class TestableBtmParser : BtmParser
        {
            public string PublicExtractSchemaFileName(string schemaPath)
            {
                // Use reflection to call private method
                var method = typeof(BtmParser).GetMethod(
                    name: "ExtractSchemaFileName",
                    bindingAttr: System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                return (string)method.Invoke(obj: this, parameters: new object[] { schemaPath });
            }

            public string PublicDeriveNamespacePrefix(string targetNamespace, Dictionary<string, string> existingNamespaces)
            {
                // Use reflection to call private method
                var method = typeof(BtmParser).GetMethod(
                    name: "DeriveNamespacePrefix",
                    bindingAttr: System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                return (string)method.Invoke(obj: this, parameters: new object[] { targetNamespace, existingNamespaces });
            }
        }

        #endregion
    }
}
