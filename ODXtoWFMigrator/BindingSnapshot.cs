// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;

namespace BizTalktoLogicApps.ODXtoWFMigrator
{
    /// <summary>
    /// Represents a filter condition in a send port that binds it to receive ports.
    /// </summary>
    /// <remarks>
    /// Filter conditions determine which messages a send port will process based on message context properties.
    /// The most common filter is BTS.ReceivePortName to bind send ports to specific receive ports.
    /// </remarks>
    public class FilterCondition
    {
        /// <summary>
        /// Gets or sets the message context property name (e.g., "BTS.ReceivePortName").
        /// </summary>
        public string Property { get; set; }
        
        /// <summary>
        /// Gets or sets the comparison operator ("0" for Equals, "1" for NotEquals, etc.).
        /// </summary>
        public string Operator { get; set; }
        
        /// <summary>
        /// Gets or sets the value to compare against.
        /// </summary>
        public string Value { get; set; }
    }
    
    /// <summary>
    /// Represents an outbound XSLT map configuration for a BizTalk send port.
    /// </summary>
    /// <remarks>
    /// BizTalk send ports can apply one or more maps to transform messages before transmission.
    /// These become XSLT Transform actions in Logic Apps workflows.
    /// </remarks>
    public class BindingTransform
    {
        /// <summary>
        /// Gets or sets the fully qualified map name (e.g., "CBRSample.CBRInput2USMap").
        /// </summary>
        public string FullName { get; set; }
        
        /// <summary>
        /// Gets or sets the assembly-qualified name with version and token.
        /// </summary>
        public string AssemblyQualifiedName { get; set; }
        
        /// <summary>
        /// Gets or sets the short map name extracted from FullName (e.g., "CBRInput2USMap").
        /// </summary>
        public string ShortName { get; set; }
    }
    
    /// <summary>
    /// Represents a content-based routing scenario detected from send port filters.
    /// </summary>
    /// <remarks>
    /// Groups send ports that filter on the same promoted property (e.g., "CBRSample.CountryCode")
    /// with different values. Used to generate Switch-based Logic Apps workflows.
    /// </remarks>
    public sealed class ContentBasedRoutingGroup
    {
        /// <summary>
        /// Gets or sets the promoted property used for routing (e.g., "CBRSample.CountryCode").
        /// </summary>
        public string RoutingProperty { get; set; }
        
        /// <summary>
        /// Gets send ports grouped by their filter value.
        /// </summary>
        /// <remarks>
        /// Key: Filter value (e.g., "100" for US, "200" for CAN)
        /// Value: List of send ports with that filter value
        /// </remarks>
        public Dictionary<string, List<BindingSendPort>> RoutesByValue { get; } = 
            new Dictionary<string, List<BindingSendPort>>();
    }
    
    /// <summary>
    /// Represents a snapshot of BizTalk binding configuration including receive locations and send ports.
    /// </summary>
    /// <remarks>
    /// Parses BizTalk binding XML files to extract port configurations, transport metadata,
    /// WCF settings, and filter conditions. Supports bindings-only workflow generation.
    /// </remarks>
    public sealed class BindingSnapshot
    {
        /// <summary>
        /// Gets the collection of receive locations from the binding file.
        /// </summary>
        public List<BindingReceiveLocation> ReceiveLocations { get; } = new List<BindingReceiveLocation>();
        
        /// <summary>
        /// Gets the collection of send ports from the binding file.
        /// </summary>
        public List<BindingSendPort> SendPorts { get; } = new List<BindingSendPort>();
        
        /// <summary>
        /// Groups receive locations by their parent receive port name.
        /// </summary>
        /// <returns>Dictionary with receive port names as keys and lists of associated receive locations as values.</returns>
        public Dictionary<string, List<BindingReceiveLocation>> GetReceiveLocationsByPort()
        {
            return ReceiveLocations
                .Where(rl => !string.IsNullOrEmpty(rl.ReceivePortName))
                .GroupBy(rl => rl.ReceivePortName)
                .ToDictionary(g => g.Key, g => g.ToList());
        }
        
        /// <summary>
        /// Gets send ports that are bound to a specific receive port via filter expressions.
        /// </summary>
        /// <param name="receivePortName">The receive port name to find associated send ports for.</param>
        /// <returns>List of send ports filtered to the specified receive port.</returns>
        /// <remarks>
        /// Searches for send ports with filters matching "BTS.ReceivePortName" equals the specified port name.
        /// This is the primary mechanism for bindings-only workflow generation.
        /// </remarks>
        public List<BindingSendPort> GetSendPortsForReceivePort(string receivePortName)
        {
            if (string.IsNullOrEmpty(receivePortName)) return new List<BindingSendPort>();
            
            return SendPorts.Where(sp =>
                sp.Filters != null &&
                sp.Filters.Any(f =>
                    f.Property == "BTS.ReceivePortName" &&
                    f.Operator == "0" && // Equals
                    f.Value == receivePortName
                )
            ).ToList();
        }

        /// <summary>
        /// Detects content-based routing patterns in send port filters.
        /// Groups send ports by the promoted property they filter on.
        /// </summary>
        /// <returns>Dictionary of routing property to CBR groups, or empty if no CBR detected.</returns>
        /// <remarks>
        /// Identifies send ports that filter on promoted properties (not BTS.ReceivePortName).
        /// Returns only groups with at least 2 routes to distinguish true CBR from single-property filters.
        /// </remarks>
        public Dictionary<string, ContentBasedRoutingGroup> DetectContentBasedRouting()
        {
            var cbrGroups = new Dictionary<string, ContentBasedRoutingGroup>();
            
            // Find send ports with filters on promoted properties (not BTS.ReceivePortName)
            var potentialCbrPorts = SendPorts.Where(sp =>
                sp.Filters != null &&
                sp.Filters.Any(f => 
                    !string.IsNullOrEmpty(f.Property) &&
                    !f.Property.Equals("BTS.ReceivePortName", StringComparison.OrdinalIgnoreCase) &&
                    f.Operator == "0" // Equals operator
                )
            ).ToList();
            
            // Group by routing property
            foreach (var sp in potentialCbrPorts)
            {
                // Get the first non-BTS.ReceivePortName filter
                var routingFilter = sp.Filters.FirstOrDefault(f =>
                    !f.Property.Equals("BTS.ReceivePortName", StringComparison.OrdinalIgnoreCase) &&
                    f.Operator == "0"
                );
                
                if (routingFilter == null)
                    continue;
                
                var routingProperty = routingFilter.Property;
                var routingValue = routingFilter.Value ?? "default";
                
                // Create or get CBR group
                if (!cbrGroups.ContainsKey(routingProperty))
                {
                    cbrGroups[routingProperty] = new ContentBasedRoutingGroup
                    {
                        RoutingProperty = routingProperty
                    };
                }
                
                var cbrGroup = cbrGroups[routingProperty];
                
                // Add send port to the appropriate route
                if (!cbrGroup.RoutesByValue.ContainsKey(routingValue))
                {
                    cbrGroup.RoutesByValue[routingValue] = new List<BindingSendPort>();
                }
                
                cbrGroup.RoutesByValue[routingValue].Add(sp);
            }
            
            // Only return groups with at least 2 routes (otherwise not really CBR)
            return cbrGroups.Where(kvp => kvp.Value.RoutesByValue.Count >= 2)
                            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        /// <summary>
        /// Gets the promoted property name used for routing from send port filters.
        /// </summary>
        /// <param name="sp">The send port to analyze.</param>
        /// <returns>The routing property name (e.g., "CBRSample.CountryCode"), or null if none.</returns>
        /// <remarks>
        /// Searches for filters on promoted properties, excluding BTS.ReceivePortName.
        /// Used to detect content-based routing scenarios.
        /// </remarks>
        public static string GetRoutingPropertyFromFilter(BindingSendPort sp)
        {
            return sp.Filters?
                .FirstOrDefault(f => 
                    !string.IsNullOrEmpty(f.Property) &&
                    !f.Property.Equals("BTS.ReceivePortName", StringComparison.OrdinalIgnoreCase) &&
                    f.Operator == "0"
                )?.Property;
        }

        /// <summary>
        /// Gets the routing value from send port filters.
        /// </summary>
        /// <param name="sp">The send port to analyze.</param>
        /// <returns>The filter value (e.g., "100"), or null if none.</returns>
        /// <remarks>
        /// Retrieves the value of the first promoted property filter (excluding BTS.ReceivePortName).
        /// Used to determine routing paths in content-based routing scenarios.
        /// </remarks>
        public static string GetRoutingValueFromFilter(BindingSendPort sp)
        {
            return sp.Filters?
                .FirstOrDefault(f => 
                    !string.IsNullOrEmpty(f.Property) &&
                    !f.Property.Equals("BTS.ReceivePortName", StringComparison.OrdinalIgnoreCase) &&
                    f.Operator == "0"
                )?.Value;
        }

        /// <summary>
        /// Parses a BizTalk binding XML file and extracts all receive locations and send ports.
        /// </summary>
        /// <param name="path">Path to the BizTalk binding XML file.</param>
        /// <returns>A fully populated BindingSnapshot containing all binding configuration.</returns>
        /// <exception cref="ArgumentNullException">Thrown when path is null or whitespace.</exception>
        /// <remarks>
        /// Extracts:
        /// - Receive locations with transport metadata (FILE, FTP, HTTP, WCF, HostApps)
        /// - Send ports with transport metadata and filter conditions
        /// - WCF-specific settings (security, encoding, timeouts)
        /// - HostApps metadata (CICS, IMS, VSAM detection)
        /// - Pipeline configurations
        /// </remarks>
        public static BindingSnapshot Parse(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            var snap = new BindingSnapshot();
            var doc = XDocument.Load(path);

            foreach (var rp in doc.Descendants().Where(e => e.Name.LocalName == "ReceivePort"))
            {
                var rpName = rp.Attribute("Name")?.Value;
                foreach (var rl in rp.Descendants().Where(e => e.Name.LocalName == "ReceiveLocation"))
                {
                    var transportType =
                        rl.Descendants().FirstOrDefault(e => e.Name.LocalName == "ReceiveLocationTransportType")?.Attribute("Name")?.Value
                        ?? rl.Attribute("AdapterName")?.Value
                        ?? rl.Attribute("Adapter")?.Value
                        ?? "";

                    var address = rl.Descendants().FirstOrDefault(e => e.Name.LocalName == "Address")?.Value
                                  ?? rl.Attribute("Address")?.Value;

                    bool enabled = (rl.Descendants().FirstOrDefault(e => e.Name.LocalName == "Enable")?.Value ?? "")
                        .Equals("true", StringComparison.OrdinalIgnoreCase);

                    string receivePipelineName =
                        rl.Descendants().FirstOrDefault(e => e.Name.LocalName == "ReceivePipeline")?.Attribute("Name")?.Value
                        ?? rl.Descendants().FirstOrDefault(e => e.Name.LocalName == "ReceivePipelineName")?.Value;

                    string fileMask = null;
                    string folderPath = null;
                    int? pollingSeconds = null;
                    string userName = null;
                    string password = null;
                    string connectionString = null;
                    string primaryTransport = null;
                    string endpoint = null;
                    WcfMetadata wcfMetadata = null;
                    string hostAppsSubType = null;
                    string hostAppsConnectionString = null;

                    var transportDataRaw = rl.Descendants().FirstOrDefault(e => e.Name.LocalName == "ReceiveLocationTransportTypeData")?.Value;
                    if (!string.IsNullOrWhiteSpace(transportDataRaw))
                    {
                        try
                        {
                            var unescaped = System.Net.WebUtility.HtmlDecode(transportDataRaw);
                            var innerDoc = XDocument.Parse(unescaped);
                            var customProps = innerDoc.Descendants().FirstOrDefault(d => d.Name.LocalName == "CustomProps");
                            if (customProps != null)
                            {
                                fileMask = Value(customProps, "FileMask") ?? Value(customProps, "FileMaskWildcard");
                                var pollVal = Value(customProps, "PollingInterval")
                                              ?? Value(customProps, "PollingIntervalInMinutes")
                                              ?? Value(customProps, "PollingSeconds")
                                              ?? Value(customProps, "Interval")
                                              ?? Value(customProps, "SleepTime");

                                if (int.TryParse(pollVal, out var pVal))
                                {
                                    if ((pollVal ?? "").IndexOf("IntervalInMinutes", StringComparison.OrdinalIgnoreCase) >= 0)
                                        pollingSeconds = pVal * 60;
                                    else pollingSeconds = pVal;
                                }

                                folderPath = Value(customProps, "DestinationFolder")
                                             ?? Value(customProps, "Folder")
                                             ?? folderPath;

                                userName = Value(customProps, "UserName") ?? Value(customProps, "Username");
                                password = Value(customProps, "Password");
                                connectionString = Value(customProps, "ConnectionString");
                                primaryTransport = Value(customProps, "PrimaryTransport");
                                endpoint = Value(customProps, "Endpoint") ?? Value(customProps, "Url");
                                
                                // WCF-specific metadata extraction (consolidated)
                                wcfMetadata = WcfMetadata.FromCustomProps(Value, customProps);
                                wcfMetadata.ParseReceiveOnlyProps(Value, customProps);
                                
                                // Extract HostApps subtype (CICS/IMS/VSAM) from AssemblyMappings
                                if (transportType.Equals("HostApps", StringComparison.OrdinalIgnoreCase))
                                {
                                    var assemblyMappings = Value(customProps, "AssemblyMappings");
                                    if (!string.IsNullOrEmpty(assemblyMappings))
                                    {
                                        hostAppsSubType = DetectHostAppsSubType(assemblyMappings);
                                        hostAppsConnectionString = ExtractHostAppsConnectionString(assemblyMappings);
                                    }
                                }
                            }
                        }
                        catch (Exception ex) when (!ex.IsFatal())
                        {
                            Trace.TraceWarning("Could not parse receive location transport data: {0}", ex.Message);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(address))
                    {
                        if (string.IsNullOrWhiteSpace(folderPath)) folderPath = ExtractFolder(address);
                        if (string.IsNullOrWhiteSpace(fileMask)) fileMask = ExtractMask(address);
                    }

                    snap.ReceiveLocations.Add(new BindingReceiveLocation
                    {
                        Name = rl.Attribute("Name")?.Value ?? "ReceiveLocation",
                        ReceivePortName = rpName,
                        TransportType = transportType,
                        Address = address,
                        Enabled = enabled,
                        FileMask = fileMask,
                        FolderPath = folderPath,
                        PollingIntervalSeconds = pollingSeconds,
                        ReceivePipelineName = receivePipelineName,
                        UserName = userName,
                        Password = password,
                        ConnectionString = connectionString,
                        PrimaryTransport = primaryTransport,
                        Endpoint = endpoint,
                        
                        // Consolidated WCF metadata
                        Wcf = wcfMetadata ?? new WcfMetadata(),
                        SecurityMode = wcfMetadata?.SecurityMode,
                        MessageClientCredentialType = wcfMetadata?.MessageClientCredentialType,
                        TransportClientCredentialType = wcfMetadata?.TransportClientCredentialType,
                        MessageEncoding = wcfMetadata?.MessageEncoding,
                        AlgorithmSuite = wcfMetadata?.AlgorithmSuite,
                        MaxReceivedMessageSize = wcfMetadata?.MaxReceivedMessageSize,
                        MaxConcurrentCalls = wcfMetadata?.MaxConcurrentCalls,
                        OpenTimeout = wcfMetadata?.OpenTimeout,
                        CloseTimeout = wcfMetadata?.CloseTimeout,
                        SendTimeout = wcfMetadata?.SendTimeout,
                        EstablishSecurityContext = wcfMetadata?.EstablishSecurityContext,
                        NegotiateServiceCredential = wcfMetadata?.NegotiateServiceCredential,
                        IncludeExceptionDetailInFaults = wcfMetadata?.IncludeExceptionDetailInFaults,
                        UseSSO = wcfMetadata?.UseSSO,
                        SuspendMessageOnFailure = wcfMetadata?.SuspendMessageOnFailure,
                        
                        // HostApps metadata
                        HostAppsSubType = hostAppsSubType,
                        HostAppsConnectionString = hostAppsConnectionString
                    });
                }
            }

            foreach (var sp in doc.Descendants().Where(e => e.Name.LocalName == "SendPort"))
            {
                var transportElem = sp.Descendants().FirstOrDefault(e =>
                    e.Name.LocalName == "TransportType" || e.Name.LocalName == "SendPortTransportType");
                var transportType = transportElem?.Attribute("Name")?.Value
                                    ?? sp.Attribute("AdapterName")?.Value
                                    ?? sp.Attribute("Adapter")?.Value
                                    ?? "";

                var address = sp.Descendants().FirstOrDefault(e => e.Name.LocalName == "Address")?.Value
                              ?? sp.Attribute("Address")?.Value;

                var sendPipelineName =
                    sp.Descendants().FirstOrDefault(e => e.Name.LocalName == "SendPipeline")?.Attribute("Name")?.Value
                    ?? sp.Descendants().FirstOrDefault(e => e.Name.LocalName == "SendPipelineName")?.Value;
                
                // Extract outbound transforms (maps) from <Transforms> element
                var transforms = new List<BindingTransform>();
                var transformsElem = sp.Descendants().FirstOrDefault(e => e.Name.LocalName == "Transforms");
                if (transformsElem != null)
                {
                    foreach (var transformElem in transformsElem.Descendants().Where(e => e.Name.LocalName == "Transform"))
                    {
                        var fullName = transformElem.Attribute("FullName")?.Value;
                        var assemblyQualifiedName = transformElem.Attribute("AssemblyQualifiedName")?.Value;
                        
                        if (!string.IsNullOrEmpty(fullName))
                        {
                            transforms.Add(new BindingTransform
                            {
                                FullName = fullName,
                                AssemblyQualifiedName = assemblyQualifiedName,
                                ShortName = ExtractMapShortName(fullName)
                            });
                        }
                    }
                }
                
                // Extract filter conditions for send port to receive port binding
                var filters = new List<FilterCondition>();
                var filterElem = sp.Descendants().FirstOrDefault(e => e.Name.LocalName == "Filter");
                if (filterElem != null)
                {
                    try
                    {
                        // Filter contains escaped XML with Groups and Statements
                        var filterXml = filterElem.Value;
                        if (!string.IsNullOrWhiteSpace(filterXml))
                        {
                            var unescapedFilter = System.Net.WebUtility.HtmlDecode(filterXml);
                            var filterDoc = XDocument.Parse(unescapedFilter);
                            
                            // Parse filter statements (typically BTS.ReceivePortName)
                            foreach (var statement in filterDoc.Descendants().Where(e => e.Name.LocalName == "Statement"))
                            {
                                var property = statement.Attribute("Property")?.Value;
                                var op = statement.Attribute("Operator")?.Value ?? "0"; // 0 = Equals
                                var value = statement.Attribute("Value")?.Value;
                                
                                if (!string.IsNullOrEmpty(property) && !string.IsNullOrEmpty(value))
                                {
                                    filters.Add(new FilterCondition
                                    {
                                        Property = property,
                                        Operator = op,
                                        Value = value
                                    });
                                }
                            }
                        }
                    }
                    catch (Exception ex) when (!ex.IsFatal())
                    {
                        Trace.TraceWarning("Could not parse send port filter: {0}", ex.Message);
                    }
                }

                string userName = null;
                string password = null;
                string connectionString = null;
                string primaryTransport = null;
                string endpoint = null;
                string securityMode = null;
                WcfMetadata wcfMetadata = null;
                string hostAppsSubType = null;
                string hostAppsConnectionString = null;

                var sendDataRaw = sp.Descendants().FirstOrDefault(e => e.Name.LocalName == "SendPortTransportTypeData")?.Value;
                if (!string.IsNullOrWhiteSpace(sendDataRaw))
                {
                    try
                    {
                        var unescaped = System.Net.WebUtility.HtmlDecode(sendDataRaw);
                        var innerDoc = XDocument.Parse(unescaped);
                        var customProps = innerDoc.Descendants().FirstOrDefault(d => d.Name.LocalName == "CustomProps");
                        if (customProps != null)
                        {
                            userName = Value(customProps, "UserName") ?? Value(customProps, "Username");
                            password = Value(customProps, "Password");
                            connectionString = Value(customProps, "ConnectionString");
                            primaryTransport = Value(customProps, "PrimaryTransport");
                            endpoint = Value(customProps, "Endpoint") ?? Value(customProps, "Url");
                            securityMode = Value(customProps, "SecurityMode");
                            
                            // WCF-specific metadata extraction (consolidated)
                            wcfMetadata = WcfMetadata.FromCustomProps(Value, customProps);
                            
                            // Extract HostApps subtype for send ports
                            if (transportType.Equals("HostApps", StringComparison.OrdinalIgnoreCase))
                            {
                                var assemblyMappings = Value(customProps, "AssemblyMappings");
                                if (!string.IsNullOrEmpty(assemblyMappings))
                                {
                                    hostAppsSubType = DetectHostAppsSubType(assemblyMappings);
                                    hostAppsConnectionString = ExtractHostAppsConnectionString(assemblyMappings);
                                }
                            }
                        }
                    }
                    catch (Exception ex) when (!ex.IsFatal())
                    {
                        Trace.TraceWarning("Could not parse send port transport data: {0}", ex.Message);
                    }
                }

                snap.SendPorts.Add(new BindingSendPort
                {
                    Name = sp.Attribute("Name")?.Value ?? "SendPort",
                    TransportType = transportType,
                    Address = address,
                    SendPipelineName = sendPipelineName,
                    UserName = userName,
                    Password = password,
                    ConnectionString = connectionString,
                    PrimaryTransport = primaryTransport,
                    Endpoint = endpoint,
                    SecurityMode = securityMode ?? wcfMetadata?.SecurityMode,
                    
                    // Consolidated WCF metadata
                    Wcf = wcfMetadata ?? new WcfMetadata { SecurityMode = securityMode },
                    MessageClientCredentialType = wcfMetadata?.MessageClientCredentialType,
                    TransportClientCredentialType = wcfMetadata?.TransportClientCredentialType,
                    MessageEncoding = wcfMetadata?.MessageEncoding,
                    AlgorithmSuite = wcfMetadata?.AlgorithmSuite,
                    MaxReceivedMessageSize = wcfMetadata?.MaxReceivedMessageSize,
                    MaxConcurrentCalls = wcfMetadata?.MaxConcurrentCalls,
                    OpenTimeout = wcfMetadata?.OpenTimeout,
                    CloseTimeout = wcfMetadata?.CloseTimeout,
                    SendTimeout = wcfMetadata?.SendTimeout,
                    EstablishSecurityContext = wcfMetadata?.EstablishSecurityContext,
                    NegotiateServiceCredential = wcfMetadata?.NegotiateServiceCredential,
                    
                    // HostApps metadata for send ports
                    HostAppsSubType = hostAppsSubType,
                    HostAppsConnectionString = hostAppsConnectionString,
                    
                    // Filter conditions for binding to receive ports
                    Filters = filters,
                    
                    // Outbound transforms (maps)
                    Transforms = transforms
                });
            }

            return snap;
        }
        
        /// <summary>
        /// Detects the HostApps subtype (CICS, IMS, or VSAM) from AssemblyMappings XML.
        /// </summary>
        /// <param name="assemblyMappingsXml">The AssemblyMappings XML from TransportTypeData.</param>
        /// <returns>"Cics", "Ims", "Vsam", or "HostFile" if detection successful; null otherwise.</returns>
        /// <remarks>
        /// Parses the DLL filename and connection string to determine the mainframe system type.
        /// Detection priority: DLL name indicators > connection string indicators > default to HostFile.
        /// </remarks>
        private static string DetectHostAppsSubType(string assemblyMappingsXml)
        {
            if (string.IsNullOrWhiteSpace(assemblyMappingsXml)) return null;
            
            try
            {
                // Unescape nested XML (AssemblyMappings contains escaped XML)
                var unescaped = System.Net.WebUtility.HtmlDecode(assemblyMappingsXml);
                var mappingsDoc = XDocument.Parse(unescaped);
                
                // Look for <assembly> element with DLL path
                var assemblyPath = mappingsDoc.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "assembly")?.Value;
                
                if (!string.IsNullOrEmpty(assemblyPath))
                {
                    var lowerPath = assemblyPath.ToLowerInvariant();
                    
                    // Check DLL name for indicators
                    if (lowerPath.Contains("cics")) return "Cics";
                    if (lowerPath.Contains("ims")) return "Ims";
                    if (lowerPath.Contains("vsam")) return "Vsam";
                }
                
                // Fallback: Check connection string for indicators
                var connString = mappingsDoc.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "connectionString")?.Value;
                
                if (!string.IsNullOrEmpty(connString))
                {
                    var lowerConn = connString.ToLowerInvariant();
                    if (lowerConn.Contains("cics")) return "Cics";
                    if (lowerConn.Contains("ims")) return "Ims";
                    if (lowerConn.Contains("vsam")) return "Vsam";
                }
                
                // Default to HostFile if we can't determine
                return "HostFile";
            }
            catch (Exception ex) when (!ex.IsFatal())
            {
                Trace.TraceWarning("Could not detect HostApps subtype: {0}", ex.Message);
                return null;
            }
        }
        
        /// <summary>
        /// Extracts the connection string from HostApps AssemblyMappings XML.
        /// </summary>
        /// <param name="assemblyMappingsXml">The AssemblyMappings XML from TransportTypeData.</param>
        /// <returns>The connection string if found; null otherwise.</returns>
        /// <remarks>
        /// Unescapes the nested XML and locates the connectionString element.
        /// Returns null if parsing fails or element is not found.
        /// </remarks>
        private static string ExtractHostAppsConnectionString(string assemblyMappingsXml)
        {
            if (string.IsNullOrWhiteSpace(assemblyMappingsXml)) return null;
            
            try
            {
                var unescaped = System.Net.WebUtility.HtmlDecode(assemblyMappingsXml);
                var mappingsDoc = XDocument.Parse(unescaped);
                
                return mappingsDoc.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "connectionString")?.Value;
            }
            catch (Exception ex) when (!ex.IsFatal())
            {
                Trace.TraceWarning("Could not extract HostApps connection string: {0}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Retrieves the value of an XML element by local name from CustomProps.
        /// </summary>
        /// <param name="customProps">The CustomProps XML element containing configuration.</param>
        /// <param name="localName">The local name of the element to find.</param>
        /// <returns>The element value if found; null otherwise.</returns>
        private static string Value(XElement customProps, string localName) =>
            customProps.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value;

        /// <summary>
        /// Extracts the folder path from a file address string.
        /// </summary>
        /// <param name="address">The file address (may contain wildcards).</param>
        /// <returns>The folder path portion of the address; null if extraction fails.</returns>
        private static string ExtractFolder(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return null;
            var star = address.IndexOf('*');
            if (star >= 0)
            {
                var lastSep = address.LastIndexOfAny(new[] { '\\', '/' }, star);
                if (lastSep > 0) return address.Substring(0, lastSep);
            }
            try
            {
                var dir = System.IO.Path.GetDirectoryName(address);
                return string.IsNullOrWhiteSpace(dir) ? "/" : dir;
            }
            catch (Exception ex) when (!ex.IsFatal())
            {
                return null;
            }
        }
        /// <summary>
        /// Extracts the file mask pattern from a file address string.
        /// </summary>
        /// <param name="address">The file address containing wildcard pattern.</param>
        /// <returns>The file mask if wildcards are present; null otherwise.</returns>
        private static string ExtractMask(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return null;
            try
            {
                var file = System.IO.Path.GetFileName(address);
                return file != null && file.Contains("*") ? file : null;
            }
            catch (Exception ex) when (!ex.IsFatal())
            {
                return null;
            }
        }
        
        /// <summary>
        /// Extracts the short map name from a fully qualified BizTalk map class name.
        /// Converts "MyNamespace.MyProject.MapName" to "MapName".
        /// </summary>
        /// <param name="fullName">The fully qualified map class name.</param>
        /// <returns>The short map name (last segment), or the original name if no dots.</returns>
        private static string ExtractMapShortName(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName)) return "MapName";
            var parts = fullName.Split('.');
            return parts.Length > 0 ? parts[parts.Length - 1] : fullName;
        }
    }

    /// <summary>
    /// Represents a BizTalk receive location configuration from a binding file.
    /// </summary>
    /// <remarks>
    /// Contains transport-specific metadata for FILE, FTP, HTTP, WCF, HostApps, and other adapters.
    /// Includes WCF security settings, polling intervals, and mainframe connectivity details.
    /// </remarks>
    public sealed class BindingReceiveLocation
    {
        /// <summary>
        /// Gets or sets the receive location name.
        /// </summary>
        public string Name { get; set; }
        
        /// <summary>
        /// Gets or sets the parent receive port name.
        /// </summary>
        public string ReceivePortName { get; set; }
        
        /// <summary>
        /// Gets or sets the transport adapter type (e.g., "FILE", "FTP", "WCF-BasicHttp", "HostApps").
        /// </summary>
        public string TransportType { get; set; }
        
        /// <summary>
        /// Gets or sets the transport address or URI.
        /// </summary>
        public string Address { get; set; }
        
        /// <summary>
        /// Gets or sets whether the receive location is enabled.
        /// </summary>
        public bool Enabled { get; set; }
        
        /// <summary>
        /// Gets or sets the file mask pattern for FILE adapter (e.g., "*.xml").
        /// </summary>
        public string FileMask { get; set; }
        
        /// <summary>
        /// Gets or sets the folder path for FILE adapter.
        /// </summary>
        public string FolderPath { get; set; }
        
        /// <summary>
        /// Gets or sets the polling interval in seconds for FILE/FTP adapters.
        /// </summary>
        public int? PollingIntervalSeconds { get; set; }
        
        /// <summary>
        /// Gets or sets the receive pipeline name.
        /// </summary>
        public string ReceivePipelineName { get; set; }
        
        /// <summary>
        /// Gets or sets the username for authenticated transports.
        /// </summary>
        public string UserName { get; set; }
        
        /// <summary>
        /// Gets or sets the password for authenticated transports.
        /// </summary>
        public string Password { get; set; }
        
        /// <summary>
        /// Gets or sets the connection string for database or enterprise adapters.
        /// </summary>
        public string ConnectionString { get; set; }
        
        /// <summary>
        /// Gets or sets the primary transport identifier.
        /// </summary>
        public string PrimaryTransport { get; set; }
        
        /// <summary>
        /// Gets or sets the endpoint URL for HTTP/SOAP adapters.
        /// </summary>
        public string Endpoint { get; set; }
        
        /// <summary>Consolidated WCF binding metadata parsed from transport data.</summary>
        public WcfMetadata Wcf { get; set; }

        /// <summary>
        /// Gets or sets the WCF security mode (None, Transport, Message, TransportWithMessageCredential).
        /// </summary>
        public string SecurityMode { get; set; }
        
        /// <summary>
        /// Gets or sets the WCF message client credential type.
        /// </summary>
        public string MessageClientCredentialType { get; set; }
        
        /// <summary>
        /// Gets or sets the WCF transport client credential type.
        /// </summary>
        public string TransportClientCredentialType { get; set; }
        
        /// <summary>
        /// Gets or sets the WCF message encoding (Text, Mtom).
        /// </summary>
        public string MessageEncoding { get; set; }
        
        /// <summary>
        /// Gets or sets the WCF algorithm suite for message security.
        /// </summary>
        public string AlgorithmSuite { get; set; }
        
        /// <summary>
        /// Gets or sets the maximum received message size in bytes.
        /// </summary>
        public int? MaxReceivedMessageSize { get; set; }
        
        /// <summary>
        /// Gets or sets the maximum concurrent calls allowed.
        /// </summary>
        public int? MaxConcurrentCalls { get; set; }
        
        /// <summary>
        /// Gets or sets the WCF open timeout duration.
        /// </summary>
        public string OpenTimeout { get; set; }
        
        /// <summary>
        /// Gets or sets the WCF close timeout duration.
        /// </summary>
        public string CloseTimeout { get; set; }
        
        /// <summary>
        /// Gets or sets the WCF send timeout duration.
        /// </summary>
        public string SendTimeout { get; set; }
        
        /// <summary>
        /// Gets or sets whether to establish WCF security context.
        /// </summary>
        public bool? EstablishSecurityContext { get; set; }
        
        /// <summary>
        /// Gets or sets whether to negotiate service credentials in WCF.
        /// </summary>
        public bool? NegotiateServiceCredential { get; set; }
        
        /// <summary>
        /// Gets or sets whether to include exception details in WCF faults.
        /// </summary>
        public bool? IncludeExceptionDetailInFaults { get; set; }
        
        /// <summary>
        /// Gets or sets whether to use Enterprise Single Sign-On (SSO).
        /// </summary>
        public bool? UseSSO { get; set; }
        
        /// <summary>
        /// Gets or sets whether to suspend messages on processing failure.
        /// </summary>
        public bool? SuspendMessageOnFailure { get; set; }
        
        /// <summary>
        /// Gets or sets the HostApps mainframe system type ("Cics", "Ims", "Vsam", "HostFile").
        /// </summary>
        public string HostAppsSubType { get; set; }
        
        /// <summary>
        /// Gets or sets the HostApps mainframe connection string.
        /// </summary>
        public string HostAppsConnectionString { get; set; }
    }

    /// <summary>
    /// Represents a BizTalk send port configuration from a binding file.
    /// </summary>
    /// <remarks>
    /// Contains transport-specific metadata and filter conditions for routing messages.
    /// Includes WCF security settings and mainframe connectivity details for HostApps.
    /// </remarks>
    public sealed class BindingSendPort
    {
        /// <summary>
        /// Gets or sets the send port name.
        /// </summary>
        public string Name { get; set; }
        
        /// <summary>
        /// Gets or sets the transport adapter type (e.g., "FILE", "HTTP", "WCF-BasicHttp").
        /// </summary>
        public string TransportType { get; set; }
        
        /// <summary>
        /// Gets or sets the transport address or URI.
        /// </summary>
        public string Address { get; set; }
        
        /// <summary>
        /// Gets or sets the send pipeline name.
        /// </summary>
        public string SendPipelineName { get; set; }
        
        /// <summary>
        /// Gets or sets the username for authenticated transports.
        /// </summary>
        public string UserName { get; set; }
        
        /// <summary>
        /// Gets or sets the password for authenticated transports.
        /// </summary>
        public string Password { get; set; }
        
        /// <summary>
        /// Gets or sets the connection string for database or enterprise adapters.
        /// </summary>
        public string ConnectionString { get; set; }
        
        /// <summary>
        /// Gets or sets the primary transport identifier.
        /// </summary>
        public string PrimaryTransport { get; set; }
        
        /// <summary>
        /// Gets or sets the endpoint URL for HTTP/SOAP adapters.
        /// </summary>
        public string Endpoint { get; set; }
        
        /// <summary>Consolidated WCF binding metadata parsed from transport data.</summary>
        public WcfMetadata Wcf { get; set; }

        /// <summary>
        /// Gets or sets the WCF security mode (None, Transport, Message, TransportWithMessageCredential).
        /// </summary>
        public string SecurityMode { get; set; }
        
        /// <summary>
        /// Gets or sets the WCF message client credential type.
        /// </summary>
        public string MessageClientCredentialType { get; set; }
        
        /// <summary>
        /// Gets or sets the WCF transport client credential type.
        /// </summary>
        public string TransportClientCredentialType { get; set; }
        
        /// <summary>
        /// Gets or sets the WCF message encoding (Text, Mtom).
        /// </summary>
        public string MessageEncoding { get; set; }
        
        /// <summary>
        /// Gets or sets the WCF algorithm suite for message security.
        /// </summary>
        public string AlgorithmSuite { get; set; }
        
        /// <summary>
        /// Gets or sets the maximum received message size in bytes.
        /// </summary>
        public int? MaxReceivedMessageSize { get; set; }
        
        /// <summary>
        /// Gets or sets the maximum concurrent calls allowed.
        /// </summary>
        public int? MaxConcurrentCalls { get; set; }
        
        /// <summary>
        /// Gets or sets the WCF open timeout duration.
        /// </summary>
        public string OpenTimeout { get; set; }
        
        /// <summary>
        /// Gets or sets the WCF close timeout duration.
        /// </summary>
        public string CloseTimeout { get; set; }
        
        /// <summary>
        /// Gets or sets the WCF send timeout duration.
        /// </summary>
        public string SendTimeout { get; set; }
        
        /// <summary>
        /// Gets or sets whether to establish WCF security context.
        /// </summary>
        public bool? EstablishSecurityContext { get; set; }
        
        /// <summary>
        /// Gets or sets whether to negotiate service credentials in WCF.
        /// </summary>
        public bool? NegotiateServiceCredential { get; set; }
        
        /// <summary>
        /// Gets or sets the HostApps mainframe system type ("Cics", "Ims", "Vsam", "HostFile").
        /// </summary>
        public string HostAppsSubType { get; set; }
        
        /// <summary>
        /// Gets or sets the HostApps mainframe connection string.
        /// </summary>
        public string HostAppsConnectionString { get; set; }
        
        /// <summary>
        /// Gets or sets the filter conditions that determine which messages this send port processes.
        /// </summary>
        public List<FilterCondition> Filters { get; set; } = new List<FilterCondition>();
        
        /// <summary>
        /// Gets or sets the outbound XSLT maps applied by this send port.
        /// </summary>
        /// <remarks>
        /// BizTalk send ports can have 0 or more transforms. These become XSLT actions in Logic Apps.
        /// Maps are applied in sequence before message transmission.
        /// </remarks>
        public List<BindingTransform> Transforms { get; set; } = new List<BindingTransform>();
        
        /// <summary>
        /// Gets the receive port name this send port is filtered to (if any).
        /// </summary>
        /// <returns>The receive port name from the filter; null if no BTS.ReceivePortName filter exists.</returns>
        /// <remarks>
        /// Searches for a filter with property "BTS.ReceivePortName" and operator "0" (Equals).
        /// This is the primary mechanism for bindings-only workflow generation.
        /// </remarks>
        public string GetReceivePortNameFromFilter()
        {
            return Filters?.
                FirstOrDefault(f => f.Property == "BTS.ReceivePortName" && f.Operator == "0")?.
                Value;
        }
    }
}