// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using BizTalktoLogicApps.ODXtoWFMigrator;

namespace BizTalktoLogicApps.Tests.Unit
{
    [TestClass]
    public class ExpressionMapperTests
    {
        
        [TestMethod]
        public void MapExpression_SimpleVariable_ConvertsCorrectly()
        {
            // Arrange
            var bizTalkExpr = "myVariable";
            
            // Act
            var result = ExpressionMapper.MapExpression(bizTalkExpr);
            
            // Assert
            Assert.AreEqual("@variables('myVariable')", result, 
                "Simple variable should be wrapped in variables() function");
        }
        
        [TestMethod]
        public void MapExpression_StringConcatenation_ConvertsToConcat()
        {
            // Arrange
            var bizTalkExpr = "\"Hello\" + \" World\"";
            
            // Act
            var result = ExpressionMapper.MapExpression(bizTalkExpr);
            
            // Assert
            Assert.IsTrue(result.Contains("concat("), 
                "String concatenation should use concat() function");
        }
        
        [TestMethod]
        public void MapExpression_XPathExpression_ConvertsCorrectly()
        {
            // Arrange
            var bizTalkExpr = "xpath(msgIn, \"/root/element\")";
            
            // Act
            var result = ExpressionMapper.MapExpression(bizTalkExpr);
            
            // Assert
            Assert.IsTrue(result.Contains("xml(") || result.Contains("xpath("), 
                "XPath expression should be converted to Logic Apps XML function");
        }
        
        [TestMethod]
        public void MapExpression_BooleanComparison_ConvertsCorrectly()
        {
            // Arrange
            var bizTalkExpr = "count > 10";
            
            // Act
            var result = ExpressionMapper.MapExpression(bizTalkExpr);
            
            // Assert
            Assert.IsTrue(result.Contains("greater("), 
                "Comparison operators should convert to Logic Apps functions");
        }
        
        [TestMethod]
        public void MapExpression_NullExpression_ReturnsEmpty()
        {
            // Act
            var result = ExpressionMapper.MapExpression(null);
            
            // Assert
            Assert.AreEqual("@null", result, 
                "Null expression should return @null");
        }

        [TestMethod]
        public void MapExpression_ExceptionVariableToString_ConvertsToStringLiteral()
        {
            // Arrange
            var bizTalkExpr = "eBreakLoop.ToString()";

            // Act
            var result = ExpressionMapper.MapExpression(bizTalkExpr);

            // Assert
            Assert.AreEqual("@'Exception occurred'", result,
                "Exception ToString() should convert to generic message");
        }

        [TestMethod]
        public void MapExpression_ExceptionVariableMessage_ConvertsToStringLiteral()
        {
            // Arrange
            var bizTalkExpr = "eAbortException.Message";

            // Act
            var result = ExpressionMapper.MapExpression(bizTalkExpr);

            // Assert
            Assert.AreEqual("@'Exception message'", result,
                "Exception Message property should convert to generic message");
        }

        [TestMethod]
        public void MapExpression_ExceptionVariableReference_ConvertsToStringLiteral()
        {
            // Arrange
            var bizTalkExpr = "eRollbackException";

            // Act
            var result = ExpressionMapper.MapExpression(bizTalkExpr);

            // Assert
            Assert.AreEqual("@'eRollbackException'", result,
                "Exception variable reference should convert to string literal");
        }

        [DataTestMethod]
        [DataRow("eBreakLoop.ToString()", "@'Exception occurred'")]
        [DataRow("ePersistenceException.Message", "@'Exception message'")]
        [DataRow("eAbortException", "@'eAbortException'")]
        [DataRow("eRollbackException.InnerException", "@'eRollbackException'")]
        public void MapExpression_VariousExceptionVariables_ConvertCorrectly(string input, string expected)
        {
            // Act
            var result = ExpressionMapper.MapExpression(input);

            // Assert
            Assert.AreEqual(expected, result,
                $"Exception variable '{input}' should convert to '{expected}'");
        }

        [TestMethod]
        public void MapExpression_RegularVariableStartingWithE_ConvertsToVariables()
        {
            // Arrange - variables that start with 'e' but lowercase 'e' followed by lowercase
            var bizTalkExpr = "email";

            // Act
            var result = ExpressionMapper.MapExpression(bizTalkExpr);

            // Assert
            Assert.AreEqual("@variables('email')", result,
                "Regular variable 'email' should not be treated as exception variable");
        }
    }
}
