// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace Microsoft.Build.Shared.Debugging
{
    internal static class DebugUtils
    {
        private enum NodeMode
        {
            CentralNode,
            OutOfProcNode,
            OutOfProcTaskHostNode
        }

        private static readonly Lazy<NodeMode> ProcessNodeMode = new(
        () =>
        {
            return ScanNodeMode(Environment.CommandLine);

            NodeMode ScanNodeMode(string input)
            {
                var match = Regex.Match(input, @"/nodemode:(?<nodemode>[12\s])(\s|$)", RegexOptions.IgnoreCase);

                if (!match.Success)
                {
                    return NodeMode.CentralNode;
                }
                var nodeMode = match.Groups["nodemode"].Value;

                Trace.Assert(!string.IsNullOrEmpty(nodeMode));

                return nodeMode switch
                {
                    "1" => NodeMode.OutOfProcNode,
                    "2" => NodeMode.OutOfProcTaskHostNode,
                    _ => throw new NotImplementedException(),
                };
            }
        });

        public static string ProcessInfoString =
            $"{ProcessNodeMode.Value}_{Process.GetCurrentProcess().ProcessName}_PID={Process.GetCurrentProcess().Id}_x{(Environment.Is64BitProcess ? "64" : "86")}";

        public static string DebugDumpPath()
        {
            var debugDirectory = Environment.GetEnvironmentVariable("MSBUILDDEBUGPATH") ?? Path.Combine(Directory.GetCurrentDirectory(), "MSBuild_Logs");
            FileUtilities.EnsureDirectoryExists(debugDirectory);

            return debugDirectory;
        }

        public static string FindNextAvailableDebugFilePath(string fileName)
        {
            fileName = Path.Combine(DebugDumpPath(), fileName);

            var counter = 0;
            while (File.Exists(fileName))
            {
                fileName = $"{counter++}_{fileName}";
            }

            return fileName;
        }
    }
}
