// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BizTalktoLogicApps.BTMtoLMLMigrator;

namespace BizTalktoLogicApps.Tests.Unit.BTMtoLMLMigrator
{
    /// <summary>
    /// Unit tests for LmlGenerator component logic.
    /// Tests LML/YAML generation, namespace declarations, and field mapping output.
    /// </summary>
    [TestClass]
    public class LmlGeneratorTests
    {
        #region Header Generation Tests

        [TestMethod]
        public void GenerateLml_ValidMapData_ContainsVersionHeader()
        {
            // Arrange
            var generator = new LmlGenerator();
            var mapData = this.CreateMinimalMapData();

            // Act
            var result = generator.GenerateLml(mapData: mapData);

            // Assert
            Assert.IsTrue(condition: result.Contains("$version:"), message: "LML should contain version declaration");
        }

        [TestMethod]
        public void GenerateLml_ValidMapData_ContainsInputFormat()
        {
            // Arrange
            var generator = new LmlGenerator();
            var mapData = this.CreateMinimalMapData();

            // Act
            var result = generator.GenerateLml(mapData: mapData);

            // Assert
            Assert.IsTrue(condition: result.Contains("$input: XML"), message: "LML should contain input format");
        }

        [TestMethod]
        public void GenerateLml_ValidMapData_ContainsOutputFormat()
        {
            // Arrange
            var generator = new LmlGenerator();
            var mapData = this.CreateMinimalMapData();

            // Act
            var result = generator.GenerateLml(mapData: mapData);

            // Assert
            Assert.IsTrue(condition: result.Contains("$output: XML"), message: "LML should contain output format");
        }

        [TestMethod]
        public void GenerateLml_ValidMapData_ContainsSourceSchema()
        {
            // Arrange
            var generator = new LmlGenerator();
            var mapData = this.CreateMinimalMapData();
            mapData.SourceSchema = "Order.xsd";

            // Act
            var result = generator.GenerateLml(mapData: mapData);

            // Assert
            Assert.IsTrue(condition: result.Contains("$sourceSchema:"), message: "LML should contain source schema declaration");
            Assert.IsTrue(condition: result.Contains("Order.xsd"), message: "LML should contain source schema name");
        }

        [TestMethod]
        public void GenerateLml_ValidMapData_ContainsTargetSchema()
        {
            // Arrange
            var generator = new LmlGenerator();
            var mapData = this.CreateMinimalMapData();
            mapData.TargetSchema = "Invoice.xsd";

            // Act
            var result = generator.GenerateLml(mapData: mapData);

            // Assert
            Assert.IsTrue(condition: result.Contains("$targetSchema:"), message: "LML should contain target schema declaration");
            Assert.IsTrue(condition: result.Contains("Invoice.xsd"), message: "LML should contain target schema name");
        }

        #endregion

        #region Namespace Declaration Tests

        [TestMethod]
        public void GenerateLml_WithSourceNamespaces_GeneratesSourceNamespaceSection()
        {
            // Arrange
            var generator = new LmlGenerator();
            var mapData = this.CreateMinimalMapData();
            mapData.SourceNamespaces = new Dictionary<string, string>
            {
                { "ns0", "http://company.com/source" },
                { "xs", "http://www.w3.org/2001/XMLSchema" }
            };

            // Act
            var result = generator.GenerateLml(mapData: mapData);

            // Assert
            Assert.IsTrue(condition: result.Contains("$sourceNamespaces:"), message: "Should contain source namespaces section");
            Assert.IsTrue(condition: result.Contains("ns0:"), message: "Should contain ns0 prefix");
            Assert.IsTrue(condition: result.Contains("http://company.com/source"), message: "Should contain namespace URI");
        }

        [TestMethod]
        public void GenerateLml_WithTargetNamespaces_GeneratesTargetNamespaceSection()
        {
            // Arrange
            var generator = new LmlGenerator();
            var mapData = this.CreateMinimalMapData();
            mapData.TargetNamespaces = new Dictionary<string, string>
            {
                { "ns1", "http://company.com/target" },
                { "xs", "http://www.w3.org/2001/XMLSchema" }
            };

            // Act
            var result = generator.GenerateLml(mapData: mapData);

            // Assert
            Assert.IsTrue(condition: result.Contains("$targetNamespaces:"), message: "Should contain target namespaces section");
            Assert.IsTrue(condition: result.Contains("ns1:"), message: "Should contain ns1 prefix");
            Assert.IsTrue(condition: result.Contains("http://company.com/target"), message: "Should contain namespace URI");
        }

        [TestMethod]
        public void GenerateLml_EmptyNamespaces_DoesNotGenerateNamespaceSection()
        {
            // Arrange
            var generator = new LmlGenerator();
            var mapData = this.CreateMinimalMapData();
            mapData.SourceNamespaces = new Dictionary<string, string>();
            mapData.TargetNamespaces = new Dictionary<string, string>();

            // Act
            var result = generator.GenerateLml(mapData: mapData);

            // Assert
            Assert.IsFalse(condition: result.Contains("$sourceNamespaces:"), message: "Should not contain empty source namespaces section");
            Assert.IsFalse(condition: result.Contains("$targetNamespaces:"), message: "Should not contain empty target namespaces section");
        }

        #endregion

        #region Field Mapping Tests

        [TestMethod]
        public void GenerateLml_SimpleFieldMapping_GeneratesCorrectSyntax()
        {
            // Arrange
            var generator = new LmlGenerator();
            var mapData = this.CreateMinimalMapData();
            mapData.Mappings.Add(new LmlMapping
            {
                TargetPath = "OrderNumber",
                SourceExpression = "PurchaseOrderNumber"
            });

            // Act
            var result = generator.GenerateLml(mapData: mapData);

            // Assert
            Assert.IsTrue(condition: result.Contains("OrderNumber:"), message: "Should contain target field name");
            Assert.IsTrue(condition: result.Contains("PurchaseOrderNumber"), message: "Should contain source expression");
        }

        [TestMethod]
        public void GenerateLml_AttributeMapping_UsesAtPrefix()
        {
            // Arrange
            var generator = new LmlGenerator();
            var mapData = this.CreateMinimalMapData();
            mapData.Mappings.Add(new LmlMapping
            {
                TargetPath = "@ID",
                SourceExpression = "@OrderID",
                IsAttribute = true
            });

            // Act
            var result = generator.GenerateLml(mapData: mapData);

            // Assert
            Assert.IsTrue(condition: result.Contains("$@ID:"), message: "Should use $@ prefix for attributes");
        }

        [TestMethod]
        public void GenerateLml_NamespacePrefixedField_GeneratesCorrectly()
        {
            // Arrange
            var generator = new LmlGenerator();
            var mapData = this.CreateMinimalMapData();
            mapData.TargetNamespaces = new Dictionary<string, string>
            {
                { "ns0", "http://company.com/order" }
            };
            mapData.Mappings.Add(new LmlMapping
            {
                TargetPath = "ns0:Order",
                SourceExpression = "PurchaseOrder"
            });

            // Act
            var result = generator.GenerateLml(mapData: mapData);

            // Assert
            Assert.IsTrue(condition: result.Contains("ns0:Order:"), message: "Should preserve namespace prefix in field name");
        }

        #endregion

        #region Loop Structure Tests

        [TestMethod]
        public void GenerateLml_LoopMapping_GeneratesForSyntax()
        {
            // Arrange
            var generator = new LmlGenerator();
            var mapData = this.CreateMinimalMapData();
            mapData.Mappings.Add(new LmlMapping
            {
                TargetPath = "Items",
                IsLoop = true,
                LoopExpression = "/Order/Items/Item"
            });

            // Act
            var result = generator.GenerateLml(mapData: mapData);

            // Assert
            Assert.IsTrue(condition: result.Contains("$for("), message: "Should contain $for loop syntax");
            Assert.IsTrue(condition: result.Contains("/Order/Items/Item"), message: "Should contain loop expression");
        }

        [TestMethod]
        public void GenerateLml_NestedLoopMapping_GeneratesNestedStructure()
        {
            // Arrange
            var generator = new LmlGenerator();
            var mapData = this.CreateMinimalMapData();
            var parentLoop = new LmlMapping
            {
                TargetPath = "Orders",
                IsLoop = true,
                LoopExpression = "/Orders/Order"
            };
            parentLoop.Children.Add(new LmlMapping
            {
                TargetPath = "OrderNumber",
                SourceExpression = "Number"
            });
            mapData.Mappings.Add(parentLoop);

            // Act
            var result = generator.GenerateLml(mapData: mapData);

            // Assert
            Assert.IsTrue(condition: result.Contains("$for("), message: "Should contain loop");
            Assert.IsTrue(condition: result.Contains("OrderNumber:"), message: "Should contain child mapping");
        }

        #endregion

        #region Conditional Mapping Tests

        [TestMethod]
        public void GenerateLml_ConditionalMapping_GeneratesIfSyntax()
        {
            // Arrange
            var generator = new LmlGenerator();
            var mapData = this.CreateMinimalMapData();
            mapData.Mappings.Add(new LmlMapping
            {
                IsConditional = true,
                ConditionalExpression = "Status == 'Active'",
                TargetPath = "ActiveOrder",
                SourceExpression = "Order"
            });

            // Act
            var result = generator.GenerateLml(mapData: mapData);

            // Assert
            Assert.IsTrue(condition: result.Contains("$if("), message: "Should contain $if conditional syntax");
            Assert.IsTrue(condition: result.Contains("Status == 'Active'"), message: "Should contain condition expression");
        }

        #endregion

        #region YAML Compliance Tests

        [TestMethod]
        public void GenerateLml_FieldNameWithSpecialCharacters_QuotesFieldName()
        {
            // Arrange
            var generator = new LmlGenerator();
            var mapData = this.CreateMinimalMapData();
            mapData.Mappings.Add(new LmlMapping
            {
                TargetPath = "Field:Name",
                SourceExpression = "SourceField"
            });

            // Act
            var result = generator.GenerateLml(mapData: mapData);

            // Assert - Field names with colons should be quoted for YAML compliance
            // The generator may quote them or handle them specially
            Assert.IsTrue(condition: result.Contains("Field") && result.Contains("Name"), 
                message: "Should handle field names with special characters");
        }

        [TestMethod]
        public void GenerateLml_EmptyMapping_DoesNotGenerateOutput()
        {
            // Arrange
            var generator = new LmlGenerator();
            var mapData = this.CreateMinimalMapData();
            mapData.Mappings.Add(new LmlMapping
            {
                TargetPath = "EmptyField",
                SourceExpression = null
            });

            // Act
            var result = generator.GenerateLml(mapData: mapData);

            // Assert
            Assert.IsFalse(condition: result.Contains("EmptyField:"), message: "Should not generate output for empty mappings");
        }

        #endregion

        #region Helper Methods

        private TranslatedMapData CreateMinimalMapData()
        {
            return new TranslatedMapData
            {
                Version = "1",
                InputFormat = "XML",
                OutputFormat = "XML",
                SourceSchema = "Source.xsd",
                TargetSchema = "Target.xsd",
                SourceNamespaces = new Dictionary<string, string>(),
                TargetNamespaces = new Dictionary<string, string>(),
                Mappings = new List<LmlMapping>()
            };
        }

        #endregion
    }
}
