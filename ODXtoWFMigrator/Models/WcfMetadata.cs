// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Xml.Linq;

namespace BizTalktoLogicApps.ODXtoWFMigrator
{
    /// <summary>
    /// Encapsulates WCF binding metadata shared across BizTalk receive locations,
    /// send ports, Logic Apps triggers, and Logic Apps actions.
    /// </summary>
    /// <remarks>
    /// Eliminates the duplication of 11–15 WCF properties that were previously
    /// declared independently on <see cref="BindingReceiveLocation"/>,
    /// <see cref="BindingSendPort"/>, <see cref="LogicAppTrigger"/>, and
    /// <see cref="LogicAppAction"/>.
    /// </remarks>
    public sealed class WcfMetadata
    {
        public string SecurityMode { get; set; }
        public string MessageClientCredentialType { get; set; }
        public string TransportClientCredentialType { get; set; }
        public string MessageEncoding { get; set; }
        public string AlgorithmSuite { get; set; }
        public int? MaxReceivedMessageSize { get; set; }
        public int? MaxConcurrentCalls { get; set; }
        public string OpenTimeout { get; set; }
        public string CloseTimeout { get; set; }
        public string SendTimeout { get; set; }
        public bool? EstablishSecurityContext { get; set; }
        public bool? NegotiateServiceCredential { get; set; }

        // Receive-only properties
        public bool? IncludeExceptionDetailInFaults { get; set; }
        public bool? UseSSO { get; set; }
        public bool? SuspendMessageOnFailure { get; set; }

        /// <summary>
        /// Returns <c>true</c> when at least one WCF-specific property has a value.
        /// </summary>
        public bool HasValues =>
            !string.IsNullOrEmpty(SecurityMode) ||
            !string.IsNullOrEmpty(MessageClientCredentialType) ||
            !string.IsNullOrEmpty(TransportClientCredentialType) ||
            !string.IsNullOrEmpty(MessageEncoding) ||
            !string.IsNullOrEmpty(AlgorithmSuite) ||
            MaxReceivedMessageSize.HasValue ||
            MaxConcurrentCalls.HasValue ||
            !string.IsNullOrEmpty(OpenTimeout) ||
            !string.IsNullOrEmpty(CloseTimeout) ||
            !string.IsNullOrEmpty(SendTimeout) ||
            EstablishSecurityContext.HasValue ||
            NegotiateServiceCredential.HasValue ||
            IncludeExceptionDetailInFaults.HasValue ||
            UseSSO.HasValue ||
            SuspendMessageOnFailure.HasValue;

        /// <summary>
        /// Copies all shared WCF properties into a <see cref="LogicAppTrigger"/>.
        /// </summary>
        public void CopyTo(LogicAppTrigger target)
        {
            if (target == null) return;
            target.SecurityMode = SecurityMode;
            target.MessageClientCredentialType = MessageClientCredentialType;
            target.TransportClientCredentialType = TransportClientCredentialType;
            target.MessageEncoding = MessageEncoding;
            target.AlgorithmSuite = AlgorithmSuite;
            target.MaxReceivedMessageSize = MaxReceivedMessageSize;
            target.MaxConcurrentCalls = MaxConcurrentCalls;
            target.OpenTimeout = OpenTimeout;
            target.CloseTimeout = CloseTimeout;
            target.SendTimeout = SendTimeout;
            target.EstablishSecurityContext = EstablishSecurityContext;
            target.NegotiateServiceCredential = NegotiateServiceCredential;
            target.IncludeExceptionDetailInFaults = IncludeExceptionDetailInFaults;
            target.UseSSO = UseSSO;
            target.SuspendMessageOnFailure = SuspendMessageOnFailure;
        }

        /// <summary>
        /// Copies all shared WCF properties into a <see cref="LogicAppAction"/>.
        /// </summary>
        public void CopyTo(LogicAppAction target)
        {
            if (target == null) return;
            target.SecurityMode = SecurityMode;
            target.MessageClientCredentialType = MessageClientCredentialType;
            target.TransportClientCredentialType = TransportClientCredentialType;
            target.MessageEncoding = MessageEncoding;
            target.AlgorithmSuite = AlgorithmSuite;
            target.MaxReceivedMessageSize = MaxReceivedMessageSize;
            target.MaxConcurrentCalls = MaxConcurrentCalls;
            target.OpenTimeout = OpenTimeout;
            target.CloseTimeout = CloseTimeout;
            target.SendTimeout = SendTimeout;
            target.EstablishSecurityContext = EstablishSecurityContext;
            target.NegotiateServiceCredential = NegotiateServiceCredential;
        }

        /// <summary>
        /// Parses WCF-specific properties from BizTalk binding CustomProps XML.
        /// Used by <see cref="BindingSnapshot.Parse"/> for both receive locations and send ports.
        /// </summary>
        /// <param name="customProps">The CustomProps element from the binding transport data.</param>
        /// <param name="valueFunc">A delegate that reads a named value from the CustomProps element.</param>
        /// <returns>A populated <see cref="WcfMetadata"/> instance, or one with no values if nothing was found.</returns>
        public static WcfMetadata FromCustomProps(System.Func<XElement, string, string> valueFunc, XElement customProps)
        {
            var wcf = new WcfMetadata();
            if (customProps == null || valueFunc == null) return wcf;

            wcf.SecurityMode = valueFunc(customProps, "SecurityMode");
            wcf.MessageClientCredentialType = valueFunc(customProps, "MessageClientCredentialType");
            wcf.TransportClientCredentialType = valueFunc(customProps, "TransportClientCredentialType");
            wcf.MessageEncoding = valueFunc(customProps, "MessageEncoding");
            wcf.AlgorithmSuite = valueFunc(customProps, "AlgorithmSuite");
            wcf.OpenTimeout = valueFunc(customProps, "OpenTimeout");
            wcf.CloseTimeout = valueFunc(customProps, "CloseTimeout");
            wcf.SendTimeout = valueFunc(customProps, "SendTimeout");

            var maxMsgSize = valueFunc(customProps, "MaxReceivedMessageSize");
            if (!string.IsNullOrEmpty(maxMsgSize) && int.TryParse(maxMsgSize, out var msgSize))
                wcf.MaxReceivedMessageSize = msgSize;

            var maxCalls = valueFunc(customProps, "MaxConcurrentCalls");
            if (!string.IsNullOrEmpty(maxCalls) && int.TryParse(maxCalls, out var calls))
                wcf.MaxConcurrentCalls = calls;

            var estSecCtx = valueFunc(customProps, "EstablishSecurityContext");
            wcf.EstablishSecurityContext = estSecCtx == "-1" ? true : estSecCtx == "0" ? (bool?)false : null;

            var negCred = valueFunc(customProps, "NegotiateServiceCredential");
            wcf.NegotiateServiceCredential = negCred == "-1" ? true : negCred == "0" ? (bool?)false : null;

            return wcf;
        }

        /// <summary>
        /// Parses additional receive-only WCF properties from BizTalk binding CustomProps XML.
        /// Call after <see cref="FromCustomProps"/> for receive locations only.
        /// </summary>
        public void ParseReceiveOnlyProps(System.Func<XElement, string, string> valueFunc, XElement customProps)
        {
            if (customProps == null || valueFunc == null) return;

            var inclExc = valueFunc(customProps, "IncludeExceptionDetailInFaults");
            IncludeExceptionDetailInFaults = inclExc == "-1" ? true : inclExc == "0" ? (bool?)false : null;

            var sso = valueFunc(customProps, "UseSSO");
            UseSSO = sso == "-1" ? true : sso == "0" ? (bool?)false : null;

            var suspend = valueFunc(customProps, "SuspendMessageOnFailure");
            SuspendMessageOnFailure = suspend == "-1" ? true : suspend == "0" ? (bool?)false : null;
        }
    }
}
