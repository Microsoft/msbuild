﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.Build.Execution;
using Microsoft.Build.UnitTests;
using Microsoft.Build.Utilities;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Build.Engine.UnitTests.BackEnd
{
    public sealed class TaskHostFactory_Tests
    {
        private ITestOutputHelper _output;

        public TaskHostFactory_Tests(ITestOutputHelper testOutputHelper)
        {
            _output = testOutputHelper;
        }

        [Fact]
        [SkipOnTargetFramework(TargetFrameworkMonikers.Netcoreapp, "https://github.com/microsoft/msbuild/issues/5158")]
        public void TaskNodesDieAfterBuild()
        {
            using (TestEnvironment env = TestEnvironment.Create())
            {
                string pidTaskProject = $@"
<Project>
    <UsingTask TaskName=""ProcessIdTask"" AssemblyName=""Microsoft.Build.Engine.UnitTests"" TaskFactory=""TaskHostFactory"" />
    <Target Name='AccessPID'>
        <ProcessIdTask>
            <Output PropertyName=""PID"" TaskParameter=""Pid"" />
        </ProcessIdTask>
    </Target>
</Project>";
                TransientTestFile project = env.CreateFile("testProject.csproj", pidTaskProject);
                ProjectInstance projectInstance = new ProjectInstance(project.Path);
                projectInstance.Build().ShouldBeTrue();
                string processId = projectInstance.GetPropertyValue("PID");
                string.IsNullOrEmpty(processId).ShouldBeFalse();
                Int32.TryParse(processId, out int pid).ShouldBeTrue();
                Process.GetCurrentProcess().Id.ShouldNotBe<int>(pid);
                for (int a = 0; a < 20; a++)
                {
                    Thread.Sleep(100);
                    _output.WriteLine("Time so far: " + (0.1 * a));
                }
                Should.Throw<ArgumentException>(() => Process.GetProcessById(pid));
            }
        }

    }
}
