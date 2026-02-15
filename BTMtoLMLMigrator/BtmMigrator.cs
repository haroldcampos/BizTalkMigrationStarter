// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace BizTalktoLogicApps.BTMtoLMLMigrator
{
    /// <summary>
    /// Orchestrates the conversion process from BizTalk Server map (BTM) files to Azure Logic Apps Mapping Language (LML) format.
    /// </summary>
    /// <remarks>
    /// This class coordinates the three-phase conversion pipeline: parsing, translation, and generation.
    /// </remarks>
    public class BtmMigrator
    {
        private BtmParser _parser;
        private FunctoidTranslator _functoidTranslator;
        private LmlGenerator _lmlGenerator;

        /// <summary>
        /// Initializes a new instance of the <see cref="BtmMigrator"/> class.
        /// </summary>
        public BtmMigrator()
        {
            _parser = new BtmParser();
            _functoidTranslator = new FunctoidTranslator();
            _lmlGenerator = new LmlGenerator();
        }

        /// <summary>
        /// Converts a BizTalk map file to lml format.
        /// </summary>
        /// <param name="btmFilePath">Path to the input BTM file.</param>
        /// <param name="sourceSchemaPath">Path to the source XSD schema file.</param>
        /// <param name="targetSchemaPath">Path to the target XSD schema file.</param>
        /// <returns>The generated LML content as a string.</returns>
        /// <exception cref="System.IO.FileNotFoundException">Thrown when the BTM or schema files cannot be found.</exception>
        /// <exception cref="System.Xml.XmlException">Thrown when the BTM or schema files contain invalid XML.</exception>
        public string ConvertBtmToLml(string btmFilePath, string sourceSchemaPath, string targetSchemaPath)
        {
            var mapData = _parser.Parse(btmFilePath, sourceSchemaPath, targetSchemaPath);
            var translatedMap = _functoidTranslator.TranslateFunctoids(mapData, sourceSchemaPath, targetSchemaPath);
            translatedMap.BtmFilePath = btmFilePath;
            var lmlContent = _lmlGenerator.GenerateLml(translatedMap);

            return lmlContent;
        }
    }
}
