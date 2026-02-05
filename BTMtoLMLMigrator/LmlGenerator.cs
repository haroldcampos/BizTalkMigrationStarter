// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BizTalktoLogicApps.BTMtoLMLMigrator
{
    /// <summary>
    /// Generates LA Mapping Language (LML) file content from translated BizTalk map data.
    /// </summary>
    /// <remarks>
    /// This class produces YAML-formatted LML output compatible with Azure Data Mapper,
    /// handling namespaces, mappings, loops, conditionals, and attributes.
    /// </remarks>
    public class LmlGenerator
    {
        private StringBuilder _output;
        private int _indentLevel;
        private const int IndentSize = 2;
        private TranslatedMapData _currentMapData;

        /// <summary>
        /// Generates LML content from translated map data.
        /// </summary>
        /// <param name="mapData">The translated map data containing all mapping information.</param>
        /// <returns>The complete LML file content as a formatted YAML string.</returns>
        public string GenerateLml(TranslatedMapData mapData)
        {
            _output = new StringBuilder();
            _indentLevel = 0;
            _currentMapData = mapData;

            // Generate header
            GenerateHeader(mapData);

            // Generate namespace declarations
            GenerateNamespaces(mapData);

            // Generate mappings
            GenerateMappings(mapData);

            return _output.ToString();
        }

        /// <summary>
        /// Generates the LML header section containing version and schema information.
        /// </summary>
        /// <param name="mapData">The map data containing header information.</param>
        private void GenerateHeader(TranslatedMapData mapData)
        {
            AppendLine($"$version: {mapData.Version}");
            AppendLine($"$input: {mapData.InputFormat}");
            AppendLine($"$output: {mapData.OutputFormat}");
            AppendLine($"$sourceSchema: {ResolveSchemaPath(mapData.SourceSchema, mapData.BtmFilePath)}");
            AppendLine($"$targetSchema: {ResolveSchemaPath(mapData.TargetSchema, mapData.BtmFilePath)}");
        }

        /// <summary>
        /// Generates the namespace declaration sections for source and target schemas.
        /// </summary>
        /// <param name="mapData">The map data containing namespace information.</param>
        private void GenerateNamespaces(TranslatedMapData mapData)
        {
            if (mapData.SourceNamespaces.Any())
            {
                AppendLine("$sourceNamespaces:");
                _indentLevel++;
                foreach (var ns in mapData.SourceNamespaces.OrderBy(kvp => kvp.Key))
                {
                    AppendLine($"{ns.Key}: {ns.Value}");
                }
                _indentLevel--;
            }

            if (mapData.TargetNamespaces.Any())
            {
                AppendLine("$targetNamespaces:");
                _indentLevel++;
                foreach (var ns in mapData.TargetNamespaces.OrderBy(kvp => kvp.Key))
                {
                    AppendLine($"{ns.Key}: {ns.Value}");
                }
                _indentLevel--;
            }
        }

        /// <summary>
        /// Generates the field mapping section of the LML file.
        /// </summary>
        /// <param name="mapData">The map data containing all mapping definitions.</param>
        private void GenerateMappings(TranslatedMapData mapData)
        {
            if (mapData.Mappings.Count == 0)
                return;
            foreach (var mapping in mapData.Mappings)
            {
                GenerateMapping(mapping);
            }
        }

        /// <summary>
        /// Generates the LML output for a single mapping element, handling simple mappings, loops, and conditionals.
        /// </summary>
        /// <param name="mapping">The mapping element to generate.</param>
        private void GenerateMapping(LmlMapping mapping)
        {
            if (mapping.TargetPath == "$for" || mapping.TargetPath.StartsWith("$for("))
            {
                // This is a $for loop generated from Looping functoid
                var loopExpr = mapping.TargetPath == "$for" 
                    ? mapping.LoopExpression 
                    : mapping.TargetPath.Substring(5, mapping.TargetPath.Length - 6); // Remove "$for(" and ")"
                
                // CRITICAL FIX: Azure Data Mapper uses IMPLICIT loop context - NO $item variable needed
                AppendLine($"$for({loopExpr}):");
                _indentLevel++;
                
                // Generate child mappings (these will have relative XPaths with $item/ prefix)
                foreach (var child in mapping.Children)
                {
                    GenerateMapping(child);
                }
                
                _indentLevel--;
                return;
            }
            
            if (mapping.IsLoop)
            {
                GenerateLoopMapping(mapping);
            }
            else if (mapping.IsConditional)
            {
                GenerateConditionalMapping(mapping);
            }
            else
            {
                GenerateSimpleMapping(mapping);
            }
        }

        /// <summary>
        /// Generates a simple field mapping with optional children and attributes.
        /// </summary>
        /// <param name="mapping">The simple mapping to generate.</param>
        private void GenerateSimpleMapping(LmlMapping mapping)
        {
            var targetName = GetFieldName(mapping.TargetPath);
            
            // For attributes: remove @ from field name if present
            // The $@ prefix will be added during LML output generation
            var isAttribute = mapping.IsAttribute || targetName.StartsWith("@");
            if (targetName.StartsWith("@"))
            {
                targetName = targetName.Substring(1); // Remove the @ character
            }
            
            // Check if this is an attribute mapping
            if (isAttribute)
            {
                // Add $@ prefix for attribute mappings in LML output
                var attributeName = $"$@{targetName}";
                if (string.IsNullOrEmpty(mapping.SourceExpression))
                    return;
                AppendLine($"{attributeName}: {mapping.SourceExpression}");
                return;
            }
            
            var quotedTargetName = QuoteIfNeeded(targetName);
            var hasChildren = mapping.Children != null && mapping.Children.Any();
            var hasAttributes = mapping.Attributes != null && mapping.Attributes.Any();
            var hasValue = !string.IsNullOrEmpty(mapping.SourceExpression);

            // Check if this is a parent element with children or attributes
            if ((hasChildren || hasAttributes) && !hasValue)
            {
                // Parent element with no value - just the name with colon
                AppendLine($"{quotedTargetName}:");
                _indentLevel++;
                
                // Generate attributes first if any
                GenerateAttributes(mapping);
                
                // Generate child mappings
                foreach (var child in mapping.Children)
                {
                    GenerateMapping(child);
                }
                _indentLevel--;
            }
            else if (hasChildren || hasAttributes)
            {
                // Element with value and/or children and/or attributes
                AppendLine($"{quotedTargetName}:");
                _indentLevel++;
                
                // Generate attributes first
                GenerateAttributes(mapping);
                
                // Generate value if present
                if (hasValue)
                {
                    if (IsMultiLineExpression(mapping.SourceExpression))
                    {
                        AppendLine("$value: >-");
                        _indentLevel++;
                        WriteMultiLineExpression(mapping.SourceExpression);
                        _indentLevel--;
                    }
                    else
                    {
                        AppendLine($"$value: {mapping.SourceExpression}");
                    }
                }
                
                // Generate child mappings
                foreach (var child in mapping.Children)
                {
                    GenerateMapping(child);
                }
                _indentLevel--;
            }
            else
            {
                // Simple field mapping (leaf node)
                if (!hasValue)
                {
                    // Skip empty mappings
                    return;
                }
                
                if (IsMultiLineExpression(mapping.SourceExpression))
                {
                    AppendLine($"{quotedTargetName}: >-");
                    _indentLevel++;
                    WriteMultiLineExpression(mapping.SourceExpression);
                    _indentLevel--;
                }
                else
                {
                    AppendLine($"{quotedTargetName}: {mapping.SourceExpression}");
                }
            }
        }

        /// <summary>
        /// Extracts the field name from a full XPath, applying namespace prefix rules for Azure Data Mapper.
        /// </summary>
        /// <param name="fullPath">The full XPath to process.</param>
        /// <returns>The field name with appropriate namespace prefix.</returns>
        /// <remarks>
        /// Only the root element receives a namespace prefix; nested elements inherit namespace from their parent.
        /// </remarks>
        private string GetFieldName(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
                return "";

            // Handle paths like "ns0:Root", "Header", "/ns0:Root/Header"
            var parts = fullPath.Split('/');
            var lastPart = parts[parts.Length - 1];
            
            // CRITICAL FIX: Namespace prefix logic for Azure Data Mapper
            // RULE: ONLY the root element gets the namespace prefix
            // Nested elements do NOT get prefixes - they inherit namespace from parent or have no namespace
            // This is true regardless of whether the target namespace is at ns0, ns1, or any other prefix
            
            if (_currentMapData != null)
            {
                // Get the target namespace prefix
                var targetNsPrefix = GetTargetNamespacePrefix();
                
                if (!string.IsNullOrEmpty(targetNsPrefix))
                {
                    // Check if this is the root element
                    bool isRootElement = parts.Length == 1 || (parts.Length == 2 && string.IsNullOrEmpty(parts[0]));
                    
                    // CRITICAL FIX: Only add prefix to ROOT element, never to nested elements
                    // Nested elements like "Response" typically have no namespace (namespace-uri()='')
                    // and should not have a prefix in the LML output
                    bool shouldAddPrefix = isRootElement;
                    
                    if (shouldAddPrefix && !lastPart.Contains(":"))
                    {
                        Console.WriteLine($"  Adding namespace prefix to element: {lastPart} -> {targetNsPrefix}:{lastPart} (prefix={targetNsPrefix}, isRoot={isRootElement})");
                        return $"{targetNsPrefix}:{lastPart}";
                    }
                    else if (lastPart.Contains(":"))
                    {
                        Console.WriteLine($"  Element already has namespace prefix: {lastPart}");
                        return lastPart;  // Keep existing prefix
                    }
                    else
                    {
                        Console.WriteLine($"  NOT adding namespace prefix to nested element: {lastPart} (prefix={targetNsPrefix}, isRoot={isRootElement})");
                    }
                }
                else
                {
                    Console.WriteLine($"  NOT adding namespace prefix to element: {lastPart} (no target namespace prefix)");
                }
            }
            
            // Strip any existing namespace prefix from the element name
            if (lastPart.Contains(":"))
            {
                var colonIndex = lastPart.IndexOf(':');
                lastPart = lastPart.Substring(colonIndex + 1);
            }
            
            return lastPart;
        }
        
        /// <summary>
        /// Retrieves the namespace prefix for the target schema's primary namespace.
        /// </summary>
        /// <returns>The namespace prefix (e.g., "ns0", "ns1") or empty string if not found.</returns>
        private string GetTargetNamespacePrefix()
        {
            if (_currentMapData == null)
                return "";
            
            // Find the target schema's unique namespace
            string targetSchemaNamespace = null;
            string targetPrefix = null;
            
            // The target schema's primary namespace is the one that's NOT PropertySchema, XMLSchema, or BizTalk
            // and is unique to the target (not in source)
            foreach (var kvp in _currentMapData.TargetNamespaces.OrderBy(k => k.Key))
            {
                var nsUri = kvp.Value;
                
                // Skip utility namespaces
                if (nsUri.Contains("PropertySchema") ||
                    nsUri.Contains("XMLSchema") ||
                    nsUri.Contains("BizTalk/2003"))
                {
                    continue;
                }
                
                // Check if this namespace is unique to target (not in source)
                if (!_currentMapData.SourceNamespaces.Values.Contains(nsUri))
                {
                    targetSchemaNamespace = nsUri;
                    targetPrefix = kvp.Key;
                    break;
                }
            }
            
            if (string.IsNullOrEmpty(targetSchemaNamespace))
            {
                return "";  // No unique target namespace found
            }
            
            Console.WriteLine($"  Target schema namespace '{targetSchemaNamespace}' is at prefix '{targetPrefix}'");
            return targetPrefix;  // Return ns0, ns1, ns2, etc.
        }

        /// <summary>
        /// Determines if a field name requires quoting for YAML compliance and applies quotes if necessary.
        /// </summary>
        /// <param name="fieldName">The field name to evaluate.</param>
        /// <returns>The field name with quotes applied if required, otherwise the original field name.</returns>
        private string QuoteIfNeeded(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName))
                return "\"\"";

            // LML special keywords that should not be quoted
            if (fieldName.StartsWith("$@") || fieldName == "$value")
                return fieldName;

            // Check if this is a namespace-prefixed name (e.g., "ns1:OrderInquiry")
            // Pattern: one or more word characters, followed by colon, followed by word characters
            // This is valid in YAML and doesn't need quoting
            if (System.Text.RegularExpressions.Regex.IsMatch(fieldName, @"^[a-zA-Z_][\w]*:[a-zA-Z_][\w]*$"))
            {
                // This is a valid namespace-prefixed name, no quoting needed
                return fieldName;
            }

            // Characters that require quoting in YAML keys
            // @ is reserved at start, : conflicts with key-value separator (but handled above for namespace prefixes)
            // *, &, !, |, >, ', ", %, #, ` are also special in YAML
            var needsQuoting = fieldName.StartsWith("@") ||
                               fieldName.StartsWith("*") ||
                               fieldName.StartsWith("&") ||
                               fieldName.StartsWith("!") ||
                               fieldName.StartsWith("|") ||
                               fieldName.StartsWith(">") ||
                               fieldName.StartsWith("%") ||
                               fieldName.StartsWith("#") ||
                               fieldName.StartsWith("`") ||
                               fieldName.Contains(":") ||
                               fieldName.Contains("'") ||
                               fieldName.Contains("\"") ||
                               fieldName.Contains("{") ||
                               fieldName.Contains("}") ||
                               fieldName.Contains("[") ||
                               fieldName.Contains("]");

            if (needsQuoting)
            {
                // Use single quotes and escape any single quotes inside
                var escaped = fieldName.Replace("'", "''");
                return $"'{escaped}'";
            }

            return fieldName;
        }

        /// <summary>
        /// Generates a loop mapping structure using Azure Data Mapper's $for syntax.
        /// </summary>
        /// <param name="mapping">The loop mapping to generate.</param>
        private void GenerateLoopMapping(LmlMapping mapping)
        {
            // CollectionWrapper:
            //   $for(xpath):
            //     ChildElement:
            //       Field1: ns0:Field1/SubField1
            //       Field2: ns0:Field2/SubField2
            
            var targetName = GetFieldName(mapping.TargetPath);
            var quotedTargetName = QuoteIfNeeded(targetName);
            
            if (mapping.Children != null && mapping.Children.Any())
            {
                // CRITICAL FIX: Collection wrapper comes BEFORE $for, not after
                // and $item variable is required in $for declaration
                AppendLine($"{quotedTargetName}:");
                _indentLevel++;
                
                AppendLine($"$for({mapping.LoopExpression}):");
                _indentLevel++;
                
                // Generate child mappings (the actual field mappings)
                foreach (var child in mapping.Children)
                {
                    GenerateMapping(child);
                }
                
                _indentLevel--;
                _indentLevel--;
            }
            else
            {
                // Simple inline $for syntax for basic mass copy
                // This is used when schema parsing fails or for simple cases
                AppendLine($"{quotedTargetName}: $for({mapping.LoopExpression})");
            }
        }

        /// <summary>
        /// Generates a conditional mapping structure using Azure Data Mapper's $if syntax.
        /// </summary>
        /// <param name="mapping">The conditional mapping to generate.</param>
        private void GenerateConditionalMapping(LmlMapping mapping)
        {
            AppendLine($"$if({mapping.ConditionalExpression}):");
            _indentLevel++;

            // If this conditional has its own mapping, generate it
            if (!string.IsNullOrEmpty(mapping.TargetPath) && !string.IsNullOrEmpty(mapping.SourceExpression))
            {
                var targetName = GetFieldName(mapping.TargetPath);
                var quotedTargetName = QuoteIfNeeded(targetName);
                if (IsMultiLineExpression(mapping.SourceExpression))
                {
                    AppendLine($"{quotedTargetName}: >-");
                    _indentLevel++;
                    WriteMultiLineExpression(mapping.SourceExpression);
                    _indentLevel--;
                }
                else
                {
                    AppendLine($"{quotedTargetName}: {mapping.SourceExpression}");
                }
            }

            // Generate child mappings
            foreach (var child in mapping.Children)
            {
                GenerateMapping(child);
            }

            _indentLevel--;
        }

        /// <summary>
        /// Determines if an expression should be formatted as multi-line in the LML output.
        /// </summary>
        /// <param name="expression">The expression to evaluate.</param>
        /// <returns>Always returns false to ensure Azure Data Mapper compatibility.</returns>
        /// <remarks>
        /// Multi-line formatting is disabled for Azure Data Mapper compatibility.
        /// The UI handles wrapping long expressions internally.
        /// </remarks>
        private bool IsMultiLineExpression(string expression)
        {
            if (string.IsNullOrEmpty(expression))
                return false;
            // Always use single-line format for better compatibility
            // The UI will handle wrapping long lines internally
            
            // NEVER use multi-line for:
            // - Function calls (if-then-else, is-equal, concat, etc.)
            // - XPath expressions
            // - Any expression with parentheses (indicates a function)
            
            if (expression.Contains("("))
            {
                // This is a function call - always use single-line
                return false;
            }
            
            // Multi-line ONLY for very specific cases (if any)
            // Currently disabled to ensure maximum Azure Data Mapper compatibility
            return false;
            
            // Original logic (commented out):
            /*
            // Multi-line if:
            // - Contains multiple if-then-else calls (nested conditionals)
            // - Is longer than 80 characters
            // - Contains deeply nested function calls (3+ levels)
            
            var hasNestedIfThenElse = expression.Contains("if-then-else") && 
                                      expression.Split(new[] { "if-then-else" }, StringSplitOptions.None).Length > 2;
            
            var isLong = expression.Length > 80;
            
            var parenDepth = 0;
            var maxDepth = 0;
            foreach (var c in expression)
            {
                if (c == '(')
                {
                    parenDepth++;
                    maxDepth = Math.Max(maxDepth, parenDepth);
                }
                else if (c == ')')
                {
                    parenDepth--;
                }
            }
            var isDeeplyNested = maxDepth > 3;

            return hasNestedIfThenElse || isLong || isDeeplyNested;
            */
        }

        /// <summary>
        /// Writes a multi-line expression with appropriate formatting.
        /// </summary>
        /// <param name="expression">The expression to write.</param>
        /// <remarks>
        /// Currently outputs expressions as single lines. A more sophisticated implementation
        /// would parse and format nested if-then-else expressions.
        /// </remarks>
        private void WriteMultiLineExpression(string expression)
        {
            // Split on commas that are not inside parentheses
            if (expression.Contains("if-then-else"))
            {
                // Format nested if-then-else expressions
                var formatted = FormatNestedIfThenElse(expression);
                foreach (var line in formatted)
                {
                    AppendLine(line);
                }
            }
            else
            {
                // Just output as is
                AppendLine(expression);
            }
        }

        /// <summary>
        /// Formats nested if-then-else expressions for improved readability.
        /// </summary>
        /// <param name="expression">The expression containing nested if-then-else calls.</param>
        /// <returns>A list of formatted expression lines.</returns>
        private List<string> FormatNestedIfThenElse(string expression)
        {
            var lines = new List<string>();
            
            // For now, just return as single line
            // A more sophisticated implementation would parse and format properly
            lines.Add(expression);
            
            return lines;
        }

        /// <summary>
        /// Appends a line of content to the output with appropriate indentation.
        /// </summary>
        /// <param name="content">The content to append.</param>
        private void AppendLine(string content)
        {
            _output.Append(new string(' ', _indentLevel * IndentSize));
            _output.AppendLine(content);
        }

        /// <summary>
        /// Resolves a schema type name to a schema file path reference for the LML output.
        /// </summary>
        /// <param name="schemaTypeName">The schema type name from the BTM file.</param>
        /// <param name="btmFilePath">The path to the BTM file for relative path resolution.</param>
        /// <returns>The resolved schema file path with .xsd extension.</returns>
        private string ResolveSchemaPath(string schemaTypeName, string btmFilePath)
        {
            if (string.IsNullOrEmpty(schemaTypeName))
                return schemaTypeName;
            string schemaFilePath = null;
            
            if (schemaTypeName == _currentMapData?.SourceSchema && !string.IsNullOrEmpty(_currentMapData.SourceSchemaFilePath))
            {
                schemaFilePath = _currentMapData.SourceSchemaFilePath;
            }
            else if (schemaTypeName == _currentMapData?.TargetSchema && !string.IsNullOrEmpty(_currentMapData.TargetSchemaFilePath))
            {
                schemaFilePath = _currentMapData.TargetSchemaFilePath;
            }
            
            if (!string.IsNullOrEmpty(schemaFilePath) && File.Exists(schemaFilePath))
            {
                // Extract fully qualified name from filename and keep the .xsd extension
                // e.g., "Microsoft.Samples.BizTalk.Litware.Schemas.Order.PurchaseOrder.xsd"
                // returns "Microsoft.Samples.BizTalk.Litware.Schemas.Order.PurchaseOrder.xsd"
                var fileName = Path.GetFileName(schemaFilePath);
                return fileName;
            }

            // Fallback: return the schema name as-is, adding .xsd extension if not present
            if (!schemaTypeName.EndsWith(".xsd", StringComparison.OrdinalIgnoreCase))
            {
                return schemaTypeName + ".xsd";
            }
            return schemaTypeName;
        }

        /// <summary>
        /// Generates XML attribute mappings using LML's $@ prefix notation.
        /// </summary>
        /// <param name="mapping">The mapping containing attributes to generate.</param>
        private void GenerateAttributes(LmlMapping mapping)
        {
            if (mapping.Attributes == null || !mapping.Attributes.Any())
                return;

            foreach (var attr in mapping.Attributes.OrderBy(kvp => kvp.Key))
            {
                // Remove @ from attribute name if present, then add $@ prefix for LML
                var attributeName = attr.Key.StartsWith("@") ? attr.Key.Substring(1) : attr.Key;
                var lmlAttributeName = $"$@{attributeName}";
                if (!string.IsNullOrEmpty(attr.Value))
                {
                    // CRITICAL FIX: In YAML, plain scalars (unquoted values) cannot start with @ or other reserved indicators
                    // We must quote the value if it starts with @ to satisfy the YAML parser
                    // Use single quotes for attribute XPaths - Azure Data Mapper accepts: $@Name: '@Name'
                    var quotedValue = attr.Value.StartsWith("@") ? $"'{attr.Value}'" : attr.Value;
                    AppendLine($"{lmlAttributeName}: {quotedValue}");
                }
            }
        }
        
    }
}
