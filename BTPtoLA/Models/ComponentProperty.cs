using System;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace BizTalktoLogicApps.BTPtoLA.Models
{
    public class ComponentProperty
    {
        [XmlAttribute]
        public string Name { get; set; }

        [XmlElement]
        public PropertyValue Value { get; set; }
    }

    public class PropertyValue : IXmlSerializable
    {
        public string Type { get; set; }
        public string Text { get; set; }

        public XmlSchema GetSchema()
        {
            return null;
        }

        public void ReadXml(XmlReader reader)
        {
            // Read the xsi:type attribute
            Type = reader.GetAttribute("type", "http://www.w3.org/2001/XMLSchema-instance");
            
            // Move into the element
            bool isEmptyElement = reader.IsEmptyElement;
            reader.ReadStartElement();
            
            if (!isEmptyElement)
            {
                // Read the text content if element is not self-closing
                if (reader.NodeType == XmlNodeType.Text)
                {
                    Text = reader.ReadContentAsString();
                }
                else
                {
                    Text = string.Empty;
                }
                reader.ReadEndElement();
            }
            else
            {
                Text = string.Empty;
            }
        }

        public void WriteXml(XmlWriter writer)
        {
            if (!string.IsNullOrEmpty(Type))
            {
                writer.WriteAttributeString("xsi", "type", "http://www.w3.org/2001/XMLSchema-instance", Type);
            }
            if (!string.IsNullOrEmpty(Text))
            {
                writer.WriteString(Text);
            }
        }

        public object GetTypedValue()
        {
            if (string.IsNullOrEmpty(Type) || string.IsNullOrEmpty(Text))
                return Text;

            switch (Type.ToLower())
            {
                case "xsd:boolean":
                    bool boolValue;
                    if (bool.TryParse(Text, out boolValue))
                        return boolValue;
                    break;
                case "xsd:int":
                case "xsd:integer":
                    int intValue;
                    if (int.TryParse(Text, out intValue))
                        return intValue;
                    break;
                case "xsd:string":
                default:
                    return Text;
            }

            return Text;
        }
    }
}
