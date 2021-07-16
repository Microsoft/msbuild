using Microsoft.Build.UnitTests;
using Microsoft.Build.Utilities;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Build.Tasks.UnitTests
{
    sealed public class GetCompatiblePlatform_Tests
    {
        private readonly ITestOutputHelper _output;

        public GetCompatiblePlatform_Tests(ITestOutputHelper output)
        {
            _output = output;
        }

        /*
         * What tests do we need for the task?
         * Proper Cases:
         * - Based on mapping
         * - Based on same plat
         * - AnyCPU default
         * Failure Cases:
         * - Warn when no compat found
         */

        [Fact]
        public void ResolvesViaPlatformLookupTable_Task()
        {
            // PlatformLookupTable always takes priority. It is typically user-defined.
            TaskItem childProj = new TaskItem("foo.bar");
            childProj.SetMetadata("PlatformOptions", "x64;x86;AnyCPU");

            GetCompatiblePlatform task = new GetCompatiblePlatform()
            {
                BuildEngine = new MockEngine(_output),
                ParentProjectPlatform = "win32",
                PlatformLookupTable = "win32=x64",
                AnnotatedProjects = new TaskItem[] { childProj }
            };

            task.Execute();

            task.AssignedProjectsWithPlatform[0].GetMetadata("NearestPlatform").ShouldBe("x64");
        }

        [Fact]
        public void ResolvesViaAnyCPUDefault_Task()
        {
            // No valid mapping via the lookup table, should default to AnyCPU when possible because
            // it is inherently compatible with any platform.

            TaskItem childProj = new TaskItem("foo.bar");
            childProj.SetMetadata("PlatformOptions", "x86;AnyCPU");

            GetCompatiblePlatform task = new GetCompatiblePlatform()
            {
                BuildEngine = new MockEngine(_output),
                ParentProjectPlatform = "x86",
                PlatformLookupTable = "AnyCPU=x64", 
                AnnotatedProjects = new TaskItem[] { childProj }
            };

            task.Execute();

            task.AssignedProjectsWithPlatform[0].GetMetadata("NearestPlatform").ShouldBe("AnyCPU");
        }

        [Fact]
        public void ResolvesViaSamePlatform_Task()
        {
            // No valid mapping via the lookup table, child project can't default to AnyCPU,
            // child project can match with parent project so match them.
            TaskItem childProj = new TaskItem("foo.bar");
            childProj.SetMetadata("PlatformOptions", "x86;x64");

            GetCompatiblePlatform task = new GetCompatiblePlatform()
            {
                BuildEngine = new MockEngine(_output),
                ParentProjectPlatform = "x86",
                PlatformLookupTable = "AnyCPU=x64",
                AnnotatedProjects = new TaskItem[] { childProj }
            };

            task.Execute();

            task.AssignedProjectsWithPlatform[0].GetMetadata("NearestPlatform").ShouldBe("x86");
        }

        [Fact]
        public void FailsToResolve_Task()
        {
            MockLogger log = new MockLogger(_output);
            // No valid mapping via the lookup table, child project can't default to AnyCPU,
            // child can't match with parent, log a warning.
            TaskItem childProj = new TaskItem("foo.bar");
            childProj.SetMetadata("PlatformOptions", "x64");

            GetCompatiblePlatform task = new GetCompatiblePlatform()
            {
                BuildEngine = new MockEngine(_output),
                ParentProjectPlatform = "x86",
                PlatformLookupTable = "AnyCPU=x64",
                AnnotatedProjects = new TaskItem[] { childProj },
            };
            
            task.Execute();
            // When the task logs a warning, it does not set NearestPlatform
            task.AssignedProjectsWithPlatform[0].GetMetadata("NearestPlatform").ShouldBe("");
        }
    }
}
