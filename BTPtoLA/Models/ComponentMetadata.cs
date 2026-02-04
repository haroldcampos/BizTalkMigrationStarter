using System;

namespace BizTalktoLogicApps.BTPtoLA.Models
{
    public enum ComponentType
    {
        General,
        Assembling,
        Disassembling,
        Unknown
    }

    public class ComponentMetadata
    {
        public string Name { get; set; }
        public ComponentType Type { get; set; }
        public bool SupportsProbing { get; set; }
        public string Description { get; set; }
        public string Behavior { get; set; }
        public string MessageFlow { get; set; }

        public static ComponentMetadata GetMetadata(string componentName)
        {
            if (string.IsNullOrEmpty(componentName))
                return CreateUnknown(componentName);

            // Disassembling Components
            if (componentName.Contains("XmlDasmComp") || componentName.Contains("XML disassembler"))
            {
                return new ComponentMetadata
                {
                    Name = "XML Disassembler",
                    Type = ComponentType.Disassembling,
                    SupportsProbing = true,
                    Description = "Combines XML parsing and disassembling. Removes envelopes, disassembles interchanges, promotes properties. ONLY processes body part of multi-part messages. Default: AllowUnrecognizedMessage=False (rejects messages without deployed schemas). Handles XML Info Set elements: preserves comments/CDATA in documents (NOT in envelopes), preserves processing instructions before/in documents, removes XML declarations.",
                    Behavior = "Parses envelope using schemas (static or dynamic by message type). Creates BizTalk message for each document with promoted properties via XPath annotations. Promotes from BODY PART only (non-body parts copied unchanged). CRITICAL: Forces ALL datetime to UTC (correlation issues). AllowUnrecognizedMessage=False: suspends messages without schemas/empty bodies. AllowUnrecognizedMessage=True: passes unrecognized XML unchanged. Non-XML NEVER processed regardless of setting.",
                    MessageFlow = "1 interchange message in → 0-N individual document messages out"
                };
            }

            if (componentName.Contains("FFDasmComp") || componentName.Contains("Flat File Disassembler") || componentName.Contains("Flat file disassembler"))
            {
                return new ComponentMetadata
                {
                    Name = "Flat File Disassembler",
                    Type = ComponentType.Disassembling,
                    SupportsProbing = true,
                    Description = "Converts flat file messages to XML, disassembles into individual documents",
                    Behavior = "Parses flat files according to schema, splits into messages, promotes properties",
                    MessageFlow = "1 message in ? 0-N messages out"
                };
            }

            if (componentName.Contains("BTFDasmComp") || componentName.Contains("BizTalk Framework") && componentName.Contains("disassembler"))
            {
                return new ComponentMetadata
                {
                    Name = "BizTalk Framework Disassembler",
                    Type = ComponentType.Disassembling,
                    SupportsProbing = true,
                    Description = "Processes BizTalk Framework messages, handles reliable messaging",
                    Behavior = "Disassembles BizTalk Framework envelopes, processes reliable messaging headers",
                    MessageFlow = "1 message in ? 0-N messages out"
                };
            }

            // Assembling Components
            if (componentName.Contains("XmlAsmComp") || componentName.Contains("XML assembler"))
            {
                return new ComponentMetadata
                {
                    Name = "XML Assembler",
                    Type = ComponentType.Assembling,
                    SupportsProbing = false,
                    Description = "Converts XML messages to appropriate format, adds envelopes, moves properties from context to body",
                    Behavior = "Serializes message, wraps in envelope, adds headers/trailers, moves context properties to document",
                    MessageFlow = "1 message in ? 1 message out"
                };
            }

            if (componentName.Contains("FFAsmComp") || componentName.Contains("Flat File Assembler") || componentName.Contains("Flat file assembler"))
            {
                return new ComponentMetadata
                {
                    Name = "Flat File Assembler",
                    Type = ComponentType.Assembling,
                    SupportsProbing = false,
                    Description = "Converts XML to flat file format, adds headers and trailers",
                    Behavior = "Serializes XML to flat file according to schema, adds envelope components",
                    MessageFlow = "1 message in ? 1 message out"
                };
            }

            if (componentName.Contains("BTFAsmComp") || componentName.Contains("BizTalk Framework") && componentName.Contains("assembler"))
            {
                return new ComponentMetadata
                {
                    Name = "BizTalk Framework Assembler",
                    Type = ComponentType.Assembling,
                    SupportsProbing = false,
                    Description = "Assembles messages with BizTalk Framework envelope for reliable messaging",
                    Behavior = "Wraps message in BizTalk Framework envelope, adds reliable messaging headers",
                    MessageFlow = "1 message in ? 1 message out"
                };
            }

            // General Components - Decoders/Encoders
            if (componentName.Contains("MIME_SMIME_Decoder") || componentName.Contains("MIME/SMIME decoder"))
            {
                return new ComponentMetadata
                {
                    Name = "MIME/SMIME Decoder",
                    Type = ComponentType.General,
                    SupportsProbing = false,
                    Description = "ONLY out-of-box component that handles multi-part messages. Supports 7bit, 8bit, binary, quoted-printable, UUencode, and base64 decoding. Decrypts and validates signatures using certificates.",
                    Behavior = "Parses multi-part MIME into multi-part BizTalk message. Decrypts using personal certificate store of service account. Validates signatures from Address Book or message. Associates decryption certificate thumbprint with message (subscribing services must use host with that certificate). Identifies BodyPart by: 1) Content-Description='body' 2) Content-Type='text/xml' 3) Content-Type='text/' 4) First part. Other components (XML Disassembler) only process body part.",
                    MessageFlow = "1 multi-part MIME message in → 1 multi-part BizTalk message out (maintains part order)"
                };
            }

            if (componentName.Contains("MIME_SMIME_Encoder") || componentName.Contains("MIME/SMIME encoder"))
            {
                return new ComponentMetadata
                {
                    Name = "MIME/SMIME Encoder",
                    Type = ComponentType.General,
                    SupportsProbing = false,
                    Description = "Encodes messages in MIME/SMIME format with support for 7bit, 8bit, binary, quoted-printable, base64, and UUencode encoding. Can MIME encode, sign, or encrypt outgoing messages.",
                    Behavior = "Creates MIME structure, encrypts content using recipient certificates, adds digital signatures using group signing certificate. If signing certificate not found in group or personal store, or if encryption certificate not found, message is suspended. For request-response encryption, requires custom component to set BTS.EncryptionCert before this component.",
                    MessageFlow = "1 message in → 1 message out (or suspended on certificate error)"
                };
            }

            // General Components - Validators
            if (componentName.Contains("XmlValidator") || componentName.Contains("XML validator"))
            {
                return new ComponentMetadata
                {
                    Name = "XML Validator",
                    Type = ComponentType.General,
                    SupportsProbing = false,
                    Description = "Validates XML messages against specified schemas. Can be used in any stage except Disassemble or Assemble.",
                    Behavior = "Performs XSD validation against configured schemas. If validation fails, raises error and message is suspended.",
                    MessageFlow = "1 message in → 0-1 message out"
                };
            }

            // General Components - Party Resolution
            if (componentName.Contains("PartyRes") || componentName.Contains("Party resolution") || componentName.Contains("Party Resolution"))
            {
                return new ComponentMetadata
                {
                    Name = "Party Resolution",
                    Type = ComponentType.General,
                    SupportsProbing = false,
                    Description = "Maps sender certificate or security identifier (SID) to configured BizTalk Server party. Uses WindowsUser and SignatureCertificate context properties.",
                    Behavior = "Reads WindowsUser and SignatureCertificate from message context. Resolves party by certificate (if enabled) or by SID. Sets SourcePartyID as OriginatorPID if resolved and host is Authentication Trusted. If resolution fails, stamps OriginatorPID as 's-1-5-7' (anonymous user). Resolution order: Certificate first (if By Certificate = True), then SID (if By SID = True).",
                    MessageFlow = "1 message in → 1 message out"
                };
            }

            // General Components - JSON
            if (componentName.Contains("JsonDecoder") || componentName.Contains("JSON decoder"))
            {
                return new ComponentMetadata
                {
                    Name = "JSON Decoder",
                    Type = ComponentType.General,
                    SupportsProbing = false,
                    Description = "Decodes JSON messages",
                    Behavior = "Converts JSON to XML representation",
                    MessageFlow = "1 message in ? 1 message out"
                };
            }

            if (componentName.Contains("JsonEncoder") || componentName.Contains("JSON encoder"))
            {
                return new ComponentMetadata
                {
                    Name = "JSON Encoder",
                    Type = ComponentType.General,
                    SupportsProbing = false,
                    Description = "Encodes messages to JSON format",
                    Behavior = "Converts XML to JSON representation",
                    MessageFlow = "1 message in ? 1 message out"
                };
            }

            return CreateUnknown(componentName);
        }

        private static ComponentMetadata CreateUnknown(string componentName)
        {
            return new ComponentMetadata
            {
                Name = componentName ?? "Unknown",
                Type = ComponentType.Unknown,
                SupportsProbing = false,
                Description = "Unknown component type",
                Behavior = "Behavior not documented",
                MessageFlow = "Unknown"
            };
        }
    }
}
