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

        [TestMethod]
        public void MapExpression_TernaryAnd_ProducesNestedAndCalls()
        {
            // Arrange: 3 conditions joined by &&
            var bizTalkExpr = "a == 1 && b == 2 && c == 3";

            // Act
            var result = ExpressionMapper.MapExpression(bizTalkExpr);

            // Assert: Should produce nested and() calls, not a string literal fallback
            Assert.IsTrue(result.Contains("and("),
                "Ternary && should produce nested and() calls. Actual: " + result);
            // Should contain all three equality checks
            Assert.IsTrue(result.Contains("equals("),
                "Should contain equals() for each comparison. Actual: " + result);
            // Must NOT fall back to string literal
            Assert.IsFalse(result.StartsWith("@'"),
                "Should NOT fall back to string literal for 3-way &&. Actual: " + result);
        }

        [TestMethod]
        public void MapExpression_TernaryOr_ProducesNestedOrCalls()
        {
            // Arrange: 3 conditions joined by ||
            var bizTalkExpr = "x == 1 || y == 2 || z == 3";

            // Act
            var result = ExpressionMapper.MapExpression(bizTalkExpr);

            // Assert: Should produce nested or() calls, not a string literal fallback
            Assert.IsTrue(result.Contains("or("),
                "Ternary || should produce nested or() calls. Actual: " + result);
            Assert.IsTrue(result.Contains("equals("),
                "Should contain equals() for each comparison. Actual: " + result);
            Assert.IsFalse(result.StartsWith("@'"),
                "Should NOT fall back to string literal for 3-way ||. Actual: " + result);
        }

        [TestMethod]
        public void MapExpression_QuaternaryAnd_ProducesNestedAndCalls()
        {
            // Arrange: 4 conditions joined by &&
            var bizTalkExpr = "a == 1 && b == 2 && c == 3 && d == 4";

            // Act
            var result = ExpressionMapper.MapExpression(bizTalkExpr);

            // Assert: Should produce nested and() calls
            Assert.IsTrue(result.Contains("and("),
                "4-way && should produce nested and() calls. Actual: " + result);
            Assert.IsFalse(result.StartsWith("@'"),
                "Should NOT fall back to string literal for 4-way &&. Actual: " + result);
        }

        [TestMethod]
        public void MapExpression_GlobalExMessage_ConvertsToStringLiteral()
        {
            // Arrange - globalEx ends with "Ex" so IsExceptionVariable returns true
            var bizTalkExpr = "globalEx.Message";

            // Act
            var result = ExpressionMapper.MapExpression(bizTalkExpr);

            // Assert - Must NOT produce body('globalEx') which creates invalid action reference
            Assert.AreEqual("@'Exception message'", result,
                "globalEx.Message should convert to 'Exception message', not body('globalEx')?['Message']");
            Assert.IsFalse(result.Contains("body("),
                "Exception variable should never produce body() reference. Actual: " + result);
        }

        [TestMethod]
        public void MapExpression_GlobalExReference_ConvertsToStringLiteral()
        {
            // Arrange
            var bizTalkExpr = "globalEx";

            // Act
            var result = ExpressionMapper.MapExpression(bizTalkExpr);

            // Assert
            Assert.AreEqual("@'globalEx'", result,
                "globalEx should convert to string literal");
        }

        [DataTestMethod]
        [DataRow("globalEx.Message", "@'Exception message'")]
        [DataRow("globalEx.ToString()", "@'Exception occurred'")]
        [DataRow("CodeEx.Message", "@'Exception message'")]
        [DataRow("interruptEx.Message", "@'Exception message'")]
        [DataRow("pEx.Message", "@'Exception message'")]
        public void MapExpression_VariousExSuffixExceptionVariables_ConvertCorrectly(string input, string expected)
        {
            // Act
            var result = ExpressionMapper.MapExpression(input);

            // Assert
            Assert.AreEqual(expected, result,
                $"Exception variable '{input}' should convert to '{expected}', not produce body() reference");
        }

        [TestMethod]
        public void MapExpression_ConcatWithExceptionMessage_NeverReferencesBodyOfException()
        {
            // Arrange - Real BizTalk expression from catch block:
            // "Terminated because: " + globalEx.Message
            var bizTalkExpr = "\"Terminated because: \" + globalEx.Message";

            // Act
            var result = ExpressionMapper.MapExpression(bizTalkExpr);

            // Assert - Must use concat() and NOT body('globalEx')
            Assert.IsTrue(result.Contains("concat("),
                "String concatenation should use concat(). Actual: " + result);
            Assert.IsFalse(result.Contains("body('globalEx')"),
                "Must NOT reference body('globalEx') - it's an exception variable, not an action. Actual: " + result);
            Assert.IsTrue(result.Contains("Exception message"),
                "Should contain 'Exception message' placeholder for globalEx.Message. Actual: " + result);
        }

        [TestMethod]
        public void MapExpression_FullyQualifiedConstant_ReturnsLastSegment()
        {
            // Arrange - Fully-qualified .NET constant reference
            var bizTalkExpr = "Microsoft.Solutions.BTARN.ConfigurationManager.ConfigurationConstants.rnifv11VersionID";

            // Act
            var result = ExpressionMapper.MapExpression(bizTalkExpr);

            // Assert - Must NOT produce body('Microsoft') which creates invalid action reference
            Assert.IsFalse(result.Contains("body('Microsoft')"),
                "Must NOT reference body('Microsoft') - it's a namespace, not an action. Actual: " + result);
            Assert.IsTrue(result.Contains("rnifv11VersionID"),
                "Should extract the last segment as the value. Actual: " + result);
        }

        [TestMethod]
        public void MapExpression_SystemBooleanFalseString_ReturnsLastSegment()
        {
            // Arrange
            var bizTalkExpr = "System.Boolean.FalseString";

            // Act
            var result = ExpressionMapper.MapExpression(bizTalkExpr);

            // Assert - Must NOT produce body('System')
            Assert.IsFalse(result.Contains("body('System')"),
                "Must NOT reference body('System'). Actual: " + result);
            Assert.IsTrue(result.Contains("FalseString"),
                "Should extract FalseString as the value. Actual: " + result);
        }

        [DataTestMethod]
        [DataRow("Microsoft.Solutions.BTARN.ConfigurationManager.ConfigurationConstants.rnifv11VersionID", "rnifv11VersionID")]
        [DataRow("System.Boolean.FalseString", "FalseString")]
        [DataRow("System.Boolean.TrueString", "TrueString")]
        [DataRow("Microsoft.Solutions.BTARN.ConfigurationManager.GlobalMessageExceptionCode.TimeoutOnResponse", "TimeoutOnResponse")]
        public void MapExpression_FullyQualifiedReferences_NeverProduceBodyOfNamespace(string input, string expectedValue)
        {
            // Act
            var result = ExpressionMapper.MapExpression(input);

            // Assert
            Assert.IsFalse(result.Contains("body("),
                $"Fully-qualified reference '{input}' must NOT produce body() reference. Actual: {result}");
            Assert.IsTrue(result.Contains(expectedValue),
                $"Should contain '{expectedValue}'. Actual: {result}");
        }

        [TestMethod]
        public void MapExpression_ComparisonWithFullyQualifiedConstant_NeverProducesBodyOfNamespace()
        {
            // Arrange - Real BizTalk expression from ODX
            var bizTalkExpr = "rnConfig.xRNVersion == Microsoft.Solutions.BTARN.ConfigurationManager.ConfigurationConstants.rnifv11VersionID";

            // Act
            var result = ExpressionMapper.MapExpression(bizTalkExpr);

            // Assert
            Assert.IsTrue(result.Contains("equals("),
                "Comparison should produce equals(). Actual: " + result);
            Assert.IsFalse(result.Contains("body('Microsoft')"),
                "Must NOT reference body('Microsoft'). Actual: " + result);
            Assert.IsTrue(result.Contains("rnifv11VersionID"),
                "Should contain the constant value. Actual: " + result);
        }

        [TestMethod]
        public void MapExpression_MessagePartAssignment_ReturnsStringLiteral()
        {
            // Arrange - BizTalk message part assignment from construct shape
            var bizTalkExpr = "StubSAPWSRequest.BAPI_BANKACCT_GET_DETAIL_Request = CreditLimitRequest";

            // Act
            var result = ExpressionMapper.MapExpression(bizTalkExpr);

            // Assert - Must NOT produce body('StubSAPWSRequest') which creates invalid action reference
            Assert.IsFalse(result.Contains("body('StubSAPWSRequest')"),
                "Assignment should NOT produce body() reference for nonexistent action. Actual: " + result);
            Assert.IsTrue(result.StartsWith("@'"),
                "Assignment should be wrapped as string literal. Actual: " + result);
        }

        [TestMethod]
        public void MapExpression_SimpleAssignment_ReturnsStringLiteral()
        {
            // Arrange - Simple variable assignment
            var bizTalkExpr = "myVar = someValue";

            // Act
            var result = ExpressionMapper.MapExpression(bizTalkExpr);

            // Assert
            Assert.IsTrue(result.StartsWith("@'"),
                "Simple assignment should be wrapped as string literal. Actual: " + result);
            Assert.IsFalse(result.Contains("body("),
                "Assignment must NOT produce body() reference. Actual: " + result);
        }

        [DataTestMethod]
        [DataRow("StubSAPWSRequest.BAPI_BANKACCT_GET_DETAIL_Request = CreditLimitRequest")]
        [DataRow("PaymentTrackerWSRequest.LastPaymentRequest = LastPaymentRequest")]
        [DataRow("PendingTransactionsWSRequest.PendingTransactionsRequest = PendingTransactionsRequest")]
        [DataRow("CreditLimitResponse = StubSAPWSResponse.GetAccountDetailsResult")]
        [DataRow("LastPaymentResponse = PaymentTrackerWSResponse.GetLastPaymentsResult")]
        public void MapExpression_VariousAssignments_NeverProduceBodyReference(string input)
        {
            // Act
            var result = ExpressionMapper.MapExpression(input);

            // Assert
            Assert.IsFalse(result.Contains("body("),
                $"Assignment '{input}' must NOT produce body() reference. Actual: {result}");
        }

        [TestMethod]
        public void MapExpression_EqualityComparison_StillWorks()
        {
            // Arrange - Ensure '==' is NOT treated as assignment
            var bizTalkExpr = "sendAutoResponse == \"true\"";

            // Act
            var result = ExpressionMapper.MapExpression(bizTalkExpr);

            // Assert
            Assert.IsTrue(result.Contains("equals("),
                "Equality comparison should still produce equals(). Actual: " + result);
            Assert.IsFalse(result.StartsWith("@'"),
                "Equality comparison should NOT be treated as string literal. Actual: " + result);
        }

        [TestMethod]
        public void MapExpression_NotEqualsComparison_StillWorks()
        {
            // Arrange - Ensure '!=' is NOT treated as assignment
            var bizTalkExpr = "sapReturnCode != \"\"";

            // Act
            var result = ExpressionMapper.MapExpression(bizTalkExpr);

            // Assert
            Assert.IsTrue(result.Contains("not(equals("),
                "Not-equals comparison should still produce not(equals()). Actual: " + result);
        }

        [TestMethod]
        public void MapExpression_GreaterThanOrEquals_StillWorks()
        {
            // Arrange - Ensure '>=' is NOT treated as assignment
            var bizTalkExpr = "count >= 5";

            // Act
            var result = ExpressionMapper.MapExpression(bizTalkExpr);

            // Assert
            Assert.IsTrue(result.Contains("greaterOrEquals("),
                "Greater-or-equals should still produce greaterOrEquals(). Actual: " + result);
        }

        [TestMethod]
        public void MapExpression_LessThanOrEquals_StillWorks()
        {
            // Arrange - Ensure '<=' is NOT treated as assignment
            var bizTalkExpr = "count <= 10";

            // Act
            var result = ExpressionMapper.MapExpression(bizTalkExpr);

            // Assert
            Assert.IsTrue(result.Contains("lessOrEquals("),
                "Less-or-equals should still produce lessOrEquals(). Actual: " + result);
        }

        [TestMethod]
        public void MapExpression_NotEqualsNull_EmitsNullLiteral()
        {
            // Arrange - BizTalk expression: activityID != null
            var bizTalkExpr = "activityID != null";

            // Act
            var result = ExpressionMapper.MapExpression(bizTalkExpr);

            // Assert - Must produce not(equals(variables('activityID'), null))
            // and NOT not(equals(variables('activityID'), variables('null')))
            Assert.IsTrue(result.Contains("not(equals("),
                "!= null should produce not(equals()). Actual: " + result);
            Assert.IsFalse(result.Contains("variables('null')"),
                "null must NOT be treated as a variable reference. Actual: " + result);
            Assert.IsTrue(result.Contains(", null)"),
                "null should be emitted as the WDL literal null. Actual: " + result);
        }

        [TestMethod]
        public void MapExpression_EqualsNull_EmitsNullLiteral()
        {
            // Arrange - BizTalk expression: myVar == null
            var bizTalkExpr = "myVar == null";

            // Act
            var result = ExpressionMapper.MapExpression(bizTalkExpr);

            // Assert
            Assert.IsTrue(result.Contains("equals("),
                "== null should produce equals(). Actual: " + result);
            Assert.IsFalse(result.Contains("variables('null')"),
                "null must NOT be treated as a variable reference. Actual: " + result);
            Assert.IsTrue(result.Contains(", null)"),
                "null should be emitted as the WDL literal null. Actual: " + result);
        }

        [TestMethod]
        public void MapExpression_StandaloneNull_EmitsNullLiteral()
        {
            // Arrange
            var bizTalkExpr = "null";

            // Act
            var result = ExpressionMapper.MapExpression(bizTalkExpr);

            // Assert
            Assert.AreEqual("@null", result,
                "Standalone null should produce @null. Actual: " + result);
            Assert.IsFalse(result.Contains("variables("),
                "null must NOT be wrapped in variables(). Actual: " + result);
        }

        [TestMethod]
        public void MapExpression_ConcatWithTrailingParen_StripsTrailingNonIdentifier()
        {
            // Arrange - Real BizTalk expression from catch block with enclosing method call:
            // PostError( "OrderBroker terminated because: " + FailureReason );
            // After semicolon trim and string concat split, right operand = "FailureReason )"
            var bizTalkExpr = "\"OrderBroker terminated because: \" + FailureReason )";
            var variableNames = new[] { "FailureReason" };

            // Act
            var result = ExpressionMapper.MapExpression(bizTalkExpr, variableNames);

            // Assert - Must produce variables('FailureReason'), NOT variables('FailureReason )')
            Assert.IsTrue(result.Contains("concat("),
                "Should use concat(). Actual: " + result);
            Assert.IsTrue(result.Contains("variables('FailureReason')"),
                "Should reference variables('FailureReason') without trailing paren. Actual: " + result);
            Assert.IsFalse(result.Contains("FailureReason )"),
                "Must NOT include trailing ' )' in variable name. Actual: " + result);
        }

        [TestMethod]
        public void MapExpression_ConcatWithExceptionAndTrailingParen_HandlesCorrectly()
        {
            // Arrange - Real BizTalk expression: "Send failed because: " + SendEx.Message;
            // After split on +, right part = " SendEx.Message" (clean, no trailing paren)
            // But sometimes the enclosing context adds parens:
            // "Send failed because: " + OrderEx.Message )
            var bizTalkExpr = "\"Send failed because: \" + OrderEx.Message";

            // Act
            var result = ExpressionMapper.MapExpression(bizTalkExpr);

            // Assert
            Assert.IsTrue(result.Contains("concat("),
                "Should use concat(). Actual: " + result);
            Assert.IsTrue(result.Contains("'Exception message'"),
                "Should contain 'Exception message' for OrderEx.Message. Actual: " + result);
        }

        [TestMethod]
        public void MapExpression_PromotedPropertyEquals_ConvertsToBodyPropertyAccess()
        {
            // Arrange - BizTalk promoted property access: message(Schema.Property) == value
            var bizTalkExpr = "ship_request_ack(ShippingSchemas.Ship_Acknowledged)== true";

            // Act
            var result = ExpressionMapper.MapExpression(bizTalkExpr);

            // Assert - Must produce equals(body('ship_request_ack')?['Ship_Acknowledged'], true)
            // NOT body('ship_request_ack(ShippingSchemas')?['Ship_Acknowledged)']
            Assert.IsTrue(result.Contains("equals("),
                "Should produce equals(). Actual: " + result);
            Assert.IsTrue(result.Contains("variables('ship_request_ack')"),
                "Should reference variables('ship_request_ack'). Actual: " + result);
            Assert.IsTrue(result.Contains("?['Ship_Acknowledged']"),
                "Should access property Ship_Acknowledged. Actual: " + result);
            Assert.IsFalse(result.Contains("body('ship_request_ack(ShippingSchemas')"),
                "Must NOT include schema name in body() reference. Actual: " + result);
        }

        [TestMethod]
        public void MapExpression_PromotedPropertyEqualsString_ConvertsCorrectly()
        {
            // Arrange - BizTalk promoted property compared to string literal
            var bizTalkExpr = "ship_status(ShippingSchemas.ShipStatus) == \"DONE\"";

            // Act
            var result = ExpressionMapper.MapExpression(bizTalkExpr);

            // Assert
            Assert.IsTrue(result.Contains("equals("),
                "Should produce equals(). Actual: " + result);
            Assert.IsTrue(result.Contains("variables('ship_status')?['ShipStatus']"),
                "Should reference variables('ship_status')?['ShipStatus']. Actual: " + result);
            Assert.IsTrue(result.Contains("'DONE'"),
                "Should contain the string value. Actual: " + result);
        }

        [TestMethod]
        public void MapExpression_PromotedPropertyEqualsFalse_ConvertsCorrectly()
        {
            // Arrange - While loop condition from BizTalk
            var bizTalkExpr = "ship_history(ShippingSchemas.Ship_Completed) == false";

            // Act
            var result = ExpressionMapper.MapExpression(bizTalkExpr);

            // Assert
            Assert.IsTrue(result.Contains("equals("),
                "Should produce equals(). Actual: " + result);
            Assert.IsTrue(result.Contains("variables('ship_history')?['Ship_Completed']"),
                "Should reference variables('ship_history')?['Ship_Completed']. Actual: " + result);
            Assert.IsTrue(result.Contains(", false)"),
                "Should compare to false. Actual: " + result);
        }

        [TestMethod]
        public void MapExpression_ExpExceptionMessage_ConvertsToStringLiteral()
        {
            // Arrange - 'exp' is a common BizTalk exception variable name (catch (System.Exception exp))
            var bizTalkExpr = "exp.Message";

            // Act
            var result = ExpressionMapper.MapExpression(bizTalkExpr);

            // Assert - Must NOT produce body('exp') which creates invalid action reference
            Assert.AreEqual("@'Exception message'", result,
                "exp.Message should convert to 'Exception message', not body('exp')?['Message']. Actual: " + result);
            Assert.IsFalse(result.Contains("body('exp')"),
                "Exception variable 'exp' should never produce body() reference. Actual: " + result);
        }

        [TestMethod]
        public void MapExpression_ExpExceptionReference_ConvertsToStringLiteral()
        {
            // Arrange
            var bizTalkExpr = "exp";

            // Act
            var result = ExpressionMapper.MapExpression(bizTalkExpr);

            // Assert
            Assert.AreEqual("@'exp'", result,
                "exp should convert to string literal. Actual: " + result);
        }

        [TestMethod]
        public void MapExpression_ConcatWithExpMessage_NeverReferencesBodyOfExp()
        {
            // Arrange - Real BizTalk expression from catch block:
            // "Failed due to error " + exp.Message
            var bizTalkExpr = "\"Failed due to error \" + exp.Message";

            // Act
            var result = ExpressionMapper.MapExpression(bizTalkExpr);

            // Assert - Must use concat() and NOT body('exp')
            Assert.IsTrue(result.Contains("concat("),
                "String concatenation should use concat(). Actual: " + result);
            Assert.IsFalse(result.Contains("body('exp')"),
                "Must NOT reference body('exp') - it's an exception variable, not an action. Actual: " + result);
            Assert.IsTrue(result.Contains("Exception message"),
                "Should contain 'Exception message' placeholder for exp.Message. Actual: " + result);
        }

        [DataTestMethod]
        [DataRow("msg(Schema.Prop)== true", "variables('msg')?['Prop']")]
        [DataRow("order(OrderSchema.Status) == \"Active\"", "variables('order')?['Status']")]
        [DataRow("invoice(BillingSchemas.IsPaid) == false", "variables('invoice')?['IsPaid']")]
        public void MapExpression_VariousPromotedProperties_ConvertCorrectly(string input, string expectedRef)
        {
            // Act
            var result = ExpressionMapper.MapExpression(input);

            // Assert
            Assert.IsTrue(result.Contains(expectedRef),
                $"Should contain '{expectedRef}'. Actual: {result}");
            Assert.IsFalse(result.Contains("body('" + input.Split('(')[0] + "("),
                $"Must NOT include parenthesized schema in body() reference. Actual: {result}");
        }
    }
}
