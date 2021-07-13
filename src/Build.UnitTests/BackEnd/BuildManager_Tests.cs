// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;

using Microsoft.Build.BackEnd;
using Microsoft.Build.Collections;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Microsoft.Build.Graph;
using Microsoft.Build.Logging;
using Microsoft.Build.Shared;
using Microsoft.Build.Utilities;
using Shouldly;
using Xunit;
using Xunit.Abstractions;
using static Microsoft.Build.UnitTests.ObjectModelHelpers;

namespace Microsoft.Build.UnitTests.BackEnd
{
    /// <summary>
    /// The test fixture for the BuildManager
    /// </summary>
    public class BuildManager_Tests : IDisposable
    {
        /// <summary>
        /// The mock logger for testing.
        /// </summary>
        private readonly MockLogger _logger;

        /// <summary>
        /// The standard build manager for each test.
        /// </summary>
        private readonly BuildManager _buildManager;

        /// <summary>
        /// The build parameters.
        /// </summary>
        private readonly BuildParameters _parameters;

        /// <summary>
        /// The project collection used.
        /// </summary>
        private readonly ProjectCollection _projectCollection;

        private readonly TestEnvironment _env;
        private readonly ITestOutputHelper _output;

        /// <summary>
        /// The transient state corresponding to setting MSBUILDINPROCENVCHECK to 1.
        /// </summary>
        private readonly TransientTestState _inProcEnvCheckTransientEnvironmentVariable;

        /// <summary>
        /// SetUp
        /// </summary>
        public BuildManager_Tests(ITestOutputHelper output)
        {
            _output = output;
            // Ensure that any previous tests which may have been using the default BuildManager do not conflict with us.
            BuildManager.DefaultBuildManager.Dispose();

            _logger = new MockLogger(output);
            _parameters = new BuildParameters
            {
                ShutdownInProcNodeOnBuildFinish = true,
                Loggers = new ILogger[] { _logger },
                EnableNodeReuse = false
            };
            _buildManager = new BuildManager();
            _projectCollection = new ProjectCollection();

            _env = TestEnvironment.Create(output);
            _inProcEnvCheckTransientEnvironmentVariable = _env.SetEnvironmentVariable("MSBUILDINPROCENVCHECK", "1");
        }

        /// <summary>
        /// TearDown
        /// </summary>
        public void Dispose()
        {
            _buildManager.Dispose();
            _projectCollection.Dispose();
            _env.Dispose();
        }

        /// <summary>
        /// Check that we behave reasonably when passed a null ProjectCollection
        /// </summary>
        [Fact]
        public void BuildParametersWithNullCollection()
        {
            Assert.Throws<ArgumentNullException>(() => { new BuildParameters(null); });
        }

        /// <summary>
        /// A simple successful build.
        /// </summary>
        [Fact]
        public void SimpleBuild()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
<PropertyGroup>
       <InitialProperty1>InitialProperty1</InitialProperty1>
       <InitialProperty2>InitialProperty2</InitialProperty2>
       <InitialProperty3>InitialProperty3</InitialProperty3>
</PropertyGroup>
 <Target Name='test'>
    <Message Text='[success]'/>
 </Target>
</Project>
");
            BuildRequestData data = GetBuildRequestData(contents);
            BuildResult result = _buildManager.Build(_parameters, data);
            Assert.Equal(BuildResultCode.Success, result.OverallResult);
            _logger.AssertLogContains("[success]");
            Assert.Single(_logger.ProjectStartedEvents);

            ProjectStartedEventArgs projectStartedEvent = _logger.ProjectStartedEvents[0];
            Dictionary<string, string> properties = ExtractProjectStartedPropertyList(projectStartedEvent.Properties);

            Assert.True(properties.TryGetValue("InitialProperty1", out string propertyValue));
            Assert.Equal("InitialProperty1", propertyValue);

            Assert.True(properties.TryGetValue("InitialProperty2", out propertyValue));
            Assert.Equal("InitialProperty2", propertyValue);

            Assert.True(properties.TryGetValue("InitialProperty3", out propertyValue));
            Assert.Equal("InitialProperty3", propertyValue);
        }

        [Fact]
        public void SimpleP2PBuildInProc()
        {
            var newParameters = _parameters.Clone();

            newParameters.DisableInProcNode = false;
            newParameters.MaxNodeCount = 1;

            SimpleP2PBuild(newParameters);
        }

        [Fact]
        public void SimpleP2PBuildOutOfProc()
        {
            var newParameters = _parameters.Clone();

            newParameters.DisableInProcNode = true;
            newParameters.MaxNodeCount = 3;

            SimpleP2PBuild(newParameters);
        }

        private void SimpleP2PBuild(BuildParameters buildParameters)
        {
            var graph = Helpers.CreateProjectGraph(
                _env,
                new Dictionary<int, int[]>
                {
                    {1, new[] {2, 3}}
                },
                createProjectFile:
                    (env, projectNumber, references, targets, defaultTargets, content) =>
                    {
                        return Helpers.CreateProjectFile(
                            env,
                            projectNumber,
                            references,
                            targets,
                            "ActualBuild",
                            @"<Target Name='ActualBuild'>
                                            <MSBuild Projects='@(ProjectReference)'/>
                                         </Target>");
                    });

            var result = _buildManager.Build(
                buildParameters,
                new BuildRequestData(
                    graph.GraphRoots.FirstOrDefault()
                        .ProjectInstance.FullPath,
                    new Dictionary<string, string>(),
                    MSBuildConstants.CurrentToolsVersion,
                    new string[0],
                    null));

            result.OverallResult.ShouldBe(BuildResultCode.Success);

            _logger.AllBuildEvents.OfType<ProjectStartedEventArgs>()
                .Count()
                .ShouldBe(3);
            _logger.AllBuildEvents.OfType<ProjectEvaluationFinishedEventArgs>()
                .Count()
                .ShouldBe(3);

            _logger.AllBuildEvents.OfType<ProjectEvaluationStartedEventArgs>()
                .Count()
                .ShouldBe(3);
            _logger.AllBuildEvents.OfType<ProjectEvaluationFinishedEventArgs>()
                .Count()
                .ShouldBe(3);
        }

        [Fact]
        public void GraphBuildOptionsDefaults()
        {
            var options = new GraphBuildOptions();

            options.Build.ShouldBeTrue();
        }

        /// <summary>
        /// A simple successful graph build.
        /// </summary>
        [Fact]
        [ActiveIssue("https://github.com/Microsoft/msbuild/issues/4368")]
        public void SimpleGraphBuild()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
<PropertyGroup>
       <InitialProperty1>InitialProperty1</InitialProperty1>
       <InitialProperty2>InitialProperty2</InitialProperty2>
       <InitialProperty3>InitialProperty3</InitialProperty3>
</PropertyGroup>
 <Target Name='test'>
    <Message Text='[success]'/>
 </Target>
</Project>
");
            GraphBuildRequestData data = GetGraphBuildRequestData(contents);
            GraphBuildResult result = _buildManager.Build(_parameters, data);
            result.OverallResult.ShouldBe(BuildResultCode.Success);
            _logger.AssertLogContains("[success]");
            _logger.ProjectStartedEvents.Count.ShouldBe(1);

            ProjectStartedEventArgs projectStartedEvent = _logger.ProjectStartedEvents[0];
            Dictionary<string, string> properties = ExtractProjectStartedPropertyList(projectStartedEvent.Properties);

            properties.TryGetValue("InitialProperty1", out string propertyValue).ShouldBeTrue();
            propertyValue.ShouldBe("InitialProperty1", StringCompareShould.IgnoreCase);

            properties.TryGetValue("InitialProperty2", out propertyValue).ShouldBeTrue();
            propertyValue.ShouldBe("InitialProperty2", StringCompareShould.IgnoreCase);

            properties.TryGetValue("InitialProperty3", out propertyValue).ShouldBeTrue();
            propertyValue.ShouldBe("InitialProperty3", StringCompareShould.IgnoreCase);
        }

#if FEATURE_CODETASKFACTORY
        /// <summary>
        /// Verify that the environment between two msbuild calls to the same project are stored
        /// so that on the next call we get access to them
        /// </summary>
        [Fact]
        public void VerifyEnvironmentSavedBetweenCalls()
        {
            string contents1 = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <UsingTask TaskName='SetEnvv' TaskFactory='CodeTaskFactory' AssemblyFile='$(MSBuildToolsPath)\Microsoft.Build.Tasks.Core.dll' >
                            <Task>
                                <Code Language='cs'>
                                    System.Environment.SetEnvironmentVariable(""MOO"", ""When the dawn comes, tonight will be a memory too"");
                                </Code>
                           </Task>
</UsingTask>
                        <Target Name='SetEnv'>
                            <SetEnvv/>
                        </Target>
                        <Target Name='Message1'>
                            <Exec Command='echo What does a cat say : " + (NativeMethodsShared.IsWindows ? "%MOO%" : "$MOO") + @"' />
                        </Target>
</Project>
");

            var project = new Project(XmlReader.Create(new StringReader(contents1)), null, null, _projectCollection)
            {
                FullPath = _env.CreateFile(".proj").Path
            };

            project.Save();

            string contents2 = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
        <Target Name='Build' >
         <MSBuild Targets='SetEnv' Projects='" + project.FullPath + "'/>" +
             "<MSBuild Targets='Message1' Projects='" + project.FullPath + "'/>" +
            @"</Target>
</Project>
");

            ProjectInstance instance = CreateProjectInstance(contents2, null, _projectCollection, true);
            BuildRequestData data = new BuildRequestData(instance, new[] { "Build" }, _projectCollection.HostServices);

            BuildResult result = _buildManager.Build(_parameters, data);
            Assert.Equal(BuildResultCode.Success, result.OverallResult);
            _logger.AssertLogContains("What does a cat say : When the dawn comes, tonight will be a memory too");
        }
#endif

        /// <summary>
        /// Verify if idle nodes are shutdown when BuildManager.ShutdownAllNodes is evoked.
        /// The final number of nodes has to be less or equal the number of nodes already in
        /// the system before this method was called.
        /// </summary>
#if RUNTIME_TYPE_NETCORE
        [Theory(Skip = "https://github.com/Microsoft/msbuild/issues/1975")]
#elif MONO
        [Theory(Skip = "https://github.com/Microsoft/msbuild/issues/1240")]
#else
        [Theory(Skip = "https://github.com/Microsoft/msbuild/issues/2057")]
#endif
        [InlineData(8, false)]
        public void ShutdownNodesAfterParallelBuild(int numberOfParallelProjectsToBuild, bool enbaleDebugComm)
        {
            // This test has previously been failing silently. With the addition of TestEnvironment the
            // failure is now noticed (worker node is crashing with "Pipe is broken" exception. See #2057:
            // https://github.com/Microsoft/msbuild/issues/2057
            _env.ClearTestInvariants();

            // Communications debug log enabled, picked up by TestEnvironment
            if (enbaleDebugComm)
            {
                _env.SetEnvironmentVariable("MSBUILDDEBUGCOMM", "1");
            }

            var projectCollection = new ProjectCollection();

            // Get number of MSBuild processes currently instantiated
            int numberProcsOriginally = (new List<Process>(Process.GetProcessesByName("MSBuild"))).Count;
            _output.WriteLine($"numberProcsOriginally = {numberProcsOriginally}");

            // Generate a theoretically unique directory to put our dummy projects in.
            string shutdownProjectDirectory = Path.Combine(Path.GetTempPath(), String.Format(CultureInfo.InvariantCulture, "VSNodeShutdown_{0}_UnitTest", Process.GetCurrentProcess().Id));

            // Create the dummy projects we'll be "building" as our excuse to connect to and shut down
            // all the nodes.
            ProjectInstance rootProject = GenerateDummyProjects(shutdownProjectDirectory, numberOfParallelProjectsToBuild, projectCollection);

            // Build the projects.
            var buildParameters = new BuildParameters(projectCollection)
            {
                OnlyLogCriticalEvents = true,
                MaxNodeCount = numberOfParallelProjectsToBuild,
                EnableNodeReuse = true,
                DisableInProcNode = true,
                SaveOperatingEnvironment = false,
                Loggers = new List<ILogger> {new MockLogger(_output)}
            };

            // Tell the build manager to not disturb process wide state
            var requestData = new BuildRequestData(rootProject, new[] { "Build" }, null);

            // Use a separate BuildManager for the node shutdown build, so that we don't have
            // to worry about taking dependencies on whether or not the existing ones have already
            // disappeared.
            var shutdownManager = new BuildManager("IdleNodeShutdown");
            shutdownManager.Build(buildParameters, requestData);

            // Number of nodes after the build has to be greater than the original number
            int numberProcsAfterBuild = (new List<Process>(Process.GetProcessesByName("MSBuild"))).Count;
            _output.WriteLine($"numberProcsAfterBuild = {numberProcsAfterBuild}");
            Assert.True(numberProcsOriginally < numberProcsAfterBuild, $"Expected '{numberProcsOriginally}' < '{numberProcsAfterBuild}'");

            // Shutdown all nodes
            shutdownManager.ShutdownAllNodes();

            // Wait until all processes shut down
            Thread.Sleep(3000);

            // Number of nodes after the shutdown has to be smaller or equal the original number
            int numberProcsAfterShutdown = (new List<Process>(Process.GetProcessesByName("MSBuild"))).Count;
            _output.WriteLine($"numberProcsAfterShutdown = {numberProcsAfterShutdown}");
            Assert.True(numberProcsAfterShutdown <= numberProcsOriginally);

            // Delete directory with the dummy project
            if (Directory.Exists(shutdownProjectDirectory))
            {
                FileUtilities.DeleteWithoutTrailingBackslash(shutdownProjectDirectory, true /* recursive delete */);
            }
        }

        /// <summary>
        /// A simple successful build, out of process only.
        /// </summary>
#if MONO
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/1240")]
#else
        [Fact]
#endif
        public void SimpleBuildOutOfProcess()
        {
            RunOutOfProcBuild(_ => _env.SetEnvironmentVariable("MSBUILDNOINPROCNODE", "1"));
        }

        /// <summary>
        /// A simple successful build, out of process only. Triggered by setting build parameters' DisableInProcNode to true.
        /// </summary>
#if MONO
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/1240")]
#else
        [Fact]
#endif
        public void DisableInProcNode()
        {
            RunOutOfProcBuild(buildParameters => buildParameters.DisableInProcNode = true);
        }

        /// <summary>
        /// Runs a build and verifies it happens out of proc by checking the process ID.
        /// </summary>
        /// <param name="buildParametersModifier">Runs a test out of proc.</param>
        private void RunOutOfProcBuild(Action<BuildParameters> buildParametersModifier)
        {
            const string contents = @"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
<ItemGroup>
       <InitialProperty Include='InitialProperty2'/>
       <InitialProperty Include='InitialProperty3'/>
</ItemGroup>
 <Target Name='test' Returns='@(InitialProperty)'>
    <ItemGroup>
       <InitialProperty Include='$([System.Diagnostics.Process]::GetCurrentProcess().Id)'/>
    </ItemGroup>
    <Message Text='[success]'/>
 </Target>
</Project>
";

            // Need to set this env variable to enable Process.GetCurrentProcess().Id in the project file.
            _env.SetEnvironmentVariable("MSBUILDENABLEALLPROPERTYFUNCTIONS", "1");

            Project project = CreateProject(CleanupFileContents(contents), MSBuildDefaultToolsVersion, _projectCollection, false);

            var data = new BuildRequestData(project.CreateProjectInstance(), new string[0], _projectCollection.HostServices);
            var customparameters = new BuildParameters { EnableNodeReuse = false, Loggers = new ILogger[] { _logger } };
            buildParametersModifier(customparameters);

            BuildResult result = _buildManager.Build(customparameters, data);
            TargetResult targetresult = result.ResultsByTarget["test"];
            ITaskItem[] item = targetresult.Items;

            Assert.Equal(BuildResultCode.Success, result.OverallResult);
            Assert.Equal(3, item.Length);
            Assert.True(int.TryParse(item[2].ItemSpec, out int processId), $"Process ID passed from the 'test' target is not a valid integer (actual is '{item[2].ItemSpec}')");
            Assert.NotEqual(Process.GetCurrentProcess().Id, processId); // "Build is expected to be out-of-proc. In fact it was in-proc."
        }

#if MONO
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/1240")]
#else
        [Fact]
#endif
        public void RequestedResultsAreSatisfied()
        {
            const string contents = @"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
<PropertyGroup>
  <UnrequestedProperty>IsUnrequested</UnrequestedProperty>
  <RequestedProperty>IsRequested</RequestedProperty>
  <UpdatedProperty>Stale</UpdatedProperty>
</PropertyGroup>
<ItemGroup>
  <AnItem Include='Item1' UnexpectedMetadatum='Unexpected' />
  <AnItem Include='Item2'/>
</ItemGroup>
<Target Name='test' Returns='@(ItemWithMetadata)'>
  <ItemGroup>
    <AnItem Include='$([System.Diagnostics.Process]::GetCurrentProcess().Id)' />
    <ItemWithMetadata Metadatum1='m1' Metadatum2='m2' Include='ItemFromTarget' />
  </ItemGroup>
  <PropertyGroup>
    <NewProperty>FunValue</NewProperty>
    <UpdatedProperty>Updated</UpdatedProperty>
  </PropertyGroup>
  <Message Text='[success]'/>
</Target>

<Target Name='other' Returns='@(ItemWithMetadata)' DependsOnTargets='test' />

</Project>
";

            // Need to set this env variable to enable Process.GetCurrentProcess().Id in the project file.
            _env.SetEnvironmentVariable("MSBUILDENABLEALLPROPERTYFUNCTIONS", "1");

            Project project = CreateProject(CleanupFileContents(contents), MSBuildDefaultToolsVersion,
                _projectCollection, false);

            var requestedProjectState = new RequestedProjectState
            {
                ItemFilters = new Dictionary<string, List<string>>
                {
                    {"AnItem", null},
                    {"ItemWithMetadata", new List<string> {"Metadatum1"}},
                },
                PropertyFilters = new List<string> {"NewProperty", "RequestedProperty"},
            };

            var data = new BuildRequestData(project.CreateProjectInstance(), new [] {"test", "other"},
                _projectCollection.HostServices, BuildRequestDataFlags.ProvideSubsetOfStateAfterBuild, null,
                requestedProjectState);
            var customparameters = new BuildParameters
            {
                EnableNodeReuse = false,
                Loggers = new ILogger[] {_logger},
                DisableInProcNode = true,
            };

            BuildResult result = _buildManager.Build(customparameters, data);

            result.OverallResult.ShouldBe(BuildResultCode.Success);

            result.ProjectStateAfterBuild.ShouldNotBeNull();

            result.ProjectStateAfterBuild.Properties.ShouldNotContain(p => p.Name == "UnrequestedProperty");

            result.ProjectStateAfterBuild.Properties.ShouldContain(p => p.Name == "NewProperty");
            result.ProjectStateAfterBuild.GetPropertyValue("NewProperty").ShouldBe("FunValue");

            result.ProjectStateAfterBuild.Properties.ShouldContain(p => p.Name == "RequestedProperty");
            result.ProjectStateAfterBuild.GetPropertyValue("RequestedProperty").ShouldBe("IsRequested");

            result.ProjectStateAfterBuild.Items.Count.ShouldBe(4);

            result.ProjectStateAfterBuild.GetItems("ItemWithMetadata").ShouldHaveSingleItem();
            result.ProjectStateAfterBuild.GetItems("ItemWithMetadata").First().DirectMetadataCount.ShouldBe(1);
            result.ProjectStateAfterBuild.GetItems("ItemWithMetadata").First().GetMetadataValue("Metadatum1")
                .ShouldBe("m1");
            result.ProjectStateAfterBuild.GetItems("ItemWithMetadata").First().GetMetadataValue("Metadatum2")
                .ShouldBeNullOrEmpty();

            result.ProjectStateAfterBuild.GetItems("AnItem").Count.ShouldBe(3);
            result.ProjectStateAfterBuild.GetItems("AnItem").ShouldContain(p => p.EvaluatedInclude == "Item2");

            result.ProjectStateAfterBuild.GetItemsByItemTypeAndEvaluatedInclude("AnItem", "Item1")
                .ShouldHaveSingleItem();
            result.ProjectStateAfterBuild.GetItemsByItemTypeAndEvaluatedInclude("AnItem", "Item1").First()
                .GetMetadataValue("UnexpectedMetadatum").ShouldBe("Unexpected");
        }

        /// <summary>
        /// Make sure when we are doing an in-process build that even if the environment variable MSBUILDFORWARDPROPERTIESFROMCHILD is set that we still
        /// get all of the initial properties.
        /// </summary>
        [Fact]
        public void InProcForwardPropertiesFromChild()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <PropertyGroup>
       <InitialProperty1>InitialProperty1</InitialProperty1>
       <InitialProperty2>InitialProperty2</InitialProperty2>
       <InitialProperty3>InitialProperty3</InitialProperty3>
</PropertyGroup>
<Target Name='test'>
    <Message Text='[success]'/>
 </Target>
</Project>
");

            _env.SetEnvironmentVariable("MSBuildForwardPropertiesFromChild", "InitialProperty2;IAMNOTREAL");
            BuildRequestData data = GetBuildRequestData(contents);
            BuildResult result = _buildManager.Build(_parameters, data);
            Assert.Equal(BuildResultCode.Success, result.OverallResult);
            _logger.AssertLogContains("[success]");
            Assert.Single(_logger.ProjectStartedEvents);

            ProjectStartedEventArgs projectStartedEvent = _logger.ProjectStartedEvents[0];
            Dictionary<string, string> properties = ExtractProjectStartedPropertyList(projectStartedEvent.Properties);

            Assert.True(properties.TryGetValue("InitialProperty1", out string propertyValue));
            Assert.Equal("InitialProperty1", propertyValue);

            Assert.True(properties.TryGetValue("InitialProperty2", out propertyValue));
            Assert.Equal("InitialProperty2", propertyValue);

            Assert.True(properties.TryGetValue("InitialProperty3", out propertyValue));
            Assert.Equal("InitialProperty3", propertyValue);
        }

        /// <summary>
        /// Make sure when we are doing an in-process build that even if the environment variable MsBuildForwardAllPropertiesFromChild is set that we still
        /// get all of the initial properties.
        /// </summary>
        [Fact]
        public void InProcMsBuildForwardAllPropertiesFromChild()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <PropertyGroup>
       <InitialProperty1>InitialProperty1</InitialProperty1>
       <InitialProperty2>InitialProperty2</InitialProperty2>
       <InitialProperty3>InitialProperty3</InitialProperty3>
</PropertyGroup>
<Target Name='test'>
    <Message Text='[success]'/>
 </Target>
</Project>
");
            _env.SetEnvironmentVariable("MsBuildForwardAllPropertiesFromChild", "InitialProperty2;IAMNOTREAL");

            BuildRequestData data = GetBuildRequestData(contents);
            BuildResult result = _buildManager.Build(_parameters, data);
            Assert.Equal(BuildResultCode.Success, result.OverallResult);
            _logger.AssertLogContains("[success]");
            Assert.Single(_logger.ProjectStartedEvents);

            ProjectStartedEventArgs projectStartedEvent = _logger.ProjectStartedEvents[0];
            Dictionary<string, string> properties = ExtractProjectStartedPropertyList(projectStartedEvent.Properties);

            Assert.True(properties.TryGetValue("InitialProperty1", out string propertyValue));
            Assert.Equal("InitialProperty1", propertyValue);

            Assert.True(properties.TryGetValue("InitialProperty2", out propertyValue));
            Assert.Equal("InitialProperty2", propertyValue);

            Assert.True(properties.TryGetValue("InitialProperty3", out propertyValue));
            Assert.Equal("InitialProperty3", propertyValue);
        }

        /// <summary>
        /// Make sure when we launch a child node and set MsBuildForwardAllPropertiesFromChild that we get all of our properties. This needs to happen
        /// even if the msbuildforwardpropertiesfromchild is set to something.
        /// </summary>
        [Fact]
        public void MsBuildForwardAllPropertiesFromChildLaunchChildNode()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <PropertyGroup>
       <InitialProperty1>InitialProperty1</InitialProperty1>
       <InitialProperty2>InitialProperty2</InitialProperty2>
       <InitialProperty3>InitialProperty3</InitialProperty3>
</PropertyGroup>
<Target Name='test'>
    <Message Text='[success]'/>
 </Target>
</Project>
");

            _env.SetEnvironmentVariable("MsBuildForwardAllPropertiesFromChild", "InitialProperty2;IAMNOTREAL");
            _env.SetEnvironmentVariable("MsBuildForwardPropertiesFromChild", "Something");

            var project = CreateProject(contents, null, _projectCollection, false);
            var data = new BuildRequestData(project.FullPath, new Dictionary<string, string>(), MSBuildDefaultToolsVersion, new string[] { }, null);

            BuildResult result = _buildManager.Build(_parameters, data);
            Assert.Equal(BuildResultCode.Success, result.OverallResult);
            _logger.AssertLogContains("[success]");
            Assert.Single(_logger.ProjectStartedEvents);

            ProjectStartedEventArgs projectStartedEvent = _logger.ProjectStartedEvents[0];
            Dictionary<string, string> properties = ExtractProjectStartedPropertyList(projectStartedEvent.Properties);

            Assert.True(properties.TryGetValue("InitialProperty1", out string propertyValue));
            Assert.Equal("InitialProperty1", propertyValue);

            Assert.True(properties.TryGetValue("InitialProperty2", out propertyValue));
            Assert.Equal("InitialProperty2", propertyValue);

            Assert.True(properties.TryGetValue("InitialProperty3", out propertyValue));
            Assert.Equal("InitialProperty3", propertyValue);
        }

        /// <summary>
        /// Make sure when if the environment variable MsBuildForwardPropertiesFromChild is set to a value and
        /// we launch a child node that we get only that value.
        /// </summary>
#if RUNTIME_TYPE_NETCORE
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/1976")]
#elif MONO
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/1240")]
#else
        [Fact]
#endif
        public void OutOfProcNodeForwardCertainproperties()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <PropertyGroup>
       <InitialProperty1>InitialProperty1</InitialProperty1>
       <InitialProperty2>InitialProperty2</InitialProperty2>
       <InitialProperty3>InitialProperty3</InitialProperty3>
</PropertyGroup>
<Target Name='test'>
    <Message Text='[success]'/>
 </Target>
</Project>
");

            _env.SetEnvironmentVariable("MsBuildForwardPropertiesFromChild", "InitialProperty3;IAMNOTREAL");
            _env.SetEnvironmentVariable("MSBUILDNOINPROCNODE", "1");

            // ProjectEvaluationFinished automatically and always forwards all properties, so we'd
            // end up with all ~136 properties. Since this test is explicitly testing forwarding specific
            // properties on ProjectStarted, turn off the new behavior.
            _env.SetEnvironmentVariable("MSBUILDLOGPROPERTIESANDITEMSAFTEREVALUATION", "0");

            var project = CreateProject(contents, null, _projectCollection, false);
            var data = new BuildRequestData(project.FullPath, new Dictionary<string, string>(),
                MSBuildDefaultToolsVersion, new string[] { }, null);

            BuildResult result = _buildManager.Build(_parameters, data);
            Assert.Equal(BuildResultCode.Success, result.OverallResult);
            _logger.AssertLogContains("[success]");
            Assert.Single(_logger.ProjectStartedEvents);

            ProjectStartedEventArgs projectStartedEvent = _logger.ProjectStartedEvents[0];
            Dictionary<string, string> properties = ExtractProjectStartedPropertyList(projectStartedEvent.Properties);

            Assert.Single(properties);

            Assert.True(properties.TryGetValue("InitialProperty3", out string propertyValue));
            Assert.Equal("InitialProperty3", propertyValue);
        }

        /// <summary>
        /// Make sure when if the environment variable MsBuildForwardPropertiesFromChild is set to a value and
        /// we launch a child node that we get only that value. Also, make sure that when a project is pulled from the results cache
        /// and we have a list of properties to serialize that we do not crash. This is to prevent a regression of 826594
        /// </summary>
#if RUNTIME_TYPE_NETCORE
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/1976")]
#elif MONO
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/1240")]
#else
        [Fact]
#endif
        public void OutOfProcNodeForwardCertainpropertiesAlsoGetResultsFromCache()
        {
            string tempProject = _env.CreateFile(".proj").Path;

            string contents = CleanupFileContents($@"
<Project ToolsVersion='msbuilddefaulttoolsversion' DefaultTargets='Build' xmlns='msbuildnamespace'>
  <Target Name='Build'>
       <MsBuild Projects='{tempProject}' Targets='BuildA'/>
       <MsBuild Projects='{tempProject}' Targets='BuildA'/>
  </Target>
</Project>
");

            string projectContents = CleanupFileContents(@"
<Project ToolsVersion='msbuilddefaulttoolsversion' DefaultTargets='Build' xmlns='msbuildnamespace'>
 <PropertyGroup>
       <InitialProperty1>InitialProperty1</InitialProperty1>
       <InitialProperty2>InitialProperty2</InitialProperty2>
       <InitialProperty3>InitialProperty3</InitialProperty3>
</PropertyGroup>
<Target Name='BuildA'>
       <Message Text='BuildA' Importance='High'/>
       <Message Text='[success]'/>
  </Target>
</Project>
");

            File.WriteAllText(tempProject, projectContents);

            _env.SetEnvironmentVariable("MsBuildForwardPropertiesFromChild", "InitialProperty3;IAMNOTREAL");
            _env.SetEnvironmentVariable("MSBUILDNOINPROCNODE", "1");

            var project = CreateProject(contents, null, _projectCollection, false);
            var data = new BuildRequestData(project.FullPath, new Dictionary<string, string>(),
                MSBuildDefaultToolsVersion, new string[] { }, null);

            BuildResult result = _buildManager.Build(_parameters, data);
            Assert.Equal(BuildResultCode.Success, result.OverallResult);
            _logger.AssertLogContains("[success]");
            Assert.Equal(3, _logger.ProjectStartedEvents.Count);

            ProjectStartedEventArgs projectStartedEvent = _logger.ProjectStartedEvents[1];

            // After conversion to xunit, this test sometimes fails at this assertion.
            // Related to shared state that the test touches that's getting handled
            // differently in xunit?
            Assert.NotNull(projectStartedEvent.Properties);

            Dictionary<string, string> properties = ExtractProjectStartedPropertyList(projectStartedEvent.Properties);

            Assert.NotNull(properties);
            Assert.Single(properties);

            Assert.True(properties.TryGetValue("InitialProperty3", out string propertyValue));
            Assert.Equal("InitialProperty3", propertyValue);

            projectStartedEvent = _logger.ProjectStartedEvents[2];
            Assert.Null(projectStartedEvent.Properties);
        }

        /// <summary>
        /// Make sure when if the environment variable MsBuildForwardPropertiesFromChild is set to empty and
        /// we launch a child node that we get no properties
        /// </summary>
#if MONO
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/1240")]
#else
        [Fact]
#endif
        public void ForwardNoPropertiesLaunchChildNode()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <PropertyGroup>
       <InitialProperty1>InitialProperty1</InitialProperty1>
       <InitialProperty2>InitialProperty2</InitialProperty2>
       <InitialProperty3>InitialProperty3</InitialProperty3>
</PropertyGroup>
<Target Name='test'>
    <Message Text='[success]'/>
 </Target>
</Project>
");

            _env.SetEnvironmentVariable("MsBuildForwardPropertiesFromChild", "");
            _env.SetEnvironmentVariable("MSBUILDNOINPROCNODE", "1");

            var project = CreateProject(contents, null, _projectCollection, false);
            var data = new BuildRequestData(project.FullPath, new Dictionary<string, string>(),
                MSBuildDefaultToolsVersion, new string[] { }, null);
            BuildResult result = _buildManager.Build(_parameters, data);
            Assert.Equal(BuildResultCode.Success, result.OverallResult);

            _logger.AssertLogContains("[success]");
            Assert.Single(_logger.ProjectStartedEvents);

            ProjectStartedEventArgs projectStartedEvent = _logger.ProjectStartedEvents[0];
            Dictionary<string, string> properties = ExtractProjectStartedPropertyList(projectStartedEvent.Properties);
            Assert.Null(properties);
        }

        /// <summary>
        /// We want to pass the toolsets from the parent to the child nodes so that any custom toolsets
        /// defined on the parent are also available on the child nodes for tasks which use the global project
        /// collection
        /// </summary>
#if RUNTIME_TYPE_NETCORE
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/933")]
#elif MONO
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/1240")]
#else
        [Fact]
#endif
        public void VerifyCustomToolSetsPropagated()
        {
            string netFrameworkDirectory = ToolLocationHelper.GetPathToDotNetFrameworkReferenceAssemblies(TargetDotNetFrameworkVersion.Version45);

            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
<UsingTask TaskName='VerifyGlobalProjectCollection' TaskFactory='CodeTaskFactory' AssemblyFile='$(MSBuildToolsPath)\Microsoft.Build.Tasks.Core.dll'>
                        <Task>
                            <Using Namespace='Microsoft.Build.Evaluation'/>
                               <Reference Include='$(MSBuildToolsPath)\Microsoft.Build.dll'/>
<Code Type='Method'>
 <![CDATA[

                                public override bool Execute()
                                {
                                    bool foundToolSet = false;
                                    foreach(Toolset t in ProjectCollection.GlobalProjectCollection.Toolsets)
                                    {
                                        if(t.ToolsVersion.Equals(""CustomToolSet"", StringComparison.OrdinalIgnoreCase))
                                        {
                                            foundToolSet = true;
                                            break;
                                        }
                                     }

                                    Log.LogMessage(MessageImportance.High, ""foundToolset:"" + foundToolSet.ToString());
                                    return foundToolSet;
                                }
  ]]>
                            </Code>
                         </Task>
                         </UsingTask>
                        <Target Name='Build'>
                            <VerifyGlobalProjectCollection/>
                        </Target>
                    </Project>");

            _env.SetEnvironmentVariable("MSBUILDNOINPROCNODE", "1");

            var projectCollection = new ProjectCollection();
            var newToolSet = new Toolset("CustomToolSet", "c:\\SomePath", projectCollection, null);
            projectCollection.AddToolset(newToolSet);

            var project = CreateProject(contents, null, projectCollection, false);
            var data = new BuildRequestData(project.FullPath, new Dictionary<string, string>(),
                MSBuildDefaultToolsVersion, new string[] { }, null);

            var customParameters = new BuildParameters(projectCollection) { Loggers = new ILogger[] { _logger } };
            BuildResult result = _buildManager.Build(customParameters, data);
            Assert.Equal(BuildResultCode.Success, result.OverallResult);
        }

        /// <summary>
        /// When a child node is launched by default we should not send any properties.
        /// we launch a child node that we get no properties
        /// </summary>
#if MONO
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/1240")]
#else
        [Fact]
#endif
        public void ForwardNoPropertiesLaunchChildNodeDefault()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <PropertyGroup>
       <InitialProperty1>InitialProperty1</InitialProperty1>
       <InitialProperty2>InitialProperty2</InitialProperty2>
       <InitialProperty3>InitialProperty3</InitialProperty3>
</PropertyGroup>
<Target Name='test'>
    <Message Text='[success]'/>
 </Target>
</Project>
");
            _env.SetEnvironmentVariable("MsBuildForwardPropertiesFromChild", null);
            _env.SetEnvironmentVariable("MSBUILDNOINPROCNODE", "1");

            var project = CreateProject(contents, null, _projectCollection, false);
            var data = new BuildRequestData(project.FullPath, new Dictionary<string, string>(),
                MSBuildDefaultToolsVersion, new string[] { }, null);
            BuildResult result = _buildManager.Build(_parameters, data);
            Assert.Equal(BuildResultCode.Success, result.OverallResult);
            _logger.AssertLogContains("[success]");
            Assert.Single(_logger.ProjectStartedEvents);

            ProjectStartedEventArgs projectStartedEvent = _logger.ProjectStartedEvents[0];
            Dictionary<string, string> properties = ExtractProjectStartedPropertyList(projectStartedEvent.Properties);
            Assert.Null(properties);
        }

        /// <summary>
        /// A simple failing build.
        /// </summary>
        [Fact]
        public void SimpleBuildWithFailure()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <Target Name='test'>
    <Error Text='[errormessage]'/>
 </Target>
</Project>
");
            BuildRequestData data = GetBuildRequestData(contents);
            BuildResult result = _buildManager.Build(_parameters, data);
            Assert.Equal(BuildResultCode.Failure, result.OverallResult);
            _logger.AssertLogContains("[errormessage]");
        }

        [Fact]
        public void DeferredMessageShouldBeLogged()
        {
            string contents = CleanupFileContents(@"
              <Project>
                 <Target Name='Build'>
                     <Message Text='[Message]' Importance='high'/>
                     <Warning Text='[Warn]'/>
                </Target>
              </Project>
            ");

            MockLogger logger;

            const string highMessage = "deferred[High]";
            const string normalMessage = "deferred[Normal]";
            const string lowMessage = "deferred[Low]";

            using (var buildManagerSession = new Helpers.BuildManagerSession(
                _env,
                deferredMessages: new[]
                {
                    new BuildManager.DeferredBuildMessage(highMessage, MessageImportance.High),
                    new BuildManager.DeferredBuildMessage(normalMessage, MessageImportance.Normal),
                    new BuildManager.DeferredBuildMessage(lowMessage, MessageImportance.Low)
                }))
            {
                var result = buildManagerSession.BuildProjectFile(_env.CreateFile("build.proj", contents).Path);

                result.OverallResult.ShouldBe(BuildResultCode.Success);

                logger = buildManagerSession.Logger;
            }

            logger.AssertLogContains("[Warn]");
            logger.AssertLogContains("[Message]");

            logger.AssertLogContains(highMessage);
            logger.AssertLogContains(normalMessage);
            logger.AssertLogContains(lowMessage);

            var deferredMessages = logger.BuildMessageEvents.Where(e => e.Message.StartsWith("deferred")).ToArray();

            deferredMessages.Length.ShouldBe(3);

            deferredMessages[0].Message.ShouldBe(highMessage);
            deferredMessages[0].Importance.ShouldBe(MessageImportance.High);
            deferredMessages[1].Message.ShouldBe(normalMessage);
            deferredMessages[1].Importance.ShouldBe(MessageImportance.Normal);
            deferredMessages[2].Message.ShouldBe(lowMessage);
            deferredMessages[2].Importance.ShouldBe(MessageImportance.Low);

            logger.BuildStartedEvents.Count.ShouldBe(1);
            logger.BuildFinishedEvents.Count.ShouldBe(1);
            logger.ProjectStartedEvents.Count.ShouldBe(1);
            logger.ProjectFinishedEvents.Count.ShouldBe(1);
            logger.TargetStartedEvents.Count.ShouldBe(1);
            logger.TargetFinishedEvents.Count.ShouldBe(1);
            logger.TaskStartedEvents.Count.ShouldBe(2);
            logger.TaskFinishedEvents.Count.ShouldBe(2);
        }

        /// <summary>
        /// A build with a message, error and warning, verify that
        /// we only get errors, warnings, and project started and finished when OnlyLogCriticalEvents is true
        /// </summary>
        [Fact]
        public void SimpleBuildWithFailureAndWarningOnlyLogCriticalEventsTrue()
        {
            string contents = CleanupFileContents(@"
              <Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
                 <Target Name='test'>
                     <Message Text='[Message]' Importance='high'/>
                     <Warning Text='[warn]'/>
                     <Error Text='[errormessage]'/>
                </Target>
              </Project>
            ");

            BuildRequestData data = GetBuildRequestData(contents);
            _parameters.OnlyLogCriticalEvents = true;
            BuildResult result = _buildManager.Build(_parameters, data);
            Assert.Equal(BuildResultCode.Failure, result.OverallResult);
            _logger.AssertLogContains("[errormessage]");
            _logger.AssertLogContains("[warn]");
            _logger.AssertLogDoesntContain("[message]");
            Assert.Single(_logger.BuildStartedEvents);
            Assert.Single(_logger.BuildFinishedEvents);
            Assert.Single(_logger.ProjectStartedEvents);
            Assert.Single(_logger.ProjectFinishedEvents);
            Assert.Empty(_logger.TargetStartedEvents);
            Assert.Empty(_logger.TargetFinishedEvents);
            Assert.Empty(_logger.TaskStartedEvents);
            Assert.Empty(_logger.TaskFinishedEvents);
        }

        /// <summary>
        /// A build with a message, error and warning, verify that
        /// we only get errors, warnings, messages, task and target messages OnlyLogCriticalEvents is false
        /// </summary>
        [Fact]
        public void SimpleBuildWithFailureAndWarningOnlyLogCriticalEventsFalse()
        {
            string contents = CleanupFileContents(@"
              <Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
                 <Target Name='test'>
                     <Message Text='[message]' Importance='high'/>
                     <Warning Text='[warn]'/>
                     <Error Text='[errormessage]'/>
                </Target>
              </Project>
            ");

            BuildRequestData data = GetBuildRequestData(contents);
            _parameters.OnlyLogCriticalEvents = false;
            BuildResult result = _buildManager.Build(_parameters, data);
            Assert.Equal(BuildResultCode.Failure, result.OverallResult);
            _logger.AssertLogContains("[errormessage]");
            _logger.AssertLogContains("[warn]");
            _logger.AssertLogContains("[message]");
            Assert.Single(_logger.BuildStartedEvents);
            Assert.Single(_logger.BuildFinishedEvents);
            Assert.Single(_logger.ProjectStartedEvents);
            Assert.Single(_logger.ProjectFinishedEvents);
            Assert.Single(_logger.TargetStartedEvents);
            Assert.Single(_logger.TargetFinishedEvents);
            Assert.Equal(3, _logger.TaskStartedEvents.Count);
            Assert.Equal(3, _logger.TaskFinishedEvents.Count);
        }

        /// <summary>
        /// Submitting a synchronous build request before calling BeginBuild yields an InvalidOperationException.
        /// </summary>
        [Fact]
        public void BuildRequestWithoutBegin()
        {
            BuildRequestData data = new BuildRequestData("foo", new Dictionary<string, string>(), "2.0", new string[0], null);
            Should.Throw<InvalidOperationException>(() => _buildManager.BuildRequest(data));
        }

        /// <summary>
        /// Submitting a synchronous graph build request before calling BeginBuild yields an InvalidOperationException.
        /// </summary>
        [Fact]
        public void GraphBuildRequestWithoutBegin()
        {
            GraphBuildRequestData data = new GraphBuildRequestData("foo", new Dictionary<string, string>(), new string[0], null);
            Should.Throw<InvalidOperationException>(() => _buildManager.BuildRequest(data));
        }

        /// <summary>
        /// Pending a build request before calling BeginBuild yields an InvalidOperationException.
        /// </summary>
        [Fact]
        public void PendBuildRequestWithoutBegin()
        {
            BuildRequestData data = new BuildRequestData("foo", new Dictionary<string, string>(), "2.0", new string[0], null);
            Should.Throw<InvalidOperationException>(() => _buildManager.PendBuildRequest(data));
        }

        /// <summary>
        /// Pending a build request before calling BeginBuild yields an InvalidOperationException.
        /// </summary>
        [Fact]
        public void PendGraphBuildRequestWithoutBegin()
        {
            GraphBuildRequestData data = new GraphBuildRequestData("foo", new Dictionary<string, string>(), new string[0], null);
            Should.Throw<InvalidOperationException>(() => _buildManager.PendBuildRequest(data));
        }

        /// <summary>
        /// Calling EndBuild before BeginBuild yields an InvalidOperationException.
        /// </summary>
        [Fact]
        public void EndWithoutBegin()
        {
            Assert.Throws<InvalidOperationException>(() =>
                {
                    _buildManager.EndBuild();
                }
            );
        }

        [Fact]
        public void DisposeAfterUse()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
</Project>
");
            var project = CreateProject(contents, null, _projectCollection, false);
            var globalProperties = new Dictionary<string, string>();
            var targets = new string[0];
            var brd = new BuildRequestData(project.FullPath, globalProperties, null, targets, new HostServices());
            using (var bm = new BuildManager())
            {
                bm.Build(new BuildParameters(), brd);
            }
        }

        [Fact]
        public void DisposeWithoutUse()
        {
            var bm = new BuildManager();
            bm.Dispose();
        }

        /// <summary>
        /// Calling BeginBuild after BeginBuild has already been called yields an InvalidOperationException.
        /// </summary>
        [Fact]
        public void OverlappingBegin()
        {
            try
            {
                _buildManager.BeginBuild(new BuildParameters());
                Assert.Throws<InvalidOperationException>(() => _buildManager.BeginBuild(new BuildParameters()));
            }
            finally
            {
                // Call EndBuild to get us back into a state that approximates reasonable
                _buildManager.EndBuild();
            }
        }

        /// <summary>
        /// Starting and ending a build without submitting any requests is valid.
        /// </summary>
        [Fact]
        public void EmptyBuild()
        {
            _buildManager.BeginBuild(_parameters);
            _buildManager.EndBuild();

            Assert.Equal(0, _logger.ErrorCount);
            Assert.Equal(0, _logger.WarningCount);
        }

        /// <summary>
        /// Calling EndBuild after it has already been called yields an InvalidOperationException.
        /// </summary>
        [Fact]
        public void ExtraEnds()
        {
            _buildManager.BeginBuild(new BuildParameters());
            _buildManager.EndBuild();

            Assert.Throws<InvalidOperationException>(() => _buildManager.EndBuild());
        }

        /// <summary>
        /// Pending a request after EndBuild has been called yields an InvalidOperationException.
        /// </summary>
        [Fact]
        public void PendBuildRequestAfterEnd()
        {
            BuildRequestData data = new BuildRequestData("foo", new Dictionary<string, string>(), "2.0", new string[0], null);
            _buildManager.BeginBuild(new BuildParameters());
            _buildManager.EndBuild();

            Should.Throw<InvalidOperationException>(() => _buildManager.PendBuildRequest(data));
        }

        /// <summary>
        /// Pending a request after EndBuild has been called yields an InvalidOperationException.
        /// </summary>
        [Fact]
        public void PendGraphBuildRequestAfterEnd()
        {
            GraphBuildRequestData data = new GraphBuildRequestData("foo", new Dictionary<string, string>(), new string[0], null);
            _buildManager.BeginBuild(new BuildParameters());
            _buildManager.EndBuild();

            Should.Throw<InvalidOperationException>(() => _buildManager.PendBuildRequest(data));
        }

        /// <summary>
        /// Attempting a synchronous build when a build is in progress yields an InvalidOperationException.
        /// </summary>
        [Fact]
        public void BuildDuringBuild()
        {
            try
            {
                BuildRequestData data =
                    new BuildRequestData("foo", new Dictionary<string, string>(), "2.0", new string[0], null);
                _buildManager.BeginBuild(new BuildParameters());

                Assert.Throws<InvalidOperationException>(() => { _buildManager.Build(new BuildParameters(), data); });
            }
            finally
            {
                // Call EndBuild to get us back into a state that approximates reasonable
                _buildManager.EndBuild();
            }
        }

        /// <summary>
        /// A sequential build.
        /// </summary>
        [Fact]
        public void EndBuildBlocks()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <Target Name='test'>
    <Exec Command='" + Helpers.GetSleepCommand(TimeSpan.FromSeconds(1)) + @"'/>
    <Message Text='[success 1]'/>
 </Target>
</Project>
");

            BuildRequestData data = GetBuildRequestData(contents);
            _buildManager.BeginBuild(_parameters);
            BuildSubmission submission1 = _buildManager.PendBuildRequest(data);
            submission1.ExecuteAsync(null, null);
            Assert.False(submission1.IsCompleted);
            _buildManager.EndBuild();
            Assert.True(submission1.IsCompleted);
            _logger.AssertLogContains("[success 1]");
        }

        /// <summary>
        /// Validate that EndBuild can be called during a submission completion callback.
        /// </summary>
        [Fact]
        public void EndBuildCalledWithinSubmissionCallback()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <Target Name='test'>
    <Message Text='[success 1]'/>
 </Target>
</Project>
");

            BuildRequestData data = GetBuildRequestData(contents);
            _buildManager.BeginBuild(_parameters);
            BuildSubmission submission1 = _buildManager.PendBuildRequest(data);
            var callbackFinished = new AutoResetEvent(false);
            submission1.ExecuteAsync(submission =>
            {
                _buildManager.EndBuild();
                callbackFinished.Set();
            }, null);

            // Wait for the build to finish
            Assert.True(callbackFinished.WaitOne(5000)); // "Build is hung."

            // EndBuild should now have been called, so invoking it again should give us an invalid operation error.
            Assert.Throws<InvalidOperationException>(() => _buildManager.EndBuild());
        }

        /// <summary>
        /// A sequential build.
        /// </summary>
        [Fact]
        public void SequentialBuild()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <Target Name='test'>
    <Message Text='[success 1]'/>
 </Target>
</Project>
");

            string contents2 = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <Target Name='test'>
    <Message Text='[success 2]'/>
 </Target>
</Project>
");

            BuildRequestData data = GetBuildRequestData(contents);
            BuildRequestData data2 = GetBuildRequestData(contents2);
            _buildManager.BeginBuild(_parameters);
            BuildResult result = _buildManager.BuildRequest(data);
            Assert.Equal(BuildResultCode.Success, result.OverallResult);

            BuildResult result2 = _buildManager.BuildRequest(data2);
            Assert.Equal(BuildResultCode.Success, result2.OverallResult);
            _buildManager.EndBuild();

            _logger.AssertLogContains("[success 1]");
            _logger.AssertLogContains("[success 2]");
        }

        /// <summary>
        /// A sequential build.
        /// </summary>
        [Fact]
        public void OverlappingBuildSubmissions()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <Target Name='test'>
    <Exec Command='" + Helpers.GetSleepCommand(TimeSpan.FromMilliseconds(500)) + @"'/>
    <Message Text='[success 1]'/>
 </Target>
</Project>
");

            string contents2 = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <Target Name='test'>
    <Message Text='[success 2]'/>
 </Target>
</Project>
");

            BuildRequestData data = GetBuildRequestData(contents);
            BuildRequestData data2 = GetBuildRequestData(contents2);
            _buildManager.BeginBuild(_parameters);
            BuildSubmission submission1 = _buildManager.PendBuildRequest(data);
            submission1.ExecuteAsync(null, null);
            BuildResult result2 = _buildManager.BuildRequest(data2);
            submission1.WaitHandle.WaitOne();
            BuildResult result = submission1.BuildResult;
            _buildManager.EndBuild();

            Assert.Equal(BuildResultCode.Success, result2.OverallResult);
            Assert.Equal(BuildResultCode.Success, result.OverallResult);

            _logger.AssertLogContains("[success 1]");
            _logger.AssertLogContains("[success 2]");
        }

        /// <summary>
        /// If two overlapping submissions are executed against the same project, with at least one
        /// target involved that skipped, make sure that the second one successfully completes
        /// (retrieved from the cache).
        /// </summary>
        [Fact]
        public void OverlappingIdenticalBuildSubmissions()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <Target Name='test' Condition='false' />
</Project>
");

            BuildRequestData data = GetBuildRequestData(contents);
            var data2 = new BuildRequestData(data.ProjectInstance, data.TargetNames.ToArray(), data.HostServices);

            _buildManager.BeginBuild(_parameters);
            BuildSubmission submission1 = _buildManager.PendBuildRequest(data);
            BuildSubmission submission2 = _buildManager.PendBuildRequest(data2);

            submission2.ExecuteAsync(null, null);
            submission1.ExecuteAsync(null, null);

            submission1.WaitHandle.WaitOne();
            submission2.WaitHandle.WaitOne();

            _buildManager.EndBuild();

            Assert.Equal(BuildResultCode.Success, submission1.BuildResult.OverallResult);
            Assert.Equal(BuildResultCode.Success, submission2.BuildResult.OverallResult);
        }

        /// <summary>
        /// With two overlapping submissions, the first of which skips a target and the second
        /// of which should not, ensure that the second submission does not, in fact, skip
        /// the target.  (E.g. despite the fact that the target results are in the cache already
        /// as 'skipped', ensure that we retry execution in case conditions have changed.)
        /// </summary>
        [Fact]
        public void OverlappingBuildSubmissions_OnlyOneSucceeds()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <Target Name='A' DependsOnTargets='SetProp;MaySkip;UnsetProp' />

 <Target Name='SetProp'>
  <PropertyGroup>
   <ShouldSkip>true</ShouldSkip>
  </PropertyGroup>
 </Target>

 <Target Name='MaySkip' Condition='!$(ShouldSkip)'>
  <Error Text='[ERROR]' />
 </Target>

 <Target Name='UnsetProp'>
  <PropertyGroup>
   <ShouldSkip>false</ShouldSkip>
  </PropertyGroup>
 </Target>

</Project>
");

            BuildRequestData data = GetBuildRequestData(contents, new[] { "A" });
            var data2 = new BuildRequestData(data.ProjectInstance, new[] { "MaySkip" }, data.HostServices);

            _buildManager.BeginBuild(_parameters);
            BuildSubmission submission1 = _buildManager.PendBuildRequest(data);
            BuildSubmission submission2 = _buildManager.PendBuildRequest(data2);

            submission1.ExecuteAsync(null, null);
            submission2.ExecuteAsync(null, null);

            submission1.WaitHandle.WaitOne();
            submission2.WaitHandle.WaitOne();

            _buildManager.EndBuild();

            Assert.Equal(BuildResultCode.Success, submission1.BuildResult.OverallResult);
            Assert.Equal(BuildResultCode.Failure, submission2.BuildResult.OverallResult);
        }

        /// <summary>
        /// Calling EndBuild with an unexecuted submission.
        /// </summary>
        [Fact]
        public void EndWithUnexecutedSubmission()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <Target Name='test'>
    <Exec Command='" + Helpers.GetSleepCommand(TimeSpan.FromSeconds(20)) + @"'/>
    <Message Text='[errormessage]'/>
 </Target>
</Project>
");
            BuildRequestData data = GetBuildRequestData(contents, new string[] { }, MSBuildDefaultToolsVersion);
            _buildManager.BeginBuild(_parameters);
            _buildManager.PendBuildRequest(data);
            _buildManager.EndBuild();
        }

        /// <summary>
        /// A canceled build with a submission which is not executed yet.
        /// </summary>
        [Fact]
        public void CancelledBuildWithUnexecutedSubmission()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <Target Name='test'>
    <Exec Command='" + Helpers.GetSleepCommand(TimeSpan.FromSeconds(20)) + @"'/>
    <Message Text='[errormessage]'/>
 </Target>
</Project>
");
            BuildRequestData data = GetBuildRequestData(contents, new string[] { }, MSBuildDefaultToolsVersion);
            _buildManager.BeginBuild(_parameters);
            _buildManager.PendBuildRequest(data);
            _buildManager.CancelAllSubmissions();
            _buildManager.EndBuild();
        }

        /// <summary>
        /// A canceled build
        /// </summary>
        [Fact(Timeout = 20_000)]
        public void CancelledBuild()
        {
            Console.WriteLine("Starting CancelledBuild test that is known to hang.");
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <Target Name='test'>
    <Exec Command='" + Helpers.GetSleepCommand(TimeSpan.FromSeconds(60)) + @"'/>
    <Message Text='[errormessage]'/>
 </Target>
</Project>
");

            BuildParameters parameters = new ()
            {
                ShutdownInProcNodeOnBuildFinish = true,
                Loggers = new ILogger[] { _logger, new MockLogger(printEventsToStdout: true) },
                EnableNodeReuse = false
            };

            BuildRequestData data = GetBuildRequestData(contents, new string[] { }, MSBuildDefaultToolsVersion);

            Console.WriteLine("CancelledBuild: beginning build");
            _buildManager.BeginBuild(_parameters);
            Console.WriteLine("CancelledBuild: build begun");

            BuildSubmission asyncResult = _buildManager.PendBuildRequest(data);
            Console.WriteLine("CancelledBuild: pend build returned");


            asyncResult.ExecuteAsync(null, null);
            Console.WriteLine("CancelledBuild: ExecuteAsync called");
            _buildManager.CancelAllSubmissions();
            Console.WriteLine("CancelledBuild: submissions cancelled");

            // This test intermittently hangs. This timeout is designed to prevent that, turning a hang into a failure.
            // Todo: Investigate why this test sometimes hangs.
            asyncResult.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)).ShouldBeTrue();
            asyncResult.IsCompleted.ShouldBeTrue("Failing to complete by this point indicates a hang.");
            BuildResult result = asyncResult.BuildResult;
            _buildManager.EndBuild();
            Console.WriteLine("CancelledBuild: build ended");

            Assert.Equal(BuildResultCode.Failure, result.OverallResult); // "Build should have failed."
            _logger.AssertLogDoesntContain("[errormessage]");
        }

        /// <summary>
        /// A canceled build which waits for the task to get started before canceling.  Because it is a 2.0 task, we should
        /// wait until the task finishes normally (cancellation not supported.)
        /// </summary>
        [Fact]
        public void CancelledBuildWithDelay20()
        {
            if (FrameworkLocationHelper.PathToDotNetFrameworkV20 == null) return;

            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='2.0'>
 <Target Name='test'>
    <Exec Command='" + Helpers.GetSleepCommand(TimeSpan.FromSeconds(5)) + @"'/>
    <Message Text='[errormessage]'/>
 </Target>
</Project>
");
            BuildRequestData data = GetBuildRequestData(contents);
            _buildManager.BeginBuild(_parameters);
            BuildSubmission asyncResult = _buildManager.PendBuildRequest(data);
            asyncResult.ExecuteAsync(null, null);

            Thread.Sleep(500);
            _buildManager.CancelAllSubmissions();
            asyncResult.WaitHandle.WaitOne();
            BuildResult result = asyncResult.BuildResult;
            _buildManager.EndBuild();

            Assert.Equal(BuildResultCode.Failure, result.OverallResult); // "Build should have failed."
            _logger.AssertLogDoesntContain("[errormessage]");
        }

#if FEATURE_TASKHOST && !NO_MSBUILDTASKHOST
        // Run this test only if we expect MSBuildTaskHost to have been produced, which requires that MSBuildTaskHost.csproj
        // be built with full-framework MSBuild (so that it can target .NET 3.5).

        /// <summary>
        /// A canceled build which waits for the task to get started before canceling.  Because it is a 2.0 task, we should
        /// wait until the task finishes normally (cancellation not supported.)
        /// </summary>
        [Fact]
        public void CancelledBuildInTaskHostWithDelay20()
        {
            if (FrameworkLocationHelper.PathToDotNetFrameworkV20 == null) return;

            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <UsingTask TaskName='Microsoft.Build.Tasks.Exec' AssemblyName='Microsoft.Build.Tasks.v3.5, Version=3.5.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a' TaskFactory='TaskHostFactory' Runtime='CLR2' />
 <Target Name='test'>
    <Exec Command='" + Helpers.GetSleepCommand(TimeSpan.FromSeconds(10)) + @"'/>
    <Message Text='[errormessage]'/>
 </Target>
</Project>
");
            BuildRequestData data = GetBuildRequestData(contents, new string[] { }, MSBuildDefaultToolsVersion);
            _buildManager.BeginBuild(_parameters);
            BuildSubmission asyncResult = _buildManager.PendBuildRequest(data);
            asyncResult.ExecuteAsync(null, null);

            Thread.Sleep(500);
            _buildManager.CancelAllSubmissions();
            asyncResult.WaitHandle.WaitOne();
            BuildResult result = asyncResult.BuildResult;
            _buildManager.EndBuild();

            Assert.Equal(BuildResultCode.Failure, result.OverallResult); // "Build should have failed."
            _logger.AssertLogDoesntContain("[errormessage]");

            // Task host should not have exited prematurely
            _logger.AssertLogDoesntContain("MSB4217");

            // Task host should have been successfully found and run
            _logger.AssertLogDoesntContain("MSB4216");
        }
#endif

        /// <summary>
        /// A canceled build which waits for the task to get started before canceling.  Because it is a 12.. task, we should
        /// cancel the task and exit out after a short period wherein we wait for the task to exit cleanly.
        /// </summary>
        [Fact]
        public void CancelledBuildWithDelay40()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <Target Name='test'>
    <Exec Command='" + Helpers.GetSleepCommand(TimeSpan.FromSeconds(10)) + @"'/>
    <Message Text='[errormessage]'/>
 </Target>
</Project>
");
            BuildRequestData data = GetBuildRequestData(contents, new string[] { }, MSBuildDefaultToolsVersion);
            _buildManager.BeginBuild(_parameters);
            BuildSubmission asyncResult = _buildManager.PendBuildRequest(data);
            asyncResult.ExecuteAsync(null, null);

            Thread.Sleep(500);
            _buildManager.CancelAllSubmissions();
            asyncResult.WaitHandle.WaitOne();
            BuildResult result = asyncResult.BuildResult;
            _buildManager.EndBuild();

            Assert.Equal(BuildResultCode.Failure, result.OverallResult); // "Build should have failed."
            _logger.AssertLogDoesntContain("[errormessage]");
        }

#if FEATURE_TASKHOST
        /// <summary>
        /// A canceled build which waits for the task to get started before canceling.  Because it is a 12.0 task, we should
        /// cancel the task and exit out after a short period wherein we wait for the task to exit cleanly.
        /// </summary>
        [Fact]
        public void CancelledBuildInTaskHostWithDelay40()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <UsingTask TaskName='Microsoft.Build.Tasks.Exec' AssemblyName='Microsoft.Build.Tasks.Core, Version=msbuildassemblyversion, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a' TaskFactory='TaskHostFactory' />
 <Target Name='test'>
    <Exec Command='" + Helpers.GetSleepCommand(TimeSpan.FromSeconds(10)) + @"'/>
    <Message Text='[errormessage]'/>
 </Target>
</Project>
");
            BuildRequestData data = GetBuildRequestData(contents, new string[] { }, MSBuildDefaultToolsVersion);
            _buildManager.BeginBuild(_parameters);
            BuildSubmission asyncResult = _buildManager.PendBuildRequest(data);
            asyncResult.ExecuteAsync(null, null);

            Thread.Sleep(500);
            _buildManager.CancelAllSubmissions();
            asyncResult.WaitHandle.WaitOne();
            BuildResult result = asyncResult.BuildResult;
            _buildManager.EndBuild();

            Assert.Equal(BuildResultCode.Failure, result.OverallResult); // "Build should have failed."
            _logger.AssertLogDoesntContain("[errormessage]");

            // Task host should not have exited prematurely
            _logger.AssertLogDoesntContain("MSB4217");
        }
#endif

        /// <summary>
        /// This test verifies that builds of the same project instance in sequence are permitted.
        /// </summary>
        [Fact]
        public void SequentialBuildsOfTheSameProjectAllowed()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <Target Name='target1'>
    <Message Text='text'/>
 </Target>
 <Target Name='target2'>
    <Message Text='text'/>
 </Target>
</Project>
");
            Project project = CreateProject(contents, MSBuildDefaultToolsVersion, _projectCollection, true);
            ProjectInstance instance = _buildManager.GetProjectInstanceForBuild(project);
            _buildManager.BeginBuild(_parameters);
            BuildResult result1 = _buildManager.BuildRequest(new BuildRequestData(instance, new[] {"target1"}));
            BuildResult result2 = _buildManager.BuildRequest(new BuildRequestData(instance, new[] {"target2"}));
            _buildManager.EndBuild();

            Assert.Equal(BuildResultCode.Success, result1.OverallResult);
            Assert.True(result1.HasResultsForTarget("target1")); // "Results for target1 missing"
            Assert.Equal(BuildResultCode.Success, result2.OverallResult);
            Assert.True(result2.HasResultsForTarget("target2")); // "Results for target2 missing"
        }

        /// <summary>
        /// This test verifies that overlapping builds of the same project are allowed.
        /// </summary>
        [Fact]
        public void OverlappingBuildsOfTheSameProjectDifferentTargetsAreAllowed()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <Target Name='target1'>
    <Exec Command='" + Helpers.GetSleepCommand(TimeSpan.FromSeconds(3)) + @"'/>
    <Message Text='text'/>
 </Target>
 <Target Name='target2'>
    <Message Text='text'/>
 </Target>
</Project>
");

            Project project = CreateProject(contents, MSBuildDefaultToolsVersion, _projectCollection, true);
            ProjectInstance instance = _buildManager.GetProjectInstanceForBuild(project);
            _buildManager.BeginBuild(_parameters);

            BuildSubmission submission =_buildManager.PendBuildRequest(new BuildRequestData(instance, new[] {"target1"}));
            submission.ExecuteAsync(null, null);
            BuildResult result2 =_buildManager.BuildRequest(new BuildRequestData(project.CreateProjectInstance(), new[] {"target2"}));

            submission.WaitHandle.WaitOne();
            var result1 = submission.BuildResult;

            Assert.Equal(BuildResultCode.Success, result1.OverallResult);
            Assert.True(result1.HasResultsForTarget("target1")); // "Results for target1 missing"
            Assert.Equal(BuildResultCode.Success, result2.OverallResult);
            Assert.True(result2.HasResultsForTarget("target2")); // "Results for target2 missing"
            _buildManager.EndBuild();
        }

        /// <summary>
        /// This test verifies that overlapping builds of the same project are allowed.
        /// </summary>
        [Fact]
        public void OverlappingBuildsOfTheSameProjectSameTargetsAreAllowed()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <Target Name='target1'>
    <Exec Command='" + Helpers.GetSleepCommand(TimeSpan.FromSeconds(3)) + @"'/>
    <Message Text='text'/>
 </Target>
 <Target Name='target2'>
    <Message Text='text'/>
 </Target>
</Project>
");
            Project project = CreateProject(contents, MSBuildDefaultToolsVersion, _projectCollection, true);
            ProjectInstance instance = _buildManager.GetProjectInstanceForBuild(project);
            _buildManager.BeginBuild(_parameters);

            BuildSubmission submission = _buildManager.PendBuildRequest(new BuildRequestData(instance, new[] {"target1"}));
            submission.ExecuteAsync(null, null);
            BuildResult result2 = _buildManager.BuildRequest(new BuildRequestData(project.CreateProjectInstance(), new[] {"target1"}));
            submission.WaitHandle.WaitOne();
            var result1 = submission.BuildResult;

            Assert.Equal(BuildResultCode.Success, result1.OverallResult);
            Assert.True(result1.HasResultsForTarget("target1")); // "Results for target1 missing"
            Assert.Equal(BuildResultCode.Success, result2.OverallResult);
            Assert.True(result2.HasResultsForTarget("target1")); // "Results for target1 (second call) missing"
            _buildManager.EndBuild();
        }

        /// <summary>
        /// This test verifies that the out-of-proc node won't lock the directory containing the target project.
        /// </summary>
#if RUNTIME_TYPE_NETCORE
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/933")]
#else
        [Fact]
#endif
        public void OutOfProcNodeDoesntLockWorkingDirectory()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <Target Name='test'>
    <Message Text='[success]'/>
 </Target>
</Project>
");

            var projectFolder = _env.CreateFolder();
            string projectFile = _env.CreateFile(projectFolder, ".proj").Path;

            File.WriteAllText(projectFile, contents);
            _env.SetEnvironmentVariable("MSBUILDNOINPROCNODE", "1");
            var data = new BuildRequestData(projectFile, new Dictionary<string, string>(), MSBuildDefaultToolsVersion, new string[] { }, null);
            _buildManager.Build(_parameters, data);
        }

        /// <summary>
        /// Retrieving a ProjectInstance from the BuildManager stores it in the cache
        /// </summary>
        [Fact]
        public void ProjectInstanceStoredInCache()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <Target Name='test'>
    <Message Text='text'/>
 </Target>
</Project>
");
            Project project = CreateProject(contents, MSBuildDefaultToolsVersion, _projectCollection, true);
            ProjectInstance instance = _buildManager.GetProjectInstanceForBuild(project);
            ProjectInstance instance2 = _buildManager.GetProjectInstanceForBuild(project);

            Assert.Equal(instance, instance2); // "Instances don't match"
        }

        /// <summary>
        /// Retrieving a ProjectInstance from the BuildManager after a build.
        /// </summary>
        [Fact]
        public void ProjectInstanceRetrievedAfterBuildMatchesSourceProject()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <Target Name='test'>
    <PropertyGroup>
        <Foo>bar</Foo>
    </PropertyGroup>
    <Message Text='[success]'/>
 </Target>
</Project>
");
            IBuildComponentHost host = _buildManager;
            host.GetComponent(BuildComponentType.ConfigCache);
            BuildRequestData data = GetBuildRequestData(contents);
            _buildManager.Build(_parameters, data);

            Project project = _projectCollection.LoadProject(data.ProjectFullPath);
            ProjectInstance instance = _buildManager.GetProjectInstanceForBuild(project);
            Assert.Equal("bar", instance.GetPropertyValue("Foo"));
        }

        /// <summary>
        /// Retrieving a ProjectInstance after resetting the cache clears the instances.
        /// </summary>
        [Fact]
        public void ResetCacheClearsInstances()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <Target Name='test'>
    <PropertyGroup>
        <Foo>bar</Foo>
    </PropertyGroup>
    <Message Text='[success]'/>
 </Target>
</Project>
");
            IBuildComponentHost host = _buildManager;
            host.GetComponent(BuildComponentType.ConfigCache);
            BuildRequestData data = GetBuildRequestData(contents);
            _buildManager.Build(_parameters, data);

            Project project = _projectCollection.LoadProject(data.ProjectFullPath);
            ProjectInstance instance = _buildManager.GetProjectInstanceForBuild(project);
            Assert.Equal("bar", instance.GetPropertyValue("Foo"));

            _buildManager.BeginBuild(_parameters);
            _buildManager.EndBuild();

            instance = _buildManager.GetProjectInstanceForBuild(project);
            Assert.Null(instance.GetProperty("Foo"));
        }

        /// <summary>
        /// Retrieving a ProjectInstance after another build without resetting the cache keeps the existing instance
        /// </summary>
        [Fact]
        public void DisablingCacheResetKeepsInstance()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <Target Name='test'>
    <PropertyGroup>
        <Foo>bar</Foo>
    </PropertyGroup>
    <Message Text='[success]'/>
 </Target>
</Project>
");
            IBuildComponentHost host = _buildManager;
            host.GetComponent(BuildComponentType.ConfigCache);
            BuildRequestData data = GetBuildRequestData(contents);
            _buildManager.Build(_parameters, data);

            Project project = _projectCollection.LoadProject(data.ProjectFullPath);
            ProjectInstance instance = _buildManager.GetProjectInstanceForBuild(project);
            Assert.Equal("bar", instance.GetPropertyValue("Foo"));

            _logger.ClearLog();
            _parameters.ResetCaches = false;
            _buildManager.BeginBuild(_parameters);
            _buildManager.BuildRequest(data);
            _buildManager.EndBuild();

            // We should have built the same instance, with the same results, so the target will be skipped.
            string skippedMessage = ResourceUtilities.FormatResourceStringStripCodeAndKeyword("TargetAlreadyCompleteSuccess", "test");
            Assert.Contains(skippedMessage, _logger.FullLog);

            ProjectInstance instance2 = _buildManager.GetProjectInstanceForBuild(project);
            Assert.Equal(instance, instance2); // "Instances are not the same"
        }

        /// <summary>
        /// Retrieving a ProjectInstance after another build without resetting the cache keeps the existing instance
        /// </summary>
        [Fact]
        public void GhostProjectRootElementCache()
        {
            string p2pProject = _env.CreateFile(".Project2.proj").Path;

            string contents1 = CleanupFileContents($@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <Target Name='test'>
    <Msbuild Projects='{p2pProject}'>
      <Output TaskParameter='TargetOutputs' ItemName='P2pOutput'/>
    </Msbuild>

     <Message Text='Value:@(P2pOutput)' Importance='high'/>
 </Target>
</Project>
");

            string contents2 = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
    <PropertyGroup>
        <Bar Condition=""'$(Bar)' == ''"">Baz</Bar>
    </PropertyGroup>

<Target Name='test' Returns='$(Bar)'/>
</Project>
");
            IBuildComponentHost host = _buildManager;
            host.GetComponent(BuildComponentType.ConfigCache);

            // Create Project 1
            ProjectInstance projectInstance = CreateProjectInstance(contents1, null, _projectCollection, false);
            var data = new BuildRequestData(projectInstance, new string[0]);

            _logger.ClearLog();

            // Write the second project to disk and load it into its own project collection
            var projectCollection2 = new ProjectCollection();
            File.WriteAllText(p2pProject, contents2);

            Project project2 = projectCollection2.LoadProject(p2pProject);

            _parameters.ResetCaches = false;

            // Build the first project to make sure we get the expected default values out for the p2p call.
            _parameters.ProjectRootElementCache = _projectCollection.ProjectRootElementCache;
            _buildManager.BeginBuild(_parameters);
            _buildManager.BuildRequest(data);
            _buildManager.EndBuild();

            _logger.AssertLogContains("Value:Baz");
            _logger.ClearLog();

            // Modify the property in the second project and save it to disk.
            project2.SetProperty("Bar", "FOO");
            project2.Save();

            // Create a new build.
            ProjectInstance projectInstance2 = CreateProjectInstance(contents1, null, _projectCollection, false);
            var data2 = new BuildRequestData(projectInstance2, new string[0]);

            // Build again.
            _parameters.ResetCaches = false;
            _buildManager.BeginBuild(_parameters);
            _buildManager.BuildRequest(data2);
            _buildManager.EndBuild();
            _logger.AssertLogContains("Value:FOO");
        }

        /// <summary>
        /// Verifies that explicitly loaded projects' imports are all marked as also explicitly loaded.
        /// </summary>
        [Fact]
        public void VerifyImportedProjectRootElementsInheritExplicitLoadFlag()
        {
            string contents1 = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <Import Project='{0}' />
 <Target Name='test' />
</Project>
");

            string contents2 = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
    <PropertyGroup>
        <ImportedProperty>ImportedValue</ImportedProperty>
    </PropertyGroup>
</Project>
");

            string importedProjectPath = _env.CreateFile(".proj").Path;
            string rootProjectPath = _env.CreateFile(".proj").Path;

            File.WriteAllText(importedProjectPath, contents2);
            File.WriteAllText(rootProjectPath, String.Format(CultureInfo.InvariantCulture, contents1, importedProjectPath));

            var projectCollection = new ProjectCollection();

            // Run a simple build just to prove that nothing is left in the cache.
            var data = new BuildRequestData(rootProjectPath, ReadOnlyEmptyDictionary<string, string>.Instance, null, new[] { "test" }, null);
            _parameters.ResetCaches = true;
            _parameters.ProjectRootElementCache = projectCollection.ProjectRootElementCache;
            _buildManager.BeginBuild(_parameters);
            _buildManager.BuildRequest(data);
            _buildManager.EndBuild();
            _buildManager.ResetCaches();

            // The semantic of TryOpen is to only retrieve the PRE if it is already in the weak cache.
            Assert.Null(ProjectRootElement.TryOpen(rootProjectPath, projectCollection)); // "The built project shouldn't be in the cache anymore."
            Assert.Null(ProjectRootElement.TryOpen(importedProjectPath, projectCollection)); // "The built project's import shouldn't be in the cache anymore."

            Project project = projectCollection.LoadProject(rootProjectPath);
            ProjectRootElement preRoot, preImported;
            Assert.NotNull(preRoot = ProjectRootElement.TryOpen(rootProjectPath, projectCollection)); // "The root project file should be in the weak cache."
            Assert.NotNull(preImported = ProjectRootElement.TryOpen(importedProjectPath, projectCollection)); // "The imported project file should be in the weak cache."
            Assert.True(preRoot.IsExplicitlyLoaded);
            Assert.True(preImported.IsExplicitlyLoaded);

            // Run a simple build just to prove that it doesn't impact what is in the cache.
            data = new BuildRequestData(rootProjectPath, ReadOnlyEmptyDictionary<string, string>.Instance, null, new[] { "test" }, null);
            _parameters.ResetCaches = true;
            _parameters.ProjectRootElementCache = projectCollection.ProjectRootElementCache;
            _buildManager.BeginBuild(_parameters);
            _buildManager.BuildRequest(data);
            _buildManager.EndBuild();
            _buildManager.ResetCaches();

            // Now make sure they are still in the weak cache.  Since they were loaded explicitly before the build, the build shouldn't have unloaded them from the cache.
            Assert.Same(preRoot, ProjectRootElement.TryOpen(rootProjectPath, projectCollection)); // "The root project file should be in the weak cache after a build."
            Assert.Same(preImported, ProjectRootElement.TryOpen(importedProjectPath, projectCollection)); // "The imported project file should be in the weak cache after a build."
            Assert.True(preRoot.IsExplicitlyLoaded);
            Assert.True(preImported.IsExplicitlyLoaded);

            projectCollection.UnloadProject(project);
            projectCollection.UnloadAllProjects();
            Assert.Null(ProjectRootElement.TryOpen(rootProjectPath, projectCollection)); // "The unloaded project shouldn't be in the cache anymore."
            Assert.Null(ProjectRootElement.TryOpen(importedProjectPath, projectCollection)); // "The unloaded project's import shouldn't be in the cache anymore."
        }

        /// <summary>
        /// Verify that using a second BuildManager doesn't cause the system to crash.
        /// </summary>
        [Fact]
        public void Regress251333()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
<PropertyGroup>
       <InitialProperty1>InitialProperty1</InitialProperty1>
       <InitialProperty2>InitialProperty2</InitialProperty2>
       <InitialProperty3>InitialProperty3</InitialProperty3>
</PropertyGroup>
 <Target Name='test'>
    <Message Text='[success]'/>
 </Target>
</Project>
");

            // First a normal build
            BuildRequestData data = GetBuildRequestData(contents);
            BuildResult result = _buildManager.Build(_parameters, data);
            Assert.Equal(BuildResultCode.Success, result.OverallResult);

            // Now a build using a different build manager.
            using (var newBuildManager = new BuildManager())
            {
                GetBuildRequestData(contents);
                BuildResult result2 = newBuildManager.Build(_parameters, data);
                Assert.Equal(BuildResultCode.Success, result2.OverallResult);
            }
        }

        /// <summary>
        /// Verify that disabling the in-proc node doesn't cause projects which don't require it to fail.
        /// </summary>
#if MONO
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/1240")]
#else
        [Fact]
#endif
        public void Regress239661()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
<PropertyGroup>
       <InitialProperty1>InitialProperty1</InitialProperty1>
       <InitialProperty2>InitialProperty2</InitialProperty2>
       <InitialProperty3>InitialProperty3</InitialProperty3>
</PropertyGroup>
 <Target Name='test'>
    <Message Text='[success]'/>
 </Target>
</Project>
");

            string fileName = _env.CreateFile(".proj").Path;
            File.WriteAllText(fileName, contents);
            var data = new BuildRequestData(fileName, _projectCollection.GlobalProperties, MSBuildDefaultToolsVersion, new string[0], null);
            _parameters.DisableInProcNode = true;
            BuildResult result = _buildManager.Build(_parameters, data);
            Assert.Equal(BuildResultCode.Success, result.OverallResult);
            _logger.AssertLogContains("[success]");
        }

        /// <summary>
        /// Verify that disabling the in-proc node when a project requires it will cause the project to build on the out of proc node.
        /// </summary>
        [Fact]
        public void ExplicitInprocAffinityGetsOverruledByDisableInprocNode()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
<PropertyGroup>
       <InitialProperty1>InitialProperty1</InitialProperty1>
       <InitialProperty2>InitialProperty2</InitialProperty2>
       <InitialProperty3>InitialProperty3</InitialProperty3>
</PropertyGroup>
 <Target Name='test'>
    <Message Text='[success]'/>
 </Target>
</Project>
");
            BuildRequestData data = GetBuildRequestData(contents);
            _env.CreateFile(data.ProjectFullPath, data.ProjectInstance.ToProjectRootElement().RawXml);
            _parameters.DisableInProcNode = true;

            // Require that this project build on the in-proc node, which will not be available.
            data.HostServices.SetNodeAffinity(data.ProjectFullPath, NodeAffinity.InProc);
            BuildResult result = _buildManager.Build(_parameters, data);
            Assert.Equal(BuildResultCode.Success, result.OverallResult);
            _logger.AssertLogContains("[success]");
            _logger.AssertLogDoesntContain("MSB4223");
        }

        /// <summary>
        /// Ensures that properties and items are transferred to the out-of-proc node when an instance is used to start the build.
        /// </summary>
#if MONO
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/1240")]
#else
        [Fact]
#endif
        public void ProjectInstanceTransfersToOOPNode()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <PropertyGroup>
   <DeleteMe>deleteme</DeleteMe>
   <Unmodified>unmodified</Unmodified>
   <VirtualProp>original</VirtualProp>
 </PropertyGroup>
<ItemGroup>
  <Foo Include='foo'/>
  <Foo2 Include='foo2'/>
</ItemGroup>
 <Target Name='test'>
   <Message Text='[$(DeleteMe)]'/>
   <Message Text='[$(Unmodified)]'/>
   <Message Text='[$(VirtualProp)]'/>
   <Message Text='[$(NewProp)]'/>
   <Message Text='[@(Foo)]'/>
   <Message Text='[@(Foo2)]'/>
   <Message Text='[@(Baz)]'/>
 </Target>
</Project>
");

            string fileName = _env.CreateFile(".proj").Path;
            File.WriteAllText(fileName, contents);
            var project = new Project(fileName);
            ProjectInstance instance = project.CreateProjectInstance();
            instance.RemoveProperty("DeleteMe");
            instance.SetProperty("VirtualProp", "overridden");
            instance.SetProperty("NewProp", "new");
            instance.AddItem("Baz", "baz");
            instance.AddItem("Foo2", "foo21");
            foreach (var item in instance.Items)
            {
                if (item.EvaluatedInclude == "foo")
                {
                    instance.RemoveItem(item);
                    break;
                }
            }

            var data = new BuildRequestData(instance, new string[0]);

            // Force this to build out-of-proc
            _parameters.DisableInProcNode = true;
            _buildManager.Build(_parameters, data);
            _logger.AssertLogDoesntContain("[deleteme]");
            _logger.AssertLogContains("[overridden]");
            _logger.AssertLogContains("[unmodified]");
            _logger.AssertLogContains("[new]");
            _logger.AssertLogDoesntContain("[foo]");
            _logger.AssertLogContains("[foo2;foo21]");
            _logger.AssertLogContains("[baz]");
        }

        /// <summary>
        /// Ensures that a limited set of properties are transferred from a project instance to an OOP node.
        /// </summary>
#if MONO
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/1240")]
#else
        [Fact]
#endif
        public void ProjectInstanceLimitedTransferToOOPNode()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <PropertyGroup>
   <Unmodified>unmodified</Unmodified>
   <VirtualProp>original</VirtualProp>
 </PropertyGroup>
 <Target Name='test'>
   <Message Text='[$(Unmodified)]'/>
   <Message Text='[$(VirtualProp)]'/>
 </Target>
</Project>
");

            string fileName = _env.CreateFile(".proj").Path;
            File.WriteAllText(fileName, contents);
            Project project = new Project(fileName);
            ProjectInstance instance = project.CreateProjectInstance();
            instance.SetProperty("VirtualProp", "overridden");
            instance.SetProperty("Unmodified", "changed");

            var data = new BuildRequestData(instance, new string[0], null, BuildRequestDataFlags.None, new string[] { "VirtualProp" });

            // Force this to build out-of-proc
            _parameters.DisableInProcNode = true;
            _buildManager.Build(_parameters, data);
            _logger.AssertLogContains("[overridden]");
            _logger.AssertLogContains("[unmodified]");
            _logger.AssertLogDoesntContain("[changed]");
        }

        /// <summary>
        /// Tests that cache files are created as expected and their lifetime is controlled appropriately.
        /// </summary>
        [Fact]
        [Trait("Category", "netcore-osx-failing")]
        [Trait("Category", "netcore-linux-failing")]
        [Trait("Category", "mono-osx-failing")]
        public void CacheLifetime()
        {
            FileUtilities.ClearCacheDirectory();

            _env.SetEnvironmentVariable("MSBUILDDEBUGFORCECACHING", "1");
            string outerBuildCacheDirectory;

            // Do a build with one build manager.
            using (var outerBuildManager = new BuildManager())
            {
                outerBuildCacheDirectory = BuildAndCheckCache(outerBuildManager, new string[] { });

                // Do another build with a second build manager while the first still exists.  Since both BuildManagers
                // share a process-wide cache directory, we want to verify that they don't stomp on each other, either
                // by accidentally sharing results, or by clearing them away.
                string innerBuildCacheDirectory;
                using (var innerBuildManager = new BuildManager())
                {
                    innerBuildCacheDirectory = BuildAndCheckCache(innerBuildManager, new[] { outerBuildCacheDirectory });

                    // Force the cache for this build manager (and only this build manager) to be cleared.  It should leave
                    // behind the results from the other one.
                    innerBuildManager.ResetCaches();
                }

                Assert.False(Directory.Exists(innerBuildCacheDirectory)); // "Inner build cache directory still exists after inner build manager was disposed."
                Assert.True(Directory.Exists(outerBuildCacheDirectory)); // "Outer build cache directory doesn't exist after inner build manager was disposed."

                // Force the cache for this build manager to be cleared.
                outerBuildManager.ResetCaches();
            }

            Assert.False(Directory.Exists(outerBuildCacheDirectory)); // "Outer build cache directory still exists after outer build manager was disposed."
        }

        /// <summary>
        /// If there's a P2P that otherwise succeeds, but has an AfterTarget that errors out, the
        /// overall build result -- and thus the return value of the MSBuild task -- should reflect
        /// that failure.
        /// </summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FailedAfterTargetInP2PShouldCauseOverallBuildFailure(bool disableInProcNode)
        {
            var projA = _env.CreateFile(".proj").Path;
            var projB = _env.CreateFile(".proj").Path;

            string contentsA = @"
<Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
  <Target Name=`Build`>
    <MSBuild Projects=`" + projB + @"` />

    <Warning Text=`We shouldn't reach here.` />
  </Target>
</Project>
";

            const string contentsB = @"
<Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
  <Target Name=`Build`>
    <Message Text=`Build` />
  </Target>

  <Target Name=`Error` AfterTargets=`Build`>
    <Error Text=`Error!` />
  </Target>
</Project>
";

            File.WriteAllText(projA, CleanupFileContents(contentsA));
            File.WriteAllText(projB, CleanupFileContents(contentsB));

            _parameters.DisableInProcNode = disableInProcNode;
            _buildManager.BeginBuild(_parameters);
            var data = new BuildRequestData(projA, new Dictionary<string, string>(), null, new[] { "Build" }, new HostServices());
            BuildResult result = _buildManager.PendBuildRequest(data).Execute();

            Assert.Equal(BuildResultCode.Failure, result.OverallResult);
            _logger.AssertNoWarnings();
            _buildManager.EndBuild();
        }

        /// <summary>
        /// If there's a P2P that otherwise succeeds, but has an AfterTarget that errors out, the
        /// overall build result -- and thus the return value of the MSBuild task -- should reflect
        /// that failure.  Specifically tests where there are multiple entrypoint targets with
        /// AfterTargets, only one of which fails.
        /// </summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FailedAfterTargetInP2PShouldCauseOverallBuildFailure_MultipleEntrypoints(bool disableInProcNode)
        {
            var projA = _env.CreateFile(".proj").Path;
            var projB = _env.CreateFile(".proj").Path;

            string contentsA = @"
<Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
  <Target Name=`Build`>
    <MSBuild Projects=`" + projB + @"` Targets=`Build;Build2` />

    <Warning Text=`We shouldn't reach here.` />
  </Target>
</Project>
";

            const string contentsB = @"
<Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
  <Target Name=`Build`>
    <Message Text=`[Build]` />
  </Target>

  <Target Name=`Build2`>
    <Message Text=`[Build2]` />
  </Target>

  <Target Name=`AT1` AfterTargets=`Build`>
    <Message Text=`[AT1]` />
  </Target>

  <Target Name=`AT2` AfterTargets=`Build2`>
    <Message Text=`[AT2]` />
  </Target>

  <Target Name=`Error` AfterTargets=`Build2`>
    <Error Text=`Error!` />
  </Target>
</Project>
";

            File.WriteAllText(projA, CleanupFileContents(contentsA));
            File.WriteAllText(projB, CleanupFileContents(contentsB));

            _parameters.DisableInProcNode = disableInProcNode;
            _buildManager.BeginBuild(_parameters);
            var data = new BuildRequestData(projA, new Dictionary<string, string>(), null, new[] { "Build" }, new HostServices());
            BuildResult result = _buildManager.PendBuildRequest(data).Execute();

            Assert.Equal(BuildResultCode.Failure, result.OverallResult);
            _logger.AssertNoWarnings();
            _logger.AssertLogContains("[Build]");
            _logger.AssertLogContains("[Build2]");
            _logger.AssertLogContains("[AT1]");
            _logger.AssertLogContains("[AT2]");

            _buildManager.EndBuild();
        }

        /// <summary>
        /// If there's a P2P that otherwise succeeds, but has an AfterTarget that errors out, the
        /// overall build result -- and thus the return value of the MSBuild task -- should reflect
        /// that failure. This should also be true if the AfterTarget is an AfterTarget of the
        /// entrypoint target.
        /// </summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FailedNestedAfterTargetInP2PShouldCauseOverallBuildFailure(bool disableInProcNode)
        {
            var projA = _env.CreateFile(".proj").Path;
            var projB = _env.CreateFile(".proj").Path;

            string contentsA = @"
<Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
  <Target Name=`Build`>
    <MSBuild Projects=`" + projB + @"` />

    <Warning Text=`We shouldn't reach here.` />
  </Target>
</Project>
";

            const string contentsB = @"
<Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
  <Target Name=`Build`>
    <Message Text=`Build` />
  </Target>

  <Target Name=`Target1` AfterTargets=`Build`>
    <Message Text=`Target1` />
  </Target>

  <Target Name=`Error` AfterTargets=`Target1`>
    <Error Text=`Error!` />
  </Target>
</Project>
";

            File.WriteAllText(projA, CleanupFileContents(contentsA));
            File.WriteAllText(projB, CleanupFileContents(contentsB));

            _parameters.DisableInProcNode = disableInProcNode;
            _buildManager.BeginBuild(_parameters);
            var data = new BuildRequestData(projA, new Dictionary<string, string>(), null, new[] { "Build" }, new HostServices());
            BuildResult result = _buildManager.PendBuildRequest(data).Execute();

            Assert.Equal(BuildResultCode.Failure, result.OverallResult);
            _logger.AssertNoWarnings();
            _buildManager.EndBuild();
        }

        /// <summary>
        /// If a project is called into twice, with two different entrypoint targets that
        /// depend on non-overlapping sets of targets, and the first fails, the second
        /// should not inherit that failure if all the targets it calls succeed.
        /// </summary>
        [Fact]
        public void NonOverlappingEnusingTrypointTargetsShouldNotInfluenceEachOthersResults()
        {
            var projA = _env.CreateFile(".proj").Path;
            var projB = _env.CreateFile(".proj").Path;

            string contentsA = @"
<Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
  <Target Name=`Build`>

   <Message Text=`The next MSBuild call should FAIL, but the build will continue.` />
   <MSBuild Projects=`" + projB + @"` Targets=`Build` ContinueOnError=`true` />
   <Message Text=`The next MSBuild call should SUCCEED without error.` />
   <MSBuild Projects=`" + projB + @"` Targets=`GetTargetPath` />
  </Target>
</Project>
";

            const string contentsB = @"
<Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
  <Target Name=`Build`>
    <Error Text=`Forced error in Build` />
  </Target>


  <Target Name=`GetTargetPath`>
    <Message Text=`Success` />
  </Target>
</Project>
";

            File.WriteAllText(projA, CleanupFileContents(contentsA));
            File.WriteAllText(projB, CleanupFileContents(contentsB));

            _buildManager.BeginBuild(_parameters);
            var data = new BuildRequestData(projA, new Dictionary<string, string>(), null, new[] { "Build" }, new HostServices());
            BuildResult result = _buildManager.PendBuildRequest(data).Execute();

            Assert.Equal(BuildResultCode.Success, result.OverallResult);
            Assert.Equal(1, _logger.ErrorCount);
            _buildManager.EndBuild();
        }

        /// <summary>
        /// In a situation where we have two requests calling into the same project, with different entry point
        /// targets, one of which depends on "A;B", the other of which depends on "B", which has a dependency of
        /// its own on "A", that we still properly build.
        /// </summary>
#if RUNTIME_TYPE_NETCORE
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/933")]
#elif MONO
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/1240")]
#else
        [Fact]
#endif
        public void Regress473114()
        {
            var projA = _env.CreateFile(".proj").Path;
            var projB = _env.CreateFile(".proj").Path;
            var projC = _env.CreateFile(".proj").Path;
            var projD = _env.CreateFile(".proj").Path;

            string contentsA = @"<?xml version='1.0' encoding='utf-8'?>
<Project ToolsVersion='4.0' DefaultTargets='Build' xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
  <ItemGroup>
    <ProjectReference Include='" + projD + @"' />
    <ProjectReference Include='" + projC + @"' />
    <ProjectReference Include='" + projB + @"' />
  </ItemGroup>

  <Target Name='Build'>
    <MSBuild Projects='@(ProjectReference)' BuildInParallel='true' />
  </Target>
</Project>
";

            string contentsB = @"<?xml version='1.0' encoding='utf-8'?>
<Project ToolsVersion='4.0' DefaultTargets='CallsGenerateImpLib' xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
  <Target Name='CallsGenerateImpLib'>
    <MSBuild Projects='" + projC + @"' Targets='GenerateImpLib' BuildInParallel='true' />
  </Target>

</Project>
";

            string contentsC = @"<?xml version='1.0' encoding='utf-8'?>
<Project ToolsVersion='4.0' DefaultTargets='Default' xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
  <Target Name='Default' DependsOnTargets='ResolveReferences;BuildCompile'>
    <Message Text='Executed Default' />
  </Target>

  <Target Name='GenerateImpLib' DependsOnTargets='BuildCompile'>
    <Message Text='Executed GenerateImpLib' />
  </Target>

  <Target Name='ResolveReferences'>
    <MSBuild Projects='" + projD + @"' Targets='BuildSlower' BuildInParallel='true' />
  </Target>

  <Target Name='BuildCompile' DependsOnTargets='ResolveReferences'>
    <Message Text='Executed BuildCompile' />
  </Target>

</Project>
";

            string contentsD = @"<?xml version='1.0' encoding='utf-8'?>
<Project ToolsVersion='4.0' DefaultTargets='Build' xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
  <Target Name='Build'>
    <Message Text='In d.proj' />
  </Target>

  <Target Name='BuildSlower'>
    <Exec Command='" + Helpers.GetSleepCommand(TimeSpan.FromMilliseconds(500)) + @"' />
  </Target>
</Project>
";

            File.WriteAllText(projA, contentsA);
            File.WriteAllText(projB, contentsB);
            File.WriteAllText(projC, contentsC);
            File.WriteAllText(projD, contentsD);

            _parameters.MaxNodeCount = 3;
            _parameters.EnableNodeReuse = false;
            _buildManager.BeginBuild(_parameters);
            var data = new BuildRequestData(projA, new Dictionary<string, string>(), "4.0", new[] { "Build" }, new HostServices());
            BuildResult result = _buildManager.PendBuildRequest(data).Execute();

            Assert.Equal(BuildResultCode.Success, result.OverallResult);

            _buildManager.EndBuild();
        }

        /// <summary>
        /// If two requests are made for the same project, and they call in with
        /// just the right timing such that:
        /// - request 1 builds for a while, reaches a P2P, and blocks
        /// - request 2 starts building, skips for a while, reaches the above P2P, and
        ///   blocks waiting for request 1's results
        /// - request 1 resumes building, errors, and exits
        /// - request 2 resumes building
        ///
        /// Then request 2 should end up exiting in the same fashion.
        ///
        /// This simple test verifies that if there are two error targets in a row, the
        /// second request will bail out where the first request did, as though it had
        /// executed the target, rather than skipping and continuing.
        /// </summary>
#if MONO
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/1240")]
#else
        [Fact]
#endif
        public void VerifyMultipleRequestForSameProjectWithErrors_Simple()
        {
            var projA = _env.CreateFile(".proj").Path;
            var projB = _env.CreateFile(".proj").Path;
            var projC = _env.CreateFile(".proj").Path;

            string contentsA = $@"
<Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
  <Target Name=`Build`>
    <MSBuild Projects=`{projB};{projC};{projB}` BuildInParallel=`true` />
  </Target>
</Project>";

            string contentsB = $@"
<Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
  <Target Name=`Build` DependsOnTargets=`CallMSBuild;Error1;Error2` />
  <Target Name=`CallMSBuild`>
    <MSBuild Projects=`{projC}` Targets=`Sleep` BuildInParallel=`true` />
  </Target>
  <Target Name=`Error1`>
    <Error Text=`Error 1` />
  </Target>
  <Target Name=`Error2`>
    <Error Text=`Error 2` />
  </Target>
</Project>";

            string contentsC = @"
<Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

  <Target Name=`Build`>
    <Message Text=`foo` />
  </Target>

  <Target Name=`Sleep`>
    <Exec Command=`" + Helpers.GetSleepCommand(TimeSpan.FromMilliseconds(500)) + @"` />
  </Target>
</Project>";

            File.WriteAllText(projA, CleanupFileContents(contentsA));
            File.WriteAllText(projB, CleanupFileContents(contentsB));
            File.WriteAllText(projC, CleanupFileContents(contentsC));

            _parameters.MaxNodeCount = 2;
            _parameters.EnableNodeReuse = false;
            _buildManager.BeginBuild(_parameters);
            var data = new BuildRequestData(projA, new Dictionary<string, string>(), null,
                new[] {"Build"}, new HostServices());
            BuildResult result = _buildManager.PendBuildRequest(data).Execute();

            Assert.Equal(BuildResultCode.Failure, result.OverallResult);

            // We should never get to Error2, because it's supposed to execute after Error1, which failed.
            _logger.AssertLogDoesntContain("Error 2");

            // We should, however, end up skipping Error1 on the second call to B.
            string skippedMessage =
                ResourceUtilities.FormatResourceStringStripCodeAndKeyword("TargetAlreadyCompleteFailure", "Error1");
            _logger.AssertLogContains(skippedMessage);
            _buildManager.EndBuild();
        }

        /// <summary>
        /// If two requests are made for the same project, and they call in with
        /// just the right timing such that:
        /// - request 1 builds for a while, reaches a P2P, and blocks
        /// - request 2 starts building, skips for a while, reaches the above P2P, and
        ///   blocks waiting for request 1's results
        /// - request 1 resumes building, errors, and exits
        /// - request 2 resumes building
        ///
        /// Then request 2 should end up exiting in the same fashion.
        ///
        /// This simple test verifies that if there are two error targets in a row, and the
        /// first has a chain of OnError targets, the OnError targets will all execute as
        /// expected in the first request, but be skipped by the second (since if it's "skipping
        /// unsuccessful", it can assume that all other OnError targets have also already been run)
        /// </summary>
#if MONO
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/1240")]
#else
        [Fact]
#endif
        public void VerifyMultipleRequestForSameProjectWithErrors_OnErrorChain()
        {
            var projA = _env.CreateFile(".proj").Path;
            var projB = _env.CreateFile(".proj").Path;
            var projC = _env.CreateFile(".proj").Path;

            string contentsA = @"
<Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
  <Target Name=`Build`>
    <MSBuild Projects=`" + String.Join(";", projB, projC, projB) + @"` BuildInParallel=`true` />
  </Target>
</Project>
";

            string contentsB = @"
<Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
  <Target Name=`Build` DependsOnTargets=`CallMSBuild;Error1;Error2` />
  <Target Name=`CallMSBuild`>
    <MSBuild Projects=`" + projC + @"` Targets=`Sleep` BuildInParallel=`true` />
  </Target>
  <Target Name=`Error1`>
    <Error Text=`Error 1` />
    <OnError ExecuteTargets=`Target2;Target3` />
  </Target>
  <Target Name=`Error2`>
    <Error Text=`Error 2` />
  </Target>

  <Target Name=`Target2`>
    <Error Text=`Error in Target2` />
    <OnError ExecuteTargets=`Target4` />
  </Target>

  <Target Name=`Target3`>
    <Message Text=`Target 3` />
  </Target>

  <Target Name=`Target4`>
    <Message Text=`Target 4` />
  </Target>

</Project>
";

            string contentsC = @"
<Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

  <Target Name=`Build`>
    <Message Text=`foo` />
  </Target>

  <Target Name=`Sleep`>
    <Exec Command=`" + Helpers.GetSleepCommand(TimeSpan.FromMilliseconds(500)) + @"` />
  </Target>
</Project>
";

            File.WriteAllText(projA, CleanupFileContents(contentsA));
            File.WriteAllText(projB, CleanupFileContents(contentsB));
            File.WriteAllText(projC, CleanupFileContents(contentsC));

            _parameters.MaxNodeCount = 2;
            _parameters.EnableNodeReuse = false;
            _buildManager.BeginBuild(_parameters);
            var data = new BuildRequestData(projA, new Dictionary<string, string>(), null,
                new[] {"Build"}, new HostServices());
            BuildResult result = _buildManager.PendBuildRequest(data).Execute();

            Assert.Equal(BuildResultCode.Failure, result.OverallResult);

            // We should never get to Error2, because it's supposed to execute after Error1, which failed.
            _logger.AssertLogDoesntContain("Error 2");

            // We should, however, get to Target2, Target3, and Target4, since they're part of the OnError
            // chain for Error1
            _logger.AssertLogContains("Error in Target2");
            _logger.AssertLogContains("Target 3");
            _logger.AssertLogContains("Target 4");

            // We should end up skipping Error1 on the second call to B.
            string skippedMessage1 =
                ResourceUtilities.FormatResourceStringStripCodeAndKeyword("TargetAlreadyCompleteFailure", "Error1");
            _logger.AssertLogContains(skippedMessage1);

            // We shouldn't, however, see skip messages for the OnError targets
            string skippedMessage2 =
                ResourceUtilities.FormatResourceStringStripCodeAndKeyword("TargetAlreadyCompleteFailure", "Target2");
            _logger.AssertLogDoesntContain(skippedMessage2);

            string skippedMessage3 =
                ResourceUtilities.FormatResourceStringStripCodeAndKeyword("TargetAlreadyCompleteSuccess", "Target3");
            _logger.AssertLogDoesntContain(skippedMessage3);

            string skippedMessage4 =
                ResourceUtilities.FormatResourceStringStripCodeAndKeyword("TargetAlreadyCompleteSuccess", "Target4");
            _logger.AssertLogDoesntContain(skippedMessage4);
            _buildManager.EndBuild();
        }

        /// <summary>
        /// If two requests are made for the same project, and they call in with
        /// just the right timing such that:
        /// - request 1 builds for a while, reaches a P2P, and blocks
        /// - request 2 starts building, skips for a while, reaches the above P2P, and
        ///   blocks waiting for request 1's results
        /// - request 1 resumes building, errors, and exits
        /// - request 2 resumes building
        ///
        /// Then request 2 should end up exiting in the same fashion.
        ///
        /// This simple test verifies that if there are two error targets in a row, AND
        /// they're marked as ContinueOnError=ErrorAndContinue, then we won't bail, but
        /// will continue executing (on the first request) or skipping (on the second)
        /// </summary>
#if MONO
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/1240")]
#else
        [Fact]
#endif
        public void VerifyMultipleRequestForSameProjectWithErrors_ErrorAndContinue()
        {
            var projA = _env.CreateFile(".proj").Path;
            var projB = _env.CreateFile(".proj").Path;
            var projC = _env.CreateFile(".proj").Path;

            string contentsA = @"
<Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
  <Target Name=`Build`>
    <MSBuild Projects=`" + String.Join(";", projB, projC, projB) + @"` BuildInParallel=`true` />
  </Target>
</Project>
";

            string contentsB = @"
<Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
  <Target Name=`Build` DependsOnTargets=`CallMSBuild;Error1;Error2` />
  <Target Name=`CallMSBuild`>
    <MSBuild Projects=`" + projC + @"` Targets=`Sleep` BuildInParallel=`true` />
  </Target>
  <Target Name=`Error1`>
    <Error Text=`Error 1` ContinueOnError=`ErrorAndContinue` />
  </Target>
  <Target Name=`Error2`>
    <Error Text=`Error 2` />
  </Target>
</Project>
";

            string contentsC = @"
<Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

  <Target Name=`Build`>
    <Message Text=`foo` />
  </Target>

  <Target Name=`Sleep`>
    <Exec Command=`" + Helpers.GetSleepCommand(TimeSpan.FromMilliseconds(500)) + @"` />
  </Target>
</Project>
";

            File.WriteAllText(projA, CleanupFileContents(contentsA));
            File.WriteAllText(projB, CleanupFileContents(contentsB));
            File.WriteAllText(projC, CleanupFileContents(contentsC));

            _parameters.MaxNodeCount = 2;
            _parameters.EnableNodeReuse = false;
            _buildManager.BeginBuild(_parameters);
            var data = new BuildRequestData(projA, new Dictionary<string, string>(), null,
                new[] {"Build"}, new HostServices());
            BuildResult result = _buildManager.PendBuildRequest(data).Execute();

            Assert.Equal(BuildResultCode.Failure, result.OverallResult);

            // We should see both Error1 and Error2
            _logger.AssertLogContains("Error 1");
            _logger.AssertLogContains("Error 2");

            // We should also end up skipping them both.
            string skippedMessage1 =
                ResourceUtilities.FormatResourceStringStripCodeAndKeyword("TargetAlreadyCompleteFailure", "Error1");
            _logger.AssertLogContains(skippedMessage1);

            string skippedMessage2 =
                ResourceUtilities.FormatResourceStringStripCodeAndKeyword("TargetAlreadyCompleteFailure", "Error2");
            _logger.AssertLogContains(skippedMessage2);

            _buildManager.EndBuild();
        }

        /// <summary>
        /// If two requests are made for the same project, and they call in with
        /// just the right timing such that:
        /// - request 1 builds for a while, reaches a P2P, and blocks
        /// - request 2 starts building, skips for a while, reaches the above P2P, and
        ///   blocks waiting for request 1's results
        /// - request 1 resumes building, errors, and exits
        /// - request 2 resumes building
        ///
        /// Then request 2 should end up exiting in the same fashion.
        ///
        /// This test verifies that if the errors are in AfterTargets, we still
        /// exit as though the target that those targets run after has already run.
        /// </summary>
#if MONO
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/1240")]
#else
        [Fact]
#endif
        public void VerifyMultipleRequestForSameProjectWithErrors_AfterTargets()
        {
            var projA = _env.CreateFile(".proj").Path;
            var projB = _env.CreateFile(".proj").Path;
            var projC = _env.CreateFile(".proj").Path;

            string contentsA = @"
<Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
  <Target Name=`Build`>
    <MSBuild Projects=`" + String.Join(";", projB, projC, projB) + @"` BuildInParallel=`true` />
  </Target>
</Project>
";

            string contentsB = @"
<Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
  <Target Name=`Build` DependsOnTargets=`CallMSBuild` />
  <Target Name=`CallMSBuild`>
    <MSBuild Projects=`" + projC + @"` Targets=`Sleep` BuildInParallel=`true` />
  </Target>
  <Target Name=`Error1` AfterTargets=`CallMSBuild`>
    <Error Text=`Error 1` />
  </Target>
  <Target Name=`Error2` AfterTargets=`CallMSBuild`>
    <Error Text=`Error 2` />
  </Target>
</Project>
";

            string contentsC = @"
<Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

  <Target Name=`Build`>
    <Message Text=`foo` />
  </Target>

  <Target Name=`Sleep`>
    <Exec Command=`" + Helpers.GetSleepCommand(TimeSpan.FromMilliseconds(500)) + @"` />
  </Target>
</Project>
";

            File.WriteAllText(projA, CleanupFileContents(contentsA));
            File.WriteAllText(projB, CleanupFileContents(contentsB));
            File.WriteAllText(projC, CleanupFileContents(contentsC));

            _parameters.MaxNodeCount = 2;
            _parameters.EnableNodeReuse = false;
            _buildManager.BeginBuild(_parameters);
            var data = new BuildRequestData(projA, new Dictionary<string, string>(), null,
                new[] {"Build"}, new HostServices());
            BuildResult result = _buildManager.PendBuildRequest(data).Execute();

            Assert.Equal(BuildResultCode.Failure, result.OverallResult);

            // We should never get to Error2, because we should never run its AfterTarget, after
            // the AfterTarget with Error1 failed
            _logger.AssertLogDoesntContain("Error 2");

            _buildManager.EndBuild();
        }

        /// <summary>
        /// Related to the two tests above, if two requests are made for the same project, but
        /// for different entry targets, and a target fails in the first request, if the second
        /// request also runs that target, its skip-unsuccessful should behave in the same
        /// way as if the target had actually errored.
        /// </summary>
        [Fact]
        public void VerifyMultipleRequestForSameProjectWithErrors_DifferentEntrypoints()
        {
            var projA = _env.CreateFile(".proj").Path;
            var projB = _env.CreateFile(".proj").Path;

            string contentsA = @"
<Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
  <ItemGroup>
    <PR Include=`" + projB + @"`>
      <Targets>Build</Targets>
    </PR>
    <PR Include=`" + projB + @"`>
      <Targets>Build2</Targets>
    </PR>
  </ItemGroup>

  <Target Name=`Build`>
    <MSBuild Projects=`@(PR)` Targets=`%(PR.Targets)` ContinueOnError=`true` />
  </Target>
</Project>
";

            const string contentsB = @"
<Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
  <Target Name=`Build` DependsOnTargets=`Target1;Error1`>
    <Message Text=`[Build]` />
  </Target>
  <Target Name=`Build2` DependsOnTargets=`Target2;Error1;Error2`>
    <Message Text=`[Build2]` />
  </Target>
  <Target Name=`Target1`>
    <Message Text=`[Target1]` />
  </Target>
  <Target Name=`Target2`>
    <Message Text=`[Target2]` />
  </Target>
  <Target Name=`Error1`>
    <Error Text=`[Error1]` />
  </Target>
  <Target Name=`Error2`>
    <Error Text=`[Error2]` />
  </Target>
</Project>
";

            File.WriteAllText(projA, CleanupFileContents(contentsA));
            File.WriteAllText(projB, CleanupFileContents(contentsB));

            _buildManager.BeginBuild(_parameters);
            var data = new BuildRequestData(projA, new Dictionary<string, string>(), null,
                new[] {"Build"}, new HostServices());
            BuildResult result = _buildManager.PendBuildRequest(data).Execute();

            Assert.Equal(BuildResultCode.Success, result.OverallResult);

            // We should never get to Error2, because it's only ever executed in the second
            // request after Error1, which should skip-unsuccessful and exit
            _logger.AssertLogDoesntContain("[Error2]");

            _buildManager.EndBuild();
        }

        /// <summary>
        /// Verify that we can submit multiple simultaneous submissions with
        /// legacy threading mode active and successfully build.
        /// </summary>
        [Fact]
        public void TestSimultaneousSubmissionsWithLegacyThreadingData()
        {
            string projectContent = @"<Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
    <Target Name=`Build`>
        <!-- Wait 200 ms -->
        <Exec Command=`" + Helpers.GetSleepCommand(TimeSpan.FromMilliseconds(200)) + @"` />
    </Target>

</Project>
";
            var projectPath1 = _env.CreateFile(".proj").Path;
            File.WriteAllText(projectPath1, CleanupFileContents(projectContent));

            var project1 = new Project(projectPath1);

            var projectPath2 = _env.CreateFile(".proj").Path;
            File.WriteAllText(projectPath2, CleanupFileContents(projectContent));

            var project2 = new Project(projectPath2);

            ConsoleLogger cl = new ConsoleLogger();
            var buildParameters =
                new BuildParameters(ProjectCollection.GlobalProjectCollection)
                {
                    Loggers = new ILogger[] { cl },
                    LegacyThreadingSemantics = true
                };
            BuildManager.DefaultBuildManager.BeginBuild(buildParameters);

            var project1DoneEvent = new AutoResetEvent(false);
            ThreadPool.QueueUserWorkItem(delegate
            {
                ProjectInstance pi = BuildManager.DefaultBuildManager.GetProjectInstanceForBuild(project1);

                BuildRequestData requestData = new BuildRequestData(pi, new[] { "Build" });
                BuildSubmission submission = BuildManager.DefaultBuildManager.PendBuildRequest(requestData);
                submission.Execute();
                project1DoneEvent.Set();
            });

            var project2DoneEvent = new AutoResetEvent(false);
            ThreadPool.QueueUserWorkItem(delegate
            {
                ProjectInstance pi = BuildManager.DefaultBuildManager.GetProjectInstanceForBuild(project2);
                BuildRequestData requestData = new BuildRequestData(pi, new[] { "Build" });
                BuildSubmission submission = BuildManager.DefaultBuildManager.PendBuildRequest(requestData);
                submission.Execute();
                project2DoneEvent.Set();
            });

            project1DoneEvent.WaitOne();
            project2DoneEvent.WaitOne();

            BuildManager.DefaultBuildManager.EndBuild();
        }

        /// <summary>
        /// Verify that we can submit multiple simultaneous submissions with
        /// legacy threading mode active and successfully build, and that one of those
        /// submissions can P2P to the other.
        /// </summary>
        [Fact]
        public void TestSimultaneousSubmissionsWithLegacyThreadingData_P2P()
        {
            string projectContent1 = @"<Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
    <Target Name=`CopyRunEnvironmentFiles`>
        <!-- Wait 100 ms -->
        <Exec Command=`" + Helpers.GetSleepCommand(TimeSpan.FromMilliseconds(100)) + @"` />
    </Target>

    <Target Name=`MyConsoleTarget`>
        <!-- Wait 100 ms -->
        <Exec Command=`" + Helpers.GetSleepCommand(TimeSpan.FromMilliseconds(100)) + @"` />
    </Target>

</Project>
";

            var projectPath1 = _env.CreateFile(".proj").Path;
            File.WriteAllText(projectPath1, CleanupFileContents(projectContent1));

            var project1 = new Project(projectPath1);

            string projectContent2 = @"<Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
    <Target Name=`MSDeployPublish` />

    <Target Name=`DoStuff` AfterTargets=`MSDeployPublish`>
        <MSBuild Projects=`" + projectPath1 + @"` Targets=`MyConsoleTarget` />
    </Target>

</Project>
";

            var projectPath2 = _env.CreateFile(".proj").Path;
            File.WriteAllText(projectPath2, CleanupFileContents(projectContent2));

            var project2 = new Project(projectPath2);

            var cl = new ConsoleLogger();
            var buildParameters =
                new BuildParameters(ProjectCollection.GlobalProjectCollection)
                {
                    Loggers = new ILogger[] { cl },
                    LegacyThreadingSemantics = true
                };
            BuildManager.DefaultBuildManager.BeginBuild(buildParameters);

            var project1DoneEvent = new AutoResetEvent(false);
            ThreadPool.QueueUserWorkItem(delegate
            {
                // need to kick off project 2 first so that it project 1 can get submitted before the P2P happens
                ProjectInstance pi = BuildManager.DefaultBuildManager.GetProjectInstanceForBuild(project2);

                BuildRequestData requestData = new BuildRequestData(pi, new[] { "MSDeployPublish" });
                BuildSubmission submission = BuildManager.DefaultBuildManager.PendBuildRequest(requestData);
                BuildResult br = submission.Execute();
                Assert.Equal(BuildResultCode.Success, br.OverallResult);
                project1DoneEvent.Set();
            });

            var project2DoneEvent = new AutoResetEvent(false);
            ThreadPool.QueueUserWorkItem(delegate
            {
                ProjectInstance pi = BuildManager.DefaultBuildManager.GetProjectInstanceForBuild(project1);
                BuildRequestData requestData = new BuildRequestData(pi, new string[] { "CopyRunEnvironmentFiles" });
                BuildSubmission submission = BuildManager.DefaultBuildManager.PendBuildRequest(requestData);
                BuildResult br = submission.Execute();
                Assert.Equal(BuildResultCode.Success, br.OverallResult);
                project2DoneEvent.Set();
            });

            project1DoneEvent.WaitOne();
            project2DoneEvent.WaitOne();

            BuildManager.DefaultBuildManager.EndBuild();
        }

        /// <summary>
        /// Verify that we can submit multiple simultaneous submissions with
        /// legacy threading mode active and successfully build, and that one of those
        /// submissions can P2P to the other.
        ///
        /// A variation of the above test, where multiple nodes are available, so the
        /// submissions aren't restricted to running strictly serially by the single in-proc
        /// node.
        /// </summary>
#if MONO
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/1245")]
#else
        [Fact]
#endif
        public void TestSimultaneousSubmissionsWithLegacyThreadingData_P2P_MP()
        {
            string projectContent1 = @"<Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
    <Target Name=`CopyRunEnvironmentFiles`>
        <!-- Wait 100 ms -->
        <Exec Command=`" + Helpers.GetSleepCommand(TimeSpan.FromMilliseconds(100)) + @"` />
    </Target>

    <Target Name=`MyConsoleTarget`>
        <!-- Wait 100 ms -->
        <Exec Command=`" + Helpers.GetSleepCommand(TimeSpan.FromMilliseconds(100)) + @"` />
    </Target>

</Project>
";

            string projectPath1 = _env.CreateFile(".proj").Path;
            File.WriteAllText(projectPath1, CleanupFileContents(projectContent1));

            var project1 = new Project(projectPath1);

            string projectContent2 = @"<Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
    <Target Name=`MSDeployPublish` />

    <Target Name=`DoStuff` AfterTargets=`MSDeployPublish`>
        <MSBuild Projects=`" + projectPath1 + @"` Targets=`MyConsoleTarget` />
    </Target>

</Project>
";

            var projectPath2 = _env.CreateFile(".proj").Path;
            File.WriteAllText(projectPath2, CleanupFileContents(projectContent2));

            var project2 = new Project(projectPath2);

            var cl = new ConsoleLogger();
            var buildParameters =
                new BuildParameters(ProjectCollection.GlobalProjectCollection)
                {
                    Loggers = new ILogger[] { cl },
                    LegacyThreadingSemantics = true,
                    MaxNodeCount = 2,
                    EnableNodeReuse = false
                };
            BuildManager.DefaultBuildManager.BeginBuild(buildParameters);

            var project1DoneEvent = new AutoResetEvent(false);
            ThreadPool.QueueUserWorkItem(delegate
            {
                // need to kick off project 2 first so that it project 1 can get submitted before the P2P happens
                ProjectInstance pi = BuildManager.DefaultBuildManager.GetProjectInstanceForBuild(project2);

                BuildRequestData requestData = new BuildRequestData(pi, new string[] { "MSDeployPublish" });
                BuildSubmission submission = BuildManager.DefaultBuildManager.PendBuildRequest(requestData);
                BuildResult br = submission.Execute();
                Assert.Equal(BuildResultCode.Success, br.OverallResult);
                project1DoneEvent.Set();
            });

            var project2DoneEvent = new AutoResetEvent(false);
            ThreadPool.QueueUserWorkItem(delegate
            {
                ProjectInstance pi = BuildManager.DefaultBuildManager.GetProjectInstanceForBuild(project1);
                BuildRequestData requestData = new BuildRequestData(pi, new string[] { "CopyRunEnvironmentFiles" });
                BuildSubmission submission = BuildManager.DefaultBuildManager.PendBuildRequest(requestData);
                BuildResult br = submission.Execute();
                Assert.Equal(BuildResultCode.Success, br.OverallResult);
                project2DoneEvent.Set();
            });

            project1DoneEvent.WaitOne();
            project2DoneEvent.WaitOne();

            BuildManager.DefaultBuildManager.EndBuild();
        }

        /// <summary>
        /// Ensures that properties and items are transferred from an out-of-proc project to an in-proc project.
        /// </summary>
        /// <remarks>
        /// This differs from transferring a project instance to an out-of-proc node because in this case the project
        /// was loaded by MSBuild, not supplied directly by the user.
        /// </remarks>
        [Fact]
        public void Regress265010()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <PropertyGroup>
   <Prop>BaseValue</Prop>
 </PropertyGroup>
<ItemGroup>
  <Item Include='BaseItem'/>
</ItemGroup>
 <Target Name='BaseTest'>
   <Message Text='[$(Prop)]'/>
   <Message Text='[@(Item)]'/>
    <PropertyGroup>
        <Prop>NewValue</Prop>
    </PropertyGroup>
    <ItemGroup>
        <Item Include='NewItem'/>
    </ItemGroup>
 </Target>

 <Target Name='MovedTest'>
   <Message Text='[$(Prop)]'/>
   <Message Text='[@(Item)]'/>
 </Target>

</Project>
");

            string fileName = _env.CreateFile(".proj").Path;
            File.WriteAllText(fileName, contents);
            _buildManager.BeginBuild(_parameters);

            var services = new HostServices();
            services.SetNodeAffinity(fileName, NodeAffinity.OutOfProc);
            var data = new BuildRequestData(fileName, new Dictionary<string, string>(), MSBuildDefaultToolsVersion, new[] { "BaseTest" }, services);
            _buildManager.PendBuildRequest(data).Execute();
            _logger.AssertLogContains("[BaseValue]");
            _logger.AssertLogContains("[BaseItem]");
            _logger.ClearLog();

            _parameters.ResetCaches = false;
            services.SetNodeAffinity(fileName, NodeAffinity.InProc);
            data = new BuildRequestData(fileName, new Dictionary<string, string>(), MSBuildDefaultToolsVersion, new[] { "MovedTest" }, services);
            _buildManager.PendBuildRequest(data).Execute();
            _logger.AssertLogContains("[NewValue]");
            _logger.AssertLogContains("[BaseItem;NewItem]");
            _logger.AssertLogDoesntContain("[BaseValue]");

            _buildManager.EndBuild();
        }

        /// <summary>
        /// Verifies that all warnings are treated as errors and that the overall build result is a failure.
        /// </summary>
        [Fact]
        public void WarningsAreTreatedAsErrorsAll()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <Target Name='target1'>
    <Warning Text='This warning should be treated as an error' Code='ABC123'/>
    <Warning Text='This warning should NOT be treated as an error' />
 </Target>
</Project>
");
            _parameters.WarningsAsErrors = new HashSet<string>();

            Project project = CreateProject(contents, MSBuildDefaultToolsVersion, _projectCollection, true);
            ProjectInstance instance = _buildManager.GetProjectInstanceForBuild(project);
            _buildManager.BeginBuild(_parameters);
            BuildResult result1 = _buildManager.BuildRequest(new BuildRequestData(instance, new[] { "target1" }));
            _buildManager.EndBuild();

            Assert.Equal(0, _logger.WarningCount);
            Assert.Equal(2, _logger.ErrorCount);

            Assert.Equal(BuildResultCode.Failure, result1.OverallResult);
            Assert.True(result1.HasResultsForTarget("target1"));
        }

        /// <summary>
        /// Verifies that only the specified warnings are treated as errors and that the overall build result is a failure.
        /// </summary>
        [Fact]
        public void WarningsAreTreatedAsErrorsSpecific()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <Target Name='target1'>
    <Warning Text='This warning should be treated as an error' Code='ABC123'/>
    <Warning Text='This warning should NOT be treated as an error' Code='NA123' />
    <Warning Text='This warning should NOT be treated as an error' />
 </Target>
</Project>
");
            _parameters.WarningsAsErrors = new HashSet<string> { "ABC123" };

            Project project = CreateProject(contents, MSBuildDefaultToolsVersion, _projectCollection, true);
            ProjectInstance instance = _buildManager.GetProjectInstanceForBuild(project);
            _buildManager.BeginBuild(_parameters);
            BuildResult result1 = _buildManager.BuildRequest(new BuildRequestData(instance, new[] { "target1" }));
            _buildManager.EndBuild();

            Assert.Equal(2, _logger.WarningCount);
            Assert.Equal(1, _logger.ErrorCount);

            Assert.Equal(BuildResultCode.Failure, result1.OverallResult);
            Assert.True(result1.HasResultsForTarget("target1"));
        }

        /// <summary>
        /// Verifies that when building targets which emit warnings, they still show as succeeding but the overall build result is a failure.
        /// </summary>
        [Fact]
        public void WarningsAreTreatedAsErrorsButTargetsStillSucceed()
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
<Target Name='target1'>
    <Message Text='text'/>
 </Target>
 <Target Name='target2'>
    <Warning Text='This warning should be treated as an error' Code='ABC123'/>
 </Target>
</Project>
");
            _parameters.WarningsAsErrors = new HashSet<string> { "ABC123" };

            Project project = CreateProject(contents, MSBuildDefaultToolsVersion, _projectCollection, true);
            ProjectInstance instance = _buildManager.GetProjectInstanceForBuild(project);
            _buildManager.BeginBuild(_parameters);
            BuildResult buildResult = _buildManager.BuildRequest(new BuildRequestData(instance, new string[] { "target1", "target2" }));
            _buildManager.EndBuild();

            Assert.Equal(0, _logger.WarningCount);
            Assert.Equal(1, _logger.ErrorCount);

            Assert.Equal(BuildResultCode.Failure, buildResult.OverallResult);
            Assert.True(buildResult.HasResultsForTarget("target1"));
            Assert.True(buildResult.HasResultsForTarget("target2"));
            // The two targets should still show as success because they don't know their warning was changed to an error
            // Logging a warning as an error does not change execution, only the final result of the build
            Assert.Equal(TargetResultCode.Success, buildResult.ResultsByTarget["target1"].ResultCode);
            Assert.Equal(TargetResultCode.Success, buildResult.ResultsByTarget["target2"].ResultCode);
        }

        /// <summary>
        /// Helper for cache tests.  Builds a project and verifies the right cache files are created.
        /// </summary>
        private static string BuildAndCheckCache(BuildManager localBuildManager, IEnumerable<string> exceptCacheDirectories)
        {
            string contents = CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <Target Name='One' Outputs='one.txt'>
 </Target>

 <Target Name='Two' Outputs='two.txt'>
 </Target>

 <Target Name='Three' Outputs='three.txt'>
 </Target>
</Project>
");
            string fileName = Path.GetTempFileName();
            File.WriteAllText(fileName, contents);

            string cacheDirectory = FileUtilities.GetCacheDirectory();

            var parameters = new BuildParameters();
            localBuildManager.BeginBuild(parameters);
            try
            {
                var services = new HostServices();
                BuildRequestData data = new BuildRequestData(fileName, new Dictionary<string, string>(), MSBuildDefaultToolsVersion, new[] { "One", "Two", "Three" }, services);
                var result = localBuildManager.PendBuildRequest(data).Execute();
                Assert.Equal(BuildResultCode.Success, result.OverallResult); // "Test project failed to build correctly."
            }
            finally
            {
                localBuildManager.EndBuild();
            }

            // Ensure that we got the cache files we expected.  There should be one set of results in there, once we exclude
            // any of the specified directories from previous builds in the same test.
            string directory = Directory.EnumerateDirectories(cacheDirectory).Except(exceptCacheDirectories).First();

            // Within this directory should be a set of target results files, one for each of the targets we invoked.
            var resultsFiles = Directory.EnumerateFiles(directory).Select(Path.GetFileName);

            Assert.Equal(3, resultsFiles.Count());
            Assert.Contains("One.cache", resultsFiles);
            Assert.Contains("Two.cache", resultsFiles);
            Assert.Contains("Three.cache", resultsFiles);

            // Return the cache directory created for this build.
            return directory;
        }

        /// <summary>
        /// Extract a string dictionary from the property enumeration on a project started event.
        /// </summary>
        private static Dictionary<string, string> ExtractProjectStartedPropertyList(IEnumerable properties)
        {
            // Gather a sorted list of all the properties.
            return properties?.Cast<DictionaryEntry>()
                .ToDictionary(prop => (string) prop.Key, prop => (string) prop.Value, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Retrieves a BuildRequestData using the specified contents, default targets and an empty project collection.
        /// </summary>
        private BuildRequestData GetBuildRequestData(string projectContents) => GetBuildRequestData(projectContents, Array.Empty<string>());

        /// <summary>
        /// Retrieves a GraphBuildRequestData using the specified contents, default targets and an empty project collection.
        /// </summary>
        private GraphBuildRequestData GetGraphBuildRequestData(string projectContents) => GetGraphBuildRequestData(projectContents, Array.Empty<string>());

        /// <summary>
        /// Retrieves a BuildRequestData using the specified contents, targets and project collection.
        /// </summary>
        private BuildRequestData GetBuildRequestData(string projectContents, string[] targets, string toolsVersion = null)
            => new BuildRequestData(CreateProjectInstance(projectContents, toolsVersion, _projectCollection, true), targets, _projectCollection.HostServices);

        /// <summary>
        /// Retrieves a GraphBuildRequestData using the specified contents, targets and project collection.
        /// </summary>
        private GraphBuildRequestData GetGraphBuildRequestData(string projectContents, string[] targets)
            => new GraphBuildRequestData(CreateProjectGraph(projectContents, _projectCollection), targets, _projectCollection.HostServices);

        /// <summary>
        /// Retrieve a ProjectInstance evaluated with the specified contents using the specified projectCollection
        /// </summary>
        private ProjectInstance CreateProjectInstance(string contents, string toolsVersion, ProjectCollection projectCollection, bool deleteTempProject)
        {
            Project project = CreateProject(contents, toolsVersion, projectCollection, deleteTempProject);
            return project.CreateProjectInstance();
        }

        /// <summary>
        /// Retrieve a CreateProjectGraph evaluated with the specified contents using the specified projectCollection
        /// </summary>
        private ProjectGraph CreateProjectGraph(string contents, ProjectCollection projectCollection)
        {
            var projectFilePath = _env.CreateFile().Path;
            File.WriteAllText(projectFilePath, contents);

            return new ProjectGraph(projectFilePath, projectCollection);
        }

        /// <summary>
        /// Retrieve a Project with the specified contents using the specified projectCollection
        /// </summary>
        private Project CreateProject(string contents, string toolsVersion, ProjectCollection projectCollection, bool deleteTempProject)
        {
            var project = new Project(XmlReader.Create(new StringReader(contents)), null, toolsVersion, projectCollection)
            {
                FullPath = _env.CreateFile().Path
            };

            if (!deleteTempProject)
            {
                project.Save();
            }

            if (deleteTempProject)
            {
                File.Delete(project.FullPath);
            }

            return project;
        }

        /// <summary>
        /// Generate dummy projects
        /// </summary>
        private static ProjectInstance GenerateDummyProjects(string shutdownProjectDirectory, int parallelProjectCount, ProjectCollection projectCollection)
        {
            Directory.CreateDirectory(shutdownProjectDirectory);

            // Generate the project.  It will have the following format.  Setting the AdditionalProperties
            // causes the projects to be built to be separate configs, which allows us to build the same project
            // a bunch of times in parallel.
            //
            // <Project xmlns='http://schemas.microsoft.com/developer/msbuild/2003'/>
            //   <ItemGroup>
            //     <ProjectReference Include="RootProjectName.proj">
            //       <AdditionalProperties>p={incremented value}</AdditionalProperties>
            //     </ProjectReference>
            //     ...
            //   </ItemGroup>
            //
            //   <Target Name="Build">
            //     <MSBuild Projects="@(ProjectReference) Targets="ChildBuild" BuildInParallel="true" />
            //   </Target>
            //
            //   <Target Name="ChildBuild" />
            // </Project>
            string rootProjectPath = Path.Combine(shutdownProjectDirectory, String.Format(CultureInfo.InvariantCulture, "RootProj_{0}.proj", Guid.NewGuid().ToString("N")));
            var rootProject = ProjectRootElement.Create(rootProjectPath, projectCollection);

            ProjectTargetElement buildTarget = rootProject.AddTarget("Build");
            ProjectTaskElement buildTask = buildTarget.AddTask("MSBuild");
            buildTask.SetParameter("Projects", "@(ProjectReference)");
            buildTask.SetParameter("BuildInParallel", "true");
            buildTask.SetParameter("Targets", "ChildBuild");

            rootProject.AddTarget("ChildBuild");

            IDictionary<string, string> metadata = new Dictionary<string, string>(1);
            for (int i = 0; i < parallelProjectCount; i++)
            {
                // Add the ProjectReference item for this actual config.
                metadata["AdditionalProperties"] = String.Format(CultureInfo.InvariantCulture, "p={0}", i);
                rootProject.AddItem("ProjectReference", rootProjectPath, metadata);
            }

            rootProject.Save();
            return new ProjectInstance(rootProject);
        }

        [Fact]
        public void ShouldBuildMutatedProjectInstanceWhoseProjectWasPreviouslyBuiltAsAP2PDependency()
        {
            const string mainProjectContents = @"<Project>

  <Target Name=""BuildOther"" Outputs=""@(ReturnValue)"">
    <MSBuild Projects=""{0}"" Targets=""Foo"">
      <Output TaskParameter=""TargetOutputs"" ItemName=""ReturnValue"" />
    </MSBuild>
  </Target>

</Project>";


            var p2pProjectContents =
@"<Project>

  <PropertyGroup>
    <P>InitialValue</P>
  </PropertyGroup>

  <Target Name=""Foo"" Outputs=""$(P)""/>

</Project>";

            using (var env = TestEnvironment.Create())
            using (var collection = new ProjectCollection())
            using (var manager = new BuildManager())
            {
                try
                {
                    var testFiles = env.CreateTestProjectWithFiles(string.Empty, new[] { "p2p", "main" });
                    var p2pProjectPath = testFiles.CreatedFiles[0];
                    File.WriteAllText(p2pProjectPath, p2pProjectContents);

                    var mainRootElement = ProjectRootElement.Create(XmlReader.Create(new StringReader(string.Format(mainProjectContents, p2pProjectPath))), collection);

                    mainRootElement.FullPath = testFiles.CreatedFiles[1];
                    mainRootElement.Save();

                    // build p2p project as a real p2p dependency of some other project. This loads the p2p into msbuild's caches

                    var mainProject = new Project(mainRootElement, new Dictionary<string, string>(), MSBuildConstants.CurrentToolsVersion, collection);
                    var mainInstance = mainProject.CreateProjectInstance(ProjectInstanceSettings.Immutable).DeepCopy(isImmutable: false);

                    Assert.Empty(mainInstance.GlobalProperties);

                    var request = new BuildRequestData(mainInstance, new[] {"BuildOther"});

                    var parameters = new BuildParameters
                    {
                        DisableInProcNode = true,
                        EnableNodeReuse = false,
                    };

                    manager.BeginBuild(parameters);

                    var submission = manager.PendBuildRequest(request);

                    var results = submission.Execute();
                    Assert.Equal(BuildResultCode.Success, results.OverallResult);
                    Assert.Equal("InitialValue", results.ResultsByTarget["BuildOther"].Items.First().ItemSpec);

                    // build p2p directly via mutated ProjectInstances based of the same Project.
                    // This should rebuild and the result should reflect the in-memory changes and not reuse stale cache info

                    var p2pProject = new Project(p2pProjectPath, new Dictionary<string, string>(), MSBuildConstants.CurrentToolsVersion, collection);

                    for (var i = 0; i < 2; i++)
                    {
                        var p2pInstance = p2pProject.CreateProjectInstance(ProjectInstanceSettings.Immutable).DeepCopy(isImmutable: false);

                        var newPropertyValue = $"NewValue_{i}";

                        p2pInstance.SetProperty("P", newPropertyValue);

                        request = new BuildRequestData(p2pInstance, new[] {"Foo"});
                        submission = manager.PendBuildRequest(request);
                        results = submission.Execute();

                        Assert.Empty(p2pInstance.GlobalProperties);

                        Assert.Equal(BuildResultCode.Success, results.OverallResult);
                        Assert.Equal(newPropertyValue, results.ResultsByTarget["Foo"].Items.First().ItemSpec);
                    }
                }
                finally
                {
                    manager.EndBuild();
                }
            }
        }

        [Fact]
        public void OutOfProcFileBasedP2PBuildSucceeds()
        {
            const string mainProject = @"<Project>

  <Target Name=`MainTarget` Returns=`foo;@(P2PReturnValue)`>
    <MSBuild Projects=`{0}` Targets=`P2PTarget`>
      <Output TaskParameter=`TargetOutputs` ItemName=`P2PReturnValue` />
    </MSBuild>
  </Target>

</Project>";

            const string p2pProject = @"<Project>

  <Target Name=`P2PTarget` Returns=`bar`>
    <Message Text=`Bar` Importance=`High` />
  </Target>

</Project>";
            var testFiles = _env.CreateTestProjectWithFiles(string.Empty, new[] {"main", "p2p"}, string.Empty);

            var buildParameters = new BuildParameters(_projectCollection)
            {
                DisableInProcNode = true,
                EnableNodeReuse = false,
                Loggers = new ILogger[] {_logger}
            };

            _buildManager.BeginBuild(buildParameters);

            try
            {
                var p2pProjectPath = testFiles.CreatedFiles[1];
                var cleanedUpP2pContents = CleanupFileContents(p2pProject);
                File.WriteAllText(p2pProjectPath, cleanedUpP2pContents);

                var mainProjectPath = testFiles.CreatedFiles[0];
                var cleanedUpMainContents = CleanupFileContents(string.Format(mainProject, p2pProjectPath));
                File.WriteAllText(mainProjectPath, cleanedUpMainContents);

                var buildRequestData = new BuildRequestData(
                    mainProjectPath,
                    new Dictionary<string, string>(),
                    MSBuildConstants.CurrentToolsVersion,
                    new[] {"MainTarget"},
                    null
                );

                var submission = _buildManager.PendBuildRequest(buildRequestData);

                var result = submission.Execute();

                Assert.Equal(BuildResultCode.Success, result.OverallResult);
                Assert.Equal("foo;bar",
                    string.Join(";", result.ResultsByTarget["MainTarget"].Items.Select(i => i.ItemSpec)));
            }
            finally
            {
                _buildManager.EndBuild();
            }
        }

        /// When a ProjectInstance based BuildRequestData is built out of proc, the node should
        /// not reload it from disk but instead fully utilize the entire translate project instance state
        /// to do the build
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OutOfProcProjectInstanceBasedBuildDoesNotReloadFromDisk(bool shouldSerializeEntireState)
        {
            const string mainProject = @"<Project>
  <PropertyGroup>
    <ImportIt>true</ImportIt>
  </PropertyGroup>

  <Import Project=""{0}"" Condition=""'$(ImportIt)' == 'true'""/>

</Project>";

            const string importProject = @"<Project>
  <Target Name=""Foo"">
    <Message Text=""Bar"" Importance=""High"" />
  </Target>
</Project>";

            var testFiles = _env.CreateTestProjectWithFiles(string.Empty, new[] {"main", "import"}, string.Empty);

            var importPath = testFiles.CreatedFiles[1];
            File.WriteAllText(importPath, CleanupFileContents(importProject));

            var root = ProjectRootElement.Create(
                XmlReader.Create(new StringReader(string.Format(mainProject, importPath))), _projectCollection);
            root.FullPath = Path.GetTempFileName();
            root.Save();

            // build a project which runs a target from an imported file

            var project = new Project(root, new Dictionary<string, string>(), MSBuildConstants.CurrentToolsVersion,
                _projectCollection);
            ProjectInstance instance = project.CreateProjectInstance(ProjectInstanceSettings.Immutable).DeepCopy(false);

            instance.TranslateEntireState = shouldSerializeEntireState;

            var request = new BuildRequestData(instance, new[] {"Foo"});

            var parameters = new BuildParameters(_projectCollection)
            {
                DisableInProcNode = true,
                EnableNodeReuse = false,
                Loggers = new ILogger[] {_logger}
            };

            _buildManager.BeginBuild(parameters);

            var submission = _buildManager.PendBuildRequest(request);
            var results = submission.Execute();
            Assert.True(results.OverallResult == BuildResultCode.Success);

            // reset caches to ensure nothing is reused
            _buildManager.EndBuild();
            _buildManager.ResetCaches();

            // mutate the file on disk such that the import (containing the target to get executed)
            // is no longer imported
            project.SetProperty("ImportIt", "false");
            project.Save();

            // Build the initial project instance again.
            // The project instance is not in sync with the file anymore, making it an in-memory build:
            // the file does not contain the target Foo, but the project instance does
            // Building the stale project instance should still succeed when the entire state is translated: MSBuild should use the
            // in-memory state to build and not reload from disk.
            _buildManager.BeginBuild(parameters);
            request = new BuildRequestData(instance, new[] {"Foo"}, null,
                BuildRequestDataFlags.ReplaceExistingProjectInstance);
            submission = _buildManager.PendBuildRequest(request);

            results = submission.Execute();

            _buildManager.EndBuild();

            if (shouldSerializeEntireState)
            {
                Assert.Equal(BuildResultCode.Success, results.OverallResult);
            }
            else
            {
                Assert.Equal(BuildResultCode.Failure, results.OverallResult);
                Assert.Contains("The target \"Foo\" does not exist in the project", _logger.FullLog,
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void OutOfProcEvaluationIdsUnique()
        {
            const string mainProject = @"<Project>

  <Target Name=`MainTarget`>
    <MSBuild Projects=`{0};{1}` Targets=`DummyTarget` />
  </Target>

</Project>";

            const string childProject = @"<Project>

  <Target Name=`DummyTarget`>
    <Message Text=`Bar` Importance=`High` />
  </Target>

</Project>";

            var testFiles = _env.CreateTestProjectWithFiles(string.Empty, new[] { "main", "child1", "child2" }, string.Empty);

            var buildParameters = new BuildParameters(_projectCollection)
            {
                DisableInProcNode = true,
                EnableNodeReuse = false,
                Loggers = new ILogger[] { _logger }
            };

            _buildManager.BeginBuild(buildParameters);

            try
            {
                var child1ProjectPath = testFiles.CreatedFiles[1];
                var child2ProjectPath = testFiles.CreatedFiles[2];
                var cleanedUpChildContents = CleanupFileContents(childProject);
                File.WriteAllText(child1ProjectPath, cleanedUpChildContents);
                File.WriteAllText(child2ProjectPath, cleanedUpChildContents);

                var mainProjectPath = testFiles.CreatedFiles[0];
                var cleanedUpMainContents = CleanupFileContents(string.Format(mainProject, child1ProjectPath, child2ProjectPath));
                File.WriteAllText(mainProjectPath, cleanedUpMainContents);

                var buildRequestData = new BuildRequestData(
                    mainProjectPath,
                    new Dictionary<string, string>(),
                    MSBuildConstants.CurrentToolsVersion,
                    new[] { "MainTarget" },
                    null
                );

                var submission = _buildManager.PendBuildRequest(buildRequestData);

                var result = submission.Execute();

                Assert.Equal(BuildResultCode.Success, result.OverallResult);
                Assert.True(_logger.AllBuildEvents.OfType<ProjectEvaluationStartedEventArgs>().GroupBy(args => args.BuildEventContext.EvaluationId).All(g => g.Count() == 1));
            }
            finally
            {
                _buildManager.EndBuild();
            }
        }

        /// <summary>
        /// Regression test for https://github.com/Microsoft/msbuild/issues/3047
        /// </summary>
        [Fact]
        [SkipOnTargetFramework(TargetFrameworkMonikers.Mono, "out-of-proc nodes not working on mono yet")]
        public void MultiProcReentrantProjectWithCallTargetDoesNotFail()
        {
            var a =
                @"<Project>
                     <Target Name=`EntryTarget`>
                         <MSBuild Projects=`b;c` BuildInParallel=`true` />
                     </Target>
                 </Project>".Cleanup();

            var b =
                @"<Project>
                     <Target Name=`BTarget`>
                         <MSBuild Projects=`reentrant` Targets=`BuildGenerateSources` BuildInParallel=`true` />
                     </Target>
                 </Project>".Cleanup();

            var c =
                $@"<Project>
                     <Target Name=`CTarget`>
                         <Exec Command=`{Helpers.GetSleepCommand(TimeSpan.FromSeconds(1))}` />
                         <MSBuild Projects=`reentrant` Targets=`BuildGenerated` BuildInParallel=`true` />
                     </Target>
                 </Project>".Cleanup();

            var delay =
                $@"<Project>
                     <Target Name=`Delay`>
                         <Exec Command=`{Helpers.GetSleepCommand(TimeSpan.FromSeconds(2))}` />
                     </Target>
                 </Project>".Cleanup();

            var reentrant =
                $@"<Project DefaultTargets=`Build`>
                     <Target Name=`BuildGenerateSources` DependsOnTargets=`_Get;Build`></Target>
                     <Target Name=`BuildGenerated`>
                         <CallTarget Targets=`Build` />
                     </Target>
                     <Target Name=`Build`>
                         <CallTarget Targets=`_Get` />
                     </Target>
                     <Target Name=`_Get`>
                         <MSBuild Projects=`delay` BuildInParallel=`true` />
                         <Exec Command=`{Helpers.GetSleepCommand(TimeSpan.FromSeconds(5))}` YieldDuringToolExecution=`true` StandardOutputImportance=`low` />
                     </Target>
                 </Project>".Cleanup();

            using (var env = TestEnvironment.Create(_output))
            {
                var entryFile = env.CreateFile(nameof(a), a).Path;
                env.CreateFile(nameof(b), b);
                env.CreateFile(nameof(c), c);
                env.CreateFile(nameof(delay), delay);
                env.CreateFile(nameof(reentrant), reentrant);

                var mockLogger = new MockLogger(_output);

                var buildParameters = new BuildParameters()
                {
                    DisableInProcNode = true,
                    MaxNodeCount = NativeMethodsShared.GetLogicalCoreCount(),
                    EnableNodeReuse = false,
                    Loggers = new List<ILogger>()
                    {
                        mockLogger
                    }
                };

                var buildRequestData = new BuildRequestData(entryFile, new Dictionary<string, string>(), MSBuildDefaultToolsVersion, new[]{ "EntryTarget" }, null);

                var result = _buildManager.Build(buildParameters, buildRequestData);

                result.OverallResult.ShouldBe(BuildResultCode.Success);
            }
        }

        [Fact]
        public void IdenticalSubmissionsShouldCompleteAndNotHangTheBuildOnMissingTargetExceptions()
        {
            var projectContents =
$@"<Project InitialTargets=`Sleep`>

  <Target Name=`Sleep`>
    <!-- Add a sleep to give the Scheduler time to register both submissions -->
    <Exec Command='{Helpers.GetSleepCommand(TimeSpan.FromMilliseconds(200))}' />
  </Target>

  <!-- The nonexistent target will force the TargetBuilder to not include a result for the Build target,
       thus causing a BuildResult cache miss in the Scheduler, which causes the Scheduler to
       handoff logging completion to the BuildManager.
  -->
  <Target Name='Build' DependsOnTargets='NonExistent'>
  </Target>

</Project>";

            Exception exception = null;
            var manager = new BuildManager();

            using (var env = TestEnvironment.Create())
            {
                try
                {
                    var testFiles = env.CreateTestProjectWithFiles(projectContents);

                    var parameters = new BuildParameters
                    {
                        MaxNodeCount = 1,
                        DisableInProcNode = false,
                        ShutdownInProcNodeOnBuildFinish = false,
                        UseSynchronousLogging = false,
                    };

                    /*
                     * When the scheduler completes a request, it looks ahead at the queue of unscheduled requests and
                     * preemptively completes identical ones (same global properties, path, toolsversion, AND requested targets).
                     * When the BuildResult has a result for the unscheduled identical request (most of the times), then the scheduler
                     * simulates the ProjectStarted / ProjectEnded events for the unscheduled request and completes it.
                     *
                     * However, when the initial build fails in such a way that there is no result for the requested targets,
                     * the initial BuildResult cannot satisfy the unscheduled submission. In this scenario the Scheduler still completes
                     * the request but does not simulate the ProjectStarted / ProjectEnded events. It also leaves logging completion to the
                     * BuildManager.
                     */
                    var request1 = new BuildRequestData(testFiles.ProjectFile, new Dictionary<string, string>(), MSBuildConstants.CurrentToolsVersion, new[] {"Build"}, null);
                    var request2 = new BuildRequestData(testFiles.ProjectFile, new Dictionary<string, string>(), MSBuildConstants.CurrentToolsVersion, new[] {"Build"}, null);

                    /* During builds, msbuild changes the current directory.
                     * When this test fails, the build never finishes so the current directory never gets restored.
                     * Force it to restore via the TestEnvironment, otherwise the true test failure gets masked by the current directory not getting restored.
                     */
                    env.SetCurrentDirectory(Environment.CurrentDirectory);

                    manager.BeginBuild(parameters);

                    /* Executing async will make the Scheduler aware that there are two identical submissions under consideration.
                     * Otherwise it builds both sequentually and does not perform the lookahead optimization.
                     */
                    var submission1 = manager.PendBuildRequest(request1);
                    var submission2 = manager.PendBuildRequest(request2);

                    submission1.ExecuteAsync(null, null);
                    submission2.ExecuteAsync(null, null);

                    submission1.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
                    submission2.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));

                    submission1.IsCompleted.ShouldBeTrue();
                    submission2.IsCompleted.ShouldBeTrue();

                    submission1.BuildResult.Exception.ShouldBeOfType<InvalidProjectFileException>();
                    submission2.BuildResult.Exception.ShouldBeOfType<InvalidProjectFileException>();
                }
                catch (Exception e)
                {
                    exception = e;
                }
                finally
                {
                    // Only cleanup the manager when the test does not fail with Shouldly assertions (actual assert failures).
                    // If the assert exceptions hit then EndBuild hangs, waiting for submissions which will never complete.
                    if (exception is ShouldAssertException)
                    {
                        throw exception;
                    }

                    manager.EndBuild();
                    manager.Dispose();
                }
            }
        }

        [Fact]
        public void BuildWithZeroConnectionTimeout()
        {
            string contents = CleanupFileContents(@"
<Project>
 <Target Name='test'>
    <Message Text='Text'/>
 </Target>
</Project>
");
            // Do not use MSBUILDINPROCENVCHECK because this test case is expected to leave a defunct in-proc node behind.
            _inProcEnvCheckTransientEnvironmentVariable.Revert();
            _env.SetEnvironmentVariable("MSBUILDNODECONNECTIONTIMEOUT", "0");

            BuildRequestData data = GetBuildRequestData(contents);
            try
            {
                BuildResult result = _buildManager.Build(_parameters, data);

                // The build should either finish successfully (very unlikely).
                result.OverallResult.ShouldBe(BuildResultCode.Success);
            }
            catch (Exception e)
            {
                // Or it should throw InternalErrorException because the node didn't get connected within 0ms.
                e.ShouldBeOfType<InternalErrorException>();
            }
        }

        [Fact]
        [ActiveIssue("https://github.com/Microsoft/msbuild/issues/4368")]
        public void GraphBuildValid()
        {
            string project1 = _env.CreateFile(".proj").Path;
            string project2 = _env.CreateFile(".proj").Path;

            File.WriteAllText(project1, CleanupFileContents($@"
<Project>
  <ItemGroup>
    <ProjectReferenceTargets Include='Build' Targets='Build' />
    <ProjectReference Include='{project2}' />
  </ItemGroup>
  <Target Name='Build'>
    <MsBuild Projects='{project2}' Targets='Build' />
  </Target>
</Project>
"));
            File.WriteAllText(project2, CleanupFileContents(@"
<Project>
  <Target Name='Build' />
</Project>
"));

            var graph = new ProjectGraph(project1);
            graph.ProjectNodes.Count.ShouldBe(2);

            var data = new GraphBuildRequestData(graph, Array.Empty<string>());

            GraphBuildResult result = _buildManager.Build(_parameters, data);
            result.OverallResult.ShouldBe(BuildResultCode.Success);

            var node1 = graph.ProjectNodes.First(node => node.ProjectInstance.FullPath.Equals(project1, StringComparison.OrdinalIgnoreCase));
            result.ResultsByNode.ContainsKey(node1).ShouldBeTrue();
            result.ResultsByNode[node1].OverallResult.ShouldBe(BuildResultCode.Success);

            var node2 = graph.ProjectNodes.First(node => node.ProjectInstance.FullPath.Equals(project2, StringComparison.OrdinalIgnoreCase));
            result.ResultsByNode.ContainsKey(node2).ShouldBeTrue();
            result.ResultsByNode[node2].OverallResult.ShouldBe(BuildResultCode.Success);
        }

        [Fact]
        [ActiveIssue("https://github.com/Microsoft/msbuild/issues/4368")]
        public void GraphBuildInvalid()
        {
            string project1 = _env.CreateFile(".proj").Path;
            string project2 = _env.CreateFile(".proj").Path;

            File.WriteAllText(project1, CleanupFileContents($@"
<Project>
  <ItemGroup>
    <ProjectReferenceTargets Include='Build' Targets='Build' />
    <ProjectReference Include='{project2}' />
  </ItemGroup>
  <Target Name='Build'>
    <MsBuild Projects='{project2}' Targets='Build' />
  </Target>
</Project>
"));
            File.WriteAllText(project2, CleanupFileContents(@"
<Project>
  <WellThisIsntValid>
</Project>
"));

            var data = new GraphBuildRequestData(new ProjectGraphEntryPoint(project1), Array.Empty<string>());

            GraphBuildResult result = _buildManager.Build(_parameters, data);
            result.OverallResult.ShouldBe(BuildResultCode.Failure);
            result.Exception.ShouldBeOfType<InvalidProjectFileException>()
                .ProjectFile.ShouldBe(project2);
        }

        [Fact]
        [ActiveIssue("https://github.com/Microsoft/msbuild/issues/4368")]
        public void GraphBuildFail()
        {
            string project1 = _env.CreateFile(".proj").Path;
            string project2 = _env.CreateFile(".proj").Path;

            File.WriteAllText(project1, CleanupFileContents($@"
<Project>
  <ItemGroup>
    <ProjectReferenceTargets Include='Build' Targets='Build' />
    <ProjectReference Include='{project2}' />
  </ItemGroup>
  <Target Name='Build'>
    <MsBuild Projects='{project2}' Targets='Build' />
  </Target>
</Project>
"));
            File.WriteAllText(project2, CleanupFileContents(@"
<Project>
  <Target Name='Build'>
    <Error Text='Fail!'/>
  </Target>
</Project>
"));

            var graph = new ProjectGraph(project1);
            graph.ProjectNodes.Count.ShouldBe(2);

            var data = new GraphBuildRequestData(graph, Array.Empty<string>());

            GraphBuildResult result = _buildManager.Build(_parameters, data);
            result.OverallResult.ShouldBe(BuildResultCode.Failure);

            var node1 = graph.ProjectNodes.First(node => node.ProjectInstance.FullPath.Equals(project1, StringComparison.OrdinalIgnoreCase));
            result.ResultsByNode.ContainsKey(node1).ShouldBeTrue();
            result.ResultsByNode[node1].OverallResult.ShouldBe(BuildResultCode.Failure);

            var node2 = graph.ProjectNodes.First(node => node.ProjectInstance.FullPath.Equals(project2, StringComparison.OrdinalIgnoreCase));
            result.ResultsByNode.ContainsKey(node2).ShouldBeTrue();
            result.ResultsByNode[node2].OverallResult.ShouldBe(BuildResultCode.Failure);
        }

        [Fact]
        [ActiveIssue("https://github.com/Microsoft/msbuild/issues/4368")]
        public void GraphBuildCircular()
        {
            string project1 = _env.CreateFile(".proj").Path;
            string project2 = _env.CreateFile(".proj").Path;

            File.WriteAllText(project1, CleanupFileContents($@"
<Project>
  <ItemGroup>
    <ProjectReferenceTargets Include='Build' Targets='Build' />
    <ProjectReference Include='{project2}' />
  </ItemGroup>
  <Target Name='Build'>
    <MsBuild Projects='{project2}' Targets='Build' />
  </Target>
</Project>
"));
            File.WriteAllText(project2, CleanupFileContents($@"
<Project>
  <ItemGroup>
    <ProjectReferenceTargets Include='Build' Targets='Build' />
    <ProjectReference Include='{project1}' />
  </ItemGroup>
  <Target Name='Build'>
    <MsBuild Projects='{project1}' Targets='Build' />
  </Target>
</Project>
"));

            var data = new GraphBuildRequestData(new ProjectGraphEntryPoint(project1), Array.Empty<string>());

            GraphBuildResult result = _buildManager.Build(_parameters, data);
            result.OverallResult.ShouldBe(BuildResultCode.Failure);
            result.CircularDependency.ShouldBeTrue();
        }

        [Fact]
        public void GraphBuildShouldBeAbleToConstructGraphButSkipBuild()
        {
            var graph = Helpers.CreateProjectGraph(env: _env, dependencyEdges: new Dictionary<int, int[]> {{1, new[] {2, 3}}});

            MockLogger logger = null;

            using (var buildSession = new Helpers.BuildManagerSession(_env))
            {
                var graphResult = buildSession.BuildGraphSubmission(
                    new GraphBuildRequestData(
                        projectGraphEntryPoints: new[] {new ProjectGraphEntryPoint(graph.GraphRoots.First().ProjectInstance.FullPath)},
                        targetsToBuild: new string[0],
                        hostServices: null,
                        flags: BuildRequestDataFlags.None,
                        graphBuildOptions: new GraphBuildOptions {Build = false}));

                graphResult.OverallResult.ShouldBe(BuildResultCode.Success);
                logger = buildSession.Logger;
            }

            logger.EvaluationStartedEvents.Count.ShouldBe(3);
            logger.ProjectStartedEvents.ShouldBeEmpty();
            logger.TargetStartedEvents.ShouldBeEmpty();
            logger.BuildStartedEvents.ShouldHaveSingleItem();
            logger.BuildFinishedEvents.ShouldHaveSingleItem();
            logger.FullLog.ShouldContain("Static graph loaded in");
            logger.FullLog.ShouldContain("3 nodes, 2 edges");
        }
    }
}
