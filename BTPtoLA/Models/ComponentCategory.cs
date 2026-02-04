using System;
using System.Collections.Generic;

namespace BizTalktoLogicApps.BTPtoLA.Models
{
    public class ComponentCategory
    {
        public string CategoryId { get; set; }
        public string Name { get; set; }
        public List<string> AllowedStages { get; set; }
        public string Description { get; set; }

        public static ComponentCategory GetCategory(string categoryId)
        {
            switch (categoryId?.ToLower())
            {
                case "9d0e4103-4cce-4536-83fa-4a5040674ad6":
                    return new ComponentCategory
                    {
                        CategoryId = categoryId,
                        Name = "CATID_Decoder",
                        AllowedStages = new List<string> { "Decode" },
                        Description = "All decoding components should implement this category. MIME/SMIME Decoder is the ONLY out-of-box component that handles multi-part messages. Sets SignatureCertificate context property for party resolution."
                    };

                case "9d0e4105-4cce-4536-83fa-4a5040674ad6":
                    return new ComponentCategory
                    {
                        CategoryId = categoryId,
                        Name = "CATID_DisassemblingParser",
                        AllowedStages = new List<string> { "Disassemble" },
                        Description = "All disassembling and parsing components should implement this category. XML Disassembler promotes properties via XPath annotations in XSD schemas, forces datetime to UTC, and by default rejects unrecognized messages (AllowUnrecognizedMessage=False)."
                    };

                case "9d0e410d-4cce-4536-83fa-4a5040674ad6":
                    return new ComponentCategory
                    {
                        CategoryId = categoryId,
                        Name = "CATID_Validate",
                        AllowedStages = new List<string> { "Validate" },
                        Description = "Validation components should implement this category. Note: XML Validator can be used in any stage except Disassemble or Assemble."
                    };

                case "9d0e410e-4cce-4536-83fa-4a5040674ad6":
                    return new ComponentCategory
                    {
                        CategoryId = categoryId,
                        Name = "CATID_PartyResolver",
                        AllowedStages = new List<string> { "ResolveParty" },
                        Description = "Party Resolution stage. Maps sender certificate thumbprint or Windows SID to configured BizTalk party. Requires 'WindowsUser' alias or certificate configuration on party."
                    };

                case "9d0e4108-4cce-4536-83fa-4a5040674ad6":
                    return new ComponentCategory
                    {
                        CategoryId = categoryId,
                        Name = "CATID_Encoder",
                        AllowedStages = new List<string> { "Encode" },
                        Description = "All encoding components should implement this category. MIME/SMIME Encoder supports 7bit, 8bit, binary, quoted-printable, base64, and UUencode encoding."
                    };

                case "9d0e4107-4cce-4536-83fa-4a5040674ad6":
                    return new ComponentCategory
                    {
                        CategoryId = categoryId,
                        Name = "CATID_AssemblingSerializer",
                        AllowedStages = new List<string> { "Assemble" },
                        Description = "All serializing and assembling components should implement this category."
                    };

                case "9d0e4101-4cce-4536-83fa-4a5040674ad6":
                    return new ComponentCategory
                    {
                        CategoryId = categoryId,
                        Name = "CATID_Any",
                        AllowedStages = new List<string> { "Pre-Assemble", "Decode", "Disassemble", "Validate", "ResolveParty", "Assemble", "Encode" },
                        Description = "If a pipeline component implements this category, it means that the component can be placed into any stage of a pipeline."
                    };

                default:
                    return new ComponentCategory
                    {
                        CategoryId = categoryId,
                        Name = "Unknown",
                        AllowedStages = new List<string> { "Unknown" },
                        Description = "Unknown component category"
                    };
            }
        }

        public static List<ComponentCategory> GetAllCategories()
        {
            return new List<ComponentCategory>
            {
                GetCategory("9d0e4103-4cce-4536-83fa-4a5040674ad6"),
                GetCategory("9d0e4105-4cce-4536-83fa-4a5040674ad6"),
                GetCategory("9d0e410d-4cce-4536-83fa-4a5040674ad6"),
                GetCategory("9d0e410e-4cce-4536-83fa-4a5040674ad6"),
                GetCategory("9d0e4108-4cce-4536-83fa-4a5040674ad6"),
                GetCategory("9d0e4107-4cce-4536-83fa-4a5040674ad6"),
                GetCategory("9d0e4101-4cce-4536-83fa-4a5040674ad6")
            };
        }
    }
}
