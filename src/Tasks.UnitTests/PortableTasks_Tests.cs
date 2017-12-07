// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Build.Shared;
using Microsoft.Build.UnitTests.Shared;
using Xunit;

namespace Microsoft.Build.UnitTests
{
    public sealed class PortableTasks_Tests
    {
        private static readonly string ProjectFilePath = Path.GetFullPath(
            Path.Combine(BuildEnvironmentHelper.Instance.CurrentMSBuildToolsDirectory,
                        "..", "..", "Samples", "PortableTask", "netstandard2.0",
                        "portableTaskTest.proj"));

        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/2738")]
        [PlatformSpecific(TestPlatforms.Windows)]
        public static void TestDesktopMSBuildShouldRunPortableTask()
        {
            RunMSBuildOnProjectWithPortableTaskAndAssertOutput(true);
        }

        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/2738")]
        public static void TestNonDesktopMSBuildShouldRunPortableTask()
        {
            RunMSBuildOnProjectWithPortableTaskAndAssertOutput(false);
        }

        private static void RunMSBuildOnProjectWithPortableTaskAndAssertOutput(bool useDesktopMSBuild)
        {
            bool successfulExit;
            string executionOutput;
            
            Assert.True(File.Exists(ProjectFilePath), $"Project file {ProjectFilePath} does not exist");

            executionOutput = useDesktopMSBuild ? 
                RunnerUtilities.RunProcessAndGetOutput("msbuild", ProjectFilePath, out successfulExit, shellExecute: true) : 
                RunnerUtilities.ExecMSBuild(ProjectFilePath, out successfulExit);
            
            Assert.True(successfulExit, $"{(useDesktopMSBuild ? "Desktop MSBuild" : "Non Desktop MSBuild")} failed to execute the portable task");

            var matches = Regex.Matches(executionOutput, @"Microsoft\.Build\.(\w+\.)+dll");

            Assert.True(matches.Count > 1);
        }
    }
}
