// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;

namespace BizTalktoLogicApps.BTMtoLMLMigrator
{
    /// <summary>
    /// Parses BizTalk Server map (BTM) XML files and extracts the complete map structure including schemas, functoids, and links.
    /// </summary>
    /// <remarks>
    /// This parser handles both embedded schema references and external XSD file loading,
    /// resolving namespace declarations and building complete schema tree structures.
    /// </remarks>
    public class BtmParser
    {
        private string _btmDirectory;
        private string _providedSourceSchemaPath;
        private string _providedTargetSchemaPath;

        /// <summary>
        /// Parses a BTM file and its associated schemas to build a complete map data structure.
        /// </summary>
        /// <param name="btmFilePath">Path to the BTM file to parse.</param>
        /// <param name="sourceSchemaPath">Path to the source XSD schema file.</param>
        /// <param name="targetSchemaPath">Path to the target XSD schema file.</param>
        /// <returns>A populated <see cref="BtmMapData"/> object containing all parsed map information.</returns>
        /// <exception cref="System.IO.FileNotFoundException">Thrown when the BTM or schema files cannot be found.</exception>
        /// <exception cref="System.Xml.XmlException">Thrown when the BTM or schema files contain invalid XML.</exception>
        public BtmMapData Parse(string btmFilePath, string sourceSchemaPath, string targetSchemaPath)
        {
            _btmDirectory = Path.GetDirectoryName(btmFilePath);
            _providedSourceSchemaPath = sourceSchemaPath;
            _providedTargetSchemaPath = targetSchemaPath;
            
            var mapData = new BtmMapData();
            var doc = new XmlDocument();
            doc.Load(btmFilePath);

            // Parse source and target schemas
            ParseSchemas(doc, mapData, btmFilePath);

            // Parse functoids
            ParseFunctoids(doc, mapData);

            // Parse links
            ParseLinks(doc, mapData);

            // Parse schema trees (now with external XSD loading)
            ParseSchemaTrees(doc, mapData);

            return mapData;
        }

        private void ParseSchemas(XmlDocument doc, BtmMapData mapData, string btmFilePath)
        {
            // Source schema
            var srcSchemaNode = doc.SelectSingleNode("//mapsource/SrcTree");
            if (srcSchemaNode?.Attributes["Schema"] != null)
            {
                mapData.SourceSchema = ExtractSchemaFileName(srcSchemaNode.Attributes["Schema"].Value);
            }
            else
            {
                // Try to get from Reference/Location (older BTM format)
                var srcRefNode = srcSchemaNode?.SelectSingleNode("Reference");
                if (srcRefNode?.Attributes["Location"] != null)
                {
                    mapData.SourceSchema = ExtractSchemaFileName(srcRefNode.Attributes["Location"].Value);
                }
            }

            // Target schema
            var trgSchemaNode = doc.SelectSingleNode("//mapsource/TrgTree");
            if (trgSchemaNode?.Attributes["Schema"] != null)
            {
                mapData.TargetSchema = ExtractSchemaFileName(trgSchemaNode.Attributes["Schema"].Value);
            }
            else
            {
                // Try to get from Reference/Location (older BTM format)
                var trgRefNode = trgSchemaNode?.SelectSingleNode("Reference");
                if (trgRefNode?.Attributes["Location"] != null)
                {
                    mapData.TargetSchema = ExtractSchemaFileName(trgRefNode.Attributes["Location"].Value);
                }
            }

            // Parse namespaces from schema references
            ParseNamespaces(doc, mapData);
            
            // Try to load external XSD files and extract additional namespaces
            LoadExternalSchemas(doc, mapData, btmFilePath);
        }

        /// <summary>
        /// Extracts the schema file name from an assembly-qualified type name.
        /// </summary>
        /// <param name="schemaPath">The full assembly-qualified schema path.</param>
        /// <returns>The schema file name with .xsd extension.</returns>
        private string ExtractSchemaFileName(string schemaPath)
        {
            if (string.IsNullOrEmpty(schemaPath))
                return null;

            var parts = schemaPath.Split(',');
            if (parts.Length > 0)
            {
                var assemblyQualifiedName = parts[0].Trim();
                var lastDot = assemblyQualifiedName.LastIndexOf('.');
                if (lastDot >= 0)
                {
                    return assemblyQualifiedName.Substring(lastDot + 1) + ".xsd";
                }
                return assemblyQualifiedName + ".xsd";
            }
            return schemaPath;
        }

        private void ParseNamespaces(XmlDocument doc, BtmMapData mapData)
        {
            // Parse source namespaces
            var srcSchemaNode = doc.SelectSingleNode("//mapsource/SrcTree/SchemaReference");
            if (srcSchemaNode != null)
            {
                var nsManager = new XmlNamespaceManager(doc.NameTable);
                foreach (XmlAttribute attr in srcSchemaNode.Attributes)
                {
                    if (attr.Name.StartsWith("xmlns:"))
                    {
                        var prefix = attr.Name.Substring(6);
                        mapData.SourceNamespaces[prefix] = attr.Value;
                    }
                    else if (attr.Name == "xmlns" && !string.IsNullOrEmpty(attr.Value))
                    {
                        // Default namespace - assign a proper prefix instead of empty key
                        var prefix = DeriveNamespacePrefix(attr.Value, mapData.SourceNamespaces);
                        mapData.SourceNamespaces[prefix] = attr.Value;
                    }
                }
            }

            // Parse target namespaces
            var trgSchemaNode = doc.SelectSingleNode("//mapsource/TrgTree/SchemaReference");
            if (trgSchemaNode != null)
            {
                foreach (XmlAttribute attr in trgSchemaNode.Attributes)
                {
                    if (attr.Name.StartsWith("xmlns:"))
                    {
                        var prefix = attr.Name.Substring(6);
                        mapData.TargetNamespaces[prefix] = attr.Value;
                    }
                    else if (attr.Name == "xmlns" && !string.IsNullOrEmpty(attr.Value))
                    {
                        // Default namespace - assign a proper prefix instead of empty key
                        var prefix = DeriveNamespacePrefix(attr.Value, mapData.TargetNamespaces);
                        mapData.TargetNamespaces[prefix] = attr.Value;
                    }
                }
            }

            // Add common namespaces if not present
            if (!mapData.SourceNamespaces.ContainsKey("xs"))
                mapData.SourceNamespaces["xs"] = "http://www.w3.org/2001/XMLSchema";
            if (!mapData.TargetNamespaces.ContainsKey("xs"))
                mapData.TargetNamespaces["xs"] = "http://www.w3.org/2001/XMLSchema";
        }

        private void LoadExternalSchemas(XmlDocument doc, BtmMapData mapData, string btmFilePath)
        {
            var btmDir = Path.GetDirectoryName(btmFilePath);
            var processedSchemas = new HashSet<string>();
            
            // First, load the explicitly provided schema files if available
            if (!string.IsNullOrEmpty(_providedSourceSchemaPath) && File.Exists(_providedSourceSchemaPath))
            {
                Trace.TraceInformation("Loading provided source schema: {0}", _providedSourceSchemaPath);
                try
                {
                    var dummyRef = doc.CreateElement("Reference");
                    var srcTree = doc.SelectSingleNode("//mapsource/SrcTree");
                    if (srcTree != null)
                    {
                        srcTree.AppendChild(dummyRef);
                        LoadExternalSchema(_providedSourceSchemaPath, mapData, dummyRef);
                        srcTree.RemoveChild(dummyRef);
                        processedSchemas.Add(_providedSourceSchemaPath);
                        Trace.TraceInformation("Successfully loaded source schema. Source namespaces: {0}", string.Join(", ", mapData.SourceNamespaces.Select(kvp => kvp.Key + "=" + kvp.Value)));
                    }
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning("Could not load provided source schema: {0}", ex.Message);
                }
            }
            
            if (!string.IsNullOrEmpty(_providedTargetSchemaPath) && File.Exists(_providedTargetSchemaPath))
            {
                Trace.TraceInformation("Loading provided target schema: {0}", _providedTargetSchemaPath);
                try
                {
                    var dummyRef = doc.CreateElement("Reference");
                    var trgTree = doc.SelectSingleNode("//mapsource/TrgTree");
                    if (trgTree != null)
                    {
                        trgTree.AppendChild(dummyRef);
                        LoadExternalSchema(_providedTargetSchemaPath, mapData, dummyRef);
                        trgTree.RemoveChild(dummyRef);
                        processedSchemas.Add(_providedTargetSchemaPath);
                        Trace.TraceInformation("Successfully loaded target schema. Target namespaces: {0}", string.Join(", ", mapData.TargetNamespaces.Select(kvp => kvp.Key + "=" + kvp.Value)));
                    }
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning("Could not load provided target schema: {0}", ex.Message);
                }
            }
            
            // Also check the main Schema attributes on SrcTree and TrgTree (if not already loaded via provided paths)
            LoadSchemaFromTreeAttribute(doc, "//mapsource/SrcTree", mapData, btmDir, true, processedSchemas);
            LoadSchemaFromTreeAttribute(doc, "//mapsource/TrgTree", mapData, btmDir, false, processedSchemas);
            
            // Look for Reference elements that point to external XSD files or type names
            var referenceNodes = doc.SelectNodes("//Reference");
            if (referenceNodes == null) return;

            foreach (XmlNode refNode in referenceNodes)
            {
                var location = refNode.Attributes?["Location"]?.Value;
                if (string.IsNullOrEmpty(location))
                    continue;

                string xsdPath = null;
                
                // Check if location is already an XSD file path
                if (location.EndsWith(".xsd", StringComparison.OrdinalIgnoreCase))
                {
                    xsdPath = Path.IsPathRooted(location) 
                        ? location 
                        : Path.Combine(btmDir, location);

                    if (!File.Exists(xsdPath))
                    {
                        // Try relative to BTM file
                        xsdPath = Path.Combine(btmDir, Path.GetFileName(location));
                        if (!File.Exists(xsdPath))
                            xsdPath = null;
                    }
                }
                else
                {
                    // Location is a type name - try to find the XSD file
                    xsdPath = FindXsdFileFromTypeName(location, btmDir);
                }

                if (!string.IsNullOrEmpty(xsdPath) && File.Exists(xsdPath) && !processedSchemas.Contains(xsdPath))
                {
                    try
                    {
                        LoadExternalSchema(xsdPath, mapData, refNode);
                        processedSchemas.Add(xsdPath);
                    }
                    catch (Exception ex)
                    {
                    Trace.TraceWarning("Could not load schema {0}: {1}", xsdPath, ex.Message);
                    }
                }
            }
        }
        
        private void LoadSchemaFromTreeAttribute(XmlDocument doc, string treeXPath, BtmMapData mapData, 
            string btmDir, bool isSourceSchema, HashSet<string> processedSchemas)
        {
            var treeNode = doc.SelectSingleNode(treeXPath);
            var schemaAttr = treeNode?.Attributes?["Schema"]?.Value;
            
            if (string.IsNullOrEmpty(schemaAttr))
                return;
                
            var xsdPath = FindXsdFileFromTypeName(schemaAttr, btmDir);
            
            if (!string.IsNullOrEmpty(xsdPath) && File.Exists(xsdPath) && !processedSchemas.Contains(xsdPath))
            {
                try
                {
                    // Create a dummy reference node to satisfy the LoadExternalSchema signature
                    var dummyRef = doc.CreateElement("Reference");
                    var parent = isSourceSchema ? doc.SelectSingleNode("//mapsource/SrcTree") : doc.SelectSingleNode("//mapsource/TrgTree");
                    if (parent != null)
                    {
                        parent.AppendChild(dummyRef);
                        LoadExternalSchema(xsdPath, mapData, dummyRef);
                        parent.RemoveChild(dummyRef);
                    }
                    processedSchemas.Add(xsdPath);
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning("Could not load schema from tree attribute {0}: {1}", xsdPath, ex.Message);
                }
            }
        }

        /// <summary>
        /// Loads an external XSD schema file and extracts namespace declarations.
        /// </summary>
        /// <param name="xsdPath">Path to the XSD file to load.</param>
        /// <param name="mapData">The map data object to populate with namespace information.</param>
        /// <param name="referenceNode">The XML reference node indicating whether this is source or target schema.</param>
        private void LoadExternalSchema(string xsdPath, BtmMapData mapData, XmlNode referenceNode)
        {
            var xsdDoc = new XmlDocument();
            xsdDoc.Load(xsdPath);

            var nsMgr = new XmlNamespaceManager(xsdDoc.NameTable);
            nsMgr.AddNamespace("xs", "http://www.w3.org/2001/XMLSchema");

            // Extract target namespace
            var schemaNode = xsdDoc.SelectSingleNode("/xs:schema", nsMgr);
            if (schemaNode == null) return;

            var targetNamespace = schemaNode.Attributes?["targetNamespace"]?.Value;
            
            // Determine if this is source or target schema based on BTM structure
            bool isSourceSchema = IsSourceSchemaReference(referenceNode);
            var namespaces = isSourceSchema ? mapData.SourceNamespaces : mapData.TargetNamespaces;
            
            Trace.TraceInformation("Processing schema: {0}, targetNamespace: {1}", Path.GetFileName(xsdPath), targetNamespace);
            
            // First, extract all namespace declarations from the schema element (except default namespace)
            foreach (XmlAttribute attr in schemaNode.Attributes)
            {
                if (attr.Name.StartsWith("xmlns:") && attr.Name != "xmlns:xs")
                {
                    var nsPrefix = attr.Name.Substring(6);
                    if (!namespaces.ContainsKey(nsPrefix))
                    {
                        namespaces[nsPrefix] = attr.Value;
                        Trace.TraceInformation("  Added namespace from xmlns: {0} = {1}", nsPrefix, attr.Value);
                    }
                }
                else if (attr.Name == "xmlns" && !string.IsNullOrEmpty(attr.Value))
                {
                    // Default namespace - assign it a proper prefix instead of keeping it as default
                    // Check if this namespace already has a prefix
                    var existingPrefix = namespaces.FirstOrDefault(kvp => kvp.Value == attr.Value).Key;
                    
                    if (existingPrefix == null)
                    {
                        // Derive a proper prefix for the default namespace
                        var prefix = DeriveNamespacePrefix(attr.Value, namespaces);
                        if (!namespaces.ContainsKey(prefix))
                        {
                            namespaces[prefix] = attr.Value;
                            Trace.TraceInformation("  Added default namespace with prefix: {0} = {1}", prefix, attr.Value);
                        }
                    }
                }
            }
            
            // Add target namespace if present and not already added via xmlns declarations
            if (!string.IsNullOrEmpty(targetNamespace))
            {
                // Check if this namespace already has a prefix
                var existingPrefix = namespaces.FirstOrDefault(kvp => kvp.Value == targetNamespace).Key;
                
                if (existingPrefix == null)
                {
                    // Add namespace with a derived prefix
                    var prefix = DeriveNamespacePrefix(targetNamespace, namespaces);
                    if (!namespaces.ContainsKey(prefix))
                    {
                        namespaces[prefix] = targetNamespace;
                        Trace.TraceInformation("  Added target namespace with derived prefix: {0} = {1}", prefix, targetNamespace);
                    }
                }
            }
            
            // Also process any xs:import or xs:include elements to get additional namespaces
            var importNodes = schemaNode.SelectNodes("xs:import", nsMgr);
            if (importNodes != null)
            {
                foreach (XmlNode importNode in importNodes)
                {
                    var importNamespace = importNode.Attributes?["namespace"]?.Value;
                    if (!string.IsNullOrEmpty(importNamespace) && !namespaces.ContainsValue(importNamespace))
                    {
                        var prefix = DeriveNamespacePrefix(importNamespace, namespaces);
                        if (!namespaces.ContainsKey(prefix))
                        {
                            namespaces[prefix] = importNamespace;
                            Trace.TraceInformation("  Added imported namespace: {0} = {1}", prefix, importNamespace);
                        }
                    }
                }
            }
            
            // Clean up: Remove any empty prefix entries that may have been added
            if (namespaces.ContainsKey(""))
            {
                var emptyPrefixValue = namespaces[""];
                namespaces.Remove("");
                Trace.TraceInformation("  Removed empty prefix for namespace: {0}", emptyPrefixValue);
                
                // Make sure this namespace has a proper prefix
                if (!namespaces.ContainsValue(emptyPrefixValue))
                {
                    var newPrefix = DeriveNamespacePrefix(emptyPrefixValue, namespaces);
                    namespaces[newPrefix] = emptyPrefixValue;
                    Trace.TraceInformation("  Re-added with proper prefix: {0} = {1}", newPrefix, emptyPrefixValue);
                }
            }
        }

        private string FindXsdFileFromTypeName(string typeName, string btmDir)
        {
            // Extract filename from type name (e.g., "Microsoft.Samples.BizTalk.Litware.Schemas.Order.PurchaseOrder" -> "PurchaseOrder.xsd")
            var fileName = ExtractSchemaFileName(typeName);
            
            if (string.IsNullOrEmpty(fileName))
                return null;

            // Search in common locations
            var searchPaths = new[]
            {
                btmDir,
                Path.Combine(btmDir, "Schemas"),
                Path.Combine(btmDir, "..", "Schemas"),
                Path.Combine(btmDir, "schemas"),
                Path.Combine(btmDir, "..", "schemas")
            };

            foreach (var searchPath in searchPaths)
            {
                if (!Directory.Exists(searchPath))
                    continue;

                var xsdPath = Path.Combine(searchPath, fileName);
                if (File.Exists(xsdPath))
                    return xsdPath;
                    
                // Also try with full type name as filename
                var fullFileName = typeName.Replace(".", "") + ".xsd";
                xsdPath = Path.Combine(searchPath, fullFileName);
                if (File.Exists(xsdPath))
                    return xsdPath;
            }

            return null;
        }

        /// <summary>
        /// Determines whether an XML reference node belongs to the source or target schema tree.
        /// </summary>
        /// <param name="referenceNode">The reference node to check.</param>
        /// <returns>True if the node is under SrcTree; false if under TrgTree or by default.</returns>
        private bool IsSourceSchemaReference(XmlNode referenceNode)
        {
            var current = referenceNode.ParentNode;
            while (current != null)
            {
                if (current.Name == "SrcTree") return true;
                if (current.Name == "TrgTree") return false;
                current = current.ParentNode;
            }
            return true; // Default to source
        }

        /// <summary>
        /// Derives an appropriate namespace prefix for a target namespace URI.
        /// </summary>
        /// <param name="targetNamespace">The namespace URI for which to derive a prefix.</param>
        /// <param name="existingNamespaces">Dictionary of already-assigned namespace prefixes.</param>
        /// <returns>A namespace prefix (e.g., "ns0", "ns1") suitable for the namespace.</returns>
        /// <remarks>
        /// For EDI namespaces (X12, EDIFACT), always uses ns0, ns1, etc. to ensure Azure Data Mapper compatibility.
        /// Avoids year-based prefixes that may cause parsing issues.
        /// </remarks>
        private string DeriveNamespacePrefix(string targetNamespace, Dictionary<string, string> existingNamespaces)
        {
            foreach (var kvp in existingNamespaces)
            {
                if (kvp.Value == targetNamespace)
                    return kvp.Key;
            }
            
            // For EDI namespaces (X12, EDIFACT), always use ns0, ns1, etc.
            // Do NOT use year-based prefixes like "2006"
            // This ensures compatibility with Azure Data Mapper which expects ns0: not 2006:
            bool isEdiNamespace = targetNamespace.Contains("/EDI/") || 
                                  targetNamespace.Contains("/X12/") || 
                                  targetNamespace.Contains("/EDIFACT/");
            
            if (isEdiNamespace)
            {
                // Use ns0, ns1, etc. for EDI namespaces
                int counter = 0;
                while (existingNamespaces.ContainsKey($"ns{counter}"))
                {
                    counter++;
                }
                return $"ns{counter}";
            }
            
            // Try to derive a meaningful prefix from the namespace URI
            try
            {
                var uri = new Uri(targetNamespace, UriKind.Absolute);
                var segments = uri.Segments;
                
                // Check if ANY segment is a year (not just the last one)
                // If the namespace contains a year, prefer ns0, ns1, etc.
                bool containsYearSegment = false;
                foreach (var segment in segments)
                {
                    var cleanSegment = segment.TrimEnd('/').Replace(".", "").Replace("-", "").ToLower();
                    if (cleanSegment.Length == 4 && 
                        int.TryParse(cleanSegment, out var year) && 
                        year >= 1990 && 
                        year <= 2100)
                    {
                        containsYearSegment = true;
                        break;
                    }
                }
                
                if (containsYearSegment)
                {
                    // Fall through to use ns0, ns1, etc. if namespace contains a year
                }
                else if (segments.Length > 0)
                {
                    // Try last segment without extension
                    var lastSegment = segments[segments.Length - 1].TrimEnd('/');
                    var prefix = lastSegment.Replace(".", "").Replace("-", "").ToLower();
                    
                    if (!string.IsNullOrEmpty(prefix) && !existingNamespaces.ContainsKey(prefix))
                    {
                        return prefix;
                    }
                }
            }
            catch
            {
                // If URI parsing fails, continue to fallback
            }
            
            // Fallback to ns0, ns1, ns2, etc.
            // Primary schema namespaces should start at ns0 to align with Azure Data Mapper conventions
            int fallbackCounter = 0;
            while (existingNamespaces.ContainsKey($"ns{fallbackCounter}"))
            {
                fallbackCounter++;
            }
            return $"ns{fallbackCounter}";
        }

        /// <summary>
        /// Parses all functoid elements from the BTM XML document.
        /// </summary>
        /// <param name="doc">The loaded BTM XML document.</param>
        /// <param name="mapData">The map data object to populate with parsed functoids.</param>
        private void ParseFunctoids(XmlDocument doc, BtmMapData mapData)
        {
            var functoidNodes = doc.SelectNodes("//Functoid");
            if (functoidNodes == null) return;

            foreach (XmlNode functoidNode in functoidNodes)
            {
                var functoid = new BtmFunctoid
                {
                    FunctoidId = functoidNode.Attributes["FunctoidID"]?.Value,
                    FunctoidType = DetermineFunctoidType(functoidNode),
                    FunctoidFid = functoidNode.Attributes["Functoid-FID"]?.Value,
                    FunctoidClsid = functoidNode.Attributes["Functoid-CLSID"]?.Value,
                    XCell = int.TryParse(functoidNode.Attributes["X-Cell"]?.Value, out int x) ? x : 0,
                    YCell = int.TryParse(functoidNode.Attributes["Y-Cell"]?.Value, out int y) ? y : 0
                };

                // Parse page number (multi-page BTM files use a Page attribute)
                if (int.TryParse(functoidNode.Attributes?["Page"]?.Value, out int pageNum))
                {
                    functoid.PageNumber = pageNum;
                    if (pageNum > mapData.PageCount)
                        mapData.PageCount = pageNum;
                }

                // Parse input parameters
                var inputParamsNode = functoidNode.SelectSingleNode("Input-Parameters");
                if (inputParamsNode != null)
                {
                    foreach (XmlNode paramNode in inputParamsNode.ChildNodes)
                    {
                        if (paramNode.Name == "Parameter")
                        {
                            var paramType = paramNode.Attributes["Type"]?.Value?.ToLower();
                            var param = new BtmParameter
                            {
                                Type = paramType,
                                Value = paramNode.Attributes["Value"]?.Value ?? paramNode.InnerText,
                                Guid = paramNode.Attributes["Guid"]?.Value
                            };

                            // Extract explicit order if present; otherwise use document position
                            if (int.TryParse(paramNode.Attributes["Order"]?.Value, out int order))
                            {
                                param.LinkIndex = order;
                            }
                            else
                            {
                                param.LinkIndex = functoid.InputParameters.Count;
                            }

                            functoid.InputParameters.Add(param);
                        }
                    }

                    // Sort parameters by their explicit order to ensure correct positional semantics
                    functoid.InputParameters.Sort((a, b) => a.LinkIndex.CompareTo(b.LinkIndex));
                }

                // Parse scripter code
                var scripterNode = functoidNode.SelectSingleNode("ScripterCode");
                if (scripterNode != null)
                {
                    functoid.ScripterLanguage = scripterNode.Attributes["Language"]?.Value;
                    functoid.ScripterAssembly = scripterNode.Attributes["Assembly"]?.Value;
                    functoid.ScripterClass = scripterNode.Attributes["Class"]?.Value;
                    functoid.ScripterFunction = scripterNode.Attributes["Function"]?.Value;
                    functoid.ScripterCode = scripterNode.InnerText;
                }

                mapData.Functoids.Add(functoid);
            }
        }

        /// <summary>
        /// Maps a BizTalk functoid FID (Functoid ID) to its human-readable type name.
        /// </summary>
        /// <param name="functoidNode">The XML node representing the functoid.</param>
        /// <returns>The functoid type name (e.g., "StringConcatenate", "MathAdd") or "Unknown" if not recognized.</returns>
        /// <remarks>      
        /// Supports string, math, date, conversion, scientific, logical, cumulative, and advanced functoids.
        /// </remarks>
        private string DetermineFunctoidType(XmlNode functoidNode)
        {
            var fid = functoidNode.Attributes["Functoid-FID"]?.Value;

            var functoidTypeMap = new Dictionary<string, string>
            {
                // String functoids (101-110)
                { "101", "StringFind" },
                { "102", "StringLeft" },
                { "103", "StringLowerCase" },
                { "104", "StringRight" },
                { "105", "StringSize" },
                { "106", "StringSubstring" },
                { "107", "StringConcatenate" },
                { "108", "StringTrimLeft" },
                { "109", "StringTrimRight" },
                { "110", "StringUpperCase" },
                
                // Math functoids (111-121)
                { "111", "MathAbs" },           // Absolute Value
                { "112", "MathInt" },           // Integer
                { "113", "MathMax" },           // Maximum
                { "114", "MathMin" },           // Minimum
                { "115", "MathMod" },           // Modulo
                { "116", "MathRound" },         // Round
                { "117", "MathSqrt" },          // Square Root
                { "118", "MathAdd" },           // Addition
                { "119", "MathSubtract" },      // Subtraction
                { "120", "MathMultiply" },      // Multiplication
                { "121", "MathDivide" },        // Division
                
                // Date functoids (122-125)
                { "122", "DateAddDays" },       // Add Days
                { "123", "DateCurrentDate" },  // Current Date
                { "124", "DateCurrentTime" },  // Current Time
                { "125", "DateCurrentDateTime" }, // Current Date/Time
                
                // Conversion functoids (126-129)
                { "126", "ConvertAsc" },        // ASCII to Character
                { "127", "ConvertChr" },        // Character to ASCII
                { "128", "ConvertHex" },        // Hexadecimal
                { "129", "ConvertOct" },        // Octal
                
                // Scientific functoids (130-139)
                { "130", "SciArcTan" },         // Arc Tangent
                { "131", "SciCos" },            // Cosine
                { "132", "SciSin" },            // Sine
                { "133", "SciTan" },            // Tangent
                { "134", "SciExp" },            // Natural Exponential
                { "135", "SciLog" },            // Natural Logarithm
                { "136", "SciExp10" },          // Base 10 Exponential
                { "137", "SciLog10" },          // Base 10 Logarithm
                { "138", "SciPow" },            // Power
                { "139", "SciLogn" },           // Logarithm (base N)
                { "140", "StringPadLeft" },     // String Pad Left (common/fill)
                
                // Scripting functoid
                { "260", "Scripting" },
                
                // Logical functoids (311-321, 701, 705, 706)
                { "311", "LogicalGt" },         // Greater Than
                { "312", "LogicalGte" },        // Greater Than or Equal
                { "313", "LogicalLt" },         // Less Than
                { "314", "LogicalLte" },        // Less Than or Equal
                { "315", "LogicalEq" },         // Equal
                { "316", "LogicalNe" },         // Not Equal
                { "317", "LogicalIsString" },   // Is String
                { "318", "LogicalIsDate" },     // Is Date
                { "319", "LogicalIsNumeric" },  // Is Numeric
                { "320", "LogicalOr" },         // Logical OR
                { "321", "LogicalAnd" },        // Logical AND
                { "701", "LogicalExistence" },  // Logical Existence
                { "705", "LogicalNot" },        // Logical NOT
                { "706", "IsNil" },             // Is Nil
                
                // Advanced functoids - Index/Count (322-323)
                { "322", "Count" },             // Record Count
                { "323", "Index" },             // Index
                
                // Cumulative functoids (324-328)
                { "324", "CumulativeSum" },     // Cumulative Sum
                { "325", "CumulativeAvg" },     // Cumulative Average
                { "326", "CumulativeMin" },     // Cumulative Minimum
                { "327", "CumulativeMax" },     // Cumulative Maximum
                { "328", "CumulativeConcat" },  // Cumulative Concatenate
                { "329", "CumulativeCount" },  // Cumulative Count
                
                // Advanced functoids - Value Mapping (374-376)
                { "374", "ValueMappingFlattening" },
                { "375", "ValueMapping" },
                { "376", "NilValue" },          // Nil Value
                { "377", "MassFlattening" },    // Value Mapping (Flattening) variant
                
                // Advanced functoids - Looping/Iteration (424, 474)
                { "424", "Looping" },           // Looping functoid
                { "425", "TableLoopingExtract" }, // Table Looping Extract
                { "474", "Iteration" },         // Iteration functoid
                { "475", "RecordCount" },       // Record Count (alternate FID)
                
                // Database functoids (524, 574-575)
                { "524", "DBLookup" },          // Database Lookup
                { "574", "DBValueExtract" },    // Database Value Extract
                { "575", "DBErrorExtract" },    // Database Error Extract
                
                // Advanced functoids - XPath, Table, Assert (702-707)
                { "702", "XPath" },             // XPath
                { "703", "TableLooping" },      // Table Looping
                { "704", "TableExtractor" },    // Table Extractor
                { "707", "Assert" },            // Assert
                
                // Advanced functoids - Mass operations (800-802)
                { "800", "KeyMatch" },          // Key Match
                { "801", "ExistenceLooping" },  // Existence Looping
                { "802", "MassCopy" },          // Mass Copy
            };

            return functoidTypeMap.TryGetValue(fid ?? "", out string type) ? type : "Unknown";
        }

        /// <summary>
        /// Parses all link elements from the BTM XML document.
        /// </summary>
        /// <param name="doc">The loaded BTM XML document.</param>
        /// <param name="mapData">The map data object to populate with parsed links.</param>
        private void ParseLinks(XmlDocument doc, BtmMapData mapData)
        {
            var linkNodes = doc.SelectNodes("//Link");
            if (linkNodes == null) return;

            Trace.TraceInformation("Parsing {0} links from BTM", linkNodes.Count);
            foreach (XmlNode linkNode in linkNodes)
            {
                var link = new BtmLink
                {
                    LinkId = linkNode.Attributes["LinkID"]?.Value,
                    LinkFrom = linkNode.Attributes["LinkFrom"]?.Value,
                    LinkTo = linkNode.Attributes["LinkTo"]?.Value,
                    Label = linkNode.Attributes["Label"]?.Value,
                    SourceCopyDirective = linkNode.Attributes["Compiler-Copy-Directive"]?.Value,
                    TargetDirective = linkNode.Attributes["Compiler-Directive"]?.Value
                };

                Trace.TraceInformation("  Link: ID={0}, From={1}, To={2}", link.LinkId, link.LinkFrom, link.LinkTo);
                mapData.Links.Add(link);
            }
        }

        private void ParseSchemaTrees(XmlDocument doc, BtmMapData mapData)
        {
            // Parse source tree
            var srcTreeNode = doc.SelectSingleNode("//mapsource/SrcTree");
            if (srcTreeNode != null)
            {
                mapData.SourceTree = ParseSchemaTree(srcTreeNode, mapData.SourceNamespaces, true);
            }

            // Parse target tree
            var trgTreeNode = doc.SelectSingleNode("//mapsource/TrgTree");
            if (trgTreeNode != null)
            {
                mapData.TargetTree = ParseSchemaTree(trgTreeNode, mapData.TargetNamespaces, false);
            }
        }

        private BtmSchemaTree ParseSchemaTree(XmlNode treeNode, Dictionary<string, string> namespaces, bool isSource)
        {
            var tree = new BtmSchemaTree
            {
                SchemaName = treeNode.Attributes?["Schema"]?.Value
            };

            var schemaRefNode = treeNode.SelectSingleNode("SchemaReference");
            if (schemaRefNode != null)
            {
                tree.Root = ParseSchemaNode(schemaRefNode.FirstChild, null, "", namespaces);
                BuildNodeLookup(tree.Root, tree.NodeLookup);
            }
            else
            {
                // Try to load from external XSD if SchemaReference is missing
                var schemaAttr = treeNode.Attributes?["Schema"]?.Value;
                if (!string.IsNullOrEmpty(schemaAttr))
                {
                    tree.Root = LoadSchemaTreeFromExternal(schemaAttr, namespaces, isSource);
                    if (tree.Root != null)
                    {
                        BuildNodeLookup(tree.Root, tree.NodeLookup);
                    }
                }
            }

            return tree;
        }

        private BtmSchemaNode ParseSchemaNode(XmlNode node, BtmSchemaNode parent, string parentPath, Dictionary<string, string> namespaces)
        {
            if (node == null) return null;

            var schemaNode = new BtmSchemaNode
            {
                NodeId = node.Attributes?["TreeNodeID"]?.Value,
                Name = node.LocalName,
                DataType = node.Attributes?["type"]?.Value,
                IsRepeating = node.Attributes?["maxOccurs"]?.Value == "unbounded",
                Parent = parent
            };

            // Build XPath with namespace prefix
            var elementName = schemaNode.Name;
            var prefix = FindNamespacePrefix(elementName, namespaces);
            var qualifiedName = string.IsNullOrEmpty(prefix) ? elementName : $"{prefix}:{elementName}";
            
            if (string.IsNullOrEmpty(parentPath))
            {
                schemaNode.XPath = "/" + qualifiedName;
            }
            else
            {
                schemaNode.XPath = parentPath + "/" + qualifiedName;
            }

            // Normalize XPath if it contains BTM-style local-name() format
            schemaNode.XPath = NormalizeXPath(schemaNode.XPath, namespaces);

            // Parse child nodes
            foreach (XmlNode childNode in node.ChildNodes)
            {
                if (childNode.NodeType == XmlNodeType.Element)
                {
                    var child = ParseSchemaNode(childNode, schemaNode, schemaNode.XPath, namespaces);
                    if (child != null)
                    {
                        schemaNode.Children.Add(child);
                    }
                }
            }

            return schemaNode;
        }

        private BtmSchemaNode LoadSchemaTreeFromExternal(string schemaName, Dictionary<string, string> namespaces, bool isSource)
        {
            // Try to find and load the external XSD file
            var xsdFileName = ExtractSchemaFileName(schemaName);
            var xsdPath = Path.Combine(_btmDirectory, xsdFileName);
            
            if (!File.Exists(xsdPath))
                return null;

            try
            {
                var xsdDoc = new XmlDocument();
                xsdDoc.Load(xsdPath);

                var nsMgr = new XmlNamespaceManager(xsdDoc.NameTable);
                nsMgr.AddNamespace("xs", "http://www.w3.org/2001/XMLSchema");

                // Find root element
                var rootElement = xsdDoc.SelectSingleNode("//xs:schema/xs:element", nsMgr);
                if (rootElement != null)
                {
                    return ParseXsdElement(rootElement, null, "", namespaces, nsMgr);
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Could not load schema tree from {0}: {1}", xsdPath, ex.Message);
            }

            return null;
        }

        private BtmSchemaNode ParseXsdElement(XmlNode element, BtmSchemaNode parent, string parentPath, 
            Dictionary<string, string> namespaces, XmlNamespaceManager nsMgr)
        {
            var name = element.Attributes?["name"]?.Value;
            if (string.IsNullOrEmpty(name))
                return null;

            var node = new BtmSchemaNode
            {
                Name = name,
                DataType = element.Attributes?["type"]?.Value,
                IsRepeating = element.Attributes?["maxOccurs"]?.Value == "unbounded",
                Parent = parent
            };

            // Build XPath
            node.XPath = string.IsNullOrEmpty(parentPath) 
                ? "/" + name 
                : parentPath + "/" + name;

            // Parse complex type children
            var complexType = element.SelectSingleNode("xs:complexType", nsMgr);
            if (complexType != null)
            {
                // Collect child elements from xs:sequence, xs:choice, xs:all, and xs:group
                foreach (XmlNode compositorChild in complexType.ChildNodes)
                {
                    if (compositorChild.NodeType == XmlNodeType.Element &&
                        (compositorChild.LocalName == "sequence" || compositorChild.LocalName == "choice" ||
                         compositorChild.LocalName == "all" || compositorChild.LocalName == "group"))
                    {
                        ParseXsdCompositor(compositorChild, node, namespaces, nsMgr);
                    }
                }
            }

            return node;
        }

        /// <summary>
        /// Recursively parses an XSD compositor (sequence, choice, all, or group) and adds discovered elements to the parent node.
        /// </summary>
        private void ParseXsdCompositor(XmlNode compositor, BtmSchemaNode parentNode,
            Dictionary<string, string> namespaces, XmlNamespaceManager nsMgr)
        {
            foreach (XmlNode child in compositor.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element)
                    continue;

                if (child.LocalName == "element")
                {
                    var parsed = ParseXsdElement(child, parentNode, parentNode.XPath, namespaces, nsMgr);
                    if (parsed != null)
                    {
                        parentNode.Children.Add(parsed);
                    }
                }
                else if (child.LocalName == "sequence" || child.LocalName == "choice" ||
                         child.LocalName == "all" || child.LocalName == "group")
                {
                    ParseXsdCompositor(child, parentNode, namespaces, nsMgr);
                }
            }
        }

        /// <summary>
        /// Normalizes XPath expressions from BTM format to standard namespace-prefixed format.
        /// </summary>
        /// <param name="xpath">The XPath expression to normalize.</param>
        /// <param name="namespaces">Dictionary of namespace prefixes to use.</param>
        /// <returns>The normalized XPath with proper namespace prefixes.</returns>
        /// <example>
        /// Converts /*[local-name()='Element'] to /ns:Element
        /// </example>
        public string NormalizeXPath(string xpath, Dictionary<string, string> namespaces)
        {
            if (string.IsNullOrEmpty(xpath))
                return xpath;

            // Pattern to match BTM-style XPath: /*[local-name()='ElementName']
            var pattern = @"/\*\[local-name\(\)='([^']+)'\]";
            var regex = new Regex(pattern);

            var normalized = regex.Replace(xpath, match =>
            {
                var elementName = match.Groups[1].Value;
                
                // Find appropriate namespace prefix
                var prefix = FindNamespacePrefix(elementName, namespaces);
                
                return string.IsNullOrEmpty(prefix) 
                    ? "/" + elementName 
                    : "/" + prefix +":" + elementName;
            });

            return normalized;
        }

        /// <summary>
        /// Finds an appropriate namespace prefix for an element based on available namespaces.
        /// </summary>
        /// <param name="elementName">The element name for which to find a prefix.</param>
        /// <param name="namespaces">Dictionary of available namespace prefixes.</param>
        /// <returns>The namespace prefix to use, or empty string if no suitable prefix is found.</returns>
        private string FindNamespacePrefix(string elementName, Dictionary<string, string> namespaces)
        {
            // Priority: ns1, ns2, ... (these are typically the target schema namespaces)
            
            // First, look for ns1, ns2, etc. which are typically the target schema namespace
            var nsPrefixes = namespaces.Keys
                .Where(k => k.Length >= 3 && k.StartsWith("ns") && char.IsDigit(k[2]))
                .OrderBy(k => k)
                .ToList();
                
            if (nsPrefixes.Any())
                return nsPrefixes.First();
            
            // Fallback: find any suitable prefix (not xs, not empty, not msdata, not b, not ns0)
            foreach (var kvp in namespaces)
            {
                if (kvp.Key != "xs" && 
                    kvp.Key != "" && 
                    kvp.Key != "b" && 
                    kvp.Key != "ns0" &&
                    !kvp.Key.StartsWith("msdata"))
                    return kvp.Key;
            }
            
            // If no suitable prefix found, return empty (no namespace)
            return "";
        }

        private void BuildNodeLookup(BtmSchemaNode node, Dictionary<string, BtmSchemaNode> lookup)
        {
            if (node == null) return;

            if (!string.IsNullOrEmpty(node.NodeId))
            {
                lookup[node.NodeId] = node;
            }

            foreach (var child in node.Children)
            {
                BuildNodeLookup(child, lookup);
            }
        }
    }
}
