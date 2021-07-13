// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.BackEnd;
using Microsoft.Build.Shared;
using TaskItem = Microsoft.Build.Execution.ProjectItemInstance.TaskItem;
using System.Collections.Generic;
using Xunit;

namespace Microsoft.Build.UnitTests.BackEnd
{
    /// <summary>
    /// Each packet is split up into a region, the region contains the tests for 
    /// a given packet type.
    /// </summary>
    public class NodePackets_Tests
    {
        #region LogMessagePacket Tests

        /// <summary>
        /// Verify a null build event throws an exception
        /// </summary>
        [Fact]
        public void LogMessageConstructorNullBuildEvent()
        {
            Assert.Throws<InternalErrorException>(() =>
            {
                LogMessagePacket packet = new LogMessagePacket(null);
            }
           );
        }

        /// <summary>
        /// Verify when creating a LogMessagePacket
        /// that the correct Event Type is set.
        /// </summary>
        [Fact]
        public void VerifyEventType()
        {
            BuildFinishedEventArgs buildFinished = new BuildFinishedEventArgs("Message", "Keyword", true);
            BuildStartedEventArgs buildStarted = new BuildStartedEventArgs("Message", "Help");
            BuildMessageEventArgs lowMessage = new BuildMessageEventArgs("Message", "help", "sender", MessageImportance.Low);
            TaskStartedEventArgs taskStarted = new TaskStartedEventArgs("message", "help", "projectFile", "taskFile", "taskName");
            TaskFinishedEventArgs taskFinished = new TaskFinishedEventArgs("message", "help", "projectFile", "taskFile", "taskName", true);
            TaskCommandLineEventArgs commandLine = new TaskCommandLineEventArgs("commandLine", "taskName", MessageImportance.Low);
            TaskParameterEventArgs taskParameter = CreateTaskParameter();
            BuildWarningEventArgs warning = new BuildWarningEventArgs("SubCategoryForSchemaValidationErrors", "MSB4000", "file", 1, 2, 3, 4, "message", "help", "sender");
            BuildErrorEventArgs error = new BuildErrorEventArgs("SubCategoryForSchemaValidationErrors", "MSB4000", "file", 1, 2, 3, 4, "message", "help", "sender");
            TargetStartedEventArgs targetStarted = new TargetStartedEventArgs("message", "help", "targetName", "ProjectFile", "targetFile");
            TargetFinishedEventArgs targetFinished = new TargetFinishedEventArgs("message", "help", "targetName", "ProjectFile", "targetFile", true);
            TargetSkippedEventArgs targetSkipped = CreateTargetSkipped();
            ProjectStartedEventArgs projectStarted = new ProjectStartedEventArgs(-1, "message", "help", "ProjectFile", "targetNames", null, null, null);
            ProjectFinishedEventArgs projectFinished = new ProjectFinishedEventArgs("message", "help", "ProjectFile", true);
            ExternalProjectStartedEventArgs externalStartedEvent = new ExternalProjectStartedEventArgs("message", "help", "senderName", "projectFile", "targetNames");
            ProjectEvaluationStartedEventArgs evaluationStarted = new ProjectEvaluationStartedEventArgs();
            ProjectEvaluationFinishedEventArgs evaluationFinished = new ProjectEvaluationFinishedEventArgs();

            VerifyLoggingPacket(buildFinished, LoggingEventType.BuildFinishedEvent);
            VerifyLoggingPacket(buildStarted, LoggingEventType.BuildStartedEvent);
            VerifyLoggingPacket(lowMessage, LoggingEventType.BuildMessageEvent);
            VerifyLoggingPacket(taskStarted, LoggingEventType.TaskStartedEvent);
            VerifyLoggingPacket(taskFinished, LoggingEventType.TaskFinishedEvent);
            VerifyLoggingPacket(commandLine, LoggingEventType.TaskCommandLineEvent);
            VerifyLoggingPacket(taskParameter, LoggingEventType.TaskParameterEvent);
            VerifyLoggingPacket(warning, LoggingEventType.BuildWarningEvent);
            VerifyLoggingPacket(error, LoggingEventType.BuildErrorEvent);
            VerifyLoggingPacket(targetStarted, LoggingEventType.TargetStartedEvent);
            VerifyLoggingPacket(targetFinished, LoggingEventType.TargetFinishedEvent);
            VerifyLoggingPacket(targetSkipped, LoggingEventType.TargetSkipped);
            VerifyLoggingPacket(projectStarted, LoggingEventType.ProjectStartedEvent);
            VerifyLoggingPacket(projectFinished, LoggingEventType.ProjectFinishedEvent);
            VerifyLoggingPacket(evaluationStarted, LoggingEventType.ProjectEvaluationStartedEvent);
            VerifyLoggingPacket(evaluationFinished, LoggingEventType.ProjectEvaluationFinishedEvent);
            VerifyLoggingPacket(externalStartedEvent, LoggingEventType.CustomEvent);
        }

        private static BuildEventContext CreateBuildEventContext()
        {
            return new BuildEventContext(1, 2, 3, 4, 5, 6, 7);
        }

        private static ProjectEvaluationStartedEventArgs CreateProjectEvaluationStarted()
        {
            string projectFile = "test.csproj";
            var result = new ProjectEvaluationStartedEventArgs(
                ResourceUtilities.GetResourceString("EvaluationStarted"),
                projectFile)
            {
                ProjectFile = projectFile
            };
            result.BuildEventContext = CreateBuildEventContext();

            return result;
        }

        private static ProjectEvaluationFinishedEventArgs CreateProjectEvaluationFinished()
        {
            string projectFile = "test.csproj";
            var result = new ProjectEvaluationFinishedEventArgs(
                ResourceUtilities.GetResourceString("EvaluationFinished"),
                projectFile)
            {
                ProjectFile = projectFile,
                GlobalProperties = CreateProperties(),
                Properties = CreateProperties(),
                Items = new ArrayList
                {
                    new DictionaryEntry("Compile", new TaskItemData("a", null)),
                    new DictionaryEntry("Compile", new TaskItemData("b", CreateStringDictionary())),
                    new DictionaryEntry("Reference", new TaskItemData("c", CreateStringDictionary())),
                }
            };
            result.BuildEventContext = CreateBuildEventContext();

            return result;
        }

        private static IEnumerable CreateProperties()
        {
            return new ArrayList
            {
                new DictionaryEntry("a", "b"),
                new DictionaryEntry("c", "d")
            };
        }

        private static Dictionary<string, string> CreateStringDictionary()
        {
            return new Dictionary<string, string>
            {
                { "a", "b" },
                { "c", "d" }
            };
        }

        private static TaskItemData[] CreateTaskItems()
        {
            var items = new TaskItemData[]
            {
                new TaskItemData("ItemSpec1", null),
                new TaskItemData("ItemSpec1", CreateStringDictionary()),
                new TaskItemData("ItemSpec2", Enumerable.Range(1, 3).ToDictionary(i => i.ToString(), i => i.ToString() + "value"))
            };
            return items;
        }

        private static TaskParameterEventArgs CreateTaskParameter()
        {
            // touch ItemGroupLoggingHelper to ensure static constructor runs
            _ = ItemGroupLoggingHelper.ItemGroupIncludeLogMessagePrefix;

            var items = CreateTaskItems();
            var result = new TaskParameterEventArgs(
                TaskParameterMessageKind.TaskInput,
                "ItemName",
                items,
                logItemMetadata: true,
                DateTime.MinValue);
            result.LineNumber = 30000;
            result.ColumnNumber = 50;

            // normalize line endings as we can't rely on the line endings of NodePackets_Tests.cs
            Assert.Equal(@"Task Parameter:
    ItemName=
        ItemSpec1
        ItemSpec1
                a=b
                c=d
        ItemSpec2
                1=1value
                2=2value
                3=3value".Replace("\r\n", "\n"), result.Message);

            return result;
        }

        private static TargetSkippedEventArgs CreateTargetSkipped()
        {
            var result = new TargetSkippedEventArgs(message: null)
            {
                BuildReason = TargetBuiltReason.DependsOn,
                SkipReason = TargetSkipReason.PreviouslyBuiltSuccessfully,
                BuildEventContext = CreateBuildEventContext(),
                OriginalBuildEventContext = CreateBuildEventContext(),
                Condition = "$(Condition) == 'true'",
                EvaluatedCondition = "'true' == 'true'",
                Importance = MessageImportance.Normal,
                OriginallySucceeded = true,
                ProjectFile = "1.proj",
                TargetFile = "1.proj",
                TargetName = "Build",
                ParentTarget = "ParentTarget"
            };
            return result;
        }

        /// <summary>
        /// Tests serialization of LogMessagePacket with each kind of event type.
        /// </summary>
        [Fact]
        public void TestTranslation()
        {
            // need to touch the type so that the static constructor runs
            _ = ItemGroupLoggingHelper.OutputItemParameterMessagePrefix;

            TaskItem item = new TaskItem("Hello", "my.proj");
            List<TaskItem> targetOutputs = new List<TaskItem>();
            targetOutputs.Add(item);

            string _initialTargetOutputLogging = Environment.GetEnvironmentVariable("MSBUILDTARGETOUTPUTLOGGING");
            Environment.SetEnvironmentVariable("MSBUILDTARGETOUTPUTLOGGING", "1");
            try {
                BuildEventArgs[] testArgs = new BuildEventArgs[]
                {
                    new BuildFinishedEventArgs("Message", "Keyword", true),
                    new BuildStartedEventArgs("Message", "Help"),
                    new BuildMessageEventArgs("Message", "help", "sender", MessageImportance.Low),
                    new TaskStartedEventArgs("message", "help", "projectFile", "taskFile", "taskName")
                    {
                        LineNumber = 345,
                        ColumnNumber = 123
                    },
                    new TaskFinishedEventArgs("message", "help", "projectFile", "taskFile", "taskName", true),
                    new TaskCommandLineEventArgs("commandLine", "taskName", MessageImportance.Low),
                    CreateTaskParameter(),
                    new BuildWarningEventArgs("SubCategoryForSchemaValidationErrors", "MSB4000", "file", 1, 2, 3, 4, "message", "help", "sender"),
                    new BuildErrorEventArgs("SubCategoryForSchemaValidationErrors", "MSB4000", "file", 1, 2, 3, 4, "message", "help", "sender"),
                    new TargetStartedEventArgs("message", "help", "targetName", "ProjectFile", "targetFile"),
                    new TargetFinishedEventArgs("message", "help", "targetName", "ProjectFile", "targetFile", true, targetOutputs),
                    new ProjectStartedEventArgs(-1, "message", "help", "ProjectFile", "targetNames", null, null, null),
                    new ProjectFinishedEventArgs("message", "help", "ProjectFile", true),
                    new ExternalProjectStartedEventArgs("message", "help", "senderName", "projectFile", "targetNames"),
                    CreateProjectEvaluationStarted(),
                    CreateProjectEvaluationFinished(),
                    CreateTargetSkipped()
                };

                foreach (BuildEventArgs arg in testArgs)
                {
                    LogMessagePacket packet = new LogMessagePacket(new KeyValuePair<int, BuildEventArgs>(0, arg));

                    ((ITranslatable)packet).Translate(TranslationHelpers.GetWriteTranslator());
                    INodePacket tempPacket = LogMessagePacket.FactoryForDeserialization(TranslationHelpers.GetReadTranslator()) as LogMessagePacket;

                    LogMessagePacket deserializedPacket = tempPacket as LogMessagePacket;

                    CompareLogMessagePackets(packet, deserializedPacket);
                }
            } finally {
                Environment.SetEnvironmentVariable("MSBUILDTARGETOUTPUTLOGGING", _initialTargetOutputLogging);
            }
        }

        /// <summary>
        /// Verify the LoggingMessagePacket is properly created from a build event. 
        /// This includes the packet type and the event type depending on which build event arg is passed in.
        /// </summary>
        /// <param name="buildEvent">Build event to put into a packet, and verify after packet creation</param>
        /// <param name="logEventType">What is the expected logging event type</param>
        private static void VerifyLoggingPacket(BuildEventArgs buildEvent, LoggingEventType logEventType)
        {
            LogMessagePacket packet = new LogMessagePacket(new KeyValuePair<int, BuildEventArgs>(0, buildEvent));
            Assert.Equal(logEventType, packet.EventType);
            Assert.Equal(NodePacketType.LogMessage, packet.Type);
            Assert.True(Object.ReferenceEquals(buildEvent, packet.NodeBuildEvent.Value.Value)); // "Expected buildEvent to have the same object reference as packet.BuildEvent"
        }

        /// <summary>
        /// Compares two BuildEventArgs objects for equivalence.
        /// </summary>
        private void CompareNodeBuildEventArgs(KeyValuePair<int, BuildEventArgs> leftTuple, KeyValuePair<int, BuildEventArgs> rightTuple, bool expectInvalidBuildEventContext)
        {
            BuildEventArgs left = leftTuple.Value;
            BuildEventArgs right = rightTuple.Value;

            if (expectInvalidBuildEventContext)
            {
                Assert.Equal(BuildEventContext.Invalid, right.BuildEventContext);
            }
            else
            {
                Assert.Equal(left.BuildEventContext, right.BuildEventContext);
            }

            Assert.Equal(leftTuple.Key, rightTuple.Key);
            Assert.Equal(left.HelpKeyword, right.HelpKeyword);
            Assert.Equal(left.Message, right.Message);
            Assert.Equal(left.SenderName, right.SenderName);
            Assert.Equal(left.ThreadId, right.ThreadId);
            Assert.Equal(left.Timestamp, right.Timestamp);
        }

        /// <summary>
        /// Compares two LogMessagePacket objects for equivalence.
        /// </summary>
        private void CompareLogMessagePackets(LogMessagePacket left, LogMessagePacket right)
        {
            Assert.Equal(left.EventType, right.EventType);
            Assert.Equal(left.NodeBuildEvent.Value.Value.GetType(), right.NodeBuildEvent.Value.Value.GetType());

            CompareNodeBuildEventArgs(left.NodeBuildEvent.Value, right.NodeBuildEvent.Value, left.EventType == LoggingEventType.CustomEvent /* expectInvalidBuildEventContext */);

            switch (left.EventType)
            {
                case LoggingEventType.BuildErrorEvent:
                    BuildErrorEventArgs leftError = left.NodeBuildEvent.Value.Value as BuildErrorEventArgs;
                    BuildErrorEventArgs rightError = right.NodeBuildEvent.Value.Value as BuildErrorEventArgs;
                    Assert.NotNull(leftError);
                    Assert.NotNull(rightError);
                    Assert.Equal(leftError.Code, rightError.Code);
                    Assert.Equal(leftError.ColumnNumber, rightError.ColumnNumber);
                    Assert.Equal(leftError.EndColumnNumber, rightError.EndColumnNumber);
                    Assert.Equal(leftError.EndLineNumber, rightError.EndLineNumber);
                    Assert.Equal(leftError.File, rightError.File);
                    Assert.Equal(leftError.LineNumber, rightError.LineNumber);
                    Assert.Equal(leftError.Message, rightError.Message);
                    Assert.Equal(leftError.Subcategory, rightError.Subcategory);
                    break;

                case LoggingEventType.BuildFinishedEvent:
                    BuildFinishedEventArgs leftFinished = left.NodeBuildEvent.Value.Value as BuildFinishedEventArgs;
                    BuildFinishedEventArgs rightFinished = right.NodeBuildEvent.Value.Value as BuildFinishedEventArgs;
                    Assert.NotNull(leftFinished);
                    Assert.NotNull(rightFinished);
                    Assert.Equal(leftFinished.Succeeded, rightFinished.Succeeded);
                    break;

                case LoggingEventType.BuildMessageEvent:
                    BuildMessageEventArgs leftMessage = left.NodeBuildEvent.Value.Value as BuildMessageEventArgs;
                    BuildMessageEventArgs rightMessage = right.NodeBuildEvent.Value.Value as BuildMessageEventArgs;
                    Assert.NotNull(leftMessage);
                    Assert.NotNull(rightMessage);
                    Assert.Equal(leftMessage.Importance, rightMessage.Importance);
                    break;

                case LoggingEventType.BuildStartedEvent:
                    BuildStartedEventArgs leftBuildStart = left.NodeBuildEvent.Value.Value as BuildStartedEventArgs;
                    BuildStartedEventArgs rightBuildStart = right.NodeBuildEvent.Value.Value as BuildStartedEventArgs;
                    Assert.NotNull(leftBuildStart);
                    Assert.NotNull(rightBuildStart);
                    break;

                case LoggingEventType.BuildWarningEvent:
                    BuildWarningEventArgs leftBuildWarn = left.NodeBuildEvent.Value.Value as BuildWarningEventArgs;
                    BuildWarningEventArgs rightBuildWarn = right.NodeBuildEvent.Value.Value as BuildWarningEventArgs;
                    Assert.NotNull(leftBuildWarn);
                    Assert.NotNull(rightBuildWarn);
                    Assert.Equal(leftBuildWarn.Code, rightBuildWarn.Code);
                    Assert.Equal(leftBuildWarn.ColumnNumber, rightBuildWarn.ColumnNumber);
                    Assert.Equal(leftBuildWarn.EndColumnNumber, rightBuildWarn.EndColumnNumber);
                    Assert.Equal(leftBuildWarn.EndLineNumber, rightBuildWarn.EndLineNumber);
                    Assert.Equal(leftBuildWarn.File, rightBuildWarn.File);
                    Assert.Equal(leftBuildWarn.LineNumber, rightBuildWarn.LineNumber);
                    Assert.Equal(leftBuildWarn.Subcategory, rightBuildWarn.Subcategory);
                    break;

                case LoggingEventType.CustomEvent:
                    ExternalProjectStartedEventArgs leftCustom = left.NodeBuildEvent.Value.Value as ExternalProjectStartedEventArgs;
                    ExternalProjectStartedEventArgs rightCustom = right.NodeBuildEvent.Value.Value as ExternalProjectStartedEventArgs;
                    Assert.NotNull(leftCustom);
                    Assert.NotNull(rightCustom);
                    Assert.Equal(leftCustom.ProjectFile, rightCustom.ProjectFile);
                    Assert.Equal(leftCustom.TargetNames, rightCustom.TargetNames);
                    break;

                case LoggingEventType.ProjectFinishedEvent:
                    ProjectFinishedEventArgs leftProjectFinished = left.NodeBuildEvent.Value.Value as ProjectFinishedEventArgs;
                    ProjectFinishedEventArgs rightProjectFinished = right.NodeBuildEvent.Value.Value as ProjectFinishedEventArgs;
                    Assert.NotNull(leftProjectFinished);
                    Assert.NotNull(rightProjectFinished);
                    Assert.Equal(leftProjectFinished.ProjectFile, rightProjectFinished.ProjectFile);
                    Assert.Equal(leftProjectFinished.Succeeded, rightProjectFinished.Succeeded);
                    break;

                case LoggingEventType.ProjectStartedEvent:
                    ProjectStartedEventArgs leftProjectStarted = left.NodeBuildEvent.Value.Value as ProjectStartedEventArgs;
                    ProjectStartedEventArgs rightProjectStarted = right.NodeBuildEvent.Value.Value as ProjectStartedEventArgs;
                    Assert.NotNull(leftProjectStarted);
                    Assert.NotNull(rightProjectStarted);
                    Assert.Equal(leftProjectStarted.ParentProjectBuildEventContext, rightProjectStarted.ParentProjectBuildEventContext);
                    Assert.Equal(leftProjectStarted.ProjectFile, rightProjectStarted.ProjectFile);
                    Assert.Equal(leftProjectStarted.ProjectId, rightProjectStarted.ProjectId);
                    Assert.Equal(leftProjectStarted.TargetNames, rightProjectStarted.TargetNames);

                    // UNDONE: (Serialization.) We don't actually serialize the items at this time.
                    // Assert.AreEqual(leftProjectStarted.Items, rightProjectStarted.Items);
                    // UNDONE: (Serialization.) We don't actually serialize properties at this time.
                    // Assert.AreEqual(leftProjectStarted.Properties, rightProjectStarted.Properties);
                    break;

                case LoggingEventType.ProjectEvaluationStartedEvent:
                    ProjectEvaluationStartedEventArgs leftEvaluationStarted = left.NodeBuildEvent.Value.Value as ProjectEvaluationStartedEventArgs;
                    ProjectEvaluationStartedEventArgs rightEvaluationStarted = right.NodeBuildEvent.Value.Value as ProjectEvaluationStartedEventArgs;
                    Assert.NotNull(leftEvaluationStarted);
                    Assert.NotNull(rightEvaluationStarted);
                    Assert.Equal(leftEvaluationStarted.ProjectFile, rightEvaluationStarted.ProjectFile);
                    break;

                case LoggingEventType.ProjectEvaluationFinishedEvent:
                    ProjectEvaluationFinishedEventArgs leftEvaluationFinished = left.NodeBuildEvent.Value.Value as ProjectEvaluationFinishedEventArgs;
                    ProjectEvaluationFinishedEventArgs rightEvaluationFinished = right.NodeBuildEvent.Value.Value as ProjectEvaluationFinishedEventArgs;
                    Assert.NotNull(leftEvaluationFinished);
                    Assert.NotNull(rightEvaluationFinished);
                    Assert.Equal(leftEvaluationFinished.ProjectFile, rightEvaluationFinished.ProjectFile);
                    Assert.Equal(leftEvaluationFinished.ProfilerResult, rightEvaluationFinished.ProfilerResult);
                    Assert.Equal(
                        TranslationHelpers.GetPropertiesString(leftEvaluationFinished.GlobalProperties),
                        TranslationHelpers.GetPropertiesString(rightEvaluationFinished.GlobalProperties));
                    Assert.Equal(
                        TranslationHelpers.GetPropertiesString(leftEvaluationFinished.Properties),
                        TranslationHelpers.GetPropertiesString(rightEvaluationFinished.Properties));
                    Assert.Equal(
                        TranslationHelpers.GetMultiItemsString(leftEvaluationFinished.Items),
                        TranslationHelpers.GetMultiItemsString(rightEvaluationFinished.Items));
                    break;

                case LoggingEventType.TargetFinishedEvent:
                    TargetFinishedEventArgs leftTargetFinished = left.NodeBuildEvent.Value.Value as TargetFinishedEventArgs;
                    TargetFinishedEventArgs rightTargetFinished = right.NodeBuildEvent.Value.Value as TargetFinishedEventArgs;
                    Assert.NotNull(leftTargetFinished);
                    Assert.NotNull(rightTargetFinished);
                    Assert.Equal(leftTargetFinished.ProjectFile, rightTargetFinished.ProjectFile);
                    Assert.Equal(leftTargetFinished.Succeeded, rightTargetFinished.Succeeded);
                    Assert.Equal(leftTargetFinished.TargetFile, rightTargetFinished.TargetFile);
                    Assert.Equal(leftTargetFinished.TargetName, rightTargetFinished.TargetName);
                    //TODO: target output translation is a special case and is done in TranslateTargetFinishedEvent
                    //Assert.Equal(leftTargetFinished.TargetOutputs, rightTargetFinished.TargetOutputs);
                    break;

                case LoggingEventType.TargetStartedEvent:
                    TargetStartedEventArgs leftTargetStarted = left.NodeBuildEvent.Value.Value as TargetStartedEventArgs;
                    TargetStartedEventArgs rightTargetStarted = right.NodeBuildEvent.Value.Value as TargetStartedEventArgs;
                    Assert.NotNull(leftTargetStarted);
                    Assert.NotNull(rightTargetStarted);
                    Assert.Equal(leftTargetStarted.ProjectFile, rightTargetStarted.ProjectFile);
                    Assert.Equal(leftTargetStarted.TargetFile, rightTargetStarted.TargetFile);
                    Assert.Equal(leftTargetStarted.TargetName, rightTargetStarted.TargetName);
                    break;

                case LoggingEventType.TargetSkipped:
                    TargetSkippedEventArgs leftTargetSkipped = left.NodeBuildEvent.Value.Value as TargetSkippedEventArgs;
                    TargetSkippedEventArgs rightTargetSkipped = right.NodeBuildEvent.Value.Value as TargetSkippedEventArgs;
                    Assert.Equal(leftTargetSkipped.BuildReason, rightTargetSkipped.BuildReason);
                    Assert.Equal(leftTargetSkipped.SkipReason, rightTargetSkipped.SkipReason);
                    Assert.Equal(leftTargetSkipped.BuildEventContext, rightTargetSkipped.BuildEventContext);
                    Assert.Equal(leftTargetSkipped.OriginalBuildEventContext, rightTargetSkipped.OriginalBuildEventContext);
                    Assert.Equal(leftTargetSkipped.Condition, rightTargetSkipped.Condition);
                    Assert.Equal(leftTargetSkipped.EvaluatedCondition, rightTargetSkipped.EvaluatedCondition);
                    Assert.Equal(leftTargetSkipped.Importance, rightTargetSkipped.Importance);
                    Assert.Equal(leftTargetSkipped.OriginallySucceeded, rightTargetSkipped.OriginallySucceeded);
                    Assert.Equal(leftTargetSkipped.ProjectFile, rightTargetSkipped.ProjectFile);
                    Assert.Equal(leftTargetSkipped.TargetFile, rightTargetSkipped.TargetFile);
                    Assert.Equal(leftTargetSkipped.TargetName, rightTargetSkipped.TargetName);
                    Assert.Equal(leftTargetSkipped.ParentTarget, rightTargetSkipped.ParentTarget);
                    break;

                case LoggingEventType.TaskCommandLineEvent:
                    TaskCommandLineEventArgs leftCommand = left.NodeBuildEvent.Value.Value as TaskCommandLineEventArgs;
                    TaskCommandLineEventArgs rightCommand = right.NodeBuildEvent.Value.Value as TaskCommandLineEventArgs;
                    Assert.NotNull(leftCommand);
                    Assert.NotNull(rightCommand);
                    Assert.Equal(leftCommand.CommandLine, rightCommand.CommandLine);
                    Assert.Equal(leftCommand.Importance, rightCommand.Importance);
                    Assert.Equal(leftCommand.TaskName, rightCommand.TaskName);
                    break;

                case LoggingEventType.TaskParameterEvent:
                    var leftTaskParameter = left.NodeBuildEvent.Value.Value as TaskParameterEventArgs;
                    var rightTaskParameter = right.NodeBuildEvent.Value.Value as TaskParameterEventArgs;
                    Assert.NotNull(leftTaskParameter);
                    Assert.NotNull(rightTaskParameter);
                    Assert.Equal(leftTaskParameter.Kind, rightTaskParameter.Kind);
                    Assert.Equal(leftTaskParameter.ItemType, rightTaskParameter.ItemType);
                    Assert.Equal(leftTaskParameter.Items.Count, rightTaskParameter.Items.Count);
                    Assert.Equal(leftTaskParameter.Message, rightTaskParameter.Message);
                    Assert.Equal(leftTaskParameter.BuildEventContext, rightTaskParameter.BuildEventContext);
                    Assert.Equal(leftTaskParameter.Timestamp, rightTaskParameter.Timestamp);
                    Assert.Equal(leftTaskParameter.LineNumber, rightTaskParameter.LineNumber);
                    Assert.Equal(leftTaskParameter.ColumnNumber, rightTaskParameter.ColumnNumber);
                    break;

                case LoggingEventType.TaskFinishedEvent:
                    TaskFinishedEventArgs leftTaskFinished = left.NodeBuildEvent.Value.Value as TaskFinishedEventArgs;
                    TaskFinishedEventArgs rightTaskFinished = right.NodeBuildEvent.Value.Value as TaskFinishedEventArgs;
                    Assert.NotNull(leftTaskFinished);
                    Assert.NotNull(rightTaskFinished);
                    Assert.Equal(leftTaskFinished.ProjectFile, rightTaskFinished.ProjectFile);
                    Assert.Equal(leftTaskFinished.Succeeded, rightTaskFinished.Succeeded);
                    Assert.Equal(leftTaskFinished.TaskFile, rightTaskFinished.TaskFile);
                    Assert.Equal(leftTaskFinished.TaskName, rightTaskFinished.TaskName);
                    break;

                case LoggingEventType.TaskStartedEvent:
                    TaskStartedEventArgs leftTaskStarted = left.NodeBuildEvent.Value.Value as TaskStartedEventArgs;
                    TaskStartedEventArgs rightTaskStarted = right.NodeBuildEvent.Value.Value as TaskStartedEventArgs;
                    Assert.NotNull(leftTaskStarted);
                    Assert.NotNull(rightTaskStarted);
                    Assert.Equal(leftTaskStarted.ProjectFile, rightTaskStarted.ProjectFile);
                    Assert.Equal(leftTaskStarted.TaskFile, rightTaskStarted.TaskFile);
                    Assert.Equal(leftTaskStarted.TaskName, rightTaskStarted.TaskName);
                    Assert.Equal(leftTaskStarted.LineNumber, rightTaskStarted.LineNumber);
                    Assert.Equal(leftTaskStarted.ColumnNumber, rightTaskStarted.ColumnNumber);
                    break;

                default:
                    Assert.True(false, string.Format("Unexpected logging event type {0}", left.EventType));
                    break;
            }
        }

        #endregion
    }
}
