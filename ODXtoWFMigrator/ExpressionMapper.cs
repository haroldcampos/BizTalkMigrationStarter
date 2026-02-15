// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BizTalktoLogicApps.ODXtoWFMigrator
{
    /// <summary>
    /// Maps BizTalk expressions to Logic Apps Workflow Definition Language (WDL).
    /// C# 7.3 / .NET Framework 4.7.2 compatible
    /// Converts C# orchestration expressions to Azure Logic Apps expression syntax.
    /// </summary>
    public static class ExpressionMapper
    {
        private static readonly Regex TypeCastPattern = new Regex(@"^\(\s*[\w\.]+\s*\)", RegexOptions.Compiled);

        // BizTalk promoted property access: message(Schema.Property)
        // Examples: ship_request_ack(ShippingSchemas.Ship_Acknowledged)
        //          ship_history(ShippingSchemas.Ship_Completed)
        //          ship_status(ShippingSchemas.ShipStatus)
        private static readonly Regex PromotedPropertyPattern = new Regex(
            @"^(\w+)\((\w+)\.(\w+)\)$", RegexOptions.Compiled);

        /// <summary>
        /// Maps a BizTalk expression to Logic Apps WDL expression.
        /// Handles comparisons, logical operators, method calls, and property access.
        /// </summary>
        /// <param name="biztalkExpression">C# expression from BizTalk orchestration</param>
        /// <param name="variableNames">List of declared variable names for context-aware mapping</param>
        /// <returns>Logic Apps WDL expression starting with @</returns>
        public static string MapExpression(string biztalkExpression, IEnumerable<string> variableNames = null)
        {
            if (string.IsNullOrWhiteSpace(biztalkExpression))
            {
                return "@null";
            }

            var expression = biztalkExpression.Trim();
            expression = expression.TrimEnd(';').Trim();

            // Check if expression is actually a plain text description
            if (IsPlainTextDescription(expression))
            {
                return "@'" + expression.Replace("'", "''") + "'";
            }

            // Detect multi-line C# code blocks
            if (IsCodeBlock(expression))
            {
                return "@'" + expression.Replace("'", "''") + "'";
            }
            try
            {
                // Convert C# expression to Logic Apps expression with variable context
                var varList = variableNames ?? new List<string>();
                var mapped = ConvertExpression(expression, varList);

                // Ensure expression starts with @ (Logic Apps requirement)
                if (!mapped.StartsWith("@"))
                {
                    mapped = "@" + mapped;
                }

                return mapped;
            }
            catch (Exception ex) when (!ex.IsFatal())
            {
                // Safety: If conversion fails, wrap as string literal
                return "@'" + expression.Replace("'", "''") + "'";
            }
        }

        /// <summary>
        /// Detects if expression is a plain text description rather than code.
        /// </summary>
        private static bool IsPlainTextDescription(string expr)
        {
            bool hasOperators = expr.Contains("==") || expr.Contains("!=") || expr.Contains(">=") ||
                               expr.Contains("<=") || expr.Contains("&&") || expr.Contains("||") ||
                               expr.Contains(">") || expr.Contains("<") || expr.Contains("=") ||
                               expr.Contains("+") || expr.Contains("-") || expr.Contains("*") || expr.Contains("/");

            bool hasMethodCall = expr.Contains("(") && expr.Contains(")") && !expr.StartsWith("xpath(");

            bool hasPropertyAccess = Regex.IsMatch(expr, @"\w+\.\w+");

            // If it has multiple words with spaces but NO operators/methods, it's a description
            if (!hasOperators && !hasMethodCall && !hasPropertyAccess && expr.Contains(" "))
            {
                return true;
            }

            // Known plain text patterns
            if (expr.Equals("Construct Message", StringComparison.OrdinalIgnoreCase) ||
                expr.StartsWith("Receive (", StringComparison.OrdinalIgnoreCase) ||
                expr.StartsWith("Send to", StringComparison.OrdinalIgnoreCase) ||
                expr.Contains("non-activating"))
            {
                return true;
            }

            return false;
        }
        

        /// <summary>
        /// Detects if expression is a multi-line code block that cannot be converted to WDL.
        /// </summary>
        private static bool IsCodeBlock(string expr)
        {
            // Multi-line check
            if (expr.Contains("\n") || expr.Contains("\r"))
            {
                return true;
            }

            // Contains if statement
            if (Regex.IsMatch(expr, @"\bif\s*\(", RegexOptions.IgnoreCase))
            {
                return true;
            }

            // Contains loops
            if (Regex.IsMatch(expr, @"\b(for|while|foreach)\s*\(", RegexOptions.IgnoreCase))
            {
                return true;
            }

            // Contains try/catch
            if (Regex.IsMatch(expr, @"\b(try|catch|finally)\s*\{", RegexOptions.IgnoreCase))
            {
                return true;
            }

            // Contains switch statement
            if (Regex.IsMatch(expr, @"\bswitch\s*\(", RegexOptions.IgnoreCase))
            {
                return true;
            }

            // Contains curly braces (code blocks)
            if (expr.Contains("{") && expr.Contains("}"))
            {
                return true;
            }

            // Multiple semicolons (multiple statements)
            if (expr.Count(c => c == ';') > 1)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Converts C# BizTalk expression to Logic Apps expression syntax.
        /// </summary>
        private static string ConvertExpression(string expr, IEnumerable<string> variableNames)
        {
            // Handle logical operators first (they contain other operators)
            if (expr.Contains("&&"))
            {
                return ConvertLogicalAnd(expr, variableNames);
            }

            if (expr.Contains("||"))
            {
                return ConvertLogicalOr(expr, variableNames);
            }

            // Handle comparison operators
            if (expr.Contains("=="))
            {
                return ConvertEquals(expr, variableNames);
            }

            if (expr.Contains("!="))
            {
                return ConvertNotEquals(expr, variableNames);
            }

            if (expr.Contains(">="))
            {
                return ConvertGreaterThanOrEquals(expr, variableNames);
            }

            if (expr.Contains("<="))
            {
                return ConvertLessThanOrEquals(expr, variableNames);
            }

            if (expr.Contains(">"))
            {
                return ConvertGreaterThan(expr, variableNames);
            }

            if (expr.Contains("<"))
            {
                return ConvertLessThan(expr, variableNames);
            }

            // Handle assignment statements (single '=' that is not '==', '!=', '>=', '<=')
            // MUST be checked BEFORE string concatenation, because assignment expressions like
            //   FailureReason = "Message Construction Failed because: " + eOrderBroker.Message
            // contain both '=' and '+' with string literals. If ConvertStringConcatenation runs first,
            // the '+' split produces 'FailureReason = "..."' as an operand, which gets wrapped as
            // variables('FailureReason = "..."') — an invalid variable name.
            if (IsAssignmentExpression(expr))
            {
                return "'" + expr.Replace("'", "''") + "'";
            }

            // Handle string concatenation with + operator
            if (expr.Contains("+") && ContainsStringLiteral(expr))
            {
                return ConvertStringConcatenation(expr, variableNames);
            }

            // Handle method calls
            if (expr.Contains(".ToUpper()"))
            {
                return ConvertToUpper(expr, variableNames);
            }

            if (expr.Contains(".ToLower()"))
            {
                return ConvertToLower(expr, variableNames);
            }

            if (expr.Contains(".Add("))
            {
                return ConvertListAdd(expr);
            }

            if (expr.StartsWith("xpath("))
            {
                return ConvertXPath(expr);
            }

            // Handle BizTalk promoted property access: message(Schema.Property)
            // BizTalk context/promoted properties have no direct Logic Apps equivalent.
            // Use variables() to reference the message content, since messages used in
            // conditions are tracked as variables. Using body() would reference a
            // nonexistent action and cause template validation errors.
            var promotedExprMatch = PromotedPropertyPattern.Match(expr);
            if (promotedExprMatch.Success)
            {
                var messageName = promotedExprMatch.Groups[1].Value;
                var propertyName = promotedExprMatch.Groups[3].Value;
                return $"variables('{messageName}')?['{propertyName}']";
            }

            // Handle property access (Message.Field or Variable.Property)
            if (Regex.IsMatch(expr, @"^\w+\.\w+"))
            {
                return ConvertPropertyAccess(expr, variableNames);
            }

            // Handle simple variable reference
            if (Regex.IsMatch(expr, @"^\w+$"))
            {
                return ConvertVariableReference(expr, variableNames);
            }

            // Handle string literals
            if (expr.StartsWith("\"") && expr.EndsWith("\""))
            {
                return "'" + expr.Trim('"').Replace("'", "''") + "'";
            }

            // Handle numeric literals
            int numericValue;
            if (int.TryParse(expr, out numericValue))
            {
                return expr;
            }

            // Handle boolean literals
            if (expr.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return "true";
            }

            if (expr.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                return "false";
            }

            // Handle null literal
            if (expr.Equals("null", StringComparison.Ordinal))
            {
                return "null";
            }

            // Fallback: return as string literal
            return "'" + expr.Replace("'", "''") + "'";
        }

        #region Logical Operators

        private static string ConvertLogicalAnd(string expr, IEnumerable<string> variableNames)
        {
            var parts = expr.Split(new[] { "&&" }, StringSplitOptions.None);
            if (parts.Length >= 2)
            {
                // Build nested and() calls for N operands: and(a, and(b, c))
                var right = ConvertExpression(parts[parts.Length - 1].Trim(), variableNames);
                for (int i = parts.Length - 2; i >= 0; i--)
                {
                    var left = ConvertExpression(parts[i].Trim(), variableNames);
                    right = $"and({left}, {right})";
                }
                return right;
            }
            return "'" + expr.Replace("'", "''") + "'";
        }

        private static string ConvertLogicalOr(string expr, IEnumerable<string> variableNames)
        {
            var parts = expr.Split(new[] { "||" }, StringSplitOptions.None);
            if (parts.Length >= 2)
            {
                // Build nested or() calls for N operands: or(a, or(b, c))
                var right = ConvertExpression(parts[parts.Length - 1].Trim(), variableNames);
                for (int i = parts.Length - 2; i >= 0; i--)
                {
                    var left = ConvertExpression(parts[i].Trim(), variableNames);
                    right = $"or({left}, {right})";
                }
                return right;
            }
            return "'" + expr.Replace("'", "''") + "'";
        }

        #endregion

        #region Comparison Operators

        private static string ConvertEquals(string expr, IEnumerable<string> variableNames)
        {
            var parts = expr.Split(new[] { "==" }, StringSplitOptions.None);
            if (parts.Length == 2)
            {
                var left = ConvertOperand(parts[0].Trim(), variableNames);
                var right = ConvertOperand(parts[1].Trim(), variableNames);
                return $"equals({left}, {right})";
            }
            return "'" + expr.Replace("'", "''") + "'";
        }

        private static string ConvertNotEquals(string expr, IEnumerable<string> variableNames)
        {
            var parts = expr.Split(new[] { "!=" }, StringSplitOptions.None);
            if (parts.Length == 2)
            {
                var left = ConvertOperand(parts[0].Trim(), variableNames);
                var right = ConvertOperand(parts[1].Trim(), variableNames);
                return $"not(equals({left}, {right}))";
            }
            return "'" + expr.Replace("'", "''") + "'";
        }

        private static string ConvertGreaterThan(string expr, IEnumerable<string> variableNames)
        {
            var parts = expr.Split('>');
            if (parts.Length == 2)
            {
                var left = ConvertOperand(parts[0].Trim(), variableNames);
                var right = ConvertOperand(parts[1].Trim(), variableNames);
                return $"greater({left}, {right})";
            }
            return "'" + expr.Replace("'", "''") + "'";
        }

        private static string ConvertGreaterThanOrEquals(string expr, IEnumerable<string> variableNames)
        {
            var parts = expr.Split(new[] { ">=" }, StringSplitOptions.None);
            if (parts.Length == 2)
            {
                var left = ConvertOperand(parts[0].Trim(), variableNames);
                var right = ConvertOperand(parts[1].Trim(), variableNames);
                return $"greaterOrEquals({left}, {right})";
            }
            return "'" + expr.Replace("'", "''") + "'";
        }

        private static string ConvertLessThan(string expr, IEnumerable<string> variableNames)
        {
            var parts = expr.Split('<');
            if (parts.Length == 2)
            {
                var left = ConvertOperand(parts[0].Trim(), variableNames);
                var right = ConvertOperand(parts[1].Trim(), variableNames);
                return $"less({left}, {right})";
            }
            return "'" + expr.Replace("'", "''") + "'";
        }

        private static string ConvertLessThanOrEquals(string expr, IEnumerable<string> variableNames)
        {
            var parts = expr.Split(new[] { "<=" }, StringSplitOptions.None);
            if (parts.Length == 2)
            {
                var left = ConvertOperand(parts[0].Trim(), variableNames);
                var right = ConvertOperand(parts[1].Trim(), variableNames);
                return $"lessOrEquals({left}, {right})";
            }
            return "'" + expr.Replace("'", "''") + "'";
        }

        #endregion

        #region Method Calls

        private static string ConvertStringConcatenation(string expr, IEnumerable<string> variableNames)
        {
            // Split by + operator, but be careful with strings containing +
            var parts = new List<string>();
            var currentPart = "";
            var inString = false;
            
            for (int i = 0; i < expr.Length; i++)
            {
                char c = expr[i];
                
                if (c == '"' && (i == 0 || expr[i - 1] != '\\'))
                {
                    inString = !inString;
                    currentPart += c;
                }
                else if (c == '+' && !inString)
                {
                    parts.Add(currentPart.Trim());
                    currentPart = "";
                }
                else
                {
                    currentPart += c;
                }
            }
            
            if (!string.IsNullOrWhiteSpace(currentPart))
            {
                parts.Add(currentPart.Trim());
            }
            
            if (parts.Count >= 2)
            {
                var convertedParts = parts.Select(p => ConvertOperand(p, variableNames)).ToList();
                return $"concat({string.Join(", ", convertedParts)})";
            }
            
            return "'" + expr.Replace("'", "''") + "'";
        }

        private static bool ContainsStringLiteral(string expr)
        {
            return expr.Contains("\"");
        }

        private static string ConvertToUpper(string expr, IEnumerable<string> variableNames)
        {
            var varName = expr.Replace(".ToUpper()", "").Trim();
            var variable = ConvertOperand(varName, variableNames);
            return $"toUpper({variable})";
        }

        private static string ConvertToLower(string expr, IEnumerable<string> variableNames)
        {
            var varName = expr.Replace(".ToLower()", "").Trim();
            var variable = ConvertOperand(varName, variableNames);
            return $"toLower({variable})";
        }

        private static string ConvertListAdd(string expr)
        {
            // MessagesToAggregate.Add(ActivationMessage) -> union(variables('MessagesToAggregate'), createArray(variables('ActivationMessage')))
            // Both the list and the item are BizTalk variables/messages, not Logic Apps actions.
            // Using body() would reference a nonexistent action and cause template validation errors.
            var match = Regex.Match(expr, @"(\w+)\.Add\((\w+)\)");
            if (match.Success)
            {
                var listVar = match.Groups[1].Value;
                var itemVar = match.Groups[2].Value;
                return $"union(variables('{listVar}'), createArray(variables('{itemVar}')))";
            }
            return "'" + expr.Replace("'", "''") + "'";
        }

        private static string ConvertXPath(string expr)
        {
            // xpath(Message, "/Order/CustomerID") -> xpath(xml(body('Message')), '/Order/CustomerID')
            var match = Regex.Match(expr, @"xpath\((\w+),\s*""([^""]+)""\)");
            if (match.Success)
            {
                var message = match.Groups[1].Value;
                var path = match.Groups[2].Value;
                return $"xpath(xml(body('{message}')), '{path}')";
            }
            return "'" + expr.Replace("'", "''") + "'";
        }

        #endregion

        #region Data Access

        /// <summary>
        /// Converts property access expressions with context awareness.
        /// Distinguishes between variables, messages, enum constants, and exception objects.
        /// </summary>
        private static string ConvertPropertyAccess(string expr, IEnumerable<string> variableNames)
        {
            var parts = expr.Split('.');
            if (parts.Length < 2)
            {
                return ConvertVariableReference(expr, variableNames);
            }

            var firstPart = parts[0];
            var varList = variableNames.ToList();

            // Detect BizTalk exception variable (starts with 'e' followed by uppercase letter)
            // Examples: eBreakLoop, eAbortException, eRollbackException, ePersistenceException
            // These are exception objects in catch blocks and cannot be referenced in Logic Apps
            if (IsExceptionVariable(firstPart))
            {
                // Exception properties like ToString(), Message, etc. cannot be accessed in Logic Apps
                // Return a comment indicating manual implementation is needed
                if (parts.Length >= 2)
                {
                    // Clean trailing non-identifier characters (e.g., "Message )" from string concat splitting)
                    var rawPropertyName = parts[parts.Length - 1];
                    var propertyName = new string(rawPropertyName.TakeWhile(c => char.IsLetterOrDigit(c) || c == '_' || c == '(' || c == ')').ToArray());
                    if (string.Equals(propertyName, "ToString()", StringComparison.OrdinalIgnoreCase))
                    {
                        return "'Exception occurred'";
                    }
                    if (string.Equals(propertyName, "Message", StringComparison.OrdinalIgnoreCase))
                    {
                        return "'Exception message'";
                    }
                }
                return "'" + firstPart + "'";
            }

            // Detect fully-qualified .NET type reference or enum constant
            // Examples:
            //   Microsoft.Solutions.BTARN.ConfigurationManager.ConfigurationConstants.rnifv11VersionID
            //   System.Boolean.FalseString
            //   Sat.Scade.Pagos.Modelo.Comun.Procesos.RecepcionArchivosPagos
            // These must NOT be converted to body('Microsoft') or body('System') which creates
            // invalid action references in Logic Apps.
            if (parts.Length >= 2 && IsFullyQualifiedReference(parts))
            {
                // Use just the last segment as string literal value
                var lastSegment = parts[parts.Length - 1];
                return "'" + lastSegment + "'";
            }

            // Detect variable reference
            // If first part is in the variable names list, use variables()
            if (varList.Contains(firstPart))
            {
                var result = "variables('" + firstPart + "')";
                for (int i = 1; i < parts.Length; i++)
                {
                    result += "?['" + parts[i] + "']";
                }
                return result;
            }

            // Default: Message/Action body reference
            // Message.Order.CustomerID -> body('Message')?['Order']?['CustomerID']
            var messageResult = "body('" + firstPart + "')";
            for (int i = 1; i < parts.Length; i++)
            {
                messageResult += "?['" + parts[i] + "']";
            }
            return messageResult;
        }

        /// <summary>
        /// Converts simple variable references.
        /// </summary>
        private static string ConvertVariableReference(string expr, IEnumerable<string> variableNames)
        {
            var varList = variableNames.ToList();

            // Check if this is a BizTalk exception variable
            if (IsExceptionVariable(expr))
            {
                // Exception variables cannot be referenced in Logic Apps
                return "'" + expr + "'";
            }

            // Check if this is a known variable
            if (varList.Contains(expr))
            {
                return "variables('" + expr + "')";
            }

            // Default: Assume variable (safer than assuming message for simple identifiers)
            return "variables('" + expr + "')";
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Converts an operand (left or right side of comparison/logical operation).
        /// </summary>
        private static string ConvertOperand(string operand, IEnumerable<string> variableNames)
        {
            operand = operand.Trim();

            // Strip type casts like (System.Int16), (int), (string), etc.
            operand = StripTypeCast(operand);

            // Strip trailing non-identifier characters left over from expression splitting.
            // When ConvertStringConcatenation splits "text" + FailureReason ), the right
            // operand becomes "FailureReason )" — the trailing " )" must be removed before
            // it is passed to ConvertVariableReference (which would produce variables('FailureReason )')).
            operand = StripTrailingNonIdentifier(operand);

            // String literal
            if (operand.StartsWith("\"") && operand.EndsWith("\""))
            {
                return "'" + operand.Trim('"').Replace("'", "''") + "'";
            }

            // Numeric literal
            int numericValue;
            if (int.TryParse(operand, out numericValue))
            {
                return operand;
            }

            // Boolean literal
            if (operand.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return "true";
            }

            if (operand.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                return "false";
            }

            // Null literal
            if (operand.Equals("null", StringComparison.Ordinal))
            {
                return "null";
            }

            // BizTalk promoted property access: message(Schema.Property)
            // Examples: ship_request_ack(ShippingSchemas.Ship_Acknowledged)
            //           ship_status(ShippingSchemas.ShipStatus)
            // Must be checked BEFORE the generic dot-based property access, because the
            // dot inside parentheses would cause ConvertPropertyAccess to split incorrectly
            // producing body('ship_request_ack(ShippingSchemas')?['Ship_Acknowledged)'].
            // Uses variables() instead of body() because the message name refers to a
            // BizTalk message, not a Logic Apps action. Messages used in conditions are
            // tracked as variables in the generated workflow.
            var promotedMatch = PromotedPropertyPattern.Match(operand);
            if (promotedMatch.Success)
            {
                var messageName = promotedMatch.Groups[1].Value;
                var propertyName = promotedMatch.Groups[3].Value;
                return $"variables('{messageName}')?['{propertyName}']";
            }

            // Property access (Message.Field or Variable.Property)
            if (operand.Contains("."))
            {
                return ConvertPropertyAccess(operand, variableNames);
            }

            // Variable reference
            return ConvertVariableReference(operand, variableNames);
        }

        /// <summary>
        /// Removes type casts from expressions.
        /// Example: (System.Int16)Value -> Value
        /// Example: (int)Count -> Count
        /// </summary>
        private static string StripTypeCast(string expr)
        {
            if (string.IsNullOrWhiteSpace(expr))
                return expr;

            expr = expr.Trim();

            // Pattern: (TypeName)Value or (Namespace.TypeName)Value
            // Matches: (System.Int16)..., (int)..., (string)..., etc.
            var match = TypeCastPattern.Match(expr);

            if (match.Success)
            {
                var stripped = expr.Substring(match.Length).Trim();
                return stripped;
            }

            return expr;
        }

        /// <summary>
        /// Strips trailing non-identifier characters from an operand.
        /// When expressions are split on operators like '+', residual syntax characters
        /// such as closing parentheses, semicolons, or commas may remain attached.
        /// Example: "FailureReason )" -> "FailureReason"
        /// Example: "count ;" -> "count"
        /// Only strips when the core content looks like an identifier or property access;
        /// string literals and numeric literals are left untouched.
        /// </summary>
        private static string StripTrailingNonIdentifier(string operand)
        {
            if (string.IsNullOrWhiteSpace(operand))
                return operand;

            // Don't strip string literals
            if (operand.StartsWith("\""))
                return operand;

            // Trim trailing characters that are not part of an identifier or property access.
            // Valid trailing chars: letters, digits, underscore, dot (property access), quote.
            // Closing paren ')' is only valid when matched with an opening '(' (method calls like ToString()).
            // A lone trailing ')' is residual syntax from expression splitting and must be stripped.
            int end = operand.Length - 1;
            while (end >= 0)
            {
                char c = operand[end];
                if (char.IsLetterOrDigit(c) || c == '_' || c == '"')
                    break;
                // Keep ')' only if the operand contains a matching '(' (method call)
                if (c == ')' && operand.IndexOf('(') >= 0)
                    break;
                end--;
            }

            if (end < 0)
                return operand; // All characters stripped - return original to avoid empty string

            var trimmed = operand.Substring(0, end + 1).TrimEnd();

            // Also strip leading non-identifier characters (e.g., "( FailureReason" -> "FailureReason")
            // Don't strip if it starts with a quote (string literal) or digit (numeric)
            if (trimmed.Length > 0 && !char.IsLetterOrDigit(trimmed[0]) && trimmed[0] != '_' && trimmed[0] != '"' && trimmed[0] != '(')
            {
                int start = 0;
                while (start < trimmed.Length && !char.IsLetterOrDigit(trimmed[start]) && trimmed[start] != '_' && trimmed[start] != '"')
                {
                    start++;
                }
                if (start < trimmed.Length)
                {
                    trimmed = trimmed.Substring(start).TrimStart();
                }
            }

            return trimmed.Length > 0 ? trimmed : operand;
        }

        /// <summary>
        /// Determines if a single-line expression is a C# assignment statement.
        /// Detects bare '=' that is not part of '==', '!=', '>=', or '<='.
        /// Examples:
        ///   StubSAPWSRequest.BAPI_BANKACCT_GET_DETAIL_Request = CreditLimitRequest  -> true
        ///   a == b                                                                  -> false
        ///   count > 10                                                              -> false
        /// </summary>
        private static bool IsAssignmentExpression(string expr)
        {
            // Remove compound operators so they don't produce false-positive bare '='
            var stripped = expr
                .Replace("==", "")
                .Replace("!=", "")
                .Replace(">=", "")
                .Replace("<=", "");

            // If a bare '=' remains, this is an assignment
            return stripped.Contains("=");
        }

        /// <summary>
        /// Determines if a dot-separated expression is a fully-qualified .NET type/constant reference
        /// rather than a message property access.
        /// Detects patterns like:
        ///   - Microsoft.Solutions.BTARN.* (starts with known namespace root)
        ///   - System.Boolean.FalseString (starts with known namespace root)
        ///   - Sat.Scade.Pagos.* (3+ segments, most PascalCase = namespace path)
        /// </summary>
        private static bool IsFullyQualifiedReference(string[] parts)
        {
            if (parts.Length < 2)
                return false;

            var first = parts[0];

            // Known .NET / BizTalk namespace roots that are never message or action names
            if (first.Equals("Microsoft", StringComparison.Ordinal) ||
                first.Equals("System", StringComparison.Ordinal) ||
                first.Equals("IBM", StringComparison.Ordinal) ||
                first.Equals("Oracle", StringComparison.Ordinal) ||
                first.Equals("Newtonsoft", StringComparison.Ordinal))
            {
                return true;
            }

            // For 3+ segments, check if all but the last start with uppercase (namespace/type path)
            // The last segment can be any case (field, property, or enum value)
            if (parts.Length >= 3)
            {
                bool allLeadingUpperCase = true;
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    if (parts[i].Length == 0 || !char.IsUpper(parts[i][0]))
                    {
                        allLeadingUpperCase = false;
                        break;
                    }
                }
                if (allLeadingUpperCase)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Determines if an identifier is a BizTalk exception variable.
        /// BizTalk conventions include:
        ///   - Names starting with 'e' followed by an uppercase letter (eBreakLoop, eAbortException)
        ///   - Names ending with 'Ex' or 'Exception' (globalEx, CodeEx, pEx, interruptEx)
        ///   - The common short names 'ex' and 'exp'
        /// These are exception objects in catch blocks and cannot be referenced as actions in Logic Apps.
        /// </summary>
        private static bool IsExceptionVariable(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier) || identifier.Length < 2)
            {
                return false;
            }

            // Check if starts with 'e' followed by uppercase letter (eBreakLoop, eAbortException)
            if (identifier[0] == 'e' && char.IsUpper(identifier[1]))
            {
                return true;
            }

            // Check if ends with 'Ex' (globalEx, CodeEx, pEx, interruptEx, interruptFacilitiesEx)
            if (identifier.EndsWith("Ex", StringComparison.Ordinal) && identifier.Length > 2)
            {
                return true;
            }

            // Check if ends with 'Exception' (globalException, myException)
            if (identifier.EndsWith("Exception", StringComparison.Ordinal) && identifier.Length > 9)
            {
                return true;
            }

            // Check common short names 'ex' and 'exp' (case-insensitive)
            if (string.Equals(identifier, "ex", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(identifier, "exp", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        #endregion
    }
}