using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Build.BackEnd;
using Microsoft.Build.Framework;
using Microsoft.Build.Framework.Profiler;
using Microsoft.Build.Logging;
using Microsoft.Build.Shared;
using Microsoft.Build.UnitTests.BackEnd;
using Xunit;

namespace Microsoft.Build.UnitTests
{
    public class BuildEventArgsSerializationTests
    {
        public BuildEventArgsSerializationTests()
        {
            // touch the type so that static constructor runs
            _ = ItemGroupLoggingHelper.ItemGroupIncludeLogMessagePrefix;
        }

        [Fact]
        public void RoundtripBuildStartedEventArgs()
        {
            var args = new BuildStartedEventArgs(
                "Message",
                "HelpKeyword",
                DateTime.Parse("3/1/2017 11:11:56 AM"));
            Roundtrip(args,
                e => e.Message,
                e => e.HelpKeyword,
                e => e.Timestamp.ToString());

            args = new BuildStartedEventArgs(
                "M",
                null,
                new Dictionary<string, string>
                {
                    { "SampleName", "SampleValue" }
                });
            Roundtrip(args,
                e => TranslationHelpers.ToString(e.BuildEnvironment),
                e => e.HelpKeyword,
                e => e.ThreadId.ToString(),
                e => e.SenderName);
        }

        [Fact]
        public void RoundtripBuildFinishedEventArgs()
        {
            var args = new BuildFinishedEventArgs(
                "Message",
                null,
                succeeded: true,
                eventTimestamp: DateTime.Parse("12/12/2015 06:11:56 PM"));
            args.BuildEventContext = new BuildEventContext(1, 2, 3, 4, 5, 6);

            Roundtrip(args,
                e => ToString(e.BuildEventContext),
                e => e.Succeeded.ToString());
        }

        [Fact]
        public void RoundtripProjectStartedEventArgs()
        {
            var args = new ProjectStartedEventArgs(
                projectId: 42,
                message: "Project \"test.proj\" (Build target(s)):",
                helpKeyword: "help",
                projectFile: Path.Combine("a", "test.proj"),
                targetNames: "Build",
                properties: new List<DictionaryEntry>() { new DictionaryEntry("Key", "Value") },
                items: new List<DictionaryEntry>() { new DictionaryEntry("Key", new MyTaskItem() { ItemSpec = "TestItemSpec" }) },
                parentBuildEventContext: new BuildEventContext(7, 8, 9, 10, 11, 12),
                globalProperties: new Dictionary<string, string>() { { "GlobalKey", "GlobalValue" } },
                toolsVersion: "Current");
            args.BuildEventContext = new BuildEventContext(1, 2, 3, 4, 5, 6);

            Roundtrip<ProjectStartedEventArgs>(args,
                e => ToString(e.BuildEventContext),
                e => TranslationHelpers.GetPropertiesString(e.GlobalProperties),
                e => TranslationHelpers.GetMultiItemsString(e.Items),
                e => e.Message,
                e => ToString(e.ParentProjectBuildEventContext),
                e => e.ProjectFile,
                e => e.ProjectId.ToString(),
                e => TranslationHelpers.GetPropertiesString(e.Properties),
                e => e.TargetNames,
                e => e.ThreadId.ToString(),
                e => e.Timestamp.ToString(),
                e => e.ToolsVersion);
        }

        [Fact]
        public void RoundtripProjectFinishedEventArgs()
        {
            var args = new ProjectFinishedEventArgs(
                "Message",
                null,
                "C:\\projectfile.proj",
                true,
                DateTime.Parse("12/12/2015 06:11:56 PM"));

            Roundtrip(args,
                e => e.ProjectFile,
                e => e.Succeeded.ToString());
        }

        [Fact]
        public void RoundtripTargetStartedEventArgs()
        {
            var args = new TargetStartedEventArgs(
                "Message",
                "Help",
                "Build",
                "C:\\projectfile.proj",
                "C:\\Common.targets",
                "ParentTarget",
                TargetBuiltReason.AfterTargets,
                DateTime.Parse("12/12/2015 06:11:56 PM"));

            Roundtrip(args,
                e => e.ParentTarget,
                e => e.ProjectFile,
                e => e.TargetFile,
                e => e.TargetName,
                e => e.BuildReason.ToString(),
                e => e.Timestamp.ToString());
        }

        [Fact]
        public void RoundtripTargetFinishedEventArgs()
        {
            var args = new TargetFinishedEventArgs(
                "Message",
                null,
                "TargetName",
                "C:\\Project.proj",
                "C:\\TargetFile.targets",
                true,
                new List<ITaskItem> { new MyTaskItem() });

            Roundtrip(args,
                e => e.ProjectFile,
                e => e.Succeeded.ToString(),
                e => e.TargetFile,
                e => e.TargetName,
                e => ToString(e.TargetOutputs.OfType<ITaskItem>()));
        }

        [Fact]
        public void RoundtripTaskStartedEventArgs()
        {
            var args = new TaskStartedEventArgs(
                "Message",
                null,
                projectFile: "C:\\project.proj",
                taskFile: "C:\\common.targets",
                taskName: "Csc");
            args.LineNumber = 42;
            args.ColumnNumber = 999;

            Roundtrip(args,
                e => e.ProjectFile,
                e => e.TaskFile,
                e => e.TaskName,
                e => e.LineNumber.ToString(),
                e => e.ColumnNumber.ToString());
        }

        [Fact]
        public void RoundtripTaskFinishedEventArgs()
        {
            var args = new TaskFinishedEventArgs(
                "Message",
                null,
                "C:\\project.proj",
                "C:\\common.tasks",
                "Csc",
                succeeded: false);

            Roundtrip(args,
                e => e.ProjectFile,
                e => e.TaskFile,
                e => e.TaskName,
                e => e.ThreadId.ToString());
        }

        [Fact]
        public void RoundtripBuildErrorEventArgs()
        {
            var args = new BuildErrorEventArgs(
                "Subcategory",
                "Code",
                "File",
                1,
                2,
                3,
                4,
                "Message",
                "Help",
                "SenderName");

            Roundtrip(args,
                e => e.Code,
                e => e.ColumnNumber.ToString(),
                e => e.EndColumnNumber.ToString(),
                e => e.EndLineNumber.ToString(),
                e => e.File,
                e => e.LineNumber.ToString(),
                e => e.Message,
                e => e.ProjectFile,
                e => e.Subcategory);
        }

        [Fact]
        public void RoundtripBuildWarningEventArgs()
        {
            var args = new BuildWarningEventArgs(
                "Subcategory",
                "Code",
                "File",
                1,
                2,
                3,
                4,
                "Message",
                "Help",
                "SenderName");

            Roundtrip(args,
                e => e.Code,
                e => e.ColumnNumber.ToString(),
                e => e.EndColumnNumber.ToString(),
                e => e.EndLineNumber.ToString(),
                e => e.File,
                e => e.LineNumber.ToString(),
                e => e.Message,
                e => e.ProjectFile,
                e => e.Subcategory);
        }

        [Fact]
        public void RoundtripBuildMessageEventArgs()
        {
            var args = new BuildMessageEventArgs(
                "Subcategory",
                "Code",
                "File",
                1,
                2,
                3,
                4,
                "Message",
                "Help",
                "SenderName",
                MessageImportance.High,
                DateTime.Parse("12/12/2015 06:11:56 PM"));

            Roundtrip(args,
                e => e.Code,
                e => e.ColumnNumber.ToString(),
                e => e.EndColumnNumber.ToString(),
                e => e.EndLineNumber.ToString(),
                e => e.File,
                e => e.LineNumber.ToString(),
                e => e.Message,
                e => e.Importance.ToString(),
                e => e.ProjectFile,
                e => e.Subcategory);
        }

        [Fact]
        public void RoundtripCriticalBuildMessageEventArgs()
        {
            var args = new CriticalBuildMessageEventArgs(
                "Subcategory",
                "Code",
                "File",
                1,
                2,
                3,
                4,
                "Message",
                "Help",
                "SenderName",
                DateTime.Parse("12/12/2015 06:11:56 PM"));

            Roundtrip(args,
                e => e.Code,
                e => e.ColumnNumber.ToString(),
                e => e.EndColumnNumber.ToString(),
                e => e.EndLineNumber.ToString(),
                e => e.File,
                e => e.LineNumber.ToString(),
                e => e.Message,
                e => e.ProjectFile,
                e => e.Subcategory);
        }

        [Fact]
        public void RoundtripTaskCommandLineEventArgs()
        {
            var args = new TaskCommandLineEventArgs(
                "/bl /noconlog /v:diag",
                "Csc",
                MessageImportance.Low,
                DateTime.Parse("12/12/2015 06:11:56 PM"));

            Roundtrip(args,
                e => e.CommandLine,
                e => e.TaskName,
                e => e.Importance.ToString(),
                e => e.EndLineNumber.ToString(),
                e => e.File,
                e => e.LineNumber.ToString(),
                e => e.Message,
                e => e.ProjectFile,
                e => e.Subcategory);
        }

        [Fact]
        public void RoundtripTaskParameterEventArgs()
        {
            var items = new ITaskItem[]
            {
                new TaskItemData("ItemSpec1", null),
                new TaskItemData("ItemSpec2", Enumerable.Range(1,3).ToDictionary(i => i.ToString(), i => i.ToString() + "value"))
            };
            var args = new TaskParameterEventArgs(TaskParameterMessageKind.TaskOutput, "ItemName", items, true, DateTime.MinValue);
            args.LineNumber = 265;
            args.ColumnNumber = 6;

            Roundtrip(args,
                e => e.Kind.ToString(),
                e => e.ItemType,
                e => e.LogItemMetadata.ToString(),
                e => e.LineNumber.ToString(),
                e => e.ColumnNumber.ToString(),
                e => TranslationHelpers.GetItemsString(e.Items));
        }

        [Fact]
        public void RoundtripProjectEvaluationStartedEventArgs()
        {
            var projectFile = @"C:\foo\bar.proj";
            var args = new ProjectEvaluationStartedEventArgs(
                ResourceUtilities.GetResourceString("EvaluationStarted"),
                projectFile)
            {
                BuildEventContext = BuildEventContext.Invalid,
                ProjectFile = projectFile,
            };

            Roundtrip(args,
                e => e.Message,
                e => e.ProjectFile);
        }

        [Fact]
        public void RoundtripProjectEvaluationFinishedEventArgs()
        {
            var projectFile = @"C:\foo\bar.proj";
            var args = new ProjectEvaluationFinishedEventArgs(
                ResourceUtilities.GetResourceString("EvaluationFinished"),
                projectFile)
            {
                BuildEventContext = BuildEventContext.Invalid,
                ProjectFile = @"C:\foo\bar.proj",
                GlobalProperties = new Dictionary<string, string>() { { "GlobalKey", "GlobalValue" } },
                Properties = new List<DictionaryEntry>() { new DictionaryEntry("Key", "Value") },
                Items = new List<DictionaryEntry>() { new DictionaryEntry("Key", new MyTaskItem() { ItemSpec = "TestItemSpec" }) }
            };

            Roundtrip(args,
                e => e.Message,
                e => e.ProjectFile,
                e => TranslationHelpers.GetPropertiesString(e.GlobalProperties),
                e => TranslationHelpers.GetPropertiesString(e.Properties),
                e => TranslationHelpers.GetMultiItemsString(e.Items));
        }

        [Fact]
        public void RoundtripProjectEvaluationFinishedEventArgsWithProfileData()
        {
            var projectFile = @"C:\foo\bar.proj";
            var args = new ProjectEvaluationFinishedEventArgs(
                ResourceUtilities.GetResourceString("EvaluationFinished"),
                projectFile)
            {
                BuildEventContext = BuildEventContext.Invalid,
                ProjectFile = @"C:\foo\bar.proj",
                ProfilerResult = new ProfilerResult(new Dictionary<EvaluationLocation, ProfiledLocation>
                {
                    {
                        new EvaluationLocation(1, 0, EvaluationPass.InitialProperties, "desc1", "file1", 7, "element1", "description", EvaluationLocationKind.Condition),
                        new ProfiledLocation(TimeSpan.FromSeconds(1), TimeSpan.FromHours(2), 1)
                    },
                    {
                        new EvaluationLocation(0, null, EvaluationPass.LazyItems, "desc2", "file1", null, "element2", "description2", EvaluationLocationKind.Glob),
                        new ProfiledLocation(TimeSpan.FromSeconds(1), TimeSpan.FromHours(2), 2)
                    },
                    {
                        new EvaluationLocation(2, 0, EvaluationPass.Properties, "desc2", "file1", null, "element2", "description2", EvaluationLocationKind.Element),
                        new ProfiledLocation(TimeSpan.FromSeconds(1), TimeSpan.FromHours(2), 2)
                    }
                })
            };

            Roundtrip(args,
                e => e.Message,
                e => e.ProjectFile,
                e => ToString(e.ProfilerResult.Value.ProfiledLocations));
        }

        [Fact]
        public void RoundtripProjectImportedEventArgs()
        {
            var args = new ProjectImportedEventArgs(
                1,
                2,
                "Message")
            {
                BuildEventContext = BuildEventContext.Invalid,
                ImportedProjectFile = "foo.props",
                ProjectFile = "foo.csproj",
                UnexpandedProject = "$(Something)"
            };

            Roundtrip(args,
                e => e.ImportedProjectFile,
                e => e.UnexpandedProject,
                e => e.Importance.ToString(),
                e => e.LineNumber.ToString(),
                e => e.ColumnNumber.ToString(),
                e => e.LineNumber.ToString(),
                e => e.Message,
                e => e.ProjectFile);
        }

        [Fact]
        public void RoundtripTargetSkippedEventArgs()
        {
            var args = new TargetSkippedEventArgs(
                "Target \"target\" skipped. Previously built unsuccessfully.")
            {
                BuildEventContext = BuildEventContext.Invalid,
                ProjectFile = "foo.csproj",
                TargetName = "target",
                ParentTarget = "bar",
                BuildReason = TargetBuiltReason.DependsOn,
                SkipReason = TargetSkipReason.PreviouslyBuiltSuccessfully,
                Condition = "$(condition) == true",
                EvaluatedCondition = "true == true",
                OriginalBuildEventContext = new BuildEventContext(1, 2, 3, 4, 5, 6, 7),
                OriginallySucceeded = false,
                TargetFile = "foo.csproj"
            };

            Roundtrip(args,
                e => e.BuildEventContext.ToString(),
                e => e.ParentTarget,
                e => e.Importance.ToString(),
                e => e.LineNumber.ToString(),
                e => e.ColumnNumber.ToString(),
                e => e.Message,
                e => e.ProjectFile,
                e => e.TargetFile,
                e => e.TargetName,
                e => e.BuildReason.ToString(),
                e => e.SkipReason.ToString(),
                e => e.Condition,
                e => e.EvaluatedCondition,
                e => e.OriginalBuildEventContext.ToString(),
                e => e.OriginallySucceeded.ToString());
        }

        [Fact]
        public void RoundTripEnvironmentVariableReadEventArgs()
        {
            var args = new EnvironmentVariableReadEventArgs(
                environmentVariableName: Guid.NewGuid().ToString(),
                message: Guid.NewGuid().ToString(),
                helpKeyword: Guid.NewGuid().ToString(),
                senderName: Guid.NewGuid().ToString());

            Roundtrip(args,
                e => e.EnvironmentVariableName,
                e => e.Message,
                e => e.HelpKeyword,
                e => e.SenderName);
        }

        [Fact]
        public void RoundTripPropertyReassignmentEventArgs()
        {
            var args = new PropertyReassignmentEventArgs(
                propertyName: "a",
                previousValue: "b",
                newValue: "c",
                location: "d",
                message: "Property reassignment: $(a)=\"c\" (previous value: \"b\") at d",
                helpKeyword: "e",
                senderName: "f");

            Roundtrip(args,
                e => e.PropertyName,
                e => e.PreviousValue,
                e => e.NewValue,
                e => e.Location,
                e => e.Message,
                e => e.HelpKeyword,
                e => e.SenderName);
        }

        [Fact]
        public void UninitializedPropertyReadEventArgs()
        {
            var args = new UninitializedPropertyReadEventArgs(
                propertyName: Guid.NewGuid().ToString(),
                message: Guid.NewGuid().ToString(),
                helpKeyword: Guid.NewGuid().ToString(),
                senderName: Guid.NewGuid().ToString());

            Roundtrip(args,
                e => e.PropertyName,
                e => e.Message,
                e => e.HelpKeyword,
                e => e.SenderName);
        }

        [Fact]
        public void PropertyInitialValueEventArgs()
        {
            var args = new PropertyInitialValueSetEventArgs(
                propertyName: Guid.NewGuid().ToString(),
                propertyValue: Guid.NewGuid().ToString(),
                propertySource: Guid.NewGuid().ToString(),
                message: Guid.NewGuid().ToString(),
                helpKeyword: Guid.NewGuid().ToString(),
                senderName: Guid.NewGuid().ToString());

            Roundtrip(args,
                e => e.PropertyName,
                e => e.PropertyValue,
                e => e.PropertySource,
                e => e.Message,
                e => e.HelpKeyword,
                e => e.SenderName);
        }
        [Fact]
        public void ReadingCorruptedStreamThrows()
        {
            var memoryStream = new MemoryStream();
            var binaryWriter = new BinaryWriter(memoryStream);
            var buildEventArgsWriter = new BuildEventArgsWriter(binaryWriter);

            var args = new BuildStartedEventArgs(
                "Message",
                "HelpKeyword",
                DateTime.Parse("3/1/2017 11:11:56 AM"));

            buildEventArgsWriter.Write(args);

            long length = memoryStream.Length;

            for (long i = length - 1; i >= 0; i--) // try all possible places where a stream might end abruptly
            {
                memoryStream.SetLength(i); // pretend that the stream abruptly ends
                memoryStream.Position = 0;

                var binaryReader = new BinaryReader(memoryStream);
                using var buildEventArgsReader = new BuildEventArgsReader(binaryReader, BinaryLogger.FileFormatVersion);

                Assert.Throws<EndOfStreamException>(() => buildEventArgsReader.Read());
            }
        }

        private string ToString(BuildEventContext context)
        {
            return $"{context.BuildRequestId} {context.NodeId} {context.ProjectContextId} {context.ProjectInstanceId} {context.SubmissionId} {context.TargetId} {context.TaskId}";
        }

        private string ToString(IEnumerable<ITaskItem> items)
        {
            return string.Join(";", items.Select(i => ToString(i)));
        }

        private string ToString(ITaskItem i)
        {
            return i.ItemSpec + string.Join(";", i.CloneCustomMetadata().Keys.OfType<string>().Select(k => i.GetMetadata(k)));
        }

        private string ToString(IReadOnlyDictionary<EvaluationLocation, ProfiledLocation> items)
        {
            StringBuilder sb = new StringBuilder();
            foreach (var item in items)
            {
                sb.AppendLine(item.Key.ToString());
                sb.AppendLine(item.Value.ToString());
            }

            return sb.ToString();
        }

        private void Roundtrip<T>(T args, params Func<T, string>[] fieldsToCompare)
            where T : BuildEventArgs
        {
            var memoryStream = new MemoryStream();
            var binaryWriter = new BinaryWriter(memoryStream);
            var buildEventArgsWriter = new BuildEventArgsWriter(binaryWriter);

            buildEventArgsWriter.Write(args);

            long length = memoryStream.Length;

            memoryStream.Position = 0;

            var binaryReader = new BinaryReader(memoryStream);
            using var buildEventArgsReader = new BuildEventArgsReader(binaryReader, BinaryLogger.FileFormatVersion);
            var deserializedArgs = (T)buildEventArgsReader.Read();

            Assert.Equal(length, memoryStream.Position);

            Assert.NotNull(deserializedArgs);
            Assert.Equal(args.GetType(), deserializedArgs.GetType());

            foreach (var field in fieldsToCompare)
            {
                var expected = field(args);
                var actual = field(deserializedArgs);
                Assert.Equal(expected, actual);
            }
        }
    }
}
