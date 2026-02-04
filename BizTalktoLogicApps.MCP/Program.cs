// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BizTalktoLogicApps.MCP.Server;
using System;

namespace BizTalkToLogicApps.MCP
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                var server = new McpServer(Console.In, Console.Out);
                server.Start();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Fatal error: {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);
                Environment.Exit(1);
            }
        }
    }
}
