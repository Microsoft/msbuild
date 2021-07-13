// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Build.BackEnd.Logging;
using Microsoft.Build.Construction;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;
using Microsoft.Build.Shared;


using TaskItem = Microsoft.Build.Execution.ProjectItemInstance.TaskItem;
using Shouldly;
using Xunit;
using Xunit.Abstractions;
using Microsoft.Build.Execution;

namespace Microsoft.Build.UnitTests
{
    public class ConsoleLoggerTest
    {
        /// <summary>
        /// For the environment writing test
        /// </summary>
        private Dictionary<string, string> _environment;

        private static string s_dummyProjectContents = @"
         <Project xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
            <Target Name='XXX'>
               <Message Text='[hee haw]'/>
            </Target>
            <Target Name='YYY' AfterTargets='XXX'>
            </Target>
            <Target Name='GGG' AfterTargets='XXX'>
               <Exec Command='echo a'/>
            </Target>
         </Project>";

        private class SimulatedConsole
        {
            private StringBuilder _simulatedConsole;

            internal SimulatedConsole()
            {
                _simulatedConsole = new StringBuilder();
            }

            internal void Clear()
            {
                _simulatedConsole = new StringBuilder();
            }

            public override string ToString()
            {
                return _simulatedConsole.ToString();
            }

            internal void Write(string s)
            {
                _simulatedConsole.Append(s);
            }

            internal void SetColor(ConsoleColor c)
            {
                switch (c)
                {
                    case ConsoleColor.Red:
                        _simulatedConsole.Append("<red>");
                        break;

                    case ConsoleColor.Yellow:
                        _simulatedConsole.Append("<yellow>");
                        break;

                    case ConsoleColor.Cyan:
                        _simulatedConsole.Append("<cyan>");
                        break;

                    case ConsoleColor.DarkGray:
                        _simulatedConsole.Append("<darkgray>");
                        break;

                    case ConsoleColor.Green:
                        _simulatedConsole.Append("<green>");
                        break;

                    default:
                        _simulatedConsole.Append("<ERROR: invalid color>");
                        break;
                }
            }

            internal void ResetColor()
            {
                _simulatedConsole.Append("<reset color>");
            }

            public static implicit operator string (SimulatedConsole sc)
            {
                return sc.ToString();
            }
        }

        private sealed class MyCustomBuildEventArgs : CustomBuildEventArgs
        {
            internal MyCustomBuildEventArgs(string message)
                : base(message, null, null)
            {
                // do nothing
            }
        }

        private class MyCustomBuildEventArgs2 : CustomBuildEventArgs { }

        private readonly ITestOutputHelper _output;

        public ConsoleLoggerTest(ITestOutputHelper output)
        {
            _environment = new Dictionary<string, string>();

            foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                _environment.Add((string)entry.Key, (string)entry.Value);
            }

            _output = output;
        }


        /// <summary>
        /// Verify when the project has not been named that we correctly get the same placeholder
        /// project name for project started event and the target started event.
        /// Test for BUG 579935
        /// </summary>
        [Fact]
        public void TestEmptyProjectNameForTargetStarted()
        {
            Microsoft.Build.Evaluation.Project project = new Microsoft.Build.Evaluation.Project();

            ProjectTargetElement target = project.Xml.AddTarget("T");
            ProjectTaskElement task = target.AddTask("Message");

            System.Xml.XmlAttribute attribute = task.XmlDocument.CreateAttribute("Text");
            attribute.Value = "HELLO";

            attribute = task.XmlDocument.CreateAttribute("MessageImportance");
            attribute.Value = "High";

            MockLogger mockLogger = new MockLogger();
            List<ILogger> loggerList = new List<ILogger>();
            loggerList.Add(mockLogger);
            project.Build(loggerList);

            List<ProjectStartedEventArgs> projectStartedEvents = mockLogger.ProjectStartedEvents;
            projectStartedEvents.Count.ShouldBe(1);
            string projectStartedName = projectStartedEvents[0].ProjectFile;
            projectStartedName.ShouldNotBeEmpty(); // "Expected project started name to not be null or empty"

            List<TargetStartedEventArgs> targetStartedEvents = mockLogger.TargetStartedEvents;
            targetStartedEvents.Count.ShouldBe(1);
            targetStartedEvents[0].ProjectFile.ShouldBe(projectStartedName); // "Expected the project started and target started target names to match"
        }


        /// <summary>
        /// Make sure the first message after a project started event prints out the target name. This was annoying a lot of people when there were messages right after the project
        /// started event but there was no target printed out.
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void TestTargetAfterProjectStarted()
        {
            SimulatedConsole sc = new SimulatedConsole();
            ConsoleLogger logger = new ConsoleLogger(LoggerVerbosity.Normal, sc.Write, null, null);
            logger.Parameters = "EnableMPLogging";
            ObjectModelHelpers.BuildProjectExpectSuccess(s_dummyProjectContents, logger);

            sc.ToString().ShouldContain("XXX:");
        }

        [Fact]
        public void WarningMessage()
        {
            using var env = TestEnvironment.Create(_output);

            var pc = env.CreateProjectCollection();

            var project = env.CreateTestProjectWithFiles(@"
         <Project>
            <ItemGroup>
                <P Include='$(MSBuildThisFileFullPath)' AdditionalProperties='Number=1' />
                <P Include='$(MSBuildThisFileFullPath)' AdditionalProperties='Number=2' />

                <ProjectConfigurationDescription Include='Number=$(Number)' />
            </ItemGroup>
            <Target Name='Spawn'>
                <MSBuild Projects='@(P)' BuildInParallel='true' Targets='Inner' />
            </Target>
            <Target Name='Inner'>
                <Warning Text='Hello from project $(Number)'
                         File='source_of_warning' />
            </Target>
        </Project>");

            SimulatedConsole sc = new SimulatedConsole();
            ConsoleLogger logger = new ConsoleLogger(LoggerVerbosity.Minimal, sc.Write, null, null);
            logger.Parameters = "EnableMPLogging;ShowProjectFile";

            pc.Collection.RegisterLogger(logger);
            var p = pc.Collection.LoadProject(project.ProjectFile);

            BuildManager.DefaultBuildManager.Build(
                new BuildParameters(pc.Collection),
                new BuildRequestData(p.CreateProjectInstance(), new[] { "Spawn" }));

            p.Build().ShouldBeTrue();
            sc.ToString().ShouldContain("source_of_warning : warning : Hello from project 1 [" + project.ProjectFile + ":: Number=1]");
            sc.ToString().ShouldContain("source_of_warning : warning : Hello from project 2 [" + project.ProjectFile + ":: Number=2]");
        }

        [Fact]
        public void ErrorMessage()
        {
            using var env = TestEnvironment.Create(_output);

            var pc = env.CreateProjectCollection();

            var project = env.CreateTestProjectWithFiles(@"
         <Project>
            <ItemGroup>
                <P Include='$(MSBuildThisFileFullPath)' AdditionalProperties='Number=1' />
                <P Include='$(MSBuildThisFileFullPath)' AdditionalProperties='Number=2' />

                <ProjectConfigurationDescription Include='Number=$(Number)' />
            </ItemGroup>
            <Target Name='Spawn'>
                <MSBuild Projects='@(P)' BuildInParallel='true' Targets='Inner' />
            </Target>
            <Target Name='Inner'>
                <Error Text='Hello from project $(Number)'
                         File='source_of_error' />
            </Target>
        </Project>");

            SimulatedConsole sc = new SimulatedConsole();
            ConsoleLogger logger = new ConsoleLogger(LoggerVerbosity.Minimal, sc.Write, null, null);
            logger.Parameters = "EnableMPLogging;ShowProjectFile";

            pc.Collection.RegisterLogger(logger);

            var p = pc.Collection.LoadProject(project.ProjectFile);

            BuildManager.DefaultBuildManager.Build(
                new BuildParameters(pc.Collection),
                new BuildRequestData(p.CreateProjectInstance(), new[] { "Spawn" }));

            p.Build().ShouldBeFalse();
            sc.ToString().ShouldContain("source_of_error : error : Hello from project 1 [" + project.ProjectFile + ":: Number=1]");
            sc.ToString().ShouldContain("source_of_error : error : Hello from project 2 [" + project.ProjectFile + ":: Number=2]");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ErrorMessageWithMultiplePropertiesInMessage(bool includeEvaluationPropertiesAndItems)
        {
            using var env = TestEnvironment.Create(_output);

            var pc = env.CreateProjectCollection();

            if (includeEvaluationPropertiesAndItems)
            {
                pc.Collection.LoggingService.IncludeEvaluationPropertiesAndItems = true;
            }

            var project = env.CreateTestProjectWithFiles(@"
         <Project>
            <PropertyGroup>
            <TargetFramework>netcoreapp2.1</TargetFramework>
            </PropertyGroup>
            <ItemGroup>
                <P Include='$(MSBuildThisFileFullPath)' AdditionalProperties='Number=1' />
                <P Include='$(MSBuildThisFileFullPath)' AdditionalProperties='Number=2' />
    
                <ProjectConfigurationDescription Include='Number=$(Number)' />
                <ProjectConfigurationDescription Include='TargetFramework=$(TargetFramework)' />
            </ItemGroup>
            <Target Name='Spawn'>
                <MSBuild Projects='@(P)' BuildInParallel='true' Targets='Inner' />
            </Target>
            <Target Name='Inner'>
                <Error Text='Hello from project $(Number)'
                         File='source_of_error' />
            </Target>
        </Project>");

            SimulatedConsole sc = new SimulatedConsole();
            ConsoleLogger logger = new ConsoleLogger(LoggerVerbosity.Minimal, sc.Write, null, null);
            logger.Parameters = "EnableMPLogging;ShowProjectFile";

            pc.Collection.RegisterLogger(logger);

            var p = pc.Collection.LoadProject(project.ProjectFile);

            BuildManager.DefaultBuildManager.Build(
                new BuildParameters(pc.Collection),
                new BuildRequestData(p.CreateProjectInstance(), new[] { "Spawn" }));

            p.Build().ShouldBeFalse();
            string output = sc.ToString();
            output.ShouldContain("source_of_error : error : Hello from project 1 [" + project.ProjectFile + ":: Number=1 TargetFramework=netcoreapp2.1]");
            output.ShouldContain("source_of_error : error : Hello from project 2 [" + project.ProjectFile + ":: Number=2 TargetFramework=netcoreapp2.1]");
        }

        [Fact(Skip = "https://github.com/dotnet/msbuild/issues/6518")]
        [SkipOnTargetFramework(TargetFrameworkMonikers.Netcoreapp, "Minimal path validation in Core allows expanding path containing quoted slashes.")]
        [SkipOnTargetFramework(TargetFrameworkMonikers.Mono, "Minimal path validation in Mono allows expanding path containing quoted slashes.")]
        public void TestItemsWithUnexpandableMetadata()
        {
            SimulatedConsole sc = new SimulatedConsole();
            ConsoleLogger logger = new ConsoleLogger(LoggerVerbosity.Diagnostic, sc.Write, null, null);
            ObjectModelHelpers.BuildProjectExpectSuccess(@"
<Project>
<ItemDefinitionGroup>
  <F>
   <MetadataFileName>a\b\%(Filename).c</MetadataFileName>
  </F>
 </ItemDefinitionGroup>
 <ItemGroup>
  <F Include=""-in &quot;x\y\z&quot;"" />
 </ItemGroup>
 <Target Name=""X"" />
</Project>", logger);

            var text = sc.ToString();
            text.ShouldContain("\"a\\b\\%(Filename).c\"");
        }

        /// <summary>
        /// Verify that on minimal verbosity the console logger does not log the target names.
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void TestNoTargetNameOnMinimal()
        {
            SimulatedConsole sc = new SimulatedConsole();
            ConsoleLogger logger = new ConsoleLogger(LoggerVerbosity.Minimal, sc.Write, null, null);
            logger.Parameters = "EnableMPLogging";
            ObjectModelHelpers.BuildProjectExpectSuccess(s_dummyProjectContents, logger);

            string log = sc.ToString();
            log.ShouldNotContain("XXX:");
            log.ShouldNotContain("YYY:");
            log.ShouldNotContain("GGG:");
        }

        /// <summary>
        /// Make sure if a target has no messages logged that its started and finished events show up on detailed but not normal.
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void EmptyTargetsOnDetailedButNotNormal()
        {
            SimulatedConsole sc = new SimulatedConsole();
            ConsoleLogger logger = new ConsoleLogger(LoggerVerbosity.Normal, sc.Write, null, null);
            logger.Parameters = "EnableMPLogging";
            ObjectModelHelpers.BuildProjectExpectSuccess(s_dummyProjectContents, logger);

            sc.ToString().ShouldNotContain("YYY:");

            sc = new SimulatedConsole();
            logger = new ConsoleLogger(LoggerVerbosity.Detailed, sc.Write, null, null);
            logger.Parameters = "EnableMPLogging";
            string tempProjectDir = Path.Combine(Path.GetTempPath(), "EmptyTargetsOnDetailedButNotNotmal");
            string tempProjectPath = Path.Combine(tempProjectDir, "test.proj");

            try
            {
                if (FileUtilities.DirectoryExistsNoThrow(tempProjectDir))
                {
                    FileUtilities.DeleteDirectoryNoThrow(tempProjectDir, true);
                }

                Directory.CreateDirectory(tempProjectDir);
                File.WriteAllText(tempProjectPath, s_dummyProjectContents);

                ObjectModelHelpers.BuildTempProjectFileWithTargets(tempProjectPath, null, null, logger);

                string targetStartedMessage = ResourceUtilities.FormatResourceStringStripCodeAndKeyword("TargetStartedProjectEntry", "YYY", tempProjectPath);

                sc.ToString().ShouldContain(targetStartedMessage);
            }
            finally
            {
                if (FileUtilities.DirectoryExistsNoThrow(tempProjectDir))
                {
                    FileUtilities.DeleteDirectoryNoThrow(tempProjectDir, true);
                }
            }
        }

        /// <summary>
        /// Test a number of cases where difference values from showcommandline are used with normal verbosity
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void ShowCommandLineWithNormalVerbosity()
        {
            string command = "echo a";

            SimulatedConsole sc = new SimulatedConsole();
            ConsoleLogger logger = new ConsoleLogger(LoggerVerbosity.Normal, sc.Write, null, null);
            logger.Parameters = "EnableMPLogging;ShowCommandLine";
            ObjectModelHelpers.BuildProjectExpectSuccess(s_dummyProjectContents, logger);

            sc.ToString().ShouldContain(command);

            sc = new SimulatedConsole();
            logger = new ConsoleLogger(LoggerVerbosity.Normal, sc.Write, null, null);
            logger.Parameters = "EnableMPLogging;ShowCommandLine=true";
            ObjectModelHelpers.BuildProjectExpectSuccess(s_dummyProjectContents, logger);

            sc.ToString().ShouldContain(command);

            sc = new SimulatedConsole();
            logger = new ConsoleLogger(LoggerVerbosity.Normal, sc.Write, null, null);
            logger.Parameters = "EnableMPLogging;ShowCommandLine=false";
            ObjectModelHelpers.BuildProjectExpectSuccess(s_dummyProjectContents, logger);

            sc.ToString().ShouldNotContain(command);

            sc = new SimulatedConsole();
            logger = new ConsoleLogger(LoggerVerbosity.Normal, sc.Write, null, null);
            logger.Parameters = "EnableMPLogging;ShowCommandLine=NotAbool";
            ObjectModelHelpers.BuildProjectExpectSuccess(s_dummyProjectContents, logger);

            sc.ToString().ShouldNotContain(command);

            sc = new SimulatedConsole();
            logger = new ConsoleLogger(LoggerVerbosity.Normal, sc.Write, null, null);
            logger.Parameters = "EnableMPLogging";
            ObjectModelHelpers.BuildProjectExpectSuccess(s_dummyProjectContents, logger);

            sc.ToString().ShouldContain(command);
        }

        /// <summary>
        /// We should not crash when given a null message, etc.
        /// </summary>
        [Fact]
        public void NullEventFields()
        {
            EventSourceSink es = new EventSourceSink();
            SimulatedConsole sc = new SimulatedConsole();
            ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Diagnostic,
                                                sc.Write, sc.SetColor,
                                                sc.ResetColor);
            L.Initialize(es);

            // Not all parameters are null here, but that's fine, we assume the engine will never
            // fire a ProjectStarted without a project name, etc.
            es.Consume(new BuildStartedEventArgs(null, null));
            es.Consume(new ProjectStartedEventArgs(null, null, "p", null, null, null));
            es.Consume(new TargetStartedEventArgs(null, null, "t", null, null));
            es.Consume(new TaskStartedEventArgs(null, null, null, null, "task"));
            es.Consume(new BuildMessageEventArgs(null, null, null, MessageImportance.High));
            es.Consume(new BuildWarningEventArgs(null, null, null, 0, 0, 0, 0, null, null, null));
            es.Consume(new BuildErrorEventArgs(null, null, null, 0, 0, 0, 0, null, null, null));
            es.Consume(new TaskFinishedEventArgs(null, null, null, null, "task", true));
            es.Consume(new TargetFinishedEventArgs(null, null, "t", null, null, true));
            es.Consume(new ProjectFinishedEventArgs(null, null, "p", true));
            es.Consume(new BuildFinishedEventArgs(null, null, true));
            es.Consume(new BuildFinishedEventArgs(null, null, true));
            es.Consume(new BuildFinishedEventArgs(null, null, true));
            es.Consume(new MyCustomBuildEventArgs2());
            // No exception raised
        }

        [Fact]
        public void NullEventFieldsParallel()
        {
            EventSourceSink es = new EventSourceSink();
            SimulatedConsole sc = new SimulatedConsole();
            ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Diagnostic,
                                                sc.Write, sc.SetColor,
                                                sc.ResetColor);
            L.Initialize(es, 2);
            BuildEventContext buildEventContext = new BuildEventContext(1, 2, 3, 4);

            BuildStartedEventArgs bse = new BuildStartedEventArgs(null, null);
            bse.BuildEventContext = buildEventContext;
            ProjectStartedEventArgs pse = new ProjectStartedEventArgs(-1, null, null, "p", null, null, null, buildEventContext);
            pse.BuildEventContext = buildEventContext;
            TargetStartedEventArgs trse = new TargetStartedEventArgs(null, null, "t", null, null);
            trse.BuildEventContext = buildEventContext;
            TaskStartedEventArgs tase = new TaskStartedEventArgs(null, null, null, null, "task");
            tase.BuildEventContext = buildEventContext;
            BuildMessageEventArgs bmea = new BuildMessageEventArgs(null, null, null, MessageImportance.High);
            bmea.BuildEventContext = buildEventContext;
            BuildWarningEventArgs bwea = new BuildWarningEventArgs(null, null, null, 0, 0, 0, 0, null, null, null);
            bwea.BuildEventContext = buildEventContext;
            BuildErrorEventArgs beea = new BuildErrorEventArgs(null, null, null, 0, 0, 0, 0, null, null, null);
            beea.BuildEventContext = buildEventContext;
            TaskFinishedEventArgs trfea = new TaskFinishedEventArgs(null, null, null, null, "task", true);
            trfea.BuildEventContext = buildEventContext;
            TargetFinishedEventArgs tafea = new TargetFinishedEventArgs(null, null, "t", null, null, true);
            tafea.BuildEventContext = buildEventContext;
            ProjectFinishedEventArgs pfea = new ProjectFinishedEventArgs(null, null, "p", true);
            pfea.BuildEventContext = buildEventContext;
            BuildFinishedEventArgs bfea = new BuildFinishedEventArgs(null, null, true);
            bfea.BuildEventContext = buildEventContext;
            MyCustomBuildEventArgs2 mcea = new MyCustomBuildEventArgs2();
            mcea.BuildEventContext = buildEventContext;


            // Not all parameters are null here, but that's fine, we assume the engine will never
            // fire a ProjectStarted without a project name, etc.
            es.Consume(bse);
            es.Consume(pse);
            es.Consume(trse);
            es.Consume(tase);
            es.Consume(bmea);
            es.Consume(bwea);
            es.Consume(beea);
            es.Consume(trfea);
            es.Consume(tafea);
            es.Consume(pfea);
            es.Consume(bfea);
            es.Consume(bfea);
            es.Consume(bfea);
            es.Consume(mcea);
            // No exception raised
        }

        [InlineData(LoggerVerbosity.Quiet, LoggerVerbosity.Quiet, true)]
        [InlineData(LoggerVerbosity.Quiet, LoggerVerbosity.Minimal, false)]
        [InlineData(LoggerVerbosity.Quiet, LoggerVerbosity.Normal, false)]
        [InlineData(LoggerVerbosity.Quiet, LoggerVerbosity.Detailed, false)]
        [InlineData(LoggerVerbosity.Quiet, LoggerVerbosity.Diagnostic, false)]
        // Minimal should return true for Quiet and Minimal
        [InlineData(LoggerVerbosity.Minimal, LoggerVerbosity.Quiet, true)]
        [InlineData(LoggerVerbosity.Minimal, LoggerVerbosity.Minimal, true)]
        [InlineData(LoggerVerbosity.Minimal, LoggerVerbosity.Normal, false)]
        [InlineData(LoggerVerbosity.Minimal, LoggerVerbosity.Detailed, false)]
        [InlineData(LoggerVerbosity.Minimal, LoggerVerbosity.Diagnostic, false)]
        // Normal should return true for Quiet, Minimal, and Normal
        [InlineData(LoggerVerbosity.Normal, LoggerVerbosity.Quiet, true)]
        [InlineData(LoggerVerbosity.Normal, LoggerVerbosity.Minimal, true)]
        [InlineData(LoggerVerbosity.Normal, LoggerVerbosity.Normal, true)]
        [InlineData(LoggerVerbosity.Normal, LoggerVerbosity.Detailed, false)]
        [InlineData(LoggerVerbosity.Normal, LoggerVerbosity.Diagnostic, false)]
        // Detailed should return true for Quiet, Minimal, Normal, and Detailed
        [InlineData(LoggerVerbosity.Detailed, LoggerVerbosity.Quiet, true)]
        [InlineData(LoggerVerbosity.Detailed, LoggerVerbosity.Minimal, true)]
        [InlineData(LoggerVerbosity.Detailed, LoggerVerbosity.Normal, true)]
        [InlineData(LoggerVerbosity.Detailed, LoggerVerbosity.Detailed, true)]
        [InlineData(LoggerVerbosity.Detailed, LoggerVerbosity.Diagnostic, false)]
        // Diagnostic should return true for Quiet, Minimal, Normal, Detailed, and Diagnostic
        [InlineData(LoggerVerbosity.Diagnostic, LoggerVerbosity.Quiet, true)]
        [InlineData(LoggerVerbosity.Diagnostic, LoggerVerbosity.Minimal, true)]
        [InlineData(LoggerVerbosity.Diagnostic, LoggerVerbosity.Normal, true)]
        [InlineData(LoggerVerbosity.Diagnostic, LoggerVerbosity.Detailed, true)]
        [InlineData(LoggerVerbosity.Diagnostic, LoggerVerbosity.Diagnostic, true)]
        [Theory]
        public void TestVerbosityLessThan(LoggerVerbosity loggerVerbosity, LoggerVerbosity checkVerbosity, bool expectedResult)
        {
            new SerialConsoleLogger(loggerVerbosity).IsVerbosityAtLeast(checkVerbosity).ShouldBe(expectedResult);
            new ParallelConsoleLogger(loggerVerbosity).IsVerbosityAtLeast(checkVerbosity).ShouldBe(expectedResult);
        }

        /// <summary>
        /// Test of single message printing
        /// </summary>
        // Quiet should show nothing
        [InlineData(LoggerVerbosity.Quiet, MessageImportance.Low, false)]
        [InlineData(LoggerVerbosity.Quiet, MessageImportance.Normal, false)]
        [InlineData(LoggerVerbosity.Quiet, MessageImportance.High, false)]
        // Minimal should show Low
        [InlineData(LoggerVerbosity.Minimal, MessageImportance.Low, false)]
        [InlineData(LoggerVerbosity.Minimal, MessageImportance.Normal, false)]
        [InlineData(LoggerVerbosity.Minimal, MessageImportance.High, true)]
        // Normal should show Low and Normal
        [InlineData(LoggerVerbosity.Normal, MessageImportance.Low, false)]
        [InlineData(LoggerVerbosity.Normal, MessageImportance.Normal, true)]
        [InlineData(LoggerVerbosity.Normal, MessageImportance.High, true)]
        // Detailed should show Low, Normal, and High
        [InlineData(LoggerVerbosity.Detailed, MessageImportance.Low, true)]
        [InlineData(LoggerVerbosity.Detailed, MessageImportance.Normal, true)]
        [InlineData(LoggerVerbosity.Detailed, MessageImportance.High, true)]
        // Diagnostic should show everything
        [InlineData(LoggerVerbosity.Diagnostic, MessageImportance.Low, true)]
        [InlineData(LoggerVerbosity.Diagnostic, MessageImportance.Normal, true)]
        [InlineData(LoggerVerbosity.Diagnostic, MessageImportance.High, true)]
        [Theory]
        public void SingleMessageTest(LoggerVerbosity loggerVerbosity, MessageImportance messageImportance, bool shouldPrint)
        {
            for (int i = 1; i <= 2; i++)
            {
                string message = "my 1337 message";

                SimulatedConsole console = new SimulatedConsole();
                EventSourceSink eventSourceSink = new EventSourceSink();
                ConsoleLogger logger = new ConsoleLogger(loggerVerbosity, console.Write, null, null);
                logger.Initialize(eventSourceSink, i);

                BuildMessageEventArgs be = new BuildMessageEventArgs(message, "help", "sender", messageImportance)
                {
                    BuildEventContext = new BuildEventContext(1, 2, 3, 4)
                };

                eventSourceSink.Consume(be);

                if (i == 2 && loggerVerbosity == LoggerVerbosity.Diagnostic)
                {
                    string context = ResourceUtilities.FormatResourceStringStripCodeAndKeyword("BuildEventContext", LogFormatter.FormatLogTimeStamp(be.Timestamp), 0) + ">";
                    message = context + ResourceUtilities.FormatResourceStringStripCodeAndKeyword("TaskMessageWithId", "my 1337 message", be.BuildEventContext.TaskId);
                }
                else if (i == 2 && loggerVerbosity == LoggerVerbosity.Detailed)
                {
                    string context = ResourceUtilities.FormatResourceStringStripCodeAndKeyword("BuildEventContext", string.Empty, 0) + ">";
                    message = context + "my 1337 message";
                }
                else if (i == 2)
                {
                    message = "  " + message;
                }

                if (shouldPrint)
                {
                    console.ToString().ShouldBe(message + Environment.NewLine);
                }
                else
                {
                    console.ToString().ShouldBeEmpty();
                }
            }
        }

        [InlineData("error", "red", false)]
        [InlineData("error", "red", true)]
        [InlineData("warning", "yellow", false)]
        [InlineData("warning", "yellow", true)]
        [InlineData("message", "darkgray", false)]
        [InlineData("message", "darkgray", true)]
        [Theory]
        public void ColorTest(string expectedMessageType, string expectedColor, bool parallel)
        {
            const string subcategory = "VBC";
            const string code = "31415";
            const string file = "file.vb";
            const int lineNumber = 42;
            const int columnNumber = 0;
            const int endLineNumber = 0;
            const int endColumnNumber = 0;
            const string message = "Some long message";
            const string helpKeyword = "help";
            const string senderName = "sender";

            BuildEventArgs buildEventArgs;

            if (expectedMessageType.Equals("error"))
            {
                buildEventArgs = new BuildErrorEventArgs(subcategory, code, file, lineNumber, columnNumber, endLineNumber, endColumnNumber, message, helpKeyword, senderName);
            }
            else if (expectedMessageType.Equals("warning"))
            {
                buildEventArgs = new BuildWarningEventArgs(subcategory, code, file, lineNumber, columnNumber, endLineNumber, endColumnNumber, message, helpKeyword, senderName);
            }
            else if (expectedMessageType.Equals("message"))
            {
                buildEventArgs = new BuildMessageEventArgs(subcategory, code, file, lineNumber, columnNumber, endLineNumber, endColumnNumber, message, helpKeyword, senderName, MessageImportance.Low);
            }
            else
            {
                throw new InvalidOperationException($"Invalid expectedMessageType '{expectedMessageType}'");
            }

            buildEventArgs.BuildEventContext = new BuildEventContext(1, 2, 3, 4);

            EventSourceSink eventSourceSink = new EventSourceSink();
            SimulatedConsole console = new SimulatedConsole();
            ConsoleLogger logger = new ConsoleLogger(LoggerVerbosity.Diagnostic, console.Write, console.SetColor, console.ResetColor);

            if (parallel)
            {
                logger.Initialize(eventSourceSink, 4);
                eventSourceSink.Consume(buildEventArgs, 2);

                if (expectedMessageType.Equals("message"))
                {
                    console.ToString().ShouldMatch($@"<{expectedColor}><cyan>\d\d:\d\d:\d\d\.\d\d\d\s+\d+><reset color>{Regex.Escape(file)}\({lineNumber}\): {subcategory} {expectedMessageType} {code}: {message} \(TaskId:\d+\){Environment.NewLine}<reset color>");
                }
                else
                {
                    console.ToString().ShouldMatch($@"<cyan>\d\d:\d\d:\d\d\.\d\d\d\s+\d+><reset color><{expectedColor}>{Regex.Escape(file)}\({lineNumber}\): {subcategory} {expectedMessageType} {code}: {message}{Environment.NewLine}<reset color>");
                }
            }
            else
            {
                logger.Initialize(eventSourceSink);
                eventSourceSink.Consume(buildEventArgs);
                console.ToString().ShouldMatch($@"<{expectedColor}>{Regex.Escape(file)}\({lineNumber}\): {subcategory} {expectedMessageType} {code}: {message}{Environment.NewLine}<reset color>");
            }
        }


        [Fact]
        public void TestQuietWithHighMessage()
        {
            for (int i = 1; i <= 2; i++)
            {
                EventSourceSink es = new EventSourceSink();
                SimulatedConsole sc = new SimulatedConsole();
                ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Quiet,
                                                    sc.Write, sc.SetColor,
                                                    sc.ResetColor);
                L.Initialize(es, i);

                BuildEventContext buildEventContext = new BuildEventContext(1, 2, 3, 4);

                BuildStartedEventArgs bse = new BuildStartedEventArgs("bs", null);
                bse.BuildEventContext = buildEventContext;
                es.Consume(bse);

                ProjectStartedEventArgs pse = new ProjectStartedEventArgs(1, "ps", null, "fname", "", null, null, new BuildEventContext(1, 1, 1, 1));
                pse.BuildEventContext = buildEventContext;
                es.Consume(pse);

                TargetStartedEventArgs trse = new TargetStartedEventArgs("ts", null, "trname", "pfile", "tfile");
                trse.BuildEventContext = buildEventContext;
                es.Consume(trse);

                TaskStartedEventArgs tase = new TaskStartedEventArgs("tks", null, "tname", "tfname", "tsname");
                tase.BuildEventContext = buildEventContext;
                es.Consume(tase);

                BuildMessageEventArgs bmea = new BuildMessageEventArgs("foo!", null, "sender", MessageImportance.High);
                bmea.BuildEventContext = buildEventContext;
                es.Consume(bmea);

                TaskFinishedEventArgs tafea = new TaskFinishedEventArgs("tkf", null, "fname", "tsname", "tfname", true);
                tafea.BuildEventContext = buildEventContext;
                es.Consume(tafea);

                TargetFinishedEventArgs trfea = new TargetFinishedEventArgs("tf", null, "trname", "fname", "tfile", true);
                trfea.BuildEventContext = buildEventContext;
                es.Consume(trfea);

                ProjectFinishedEventArgs pfea = new ProjectFinishedEventArgs("pf", null, "fname", true);
                pfea.BuildEventContext = buildEventContext;
                es.Consume(pfea);

                BuildFinishedEventArgs bfea = new BuildFinishedEventArgs("bf", null, true);
                bfea.BuildEventContext = buildEventContext;
                es.Consume(bfea);

                sc.ToString().ShouldBeEmpty();
            }
        }

        [Fact]
        public void TestQuietWithError()
        {
            for (int i = 1; i <= 2; i++)
            {
                EventSourceSink es = new EventSourceSink();
                SimulatedConsole sc = new SimulatedConsole();
                ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Quiet,
                                                    sc.Write, sc.SetColor, sc.ResetColor);
                L.Initialize(es, i);

                BuildEventContext buildEventContext = new BuildEventContext(1, 2, 3, 4);

                BuildStartedEventArgs bse = new BuildStartedEventArgs("bs", null);
                bse.BuildEventContext = buildEventContext;
                es.Consume(bse);

                ProjectStartedEventArgs pse = new ProjectStartedEventArgs(-1, "ps", null, "fname", "", null, null, new BuildEventContext(1, 2, 3, 4));
                pse.BuildEventContext = buildEventContext;
                es.Consume(pse);

                TargetStartedEventArgs trse = new TargetStartedEventArgs("ts", null, "trname", "pfile", "tfile");
                trse.BuildEventContext = buildEventContext;
                es.Consume(trse);

                TaskStartedEventArgs tase = new TaskStartedEventArgs("tks", null, "tname", "tfname", "tsname");
                tase.BuildEventContext = buildEventContext;
                es.Consume(tase);

                BuildErrorEventArgs beea = new BuildErrorEventArgs("VBC",
                                "31415", "file.vb", 42, 0, 0, 0,
                                "Some long message", "help", "sender");

                beea.BuildEventContext = buildEventContext;
                es.Consume(beea);

                TaskFinishedEventArgs tafea = new TaskFinishedEventArgs("tkf", null, "fname", "tsname", "tfname", true);
                tafea.BuildEventContext = buildEventContext;
                es.Consume(tafea);

                TargetFinishedEventArgs trfea = new TargetFinishedEventArgs("tf", null, "trname", "fname", "tfile", true);
                trfea.BuildEventContext = buildEventContext;
                es.Consume(trfea);

                ProjectFinishedEventArgs pfea = new ProjectFinishedEventArgs("pf", null, "fname", true);
                pfea.BuildEventContext = buildEventContext;
                es.Consume(pfea);

                BuildFinishedEventArgs bfea = new BuildFinishedEventArgs("bf", null, true);
                bfea.BuildEventContext = buildEventContext;
                es.Consume(bfea);

                _output.WriteLine("==");
                _output.WriteLine(sc.ToString());
                _output.WriteLine("==");

                if (i == 1)
                {
                    sc.ToString().ShouldBe(
                            "<cyan>" + BaseConsoleLogger.projectSeparatorLine + Environment.NewLine +
                            ResourceUtilities.FormatResourceStringStripCodeAndKeyword("ProjectStartedPrefixForTopLevelProjectWithDefaultTargets", "fname") + Environment.NewLine + Environment.NewLine +
                            "<reset color><red>file.vb(42): VBC error 31415: Some long message" + Environment.NewLine +
                            "<reset color><cyan>pf" + Environment.NewLine +
                            "<reset color>");
                }
                else
                {
                    sc.ToString().ShouldBe("<red>file.vb(42): VBC error 31415: Some long message" + Environment.NewLine + "<reset color>");
                }
            }
        }

        /// <summary>
        /// Quiet build with a warning; project finished should appear
        /// but not target finished
        /// </summary>
        [Fact]
        public void TestQuietWithWarning()
        {
            for (int i = 1; i <= 2; i++)
            {
                EventSourceSink es = new EventSourceSink();
                SimulatedConsole sc = new SimulatedConsole();
                ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Quiet,
                                                    sc.Write, sc.SetColor, sc.ResetColor);
                L.Initialize(es, i);

                BuildEventContext buildEventContext = new BuildEventContext(1, 2, 3, 4);

                BuildStartedEventArgs bse = new BuildStartedEventArgs("bs", null);
                bse.BuildEventContext = buildEventContext;
                es.Consume(bse);

                ProjectStartedEventArgs pse = new ProjectStartedEventArgs(-1, "ps", null, "fname", "", null, null, new BuildEventContext(1, 2, 3, 4));
                pse.BuildEventContext = buildEventContext;
                es.Consume(pse);

                TargetStartedEventArgs trse = new TargetStartedEventArgs("ts", null, "trname", "pfile", "tfile");
                trse.BuildEventContext = buildEventContext;
                es.Consume(trse);

                TaskStartedEventArgs tase = new TaskStartedEventArgs("tks", null, "tname", "tfname", "tsname");
                tase.BuildEventContext = buildEventContext;
                es.Consume(tase);

                BuildWarningEventArgs beea = new BuildWarningEventArgs("VBC",
                                "31415", "file.vb", 42, 0, 0, 0,
                                "Some long message", "help", "sender");


                beea.BuildEventContext = buildEventContext;
                es.Consume(beea);

                TaskFinishedEventArgs tafea = new TaskFinishedEventArgs("tkf", null, "fname", "tsname", "tfname", true);
                tafea.BuildEventContext = buildEventContext;
                es.Consume(tafea);

                TargetFinishedEventArgs trfea = new TargetFinishedEventArgs("tf", null, "trname", "fname", "tfile", true);
                trfea.BuildEventContext = buildEventContext;
                es.Consume(trfea);

                ProjectFinishedEventArgs pfea = new ProjectFinishedEventArgs("pf", null, "fname", true);
                pfea.BuildEventContext = buildEventContext;
                es.Consume(pfea);

                BuildFinishedEventArgs bfea = new BuildFinishedEventArgs("bf", null, true);
                bfea.BuildEventContext = buildEventContext;
                es.Consume(bfea);

                _output.WriteLine("==");
                _output.WriteLine(sc.ToString());
                _output.WriteLine("==");

                if (i == 1)
                {
                    sc.ToString().ShouldBe(
                            "<cyan>" + BaseConsoleLogger.projectSeparatorLine + Environment.NewLine +
                            ResourceUtilities.FormatResourceStringStripCodeAndKeyword("ProjectStartedPrefixForTopLevelProjectWithDefaultTargets", "fname") + Environment.NewLine + Environment.NewLine +
                            "<reset color><yellow>file.vb(42): VBC warning 31415: Some long message" + Environment.NewLine +
                            "<reset color><cyan>pf" + Environment.NewLine +
                            "<reset color>");
                }
                else
                {
                    sc.ToString().ShouldBe("<yellow>file.vb(42): VBC warning 31415: Some long message" + Environment.NewLine + "<reset color>");
                }
            }
        }

        /// <summary>
        /// Minimal with no errors or warnings should emit nothing.
        /// </summary>
        [Fact]
        public void TestMinimalWithNormalMessage()
        {
            for (int i = 1; i <= 2; i++)
            {
                EventSourceSink es = new EventSourceSink();
                SimulatedConsole sc = new SimulatedConsole();
                ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Minimal,
                                                    sc.Write, sc.SetColor,
                                                    sc.ResetColor);
                L.Initialize(es, i);

                BuildEventContext buildEventContext = new BuildEventContext(1, 2, 3, 4);

                BuildStartedEventArgs bse = new BuildStartedEventArgs("bs", null);
                bse.BuildEventContext = buildEventContext;
                es.Consume(bse);

                ProjectStartedEventArgs pse = new ProjectStartedEventArgs(1, "ps", null, "fname", "", null, null, new BuildEventContext(1, 1, 1, 1));
                pse.BuildEventContext = buildEventContext;
                es.Consume(pse);

                TargetStartedEventArgs trse = new TargetStartedEventArgs("ts", null, "trname", "pfile", "tfile");
                trse.BuildEventContext = buildEventContext;
                es.Consume(trse);

                TaskStartedEventArgs tase = new TaskStartedEventArgs("tks", null, "tname", "tfname", "tsname");
                tase.BuildEventContext = buildEventContext;
                es.Consume(tase);

                BuildMessageEventArgs bmea = new BuildMessageEventArgs("foo!", null, "sender", MessageImportance.Normal);
                bmea.BuildEventContext = buildEventContext;
                es.Consume(bmea);

                TaskFinishedEventArgs tafea = new TaskFinishedEventArgs("tkf", null, "fname", "tsname", "tfname", true);
                tafea.BuildEventContext = buildEventContext;
                es.Consume(tafea);

                TargetFinishedEventArgs trfea = new TargetFinishedEventArgs("tf", null, "trname", "fname", "tfile", true);
                trfea.BuildEventContext = buildEventContext;
                es.Consume(trfea);

                ProjectFinishedEventArgs pfea = new ProjectFinishedEventArgs("pf", null, "fname", true);
                pfea.BuildEventContext = buildEventContext;
                es.Consume(pfea);

                BuildFinishedEventArgs bfea = new BuildFinishedEventArgs("bf", null, true);
                bfea.BuildEventContext = buildEventContext;
                es.Consume(bfea);

                sc.ToString().ShouldBeEmpty();
            }
        }

        /// <summary>
        /// Minimal with error should emit project started, the error, and project finished
        /// </summary>
        [Fact]
        public void TestMinimalWithError()
        {
            for (int i = 1; i <= 2; i++)
            {
                EventSourceSink es = new EventSourceSink();
                SimulatedConsole sc = new SimulatedConsole();
                ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Minimal,
                                                    sc.Write, sc.SetColor, sc.ResetColor);
                L.Initialize(es, i);

                BuildEventContext buildEventContext = new BuildEventContext(1, 2, 3, 4);

                BuildStartedEventArgs bse = new BuildStartedEventArgs("bs", null);
                bse.BuildEventContext = buildEventContext;
                es.Consume(bse);

                ProjectStartedEventArgs pse = new ProjectStartedEventArgs(-1, "ps", null, "fname", "", null, null, new BuildEventContext(1, 2, 3, 4));
                pse.BuildEventContext = buildEventContext;
                es.Consume(pse);

                TargetStartedEventArgs trse = new TargetStartedEventArgs("ts", null, "trname", "pfile", "tfile");
                trse.BuildEventContext = buildEventContext;
                es.Consume(trse);

                TaskStartedEventArgs tase = new TaskStartedEventArgs("tks", null, "tname", "tfname", "tsname");
                tase.BuildEventContext = buildEventContext;
                es.Consume(tase);

                BuildErrorEventArgs beea = new BuildErrorEventArgs("VBC",
                                "31415", "file.vb", 42, 0, 0, 0,
                                "Some long message", "help", "sender");

                beea.BuildEventContext = buildEventContext;
                es.Consume(beea);

                TaskFinishedEventArgs tafea = new TaskFinishedEventArgs("tkf", null, "fname", "tsname", "tfname", true);
                tafea.BuildEventContext = buildEventContext;
                es.Consume(tafea);

                TargetFinishedEventArgs trfea = new TargetFinishedEventArgs("tf", null, "trname", "fname", "tfile", true);
                trfea.BuildEventContext = buildEventContext;
                es.Consume(trfea);

                ProjectFinishedEventArgs pfea = new ProjectFinishedEventArgs("pf", null, "fname", true);
                pfea.BuildEventContext = buildEventContext;
                es.Consume(pfea);

                BuildFinishedEventArgs bfea = new BuildFinishedEventArgs("bf", null, true);
                bfea.BuildEventContext = buildEventContext;
                es.Consume(bfea);

                _output.WriteLine("==");
                _output.WriteLine(sc.ToString());
                _output.WriteLine("==");

                if (i == 1)
                {
                    sc.ToString().ShouldBe(
                            "<cyan>" + BaseConsoleLogger.projectSeparatorLine + Environment.NewLine +
                            ResourceUtilities.FormatResourceStringStripCodeAndKeyword("ProjectStartedPrefixForTopLevelProjectWithDefaultTargets", "fname") + Environment.NewLine + Environment.NewLine +
                            "<reset color><red>file.vb(42): VBC error 31415: Some long message" + Environment.NewLine +
                            "<reset color><cyan>pf" + Environment.NewLine +
                            "<reset color>");
                }
                else
                {
                    sc.ToString().ShouldBe("<red>file.vb(42): VBC error 31415: Some long message" + Environment.NewLine + "<reset color>");
                }
            }
        }

        /// <summary>
        /// Minimal with warning should emit project started, the warning, and project finished
        /// </summary>
        [Fact]
        public void TestMinimalWithWarning()
        {
            for (int i = 1; i <= 2; i++)
            {
                EventSourceSink es = new EventSourceSink();
                SimulatedConsole sc = new SimulatedConsole();
                ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Quiet,
                                                    sc.Write, sc.SetColor, sc.ResetColor);
                L.Initialize(es, i);

                BuildEventContext buildEventContext = new BuildEventContext(1, 2, 3, 4);

                BuildStartedEventArgs bse = new BuildStartedEventArgs("bs", null);
                bse.BuildEventContext = buildEventContext;
                es.Consume(bse);

                ProjectStartedEventArgs pse = new ProjectStartedEventArgs(-1, "ps", null, "fname", "", null, null, new BuildEventContext(1, 2, 3, 4));
                pse.BuildEventContext = buildEventContext;
                es.Consume(pse);

                TargetStartedEventArgs trse = new TargetStartedEventArgs("ts", null, "trname", "pfile", "tfile");
                trse.BuildEventContext = buildEventContext;
                es.Consume(trse);

                TaskStartedEventArgs tase = new TaskStartedEventArgs("tks", null, "tname", "tfname", "tsname");
                tase.BuildEventContext = buildEventContext;
                es.Consume(tase);

                BuildWarningEventArgs beea = new BuildWarningEventArgs("VBC",
                                "31415", "file.vb", 42, 0, 0, 0,
                                "Some long message", "help", "sender");


                beea.BuildEventContext = buildEventContext;
                es.Consume(beea);

                TaskFinishedEventArgs tafea = new TaskFinishedEventArgs("tkf", null, "fname", "tsname", "tfname", true);
                tafea.BuildEventContext = buildEventContext;
                es.Consume(tafea);

                TargetFinishedEventArgs trfea = new TargetFinishedEventArgs("tf", null, "trname", "fname", "tfile", true);
                trfea.BuildEventContext = buildEventContext;
                es.Consume(trfea);

                ProjectFinishedEventArgs pfea = new ProjectFinishedEventArgs("pf", null, "fname", true);
                pfea.BuildEventContext = buildEventContext;
                es.Consume(pfea);

                BuildFinishedEventArgs bfea = new BuildFinishedEventArgs("bf", null, true);
                bfea.BuildEventContext = buildEventContext;
                es.Consume(bfea);

                _output.WriteLine("==");
                _output.WriteLine(sc.ToString());
                _output.WriteLine("==");

                if (i == 1)
                {
                    sc.ToString().ShouldBe(
                            "<cyan>" + BaseConsoleLogger.projectSeparatorLine + Environment.NewLine +
                            ResourceUtilities.FormatResourceStringStripCodeAndKeyword("ProjectStartedPrefixForTopLevelProjectWithDefaultTargets", "fname") + Environment.NewLine + Environment.NewLine +
                            "<reset color><yellow>file.vb(42): VBC warning 31415: Some long message" + Environment.NewLine +
                            "<reset color><cyan>pf" + Environment.NewLine +
                            "<reset color>");
                }
                else
                {
                    sc.ToString().ShouldBe("<yellow>file.vb(42): VBC warning 31415: Some long message" + Environment.NewLine + "<reset color>");
                }
            }
        }

        /// <summary>
        /// Minimal with warning should emit project started, the warning, and project finished
        /// </summary>
        [Fact]
        public void TestDirectEventHandlers()
        {
            for (int i = 1; i <= 2; i++)
            {
                EventSourceSink es = new EventSourceSink();
                SimulatedConsole sc = new SimulatedConsole();
                ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Quiet,
                                                    sc.Write, sc.SetColor, sc.ResetColor);
                L.Initialize(es, i);

                BuildEventContext buildEventContext = new BuildEventContext(1, 2, 3, 4);

                BuildStartedEventArgs bse = new BuildStartedEventArgs("bs", null);
                bse.BuildEventContext = buildEventContext;
                L.BuildStartedHandler(null, bse);

                ProjectStartedEventArgs pse = new ProjectStartedEventArgs(-1, "ps", null, "fname", "", null, null, new BuildEventContext(1, 2, 3, 4));
                pse.BuildEventContext = buildEventContext;
                L.ProjectStartedHandler(null, pse);

                TargetStartedEventArgs trse = new TargetStartedEventArgs("ts", null, "trname", "pfile", "tfile");
                trse.BuildEventContext = buildEventContext;
                L.TargetStartedHandler(null, trse);

                TaskStartedEventArgs tase = new TaskStartedEventArgs("tks", null, "tname", "tfname", "tsname");
                tase.BuildEventContext = buildEventContext;
                L.TaskStartedHandler(null, tase);

                BuildWarningEventArgs beea = new BuildWarningEventArgs("VBC",
                                "31415", "file.vb", 42, 0, 0, 0,
                                "Some long message", "help", "sender");


                beea.BuildEventContext = buildEventContext;
                L.WarningHandler(null, beea);

                TaskFinishedEventArgs tafea = new TaskFinishedEventArgs("tkf", null, "fname", "tsname", "tfname", true);
                tafea.BuildEventContext = buildEventContext;
                L.TaskFinishedHandler(null, tafea);

                TargetFinishedEventArgs trfea = new TargetFinishedEventArgs("tf", null, "trname", "fname", "tfile", true);
                trfea.BuildEventContext = buildEventContext;
                L.TargetFinishedHandler(null, trfea);

                ProjectFinishedEventArgs pfea = new ProjectFinishedEventArgs("pf", null, "fname", true);
                pfea.BuildEventContext = buildEventContext;
                L.ProjectFinishedHandler(null, pfea);

                BuildFinishedEventArgs bfea = new BuildFinishedEventArgs("bf", null, true);
                bfea.BuildEventContext = buildEventContext;
                L.BuildFinishedHandler(null, bfea);

                _output.WriteLine("==");
                _output.WriteLine(sc.ToString());
                _output.WriteLine("==");

                if (i == 1)
                {
                    sc.ToString().ShouldBe(
                            "<cyan>" + BaseConsoleLogger.projectSeparatorLine + Environment.NewLine +
                            ResourceUtilities.FormatResourceStringStripCodeAndKeyword("ProjectStartedPrefixForTopLevelProjectWithDefaultTargets", "fname") + Environment.NewLine + Environment.NewLine +
                            "<reset color><yellow>file.vb(42): VBC warning 31415: Some long message" + Environment.NewLine +
                            "<reset color><cyan>pf" + Environment.NewLine +
                            "<reset color>");
                }
                else
                {
                    sc.ToString().ShouldBe("<yellow>file.vb(42): VBC warning 31415: Some long message" + Environment.NewLine + "<reset color>");
                }
            }
        }

        [Fact]
        public void SingleLineFormatNoop()
        {
            string s = "foo";
            SerialConsoleLogger cl = new SerialConsoleLogger();

            string ss = cl.IndentString(s, 0);

            //should be a no-op
            ss.ShouldBe($"foo{Environment.NewLine}");
        }

        [Fact]
        public void MultilineFormatWindowsLineEndings()
        {
            string newline = "\r\n";
            string s = "foo" + newline + "bar" +
                       newline + "baz" + newline;
            SerialConsoleLogger cl = new SerialConsoleLogger();

            string ss = cl.IndentString(s, 4);

            //should convert lines to system format
            ss.ShouldBe($"    foo{Environment.NewLine}    bar{Environment.NewLine}    baz{Environment.NewLine}    {Environment.NewLine}");
        }

        [Fact]
        public void MultilineFormatUnixLineEndings()
        {
            string s = "foo\nbar\nbaz\n";
            SerialConsoleLogger cl = new SerialConsoleLogger();

            string ss = cl.IndentString(s, 0);

            //should convert lines to system format
            ss.ShouldBe($"foo{Environment.NewLine}bar{Environment.NewLine}baz{Environment.NewLine}{Environment.NewLine}");
        }

        [Fact]
        public void MultilineFormatMixedLineEndings()
        {
            string s = "foo" + "\r\n\r\n" + "bar" + "\n" + "baz" + "\n\r\n\n" +
                "jazz" + "\r\n" + "razz" + "\n\n" + "matazz" + "\n" + "end";

            SerialConsoleLogger cl = new SerialConsoleLogger();

            string ss = cl.IndentString(s, 0);

            //should convert lines to system format
            ss.ShouldBe($"foo{Environment.NewLine}{Environment.NewLine}bar{Environment.NewLine}baz{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}jazz{Environment.NewLine}razz{Environment.NewLine}{Environment.NewLine}matazz{Environment.NewLine}end{Environment.NewLine}");
        }

        [Fact]
        public void NestedProjectMinimal()
        {
            EventSourceSink es = new EventSourceSink();
            SimulatedConsole sc = new SimulatedConsole();
            ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Minimal,
                                                sc.Write, sc.SetColor, sc.ResetColor);
            L.Initialize(es, 1);

            es.Consume(new BuildStartedEventArgs("bs", null));

            //Clear time dependent build started message
            sc.Clear();

            es.Consume(new ProjectStartedEventArgs("ps1", null, "fname1", "", null, null));

            es.Consume(new TargetStartedEventArgs("ts", null,
                                                     "trname", "fname", "tfile"));

            es.Consume(new ProjectStartedEventArgs("ps2", null, "fname2", "", null, null));

            sc.ToString().ShouldBeEmpty();

            BuildErrorEventArgs beea = new BuildErrorEventArgs("VBC",
                        "31415", "file.vb", 42, 0, 0, 0,
                        "Some long message", "help", "sender");

            es.Consume(beea);

            sc.ToString().ShouldBe(
                "<cyan>" + BaseConsoleLogger.projectSeparatorLine + Environment.NewLine +
                ResourceUtilities.FormatResourceStringStripCodeAndKeyword("ProjectStartedPrefixForTopLevelProjectWithDefaultTargets", "fname1") + Environment.NewLine +
                                        Environment.NewLine + "<reset color>" +
                "<cyan>" + BaseConsoleLogger.projectSeparatorLine + Environment.NewLine +
                ResourceUtilities.FormatResourceStringStripCodeAndKeyword("ProjectStartedPrefixForNestedProjectWithDefaultTargets", "fname1", "fname2") + Environment.NewLine +
                                                      Environment.NewLine + "<reset color>" +
                "<red>" + "file.vb(42): VBC error 31415: Some long message" +
                                                      Environment.NewLine + "<reset color>");
        }

        [Fact]
        public void NestedProjectNormal()
        {
            EventSourceSink es = new EventSourceSink();
            SimulatedConsole sc = new SimulatedConsole();
            ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Normal,
                                                sc.Write, sc.SetColor, sc.ResetColor);
            L.Initialize(es);

            es.Consume(new BuildStartedEventArgs("bs", null));


            //Clear time dependent build started message
            sc.Clear();

            es.Consume(new ProjectStartedEventArgs("ps1", null, "fname1", "", null, null));

            sc.ToString().ShouldBe("<cyan>" + BaseConsoleLogger.projectSeparatorLine + Environment.NewLine +
                                   ResourceUtilities.FormatResourceStringStripCodeAndKeyword("ProjectStartedPrefixForTopLevelProjectWithDefaultTargets", "fname1") + Environment.NewLine +
                                   Environment.NewLine + "<reset color>");

            sc.Clear();

            es.Consume(new TargetStartedEventArgs("ts", null,
                                                     "tarname", "fname", "tfile"));
            sc.ToString().ShouldBeEmpty();

            sc.Clear();

            es.Consume(new TaskStartedEventArgs("", "", "", "", "Exec"));
            es.Consume(new ProjectStartedEventArgs("ps2", null, "fname2", "", null, null));

            sc.ToString().ShouldBe(
                "<cyan>" + ResourceUtilities.FormatResourceStringStripCodeAndKeyword("TargetStartedPrefix", "tarname") + Environment.NewLine + "<reset color>"
                + "<cyan>" + "    " + BaseConsoleLogger.projectSeparatorLine
                                          + Environment.NewLine +
                "    " + ResourceUtilities.FormatResourceStringStripCodeAndKeyword("ProjectStartedPrefixForNestedProjectWithDefaultTargets", "fname1", "fname2") + Environment.NewLine +
                Environment.NewLine + "<reset color>");

            sc.Clear();

            es.Consume(new ProjectFinishedEventArgs("pf2", null, "fname2", true));
            es.Consume(new TaskFinishedEventArgs("", "", "", "", "Exec", true));

            sc.ToString().ShouldBeEmpty();

            sc.Clear();

            es.Consume(new TargetFinishedEventArgs("tf", null, "tarname", "fname", "tfile", true));

            sc.ToString().ShouldBeEmpty();

            sc.Clear();

            es.Consume(new ProjectFinishedEventArgs("pf1", null, "fname1", true));

            sc.ToString().ShouldBeEmpty();

            sc.Clear();

            es.Consume(new BuildFinishedEventArgs("bf", null, true));

            sc.ToString().ShouldStartWith("<green>" + Environment.NewLine + "bf" +
                        Environment.NewLine + "<reset color>" +
                "    " + ResourceUtilities.FormatResourceStringStripCodeAndKeyword("WarningCount", 0) +
                        Environment.NewLine + "<reset color>" +
                "    " + ResourceUtilities.FormatResourceStringStripCodeAndKeyword("ErrorCount", 0) +
                        Environment.NewLine + "<reset color>" +
                        Environment.NewLine);

            // Would like to add...
            //    + ResourceUtilities.FormatResourceString("TimeElapsed", String.Empty);
            // ...but this assumes that the time goes on the far right in every locale.

            sc.Clear();
        }

        [Fact]
        public void CustomDisplayedAtDetailed()
        {
            EventSourceSink es = new EventSourceSink();
            SimulatedConsole sc = new SimulatedConsole();
            ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Detailed,
                                                sc.Write, null, null);
            L.Initialize(es);

            MyCustomBuildEventArgs c =
                    new MyCustomBuildEventArgs("msg");

            es.Consume(c);

            sc.ToString().ShouldBe($"msg{Environment.NewLine}");
        }

        [Fact]
        public void CustomDisplayedAtDiagnosticMP()
        {
            EventSourceSink es = new EventSourceSink();
            SimulatedConsole sc = new SimulatedConsole();
            ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Diagnostic,
                                                sc.Write, null, null);
            L.Initialize(es, 2);

            MyCustomBuildEventArgs c =
                    new MyCustomBuildEventArgs("msg");
            c.BuildEventContext = new BuildEventContext(1, 1, 1, 1);
            es.Consume(c);

            sc.ToString().ShouldContain("msg");
        }

        [Fact]
        public void CustomNotDisplayedAtNormal()
        {
            EventSourceSink es = new EventSourceSink();
            SimulatedConsole sc = new SimulatedConsole();
            ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Normal,
                                                sc.Write, null, null);
            L.Initialize(es);

            MyCustomBuildEventArgs c =
                    new MyCustomBuildEventArgs("msg");

            es.Consume(c);

            sc.ToString().ShouldBeEmpty();
        }

        /// <summary>
        /// Create some properties and log them
        /// </summary>
        /// <param name="cl"></param>
        /// <returns></returns>
        private void WriteAndValidateProperties(BaseConsoleLogger cl, SimulatedConsole sc, bool expectToSeeLogging)
        {
            Hashtable properties = new Hashtable();
            properties.Add("prop1", "val1");
            properties.Add("prop2", "val2");
            properties.Add("pro(p3)", "va%3b%253b%3bl3");
            string prop1;
            string prop2;
            string prop3;
            if (cl is SerialConsoleLogger)
            {
                var propertyList = ((SerialConsoleLogger)cl).ExtractPropertyList(properties);
                ((SerialConsoleLogger)cl).WriteProperties(propertyList);
                prop1 = String.Format(CultureInfo.CurrentCulture, "{0,-30} = {1}", "prop1", "val1");
                prop2 = String.Format(CultureInfo.CurrentCulture, "{0,-30} = {1}", "prop2", "val2");
                prop3 = String.Format(CultureInfo.CurrentCulture, "{0,-30} = {1}", "pro(p3)", "va;%3b;l3");
            }
            else
            {
                BuildEventArgs buildEvent = new BuildErrorEventArgs("", "", "", 0, 0, 0, 0, "", "", "");
                buildEvent.BuildEventContext = new BuildEventContext(1, 2, 3, 4);
                ((ParallelConsoleLogger)cl).WriteProperties(buildEvent, properties);
                prop1 = String.Format(CultureInfo.CurrentCulture, "{0} = {1}", "prop1", "val1");
                prop2 = String.Format(CultureInfo.CurrentCulture, "{0} = {1}", "prop2", "val2");
                prop3 = String.Format(CultureInfo.CurrentCulture, "{0} = {1}", "pro(p3)", "va;%3b;l3");
            }
            string log = sc.ToString();

            _output.WriteLine("[" + log + "]");

            // Being careful not to make locale assumptions here, eg about sorting
            if (expectToSeeLogging)
            {
                log.ShouldContain(prop1);
                log.ShouldContain(prop2);
                log.ShouldContain(prop3);
            }
            else
            {
                log.ShouldNotContain(prop1);
                log.ShouldNotContain(prop2);
                log.ShouldNotContain(prop3);
            }
        }

        /// <summary>
        /// Basic test of properties list display
        /// </summary>
        [Fact]
        public void DisplayPropertiesList()
        {
            SimulatedConsole sc = new SimulatedConsole();
            SerialConsoleLogger cl = new SerialConsoleLogger(LoggerVerbosity.Diagnostic, sc.Write, null, null);

            WriteAndValidateProperties(cl, sc, true);

            sc = new SimulatedConsole();
            ParallelConsoleLogger cl2 = new ParallelConsoleLogger(LoggerVerbosity.Diagnostic, sc.Write, null, null);

            WriteAndValidateProperties(cl2, sc, true);
        }

        /// <summary>
        /// Basic test of properties list not being displayed except in Diagnostic
        /// </summary>
        [Fact]
        public void DoNotDisplayPropertiesListInDetailed()
        {
            SimulatedConsole sc = new SimulatedConsole();
            SerialConsoleLogger cl = new SerialConsoleLogger(LoggerVerbosity.Detailed, sc.Write, null, null);

            WriteAndValidateProperties(cl, sc, false);

            sc = new SimulatedConsole();
            ParallelConsoleLogger cl2 = new ParallelConsoleLogger(LoggerVerbosity.Detailed, sc.Write, null, null);

            WriteAndValidateProperties(cl2, sc, false);
        }


        /// <summary>
        /// Basic test of environment list not being displayed except in Diagnostic or if the showenvironment flag is set
        /// </summary>
        [Fact]
        public void DoNotDisplayEnvironmentInDetailed()
        {
            SimulatedConsole sc = new SimulatedConsole();
            SerialConsoleLogger cl = new SerialConsoleLogger(LoggerVerbosity.Detailed, sc.Write, null, null);

            WriteEnvironment(cl, sc, false);

            sc = new SimulatedConsole();
            ParallelConsoleLogger cl2 = new ParallelConsoleLogger(LoggerVerbosity.Detailed, sc.Write, null, null);

            WriteEnvironment(cl2, sc, false);
        }



        /// <summary>
        /// Basic test of environment list not being displayed except in Diagnostic or if the showenvironment flag is set
        /// </summary>
        [Fact]
        public void DisplayEnvironmentInDetailed()
        {
            SimulatedConsole sc = new SimulatedConsole();
            SerialConsoleLogger cl = new SerialConsoleLogger(LoggerVerbosity.Detailed, sc.Write, null, null);
            cl.Parameters = "ShowEnvironment";
            cl.ParseParameters();
            WriteEnvironment(cl, sc, true);

            sc = new SimulatedConsole();
            ParallelConsoleLogger cl2 = new ParallelConsoleLogger(LoggerVerbosity.Detailed, sc.Write, null, null);
            cl2.Parameters = "ShowEnvironment";
            cl2.ParseParameters();

            WriteEnvironment(cl2, sc, true);
        }

        /// <summary>
        /// Basic test of environment list not being displayed except in Diagnostic or if the showenvironment flag is set
        /// </summary>
        [Fact]
        public void DisplayEnvironmentInDiagnostic()
        {
            SimulatedConsole sc = new SimulatedConsole();
            SerialConsoleLogger cl = new SerialConsoleLogger(LoggerVerbosity.Diagnostic, sc.Write, null, null);
            WriteEnvironment(cl, sc, true);

            sc = new SimulatedConsole();
            ParallelConsoleLogger cl2 = new ParallelConsoleLogger(LoggerVerbosity.Diagnostic, sc.Write, null, null);
            WriteEnvironment(cl2, sc, true);
        }

        /// <summary>
        /// Basic test of environment list not being displayed except in Diagnostic or if the showenvironment flag is set
        /// </summary>
        [Fact]
        public void DoNotDisplayEnvironmentInMinimal()
        {
            SimulatedConsole sc = new SimulatedConsole();
            SerialConsoleLogger cl = new SerialConsoleLogger(LoggerVerbosity.Minimal, sc.Write, null, null);

            WriteEnvironment(cl, sc, false);

            sc = new SimulatedConsole();
            ParallelConsoleLogger cl2 = new ParallelConsoleLogger(LoggerVerbosity.Minimal, sc.Write, null, null);

            WriteEnvironment(cl2, sc, false);
        }



        /// <summary>
        /// Basic test of environment list not being displayed except in Diagnostic or if the showenvironment flag is set
        /// </summary>
        [Fact]
        public void DisplayEnvironmentInMinimal()
        {
            SimulatedConsole sc = new SimulatedConsole();
            SerialConsoleLogger cl = new SerialConsoleLogger(LoggerVerbosity.Minimal, sc.Write, null, null);
            cl.Parameters = "ShowEnvironment";
            cl.ParseParameters();
            WriteEnvironment(cl, sc, true);

            sc = new SimulatedConsole();
            ParallelConsoleLogger cl2 = new ParallelConsoleLogger(LoggerVerbosity.Minimal, sc.Write, null, null);
            cl2.Parameters = "ShowEnvironment";
            cl2.ParseParameters();

            WriteEnvironment(cl2, sc, true);
        }

        /// <summary>
        /// Basic test of properties list not being displayed when disabled
        /// </summary>
        [Fact]
        public void DoNotDisplayPropertiesListIfDisabled()
        {
            SimulatedConsole sc = new SimulatedConsole();
            SerialConsoleLogger cl = new SerialConsoleLogger(LoggerVerbosity.Diagnostic, sc.Write, null, null);
            cl.Parameters = "noitemandpropertylist";
            cl.ParseParameters();

            WriteAndValidateProperties(cl, sc, false);

            sc = new SimulatedConsole();
            ParallelConsoleLogger cl2 = new ParallelConsoleLogger(LoggerVerbosity.Diagnostic, sc.Write, null, null);
            cl2.Parameters = "noitemandpropertylist";
            cl2.ParseParameters();

            WriteAndValidateProperties(cl, sc, false);
        }


        /// <summary>
        /// Create some items and log them
        /// </summary>
        private void WriteEnvironment(BaseConsoleLogger cl, SimulatedConsole sc, bool expectToSeeLogging)
        {
            cl.WriteEnvironment(_environment);
            string log = sc.ToString();
            _output.WriteLine("[" + log + "]");

            // Being careful not to make locale assumptions here, eg about sorting
            foreach (KeyValuePair<string, string> kvp in _environment)
            {
                string message;
                if (cl is ParallelConsoleLogger)
                {
                    message = String.Format(CultureInfo.CurrentCulture, "{0} = {1}", kvp.Key, kvp.Value);
                }
                else
                {
                    message = String.Format(CultureInfo.CurrentCulture, "{0,-30} = {1}", kvp.Key, kvp.Value);
                }

                message = message.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);

                if (expectToSeeLogging)
                {
                    log.ShouldContain(message);
                }
                else
                {
                    log.ShouldNotContain(message);
                }
            }
        }

        /// <summary>
        /// Create some items and log them
        /// </summary>
        /// <returns></returns>
        private void WriteAndValidateItems(BaseConsoleLogger cl, SimulatedConsole sc, bool expectToSeeLogging)
        {
            Hashtable items = new Hashtable();
            items.Add("type", (ITaskItem2)new TaskItem("spec", String.Empty));
            items.Add("type2", (ITaskItem2)new TaskItem("spec2", String.Empty));

            // ItemSpecs are expected to be escaped coming in
            ITaskItem2 taskItem3 = new TaskItem("%28spec%3b3", String.Empty);

            // As are metadata, when set with "SetMetadata"
            taskItem3.SetMetadata("f)oo", "%21%40%23");

            items.Add("type(3)", taskItem3);

            string item1type;
            string item2type;
            string item3type;
            string item1spec;
            string item2spec;
            string item3spec;
            string item3metadatum = string.Empty;

            if (cl is SerialConsoleLogger)
            {
                SortedList itemList = ((SerialConsoleLogger)cl).ExtractItemList(items);
                ((SerialConsoleLogger)cl).WriteItems(itemList);
                item1spec = "spec" + Environment.NewLine;
                item2spec = "spec2" + Environment.NewLine;
                item3spec = "(spec;3" + Environment.NewLine;
                item3metadatum = "f)oo = !@#" + Environment.NewLine;
            }
            else
            {
                BuildEventArgs buildEvent = new BuildErrorEventArgs("", "", "", 0, 0, 0, 0, "", "", "");
                buildEvent.BuildEventContext = new BuildEventContext(1, 2, 3, 4);
                ((ParallelConsoleLogger)cl).WriteItems(buildEvent, items);
                item1spec = Environment.NewLine + "    spec" + Environment.NewLine;
                item2spec = Environment.NewLine + "    spec2" + Environment.NewLine;
                item3spec = Environment.NewLine + "    (spec;3" + Environment.NewLine;
            }

            item1type = "type" + Environment.NewLine;
            item2type = "type2" + Environment.NewLine;
            item3type = "type(3)" + Environment.NewLine;

            string log = sc.ToString();

            _output.WriteLine("[" + log + "]");



            // Being careful not to make locale assumptions here, eg about sorting
            if (expectToSeeLogging)
            {
                log.ShouldContain(item1type);
                log.ShouldContain(item2type);
                log.ShouldContain(item3type);
                log.ShouldContain(item1spec);
                log.ShouldContain(item2spec);
                log.ShouldContain(item3spec);

                if (!String.Equals(item3metadatum, String.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    log.ShouldContain(item3metadatum);
                }
            }
            else
            {
                log.ShouldNotContain(item1type);
                log.ShouldNotContain(item2type);
                log.ShouldNotContain(item3type);
                log.ShouldNotContain(item1spec);
                log.ShouldNotContain(item2spec);
                log.ShouldNotContain(item3spec);

                if (!String.Equals(item3metadatum, String.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    log.ShouldNotContain(item3metadatum);
                }
            }
        }

        /// <summary>
        /// Verify passing in an empty item list does not print anything out
        /// </summary>
        /// <returns></returns>
        [Fact]
        public void WriteItemsEmptyList()
        {
            Hashtable items = new Hashtable();

            for (int i = 0; i < 2; i++)
            {
                BaseConsoleLogger cl = null;
                SimulatedConsole sc = new SimulatedConsole();
                if (i == 0)
                {
                    cl = new SerialConsoleLogger(LoggerVerbosity.Diagnostic, sc.Write, null, null);
                }
                else
                {
                    cl = new ParallelConsoleLogger(LoggerVerbosity.Diagnostic, sc.Write, null, null);
                }

                if (cl is SerialConsoleLogger)
                {
                    SortedList itemList = ((SerialConsoleLogger)cl).ExtractItemList(items);
                    ((SerialConsoleLogger)cl).WriteItems(itemList);
                }
                else
                {
                    BuildEventArgs buildEvent = new BuildErrorEventArgs("", "", "", 0, 0, 0, 0, "", "", "");
                    buildEvent.BuildEventContext = new BuildEventContext(1, 2, 3, 4);
                    ((ParallelConsoleLogger)cl).WriteItems(buildEvent, items);
                }

                string log = sc.ToString();

                // There should be nothing in the log
                log.Length.ShouldBe(0);
                _output.WriteLine("Iteration of i: " + i + "[" + log + "]");
            }
        }

        /// <summary>
        /// Verify passing in an empty item list does not print anything out
        /// </summary>
        /// <returns></returns>
        [Fact]
        public void WritePropertiesEmptyList()
        {
            Hashtable properties = new Hashtable();

            for (int i = 0; i < 2; i++)
            {
                SimulatedConsole sc = new SimulatedConsole();
                if (i == 0)
                {
                    var cl = new SerialConsoleLogger(LoggerVerbosity.Diagnostic, sc.Write, null, null);
                    var propertyList = cl.ExtractPropertyList(properties);
                    cl.WriteProperties(propertyList);
                }
                else
                {
                    var cl = new ParallelConsoleLogger(LoggerVerbosity.Diagnostic, sc.Write, null, null);
                    BuildEventArgs buildEvent = new BuildErrorEventArgs("", "", "", 0, 0, 0, 0, "", "", "");
                    buildEvent.BuildEventContext = new BuildEventContext(1, 2, 3, 4);
                    cl.WriteProperties(buildEvent, properties);
                }

                string log = sc.ToString();

                // There should be nothing in the log
                log.Length.ShouldBe(0);
                _output.WriteLine("Iteration of i: " + i + "[" + log + "]");
            }
        }

        /// <summary>
        /// Basic test of item list display
        /// </summary>
        [Fact]
        public void DisplayItemsList()
        {
            SimulatedConsole sc = new SimulatedConsole();
            SerialConsoleLogger cl = new SerialConsoleLogger(LoggerVerbosity.Diagnostic, sc.Write, null, null);

            WriteAndValidateItems(cl, sc, true);

            sc = new SimulatedConsole();
            ParallelConsoleLogger cl2 = new ParallelConsoleLogger(LoggerVerbosity.Diagnostic, sc.Write, null, null);

            WriteAndValidateItems(cl2, sc, true);
        }

        /// <summary>
        /// Basic test of item list not being displayed except in Diagnostic
        /// </summary>
        [Fact]
        public void DoNotDisplayItemListInDetailed()
        {
            SimulatedConsole sc = new SimulatedConsole();
            SerialConsoleLogger cl = new SerialConsoleLogger(LoggerVerbosity.Detailed, sc.Write, null, null);

            WriteAndValidateItems(cl, sc, false);

            sc = new SimulatedConsole();
            ParallelConsoleLogger cl2 = new ParallelConsoleLogger(LoggerVerbosity.Detailed, sc.Write, null, null);

            WriteAndValidateItems(cl2, sc, false);
        }

        /// <summary>
        /// Basic test of item list not being displayed when disabled
        /// </summary>
        [Fact]
        public void DoNotDisplayItemListIfDisabled()
        {
            SimulatedConsole sc = new SimulatedConsole();
            SerialConsoleLogger cl = new SerialConsoleLogger(LoggerVerbosity.Diagnostic, sc.Write, null, null);
            cl.Parameters = "noitemandpropertylist";
            cl.ParseParameters();

            WriteAndValidateItems(cl, sc, false);

            sc = new SimulatedConsole();
            ParallelConsoleLogger cl2 = new ParallelConsoleLogger(LoggerVerbosity.Diagnostic, sc.Write, null, null);
            cl2.Parameters = "noitemandpropertylist";
            cl2.ParseParameters();

            WriteAndValidateItems(cl2, sc, false);
        }

        [Fact]
        public void ParametersEmptyTests()
        {
            SimulatedConsole sc = new SimulatedConsole();
            SerialConsoleLogger L = new SerialConsoleLogger(LoggerVerbosity.Normal, sc.Write, null, null);

            L.Parameters = "";
            L.ParseParameters();
            L.ShowSummary.ShouldBeNull();

            L.Parameters = null;
            L.ParseParameters();
            L.ShowSummary.ShouldBeNull();

            sc = new SimulatedConsole();
            ParallelConsoleLogger cl2 = new ParallelConsoleLogger(LoggerVerbosity.Diagnostic, sc.Write, null, null);
            cl2.Parameters = "noitemandpropertylist";
            cl2.ParseParameters();

            WriteAndValidateItems(cl2, sc, false);
        }

        [Fact]
        public void ParametersParsingTests()
        {
            SimulatedConsole sc = new SimulatedConsole();
            SerialConsoleLogger L = new SerialConsoleLogger(LoggerVerbosity.Normal, sc.Write, null, null);

            L.Parameters = "NoSuMmaRy";
            L.ParseParameters();
            L.ShowSummary.ShouldNotBeNull();
            ((bool)L.ShowSummary).ShouldBeFalse();

            L.Parameters = ";;NoSuMmaRy;";
            L.ParseParameters();
            L.ShowSummary.ShouldNotBeNull();
            ((bool)L.ShowSummary).ShouldBeFalse();

            sc = new SimulatedConsole();
            ParallelConsoleLogger L2 = new ParallelConsoleLogger(LoggerVerbosity.Normal, sc.Write, null, null);

            L2.Parameters = "NoSuMmaRy";
            L2.ParseParameters();
            L.ShowSummary.ShouldNotBeNull();
            ((bool)L.ShowSummary).ShouldBeFalse();

            L2.Parameters = ";;NoSuMmaRy;";
            L2.ParseParameters();
            L.ShowSummary.ShouldNotBeNull();
            ((bool)L.ShowSummary).ShouldBeFalse();
        }

        /// <summary>
        /// ResetConsoleLoggerState should reset the state of the console logger
        /// </summary>
        [Fact]
        public void ResetConsoleLoggerStateTestBasic()
        {
            // Create an event source
            EventSourceSink es = new EventSourceSink();
            //Create a simulated console
            SimulatedConsole sc = new SimulatedConsole();

            // error and warning string for 1 error and 1 warning
            // errorString = 1 Error(s)
            // warningString = 1 Warning(s)
            string errorString = ResourceUtilities.FormatResourceStringStripCodeAndKeyword("ErrorCount", 1);
            string warningString = ResourceUtilities.FormatResourceStringStripCodeAndKeyword("WarningCount", 1);

            // Create a ConsoleLogger with Normal verbosity
            ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Normal,
                                                sc.Write, sc.SetColor, sc.ResetColor);
            // Initialize ConsoleLogger
            L.Initialize(es);

            // BuildStarted Event
            es.Consume(new BuildStartedEventArgs("bs", null));

            // Introduce a warning
            BuildWarningEventArgs bwea = new BuildWarningEventArgs("VBC",
                            "31415", "file.vb", 42, 0, 0, 0,
                            "Some long message", "help", "sender");

            es.Consume(bwea);

            // Introduce an error
            BuildErrorEventArgs beea = new BuildErrorEventArgs("VBC",
                        "31415", "file.vb", 42, 0, 0, 0,
                        "Some long message", "help", "sender");

            es.Consume(beea);

            // BuildFinished Event
            es.Consume(new BuildFinishedEventArgs("bf",
                                                     null, true));

            // Log so far
            string actualLog = sc.ToString();

            _output.WriteLine("==");
            _output.WriteLine(sc.ToString());
            _output.WriteLine("==");

            // Verify that the log has correct error and warning string
            actualLog.ShouldContain(errorString);
            actualLog.ShouldContain(warningString);
            actualLog.ShouldContain("<red>");
            actualLog.ShouldContain("<yellow>");

            // Clear the log obtained so far
            sc.Clear();

            // BuildStarted event
            es.Consume(new BuildStartedEventArgs("bs", null));

            // BuildFinished
            es.Consume(new BuildFinishedEventArgs("bf",
                                                     null, true));
            // Log so far
            actualLog = sc.ToString();

            _output.WriteLine("==");
            _output.WriteLine(sc.ToString());
            _output.WriteLine("==");

            // Verify that the error and warning from the previous build is not
            // reported in the subsequent build
            actualLog.ShouldNotContain(errorString);
            actualLog.ShouldNotContain(warningString);
            actualLog.ShouldNotContain("<red>");
            actualLog.ShouldNotContain("<yellow>");

            // errorString = 0 Error(s)
            // warningString = 0 Warning(s)
            errorString = ResourceUtilities.FormatResourceStringStripCodeAndKeyword("ErrorCount", 0);
            warningString = ResourceUtilities.FormatResourceStringStripCodeAndKeyword("WarningCount", 0);

            // Verify that the log has correct error and warning string
            actualLog.ShouldContain(errorString);
            actualLog.ShouldContain(warningString);
        }

        /// <summary>
        /// ConsoleLogger::Initialize() should reset the state of the console logger
        /// </summary>
        [Fact]
        public void ResetConsoleLoggerState_Initialize()
        {
            // Create an event source
            EventSourceSink es = new EventSourceSink();
            //Create a simulated console
            SimulatedConsole sc = new SimulatedConsole();

            // error and warning string for 1 error and 1 warning
            // errorString = 1 Error(s)
            // warningString = 1 Warning(s)
            string errorString = ResourceUtilities.FormatResourceStringStripCodeAndKeyword("ErrorCount", 1);
            string warningString = ResourceUtilities.FormatResourceStringStripCodeAndKeyword("WarningCount", 1);

            // Create a ConsoleLogger with Normal verbosity
            ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Normal,
                                                sc.Write, sc.SetColor, sc.ResetColor);
            // Initialize ConsoleLogger
            L.Initialize(es);

            // BuildStarted Event
            es.Consume(new BuildStartedEventArgs("bs", null));

            // Introduce a warning
            BuildWarningEventArgs bwea = new BuildWarningEventArgs("VBC",
                            "31415", "file.vb", 42, 0, 0, 0,
                            "Some long message", "help", "sender");

            es.Consume(bwea);

            // Introduce an error
            BuildErrorEventArgs beea = new BuildErrorEventArgs("VBC",
                        "31415", "file.vb", 42, 0, 0, 0,
                        "Some long message", "help", "sender");

            es.Consume(beea);

            // NOTE: We don't call the es.RaiseBuildFinishedEvent(...) here as this
            // would call ResetConsoleLoggerState and we will fail to detect if Initialize()
            // is not calling it.

            // Log so far
            string actualLog = sc.ToString();

            _output.WriteLine("==");
            _output.WriteLine(sc.ToString());
            _output.WriteLine("==");

            // Verify that the log has correct error and warning string
            actualLog.ShouldContain("<red>");
            actualLog.ShouldContain("<yellow>");

            // Clear the log obtained so far
            sc.Clear();

            // Initialize (This should call ResetConsoleLoggerState(...))
            L.Initialize(es);

            // BuildStarted event
            es.Consume(new BuildStartedEventArgs("bs", null));

            // BuildFinished
            es.Consume(new BuildFinishedEventArgs("bf",
                                                     null, true));
            // Log so far
            actualLog = sc.ToString();

            _output.WriteLine("==");
            _output.WriteLine(sc.ToString());
            _output.WriteLine("==");

            // Verify that the error and warning from the previous build is not
            // reported in the subsequent build
            actualLog.ShouldNotContain("<red>");
            actualLog.ShouldNotContain("<yellow>");

            // errorString = 0 Error(s)
            errorString = ResourceUtilities.FormatResourceStringStripCodeAndKeyword("ErrorCount", 0);
            // warningString = 0 Warning(s)
            warningString = ResourceUtilities.FormatResourceStringStripCodeAndKeyword("WarningCount", 0);

            // Verify that the log has correct error and warning string
            actualLog.ShouldContain(errorString);
            actualLog.ShouldContain(warningString);
        }

        /// <summary>
        /// ResetConsoleLoggerState should reset PerformanceCounters
        /// </summary>
        [Fact]
        public void ResetConsoleLoggerState_PerformanceCounters()
        {
            for (int i = 1; i <= 2; i++)
            {
                EventSourceSink es = new EventSourceSink();
                //Create a simulated console
                SimulatedConsole sc = new SimulatedConsole();
                // Create a ConsoleLogger with Normal verbosity
                ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Normal, sc.Write, sc.SetColor, sc.ResetColor);
                // Initialize ConsoleLogger
                L.Parameters = "Performancesummary";
                L.Initialize(es, i);
                // prjPerfString = Project Performance Summary:
                string prjPerfString = ResourceUtilities.FormatResourceStringStripCodeAndKeyword("ProjectPerformanceSummary", null);
                // targetPerfString = Target Performance Summary:
                string targetPerfString = ResourceUtilities.FormatResourceStringStripCodeAndKeyword("TargetPerformanceSummary", null);
                // taskPerfString = Task Performance Summary:
                string taskPerfString = ResourceUtilities.FormatResourceStringStripCodeAndKeyword("TaskPerformanceSummary", null);

                // BuildStarted Event
                es.Consume(new BuildStartedEventArgs("bs", null));
                //Project Started Event
                ProjectStartedEventArgs project1Started = new ProjectStartedEventArgs(1, null, null, "p", "t", null, null, new BuildEventContext(BuildEventContext.InvalidNodeId, BuildEventContext.InvalidTargetId, BuildEventContext.InvalidProjectContextId, BuildEventContext.InvalidTaskId));
                project1Started.BuildEventContext = new BuildEventContext(1, 1, 1, 1);
                es.Consume(project1Started);
                TargetStartedEventArgs targetStarted1 = new TargetStartedEventArgs(null, null, "t", null, null);
                targetStarted1.BuildEventContext = project1Started.BuildEventContext;
                // TargetStarted Event
                es.Consume(targetStarted1);

                TaskStartedEventArgs taskStarted1 = new TaskStartedEventArgs(null, null, null, null, "task");
                taskStarted1.BuildEventContext = project1Started.BuildEventContext;
                // TaskStarted Event
                es.Consume(taskStarted1);

                BuildMessageEventArgs messsage1 = new BuildMessageEventArgs(null, null, null, MessageImportance.High);
                messsage1.BuildEventContext = project1Started.BuildEventContext;
                // Message Event
                es.Consume(messsage1);
                TaskFinishedEventArgs taskFinished1 = new TaskFinishedEventArgs(null, null, null, null, "task", true);
                taskFinished1.BuildEventContext = project1Started.BuildEventContext;
                // TaskFinished Event
                es.Consume(taskFinished1);

                TargetFinishedEventArgs targetFinished1 = new TargetFinishedEventArgs(null, null, "t", null, null, true);
                targetFinished1.BuildEventContext = project1Started.BuildEventContext;
                // TargetFinished Event
                es.Consume(targetFinished1);

                ProjectStartedEventArgs project2Started = new ProjectStartedEventArgs(2, null, null, "p2", "t2", null, null, project1Started.BuildEventContext);
                //Project Started Event
                project2Started.BuildEventContext = new BuildEventContext(2, 2, 2, 2);
                es.Consume(project2Started);
                TargetStartedEventArgs targetStarted2 = new TargetStartedEventArgs(null, null, "t2", null, null);
                targetStarted2.BuildEventContext = project2Started.BuildEventContext;
                // TargetStarted Event
                es.Consume(targetStarted2);

                TaskStartedEventArgs taskStarted2 = new TaskStartedEventArgs(null, null, null, null, "task2");
                taskStarted2.BuildEventContext = project2Started.BuildEventContext;
                // TaskStarted Event
                es.Consume(taskStarted2);

                BuildMessageEventArgs messsage2 = new BuildMessageEventArgs(null, null, null, MessageImportance.High);
                messsage2.BuildEventContext = project2Started.BuildEventContext;
                // Message Event
                es.Consume(messsage2);
                TaskFinishedEventArgs taskFinished2 = new TaskFinishedEventArgs(null, null, null, null, "task2", true);
                taskFinished2.BuildEventContext = project2Started.BuildEventContext;
                // TaskFinished Event
                es.Consume(taskFinished2);

                TargetFinishedEventArgs targetFinished2 = new TargetFinishedEventArgs(null, null, "t2", null, null, true);
                targetFinished2.BuildEventContext = project2Started.BuildEventContext;
                // TargetFinished Event
                es.Consume(targetFinished2);

                ProjectFinishedEventArgs finished2 = new ProjectFinishedEventArgs(null, null, "p2", true);
                finished2.BuildEventContext = project2Started.BuildEventContext;
                // ProjectFinished Event
                es.Consume(finished2);            // BuildFinished Event

                ProjectFinishedEventArgs finished1 = new ProjectFinishedEventArgs(null, null, "p", true);
                finished1.BuildEventContext = project1Started.BuildEventContext;
                // ProjectFinished Event
                es.Consume(finished1);            // BuildFinished Event
                es.Consume(new BuildFinishedEventArgs("bf",
                                                         null, true));
                // Log so far
                string actualLog = sc.ToString();

                _output.WriteLine("==");
                _output.WriteLine(sc.ToString());
                _output.WriteLine("==");

                // Verify that the log has perf summary
                // Project perf summary
                actualLog.ShouldContain(prjPerfString);
                // Target perf summary
                actualLog.ShouldContain(targetPerfString);
                // Task Perf summary
                actualLog.ShouldContain(taskPerfString);

                // Clear the log obtained so far
                sc.Clear();

                // BuildStarted event
                es.Consume(new BuildStartedEventArgs("bs", null));
                // BuildFinished
                es.Consume(new BuildFinishedEventArgs("bf",
                                                         null, true));
                // Log so far
                actualLog = sc.ToString();

                _output.WriteLine("==");
                _output.WriteLine(sc.ToString());
                _output.WriteLine("==");

                // Verify that the log doesn't have perf summary
                actualLog.ShouldNotContain(prjPerfString);
                actualLog.ShouldNotContain(targetPerfString);
                actualLog.ShouldNotContain(taskPerfString);
            }
        }


        [Fact]
        public void DeferredMessages()
        {
            EventSourceSink es = new EventSourceSink();
            //Create a simulated console
            SimulatedConsole sc = new SimulatedConsole();
            // Create a ConsoleLogger with Detailed verbosity
            ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Detailed, sc.Write, sc.SetColor, sc.ResetColor);
            L.Initialize(es, 2);
            es.Consume(new BuildStartedEventArgs("bs", null));
            TaskCommandLineEventArgs messsage1 = new TaskCommandLineEventArgs("Message", null, MessageImportance.High);
            messsage1.BuildEventContext = new BuildEventContext(1, 1, 1, 1);
            // Message Event
            es.Consume(messsage1);
            es.Consume(new BuildFinishedEventArgs("bf", null, true));
            string actualLog = sc.ToString();
            actualLog.ShouldContain(ResourceUtilities.GetResourceString("DeferredMessages"));

            es = new EventSourceSink();
            sc = new SimulatedConsole();
            // Create a ConsoleLogger with Normal verbosity
            L = new ConsoleLogger(LoggerVerbosity.Normal, sc.Write, sc.SetColor, sc.ResetColor);
            L.Initialize(es, 2);
            es.Consume(new BuildStartedEventArgs("bs", null));
            BuildMessageEventArgs messsage2 = new BuildMessageEventArgs("Message", null, null, MessageImportance.High);
            messsage2.BuildEventContext = new BuildEventContext(1, 1, 1, 1);
            // Message Event
            es.Consume(messsage2);
            es.Consume(new BuildFinishedEventArgs("bf", null, true));
            actualLog = sc.ToString();
            actualLog.ShouldContain(ResourceUtilities.GetResourceString("DeferredMessages"));

            es = new EventSourceSink();
            sc = new SimulatedConsole();
            // Create a ConsoleLogger with Normal verbosity
            L = new ConsoleLogger(LoggerVerbosity.Normal, sc.Write, sc.SetColor, sc.ResetColor);
            L.Initialize(es, 2);
            es.Consume(new BuildStartedEventArgs("bs", null));
            messsage2 = new BuildMessageEventArgs("Message", null, null, MessageImportance.High);
            messsage2.BuildEventContext = new BuildEventContext(1, 1, 1, 1);
            // Message Event
            es.Consume(messsage2);
            ProjectStartedEventArgs project = new ProjectStartedEventArgs(1, "Hello,", "HI", "None", "Build", null, null, messsage1.BuildEventContext);
            project.BuildEventContext = messsage1.BuildEventContext;
            es.Consume(project);
            es.Consume(new BuildFinishedEventArgs("bf", null, true));
            actualLog = sc.ToString();
            actualLog.ShouldContain("Message");
        }

        [Fact]
        public void VerifyMPLoggerSwitch()
        {
            for (int i = 0; i < 2; i++)
            {
                EventSourceSink es = new EventSourceSink();
                //Create a simulated console
                SimulatedConsole sc = new SimulatedConsole();
                // Create a ConsoleLogger with Normal verbosity
                ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Normal, sc.Write, sc.SetColor, sc.ResetColor);
                //Make sure the MPLogger switch will property work on both Initialize methods
                L.Parameters = "EnableMPLogging";
                if (i == 0)
                {
                    L.Initialize(es, 1);
                }
                else
                {
                    L.Initialize(es);
                }
                es.Consume(new BuildStartedEventArgs("bs", null));
                BuildEventContext context = new BuildEventContext(1, 1, 1, 1);
                BuildEventContext context2 = new BuildEventContext(2, 2, 2, 2);

                ProjectStartedEventArgs project = new ProjectStartedEventArgs(1, "Hello,", "HI", "None", "Build", null, null, context);
                project.BuildEventContext = context;
                es.Consume(project);

                TargetStartedEventArgs targetStarted1 = new TargetStartedEventArgs(null, null, "t", null, null);
                targetStarted1.BuildEventContext = context;
                es.Consume(targetStarted1);

                BuildMessageEventArgs messsage1 = new BuildMessageEventArgs("Message", null, null, MessageImportance.High);
                messsage1.BuildEventContext = context;
                es.Consume(messsage1);
                string actualLog = sc.ToString();
                string resourceString = ResourceUtilities.FormatResourceStringStripCodeAndKeyword("ProjectStartedTopLevelProjectWithTargetNames", "None", 1, "Build");
                actualLog.ShouldContain(resourceString);
            }
        }

        [Fact]
        public void TestPrintTargetNamePerMessage()
        {
            EventSourceSink es = new EventSourceSink();
            //Create a simulated console
            SimulatedConsole sc = new SimulatedConsole();
            // Create a ConsoleLogger with Normal verbosity
            ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Normal, sc.Write, sc.SetColor, sc.ResetColor);
            L.Initialize(es, 2);
            es.Consume(new BuildStartedEventArgs("bs", null));
            BuildEventContext context = new BuildEventContext(1, 1, 1, 1);
            BuildEventContext context2 = new BuildEventContext(2, 2, 2, 2);

            ProjectStartedEventArgs project = new ProjectStartedEventArgs(1, "Hello,", "HI", "None", "Build", null, null, context);
            project.BuildEventContext = context;
            es.Consume(project);

            ProjectStartedEventArgs project2 = new ProjectStartedEventArgs(2, "Hello,", "HI", "None", "Build", null, null, context2);
            project2.BuildEventContext = context2;
            es.Consume(project2);

            TargetStartedEventArgs targetStarted1 = new TargetStartedEventArgs(null, null, "t", null, null);
            targetStarted1.BuildEventContext = context;
            es.Consume(targetStarted1);

            TargetStartedEventArgs targetStarted2 = new TargetStartedEventArgs(null, null, "t2", null, null);
            targetStarted2.BuildEventContext = context2;
            es.Consume(targetStarted2);

            BuildMessageEventArgs messsage1 = new BuildMessageEventArgs("Message", null, null, MessageImportance.High);
            messsage1.BuildEventContext = context;
            BuildMessageEventArgs messsage2 = new BuildMessageEventArgs("Message2", null, null, MessageImportance.High);
            messsage2.BuildEventContext = context2;
            BuildMessageEventArgs messsage3 = new BuildMessageEventArgs("Message3", null, null, MessageImportance.High);
            messsage3.BuildEventContext = context;
            es.Consume(messsage1);
            es.Consume(messsage2);
            es.Consume(messsage3);
            string actualLog = sc.ToString();
            actualLog.ShouldContain("t:");
        }

        /// <summary>
        /// Verify that in the MP case and the older serial logger that there is no extra newline after the project done event.
        /// We cannot verify there is a newline after the project done event for the MP single proc log because
        /// nunit is showing up as an unknown output type, this causes us to not print the newline because we think it may be to a
        /// text file.
        /// </summary>
        [Fact]
        public void TestNewLineAfterProjectFinished()
        {
            bool runningWithCharDevice = NativeMethodsShared.IsWindows ? IsRunningWithCharacterFileType() : false;
            for (int i = 0; i < 3; i++)
            {
                _output.WriteLine("Iteration of I is {" + i + "}");


                EventSourceSink es = new EventSourceSink();
                //Create a simulated console
                SimulatedConsole sc = new SimulatedConsole();
                ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Normal, sc.Write, sc.SetColor, sc.ResetColor);

                if (i < 2)
                {
                    // On the second pass through use the MP single proc logger
                    if (i == 1)
                    {
                        L.Parameters = "EnableMPLogging";
                    }
                    // Use the old single proc logger
                    L.Initialize(es, 1);
                }
                else
                {
                    // Use the parallel logger
                    L.Initialize(es, 2);
                }

                es.Consume(new BuildStartedEventArgs("bs", null));
                BuildEventContext context = new BuildEventContext(1, 1, 1, 1);

                ProjectStartedEventArgs project = new ProjectStartedEventArgs(1, "Hello,", "HI", "None", "Build", null, null, context);
                project.BuildEventContext = context;
                es.Consume(project);

                TargetStartedEventArgs targetStarted1 = new TargetStartedEventArgs(null, null, "t", null, null);
                targetStarted1.BuildEventContext = context;
                es.Consume(targetStarted1);

                BuildMessageEventArgs messsage1 = new BuildMessageEventArgs("Message", null, null, MessageImportance.High);
                messsage1.BuildEventContext = context;
                es.Consume(messsage1);

                ProjectFinishedEventArgs projectFinished = new ProjectFinishedEventArgs("Finished,", "HI", "projectFile", true);
                projectFinished.BuildEventContext = context;
                es.Consume(projectFinished);

                string actualLog = sc.ToString();

                switch (i)
                {
                    case 0:
                        // There is no project finished event printed in normal verbosity
                        actualLog.ShouldNotContain(projectFinished.Message);
                        break;
                    // We are in single proc but logging with multiproc logging add an extra new line to make the log more readable.
                    case 1:
                        actualLog.ShouldContain(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("ProjectFinishedPrefixWithTargetNamesMultiProc", "None", "Build") + Environment.NewLine);
                        if (runningWithCharDevice)
                        {
                            actualLog.ShouldContain(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("ProjectFinishedPrefixWithTargetNamesMultiProc", "None", "Build") + Environment.NewLine + Environment.NewLine);
                        }
                        else
                        {
                            actualLog.ShouldNotContain(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("ProjectFinishedPrefixWithTargetNamesMultiProc", "None", "Build") + Environment.NewLine + Environment.NewLine);
                        }
                        break;
                    case 2:
                        actualLog.ShouldNotContain(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("ProjectFinishedPrefixWithTargetNamesMultiProc", "None", "Build") + Environment.NewLine + Environment.NewLine);
                        break;
                }
            }
        }

        /// <summary>
        /// Check to see what kind of device we are outputting the log to, is it a character device, a file, or something else
        /// this can be used by loggers to modify their outputs based on the device they are writing to
        /// </summary>
        internal bool IsRunningWithCharacterFileType()
        {
            // Get the std out handle
            IntPtr stdHandle = NativeMethodsShared.GetStdHandle(NativeMethodsShared.STD_OUTPUT_HANDLE);

            if (stdHandle != Microsoft.Build.BackEnd.NativeMethods.InvalidHandle)
            {
                uint fileType = NativeMethodsShared.GetFileType(stdHandle);

                // The std out is a char type(LPT or Console)
                return fileType == NativeMethodsShared.FILE_TYPE_CHAR;
            }
            else
            {
                return false;
            }
        }
    }
}
