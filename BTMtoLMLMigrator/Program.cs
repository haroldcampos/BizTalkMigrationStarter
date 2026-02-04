// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;

namespace BizTalktoLogicApps.BTMtoLMLMigrator
{
    /// <summary>
    /// Command-line entry point for the BizTalk to Logic Apps BTM to LML migration tool.
    /// </summary>
    internal class Program
    {
        /// <summary>
        /// Application entry point that processes command-line arguments and executes the migration.
        /// </summary>
        /// <param name="args">Command-line arguments containing file paths for BTM file, schemas, and optional output path.</param>
        static void Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: BtmToLmlMigrator <btm-file-path> <source-schema-path> <target-schema-path> [output-lml-file-path]");
                Console.WriteLine();
                Console.WriteLine("Parameters:");
                Console.WriteLine("  btm-file-path       : Path to the input BTM file (required)");
                Console.WriteLine("  source-schema-path  : Path to the source XSD schema file (required)");
                Console.WriteLine("  target-schema-path  : Path to the target XSD schema file (required)");
                Console.WriteLine("  output-lml-file-path: Path to the output LML file (optional, defaults to BTM file path with .lml extension)");
                Console.WriteLine();
                Console.WriteLine("Examples:");
                Console.WriteLine("  BtmToLmlMigrator C:\\Maps\\MyMap.btm C:\\Schemas\\Source.xsd C:\\Schemas\\Target.xsd");
                Console.WriteLine("  BtmToLmlMigrator C:\\Maps\\MyMap.btm C:\\Schemas\\Source.xsd C:\\Schemas\\Target.xsd C:\\Maps\\MyMap.lml");
                return;
            }

            string btmFilePath = args[0];
            string sourceSchemaPath = args[1];
            string targetSchemaPath = args[2];
            string lmlFilePath = args.Length > 3 ? args[3] : Path.ChangeExtension(btmFilePath, ".lml");

            if (!File.Exists(btmFilePath))
            {
                Console.WriteLine($"Error: BTM file not found: {btmFilePath}");
                return;
            }

            if (!File.Exists(sourceSchemaPath))
            {
                Console.WriteLine($"Error: Source schema file not found: {sourceSchemaPath}");
                Console.WriteLine("Both source and target XSD schema files are required for migration.");
                return;
            }

            if (!File.Exists(targetSchemaPath))
            {
                Console.WriteLine($"Error: Target schema file not found: {targetSchemaPath}");
                Console.WriteLine("Both source and target XSD schema files are required for migration.");
                return;
            }

            try
            {
                Console.WriteLine($"Reading BTM file: {btmFilePath}");
                Console.WriteLine($"Using source schema: {sourceSchemaPath}");
                Console.WriteLine($"Using target schema: {targetSchemaPath}");

                var migrator = new BtmMigrator();
                string lmlContent = migrator.ConvertBtmToLml(btmFilePath, sourceSchemaPath, targetSchemaPath);

                Console.WriteLine($"Writing LML file: {lmlFilePath}");
                File.WriteAllText(lmlFilePath, lmlContent);

                Console.WriteLine("Migration completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during migration: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
    }
}
