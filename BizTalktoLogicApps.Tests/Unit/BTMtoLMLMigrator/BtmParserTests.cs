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
