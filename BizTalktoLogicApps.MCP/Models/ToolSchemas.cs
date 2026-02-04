// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace BizTalktoLogicApps.MCP.Models
{
    public static class ToolSchemas
    {
        public static JObject CreateObjectSchema(Dictionary<string, object> properties, string[] required = null)
        {
            var schema = new JObject
            {
                ["type"] = "object",
                ["properties"] = JObject.FromObject(properties)
            };

            if (required != null && required.Length > 0)
            {
                schema["required"] = JArray.FromObject(required);
            }

            return schema;
        }

        public static Dictionary<string, object> StringProperty(string description)
        {
            return new Dictionary<string, object>
            {
                ["type"] = "string",
                ["description"] = description
            };
        }

        public static Dictionary<string, object> BoolProperty(string description, bool defaultValue = false)
        {
            return new Dictionary<string, object>
            {
                ["type"] = "boolean",
                ["description"] = description,
                ["default"] = defaultValue
            };
        }

        public static Dictionary<string, object> EnumProperty(string description, params string[] values)
        {
            return new Dictionary<string, object>
            {
                ["type"] = "string",
                ["description"] = description,
                ["enum"] = values
            };
        }

        public static Dictionary<string, object> ObjectProperty(string description, Dictionary<string, object> properties = null)
        {
            var prop = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["description"] = description
            };

            if (properties != null)
            {
                prop["properties"] = properties;
            }

            return prop;
        }
    }
}
