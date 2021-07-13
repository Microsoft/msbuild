// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Build.BackEnd;
using Microsoft.Build.Collections;
using Microsoft.Build.Framework;
using Microsoft.Build.Framework.Profiler;
using Microsoft.Build.Shared;

namespace Microsoft.Build.Logging
{
    /// <summary>
    /// Deserializes and returns BuildEventArgs-derived objects from a BinaryReader
    /// </summary>
    public class BuildEventArgsReader : IDisposable
    {
        private readonly BinaryReader binaryReader;
        private readonly int fileFormatVersion;
        private long recordNumber = 0;

        /// <summary>
        /// A list of string records we've encountered so far. If it's a small string, it will be the string directly.
        /// If it's a large string, it will be a pointer into a temporary page file where the string content will be
        /// written out to. This is necessary so we don't keep all the strings in memory when reading large binlogs.
        /// We will OOM otherwise.
        /// </summary>
        private readonly List<object> stringRecords = new List<object>();

        /// <summary>
        /// A list of dictionaries we've encountered so far. Dictionaries are referred to by their order in this list.
        /// </summary>
        /// <remarks>This is designed to not hold on to strings. We just store the string indices and
        /// hydrate the dictionary on demand before returning.</remarks>
        private readonly List<(int keyIndex, int valueIndex)[]> nameValueListRecords = new List<(int, int)[]>();

        /// <summary>
        /// A "page-file" for storing strings we've read so far. Keeping them in memory would OOM the 32-bit MSBuild
        /// when reading large binlogs. This is a no-op in a 64-bit process.
        /// </summary>
        private StringStorage stringStorage = new StringStorage();

        // reflection is needed to set these three fields because public constructors don't provide
        // a way to set these from the outside
        private static FieldInfo buildEventArgsFieldThreadId =
            typeof(BuildEventArgs).GetField("threadId", BindingFlags.Instance | BindingFlags.NonPublic);
        private static FieldInfo buildEventArgsFieldSenderName =
            typeof(BuildEventArgs).GetField("senderName", BindingFlags.Instance | BindingFlags.NonPublic);
        private static FieldInfo buildEventArgsFieldTimestamp =
            typeof(BuildEventArgs).GetField("timestamp", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// Initializes a new instance of BuildEventArgsReader using a BinaryReader instance
        /// </summary>
        /// <param name="binaryReader">The BinaryReader to read BuildEventArgs from</param>
        /// <param name="fileFormatVersion">The file format version of the log file being read.</param>
        public BuildEventArgsReader(BinaryReader binaryReader, int fileFormatVersion)
        {
            this.binaryReader = binaryReader;
            this.fileFormatVersion = fileFormatVersion;
        }

        public void Dispose()
        {
            if (stringStorage != null)
            {
                stringStorage.Dispose();
                stringStorage = null;
            }
        }

        /// <summary>
        /// Raised when the log reader encounters a binary blob embedded in the stream.
        /// The arguments include the blob kind and the byte buffer with the contents.
        /// </summary>
        internal event Action<BinaryLogRecordKind, byte[]> OnBlobRead;

        /// <summary>
        /// Reads the next log record from the binary reader. If there are no more records, returns null.
        /// </summary>
        public BuildEventArgs Read()
        {
            BinaryLogRecordKind recordKind = (BinaryLogRecordKind)ReadInt32();

            // Skip over data storage records since they don't result in a BuildEventArgs.
            // just ingest their data and continue.
            while (IsAuxiliaryRecord(recordKind))
            {
                // these are ordered by commonality
                if (recordKind == BinaryLogRecordKind.String)
                {
                    ReadStringRecord();
                }
                else if (recordKind == BinaryLogRecordKind.NameValueList)
                {
                    ReadNameValueList();
                }
                else if (recordKind == BinaryLogRecordKind.ProjectImportArchive)
                {
                    ReadBlob(recordKind);
                }

                recordNumber += 1;

                recordKind = (BinaryLogRecordKind)ReadInt32();
            }

            BuildEventArgs result = null;
            switch (recordKind)
            {
                case BinaryLogRecordKind.EndOfFile:
                    break;
                case BinaryLogRecordKind.BuildStarted:
                    result = ReadBuildStartedEventArgs();
                    break;
                case BinaryLogRecordKind.BuildFinished:
                    result = ReadBuildFinishedEventArgs();
                    break;
                case BinaryLogRecordKind.ProjectStarted:
                    result = ReadProjectStartedEventArgs();
                    break;
                case BinaryLogRecordKind.ProjectFinished:
                    result = ReadProjectFinishedEventArgs();
                    break;
                case BinaryLogRecordKind.TargetStarted:
                    result = ReadTargetStartedEventArgs();
                    break;
                case BinaryLogRecordKind.TargetFinished:
                    result = ReadTargetFinishedEventArgs();
                    break;
                case BinaryLogRecordKind.TaskStarted:
                    result = ReadTaskStartedEventArgs();
                    break;
                case BinaryLogRecordKind.TaskFinished:
                    result = ReadTaskFinishedEventArgs();
                    break;
                case BinaryLogRecordKind.Error:
                    result = ReadBuildErrorEventArgs();
                    break;
                case BinaryLogRecordKind.Warning:
                    result = ReadBuildWarningEventArgs();
                    break;
                case BinaryLogRecordKind.Message:
                    result = ReadBuildMessageEventArgs();
                    break;
                case BinaryLogRecordKind.CriticalBuildMessage:
                    result = ReadCriticalBuildMessageEventArgs();
                    break;
                case BinaryLogRecordKind.TaskCommandLine:
                    result = ReadTaskCommandLineEventArgs();
                    break;
                case BinaryLogRecordKind.TaskParameter:
                    result = ReadTaskParameterEventArgs();
                    break;
                case BinaryLogRecordKind.ProjectEvaluationStarted:
                    result = ReadProjectEvaluationStartedEventArgs();
                    break;
                case BinaryLogRecordKind.ProjectEvaluationFinished:
                    result = ReadProjectEvaluationFinishedEventArgs();
                    break;
                case BinaryLogRecordKind.ProjectImported:
                    result = ReadProjectImportedEventArgs();
                    break;
                case BinaryLogRecordKind.TargetSkipped:
                    result = ReadTargetSkippedEventArgs();
                    break;
                case BinaryLogRecordKind.EnvironmentVariableRead:
                    result = ReadEnvironmentVariableReadEventArgs();
                    break;
                case BinaryLogRecordKind.PropertyReassignment:
                    result = ReadPropertyReassignmentEventArgs();
                    break;
                case BinaryLogRecordKind.UninitializedPropertyRead:
                    result = ReadUninitializedPropertyReadEventArgs();
                    break;
                case BinaryLogRecordKind.PropertyInitialValueSet:
                    result = ReadPropertyInitialValueSetEventArgs();
                    break;
                default:
                    break;
            }

            recordNumber += 1;

            return result;
        }

        private static bool IsAuxiliaryRecord(BinaryLogRecordKind recordKind)
        {
            return recordKind == BinaryLogRecordKind.String
                || recordKind == BinaryLogRecordKind.NameValueList
                || recordKind == BinaryLogRecordKind.ProjectImportArchive;
        }

        private void ReadBlob(BinaryLogRecordKind kind)
        {
            int length = ReadInt32();
            byte[] bytes = binaryReader.ReadBytes(length);
            OnBlobRead?.Invoke(kind, bytes);
        }

        private void ReadNameValueList()
        {
            int count = ReadInt32();

            var list = new (int, int)[count];
            for (int i = 0; i < count; i++)
            {
                int key = ReadInt32();
                int value = ReadInt32();
                list[i] = (key, value);
            }

            nameValueListRecords.Add(list);
        }

        private IDictionary<string, string> GetNameValueList(int id)
        {
            id -= BuildEventArgsWriter.NameValueRecordStartIndex;
            if (id >= 0 && id < nameValueListRecords.Count)
            {
                var list = nameValueListRecords[id];

                // We can't cache these as they would hold on to strings.
                // This reader is designed to not hold onto strings,
                // so that we can fit in a 32-bit process when reading huge binlogs
                var dictionary = ArrayDictionary<string, string>.Create(list.Length);
                for (int i = 0; i < list.Length; i++)
                {
                    string key = GetStringFromRecord(list[i].keyIndex);
                    string value = GetStringFromRecord(list[i].valueIndex);
                    if (key != null)
                    {
                        dictionary.Add(key, value);
                    }
                }

                return dictionary;
            }

            // this should never happen for valid binlogs
            throw new InvalidDataException(
                $"NameValueList record number {recordNumber} is invalid: index {id} is not within {stringRecords.Count}.");
        }

        private void ReadStringRecord()
        {
            string text = ReadString();
            object storedString = stringStorage.Add(text);
            stringRecords.Add(storedString);
        }

        private BuildEventArgs ReadProjectImportedEventArgs()
        {
            var fields = ReadBuildEventArgsFields(readImportance: true);

            bool importIgnored = false;

            // the ImportIgnored field was introduced in file format version 3
            if (fileFormatVersion > 2)
            {
                importIgnored = ReadBoolean();
            }

            var importedProjectFile = ReadOptionalString();
            var unexpandedProject = ReadOptionalString();

            var e = new ProjectImportedEventArgs(
                fields.LineNumber,
                fields.ColumnNumber,
                fields.Message,
                fields.Arguments);

            SetCommonFields(e, fields);

            e.ProjectFile = fields.ProjectFile;

            e.ImportedProjectFile = importedProjectFile;
            e.UnexpandedProject = unexpandedProject;
            e.ImportIgnored = importIgnored;
            return e;
        }

        private BuildEventArgs ReadTargetSkippedEventArgs()
        {
            var fields = ReadBuildEventArgsFields(readImportance: true);

            var targetFile = ReadOptionalString();
            var targetName = ReadOptionalString();
            var parentTarget = ReadOptionalString();

            string condition = null;
            string evaluatedCondition = null;
            bool originallySucceeded = false;
            TargetSkipReason skipReason = TargetSkipReason.None;
            BuildEventContext originalBuildEventContext = null;
            if (fileFormatVersion >= 13)
            {
                condition = ReadOptionalString();
                evaluatedCondition = ReadOptionalString();
                originallySucceeded = ReadBoolean();

                // Attempt to infer skip reason from the data we have
                skipReason = condition != null ?
                    TargetSkipReason.ConditionWasFalse // condition expression only stored when false
                    : originallySucceeded ?
                        TargetSkipReason.PreviouslyBuiltSuccessfully
                        : TargetSkipReason.PreviouslyBuiltUnsuccessfully;
            }

            var buildReason = (TargetBuiltReason)ReadInt32();

            if (fileFormatVersion >= 14)
            {
                skipReason = (TargetSkipReason)ReadInt32();
                originalBuildEventContext = binaryReader.ReadOptionalBuildEventContext();
            }

            var e = new TargetSkippedEventArgs(
                fields.Message,
                fields.Arguments);

            SetCommonFields(e, fields);

            e.ProjectFile = fields.ProjectFile;
            e.TargetFile = targetFile;
            e.TargetName = targetName;
            e.ParentTarget = parentTarget;
            e.BuildReason = buildReason;
            e.Condition = condition;
            e.EvaluatedCondition = evaluatedCondition;
            e.OriginallySucceeded = originallySucceeded;
            e.SkipReason = skipReason;
            e.OriginalBuildEventContext = originalBuildEventContext;

            return e;
        }

        private BuildEventArgs ReadBuildStartedEventArgs()
        {
            var fields = ReadBuildEventArgsFields();
            var environment = ReadStringDictionary();

            var e = new BuildStartedEventArgs(
                fields.Message,
                fields.HelpKeyword,
                environment);
            SetCommonFields(e, fields);
            return e;
        }

        private BuildEventArgs ReadBuildFinishedEventArgs()
        {
            var fields = ReadBuildEventArgsFields();
            var succeeded = ReadBoolean();

            var e = new BuildFinishedEventArgs(
                fields.Message,
                fields.HelpKeyword,
                succeeded,
                fields.Timestamp);
            SetCommonFields(e, fields);
            return e;
        }

        private BuildEventArgs ReadProjectEvaluationStartedEventArgs()
        {
            var fields = ReadBuildEventArgsFields();
            var projectFile = ReadDeduplicatedString();

            var e = new ProjectEvaluationStartedEventArgs(
                ResourceUtilities.GetResourceString("EvaluationStarted"),
                projectFile)
            {
                ProjectFile = projectFile
            };
            SetCommonFields(e, fields);
            return e;
        }

        private BuildEventArgs ReadProjectEvaluationFinishedEventArgs()
        {
            var fields = ReadBuildEventArgsFields();
            var projectFile = ReadDeduplicatedString();

            var e = new ProjectEvaluationFinishedEventArgs(
                ResourceUtilities.GetResourceString("EvaluationFinished"),
                projectFile)
            {
                ProjectFile = projectFile
            };
            SetCommonFields(e, fields);

            if (fileFormatVersion >= 12)
            {
                IEnumerable globalProperties = null;
                if (ReadBoolean())
                {
                    globalProperties = ReadStringDictionary();
                }

                var propertyList = ReadPropertyList();
                var itemList = ReadProjectItems();

                e.GlobalProperties = globalProperties;
                e.Properties = propertyList;
                e.Items = itemList;
            }

            // ProfilerResult was introduced in version 5
            if (fileFormatVersion > 4)
            {
                var hasProfileData = ReadBoolean();
                if (hasProfileData)
                {
                    var count = ReadInt32();

                    var d = new Dictionary<EvaluationLocation, ProfiledLocation>(count);
                    for (int i = 0; i < count; i++)
                    {
                        var evaluationLocation = ReadEvaluationLocation();
                        var profiledLocation = ReadProfiledLocation();
                        d[evaluationLocation] = profiledLocation;
                    }

                    e.ProfilerResult = new ProfilerResult(d);
                }
            }

            return e;
        }

        private BuildEventArgs ReadProjectStartedEventArgs()
        {
            var fields = ReadBuildEventArgsFields();
            BuildEventContext parentContext = null;
            if (ReadBoolean())
            {
                parentContext = ReadBuildEventContext();
            }

            var projectFile = ReadOptionalString();
            var projectId = ReadInt32();
            var targetNames = ReadDeduplicatedString();
            var toolsVersion = ReadOptionalString();

            IDictionary<string, string> globalProperties = null;

            if (fileFormatVersion > 6)
            {
                if (ReadBoolean())
                {
                    globalProperties = ReadStringDictionary();
                }
            }

            var propertyList = ReadPropertyList();
            var itemList = ReadProjectItems();

            var e = new ProjectStartedEventArgs(
                projectId,
                fields.Message,
                fields.HelpKeyword,
                projectFile,
                targetNames,
                propertyList,
                itemList,
                parentContext,
                globalProperties,
                toolsVersion);
            SetCommonFields(e, fields);
            return e;
        }

        private BuildEventArgs ReadProjectFinishedEventArgs()
        {
            var fields = ReadBuildEventArgsFields();
            var projectFile = ReadOptionalString();
            var succeeded = ReadBoolean();

            var e = new ProjectFinishedEventArgs(
                fields.Message,
                fields.HelpKeyword,
                projectFile,
                succeeded,
                fields.Timestamp);
            SetCommonFields(e, fields);
            return e;
        }

        private BuildEventArgs ReadTargetStartedEventArgs()
        {
            var fields = ReadBuildEventArgsFields();
            var targetName = ReadOptionalString();
            var projectFile = ReadOptionalString();
            var targetFile = ReadOptionalString();
            var parentTarget = ReadOptionalString();
            // BuildReason was introduced in version 4
            var buildReason = fileFormatVersion > 3 ? (TargetBuiltReason)ReadInt32() : TargetBuiltReason.None;

            var e = new TargetStartedEventArgs(
                fields.Message,
                fields.HelpKeyword,
                targetName,
                projectFile,
                targetFile,
                parentTarget,
                buildReason,
                fields.Timestamp);
            SetCommonFields(e, fields);
            return e;
        }

        private BuildEventArgs ReadTargetFinishedEventArgs()
        {
            var fields = ReadBuildEventArgsFields();
            var succeeded = ReadBoolean();
            var projectFile = ReadOptionalString();
            var targetFile = ReadOptionalString();
            var targetName = ReadOptionalString();
            var targetOutputItemList = ReadTaskItemList();

            var e = new TargetFinishedEventArgs(
                fields.Message,
                fields.HelpKeyword,
                targetName,
                projectFile,
                targetFile,
                succeeded,
                fields.Timestamp,
                targetOutputItemList);
            SetCommonFields(e, fields);
            return e;
        }

        private BuildEventArgs ReadTaskStartedEventArgs()
        {
            var fields = ReadBuildEventArgsFields();
            var taskName = ReadOptionalString();
            var projectFile = ReadOptionalString();
            var taskFile = ReadOptionalString();

            var e = new TaskStartedEventArgs(
                fields.Message,
                fields.HelpKeyword,
                projectFile,
                taskFile,
                taskName,
                fields.Timestamp);
            e.LineNumber = fields.LineNumber;
            e.ColumnNumber = fields.ColumnNumber;
            SetCommonFields(e, fields);
            return e;
        }

        private BuildEventArgs ReadTaskFinishedEventArgs()
        {
            var fields = ReadBuildEventArgsFields();
            var succeeded = ReadBoolean();
            var taskName = ReadOptionalString();
            var projectFile = ReadOptionalString();
            var taskFile = ReadOptionalString();

            var e = new TaskFinishedEventArgs(
                fields.Message,
                fields.HelpKeyword,
                projectFile,
                taskFile,
                taskName,
                succeeded,
                fields.Timestamp);
            SetCommonFields(e, fields);
            return e;
        }

        private BuildEventArgs ReadBuildErrorEventArgs()
        {
            var fields = ReadBuildEventArgsFields();
            ReadDiagnosticFields(fields);

            var e = new BuildErrorEventArgs(
                fields.Subcategory,
                fields.Code,
                fields.File,
                fields.LineNumber,
                fields.ColumnNumber,
                fields.EndLineNumber,
                fields.EndColumnNumber,
                fields.Message,
                fields.HelpKeyword,
                fields.SenderName,
                fields.Timestamp,
                fields.Arguments);
            e.BuildEventContext = fields.BuildEventContext;
            e.ProjectFile = fields.ProjectFile;
            return e;
        }

        private BuildEventArgs ReadBuildWarningEventArgs()
        {
            var fields = ReadBuildEventArgsFields();
            ReadDiagnosticFields(fields);

            var e = new BuildWarningEventArgs(
                fields.Subcategory,
                fields.Code,
                fields.File,
                fields.LineNumber,
                fields.ColumnNumber,
                fields.EndLineNumber,
                fields.EndColumnNumber,
                fields.Message,
                fields.HelpKeyword,
                fields.SenderName,
                fields.Timestamp,
                fields.Arguments);
            e.BuildEventContext = fields.BuildEventContext;
            e.ProjectFile = fields.ProjectFile;
            return e;
        }

        private BuildEventArgs ReadBuildMessageEventArgs()
        {
            var fields = ReadBuildEventArgsFields(readImportance: true);

            var e = new BuildMessageEventArgs(
                fields.Subcategory,
                fields.Code,
                fields.File,
                fields.LineNumber,
                fields.ColumnNumber,
                fields.EndLineNumber,
                fields.EndColumnNumber,
                fields.Message,
                fields.HelpKeyword,
                fields.SenderName,
                fields.Importance,
                fields.Timestamp,
                fields.Arguments);
            e.BuildEventContext = fields.BuildEventContext;
            e.ProjectFile = fields.ProjectFile;
            return e;
        }

        private BuildEventArgs ReadTaskCommandLineEventArgs()
        {
            var fields = ReadBuildEventArgsFields(readImportance: true);
            var commandLine = ReadOptionalString();
            var taskName = ReadOptionalString();

            var e = new TaskCommandLineEventArgs(
                commandLine,
                taskName,
                fields.Importance,
                fields.Timestamp);
            e.BuildEventContext = fields.BuildEventContext;
            e.ProjectFile = fields.ProjectFile;
            return e;
        }

        private BuildEventArgs ReadTaskParameterEventArgs()
        {
            var fields = ReadBuildEventArgsFields(readImportance: true);

            var kind = (TaskParameterMessageKind)ReadInt32();
            var itemType = ReadDeduplicatedString();
            var items = ReadTaskItemList() as IList;

            var e = ItemGroupLoggingHelper.CreateTaskParameterEventArgs(
                fields.BuildEventContext,
                kind,
                itemType,
                items,
                logItemMetadata: true,
                fields.Timestamp,
                fields.LineNumber,
                fields.ColumnNumber);
            e.ProjectFile = fields.ProjectFile;
            return e;
        }

        private BuildEventArgs ReadCriticalBuildMessageEventArgs()
        {
            var fields = ReadBuildEventArgsFields(readImportance: true);

            var e = new CriticalBuildMessageEventArgs(
                fields.Subcategory,
                fields.Code,
                fields.File,
                fields.LineNumber,
                fields.ColumnNumber,
                fields.EndLineNumber,
                fields.EndColumnNumber,
                fields.Message,
                fields.HelpKeyword,
                fields.SenderName,
                fields.Timestamp,
                fields.Arguments);
            e.BuildEventContext = fields.BuildEventContext;
            e.ProjectFile = fields.ProjectFile;
            return e;
        }

        private BuildEventArgs ReadEnvironmentVariableReadEventArgs()
        {
            var fields = ReadBuildEventArgsFields(readImportance: true);

            var environmentVariableName = ReadDeduplicatedString();

            var e = new EnvironmentVariableReadEventArgs(
                environmentVariableName,
                fields.Message,
                fields.HelpKeyword,
                fields.SenderName,
                fields.Importance);
            SetCommonFields(e, fields);

            return e;
        }

        private BuildEventArgs ReadPropertyReassignmentEventArgs()
        {
            var fields = ReadBuildEventArgsFields(readImportance: true);

            string propertyName = ReadDeduplicatedString();
            string previousValue = ReadDeduplicatedString();
            string newValue = ReadDeduplicatedString();
            string location = ReadDeduplicatedString();

            var e = new PropertyReassignmentEventArgs(
                propertyName,
                previousValue,
                newValue,
                location,
                fields.Message,
                fields.HelpKeyword,
                fields.SenderName,
                fields.Importance);
            SetCommonFields(e, fields);

            return e;
        }

        private BuildEventArgs ReadUninitializedPropertyReadEventArgs()
        {
            var fields = ReadBuildEventArgsFields(readImportance: true);
            string propertyName = ReadDeduplicatedString();

            var e = new UninitializedPropertyReadEventArgs(
                propertyName,
                fields.Message,
                fields.HelpKeyword,
                fields.SenderName,
                fields.Importance);
            SetCommonFields(e, fields);

            return e;
        }

        private BuildEventArgs ReadPropertyInitialValueSetEventArgs()
        {
            var fields = ReadBuildEventArgsFields(readImportance: true);

            string propertyName = ReadDeduplicatedString();
            string propertyValue = ReadDeduplicatedString();
            string propertySource = ReadDeduplicatedString();

            var e = new PropertyInitialValueSetEventArgs(
                propertyName,
                propertyValue,
                propertySource,
                fields.Message,
                fields.HelpKeyword,
                fields.SenderName,
                fields.Importance);
            SetCommonFields(e, fields);

            return e;
        }

        /// <summary>
        /// For errors and warnings these 8 fields are written out explicitly
        /// (their presence is not marked as a bit in the flags). So we have to
        /// read explicitly.
        /// </summary>
        /// <param name="fields"></param>
        private void ReadDiagnosticFields(BuildEventArgsFields fields)
        {
            fields.Subcategory = ReadOptionalString();
            fields.Code = ReadOptionalString();
            fields.File = ReadOptionalString();
            fields.ProjectFile = ReadOptionalString();
            fields.LineNumber = ReadInt32();
            fields.ColumnNumber = ReadInt32();
            fields.EndLineNumber = ReadInt32();
            fields.EndColumnNumber = ReadInt32();
        }

        private BuildEventArgsFields ReadBuildEventArgsFields(bool readImportance = false)
        {
            BuildEventArgsFieldFlags flags = (BuildEventArgsFieldFlags)ReadInt32();
            var result = new BuildEventArgsFields();
            result.Flags = flags;

            if ((flags & BuildEventArgsFieldFlags.Message) != 0)
            {
                result.Message = ReadDeduplicatedString();
            }

            if ((flags & BuildEventArgsFieldFlags.BuildEventContext) != 0)
            {
                result.BuildEventContext = ReadBuildEventContext();
            }

            if ((flags & BuildEventArgsFieldFlags.ThreadId) != 0)
            {
                result.ThreadId = ReadInt32();
            }

            if ((flags & BuildEventArgsFieldFlags.HelpKeyword) != 0)
            {
                result.HelpKeyword = ReadDeduplicatedString();
            }

            if ((flags & BuildEventArgsFieldFlags.SenderName) != 0)
            {
                result.SenderName = ReadDeduplicatedString();
            }

            if ((flags & BuildEventArgsFieldFlags.Timestamp) != 0)
            {
                result.Timestamp = ReadDateTime();
            }

            if ((flags & BuildEventArgsFieldFlags.Subcategory) != 0)
            {
                result.Subcategory = ReadDeduplicatedString();
            }

            if ((flags & BuildEventArgsFieldFlags.Code) != 0)
            {
                result.Code = ReadDeduplicatedString();
            }

            if ((flags & BuildEventArgsFieldFlags.File) != 0)
            {
                result.File = ReadDeduplicatedString();
            }

            if ((flags & BuildEventArgsFieldFlags.ProjectFile) != 0)
            {
                result.ProjectFile = ReadDeduplicatedString();
            }

            if ((flags & BuildEventArgsFieldFlags.LineNumber) != 0)
            {
                result.LineNumber = ReadInt32();
            }

            if ((flags & BuildEventArgsFieldFlags.ColumnNumber) != 0)
            {
                result.ColumnNumber = ReadInt32();
            }

            if ((flags & BuildEventArgsFieldFlags.EndLineNumber) != 0)
            {
                result.EndLineNumber = ReadInt32();
            }

            if ((flags & BuildEventArgsFieldFlags.EndColumnNumber) != 0)
            {
                result.EndColumnNumber = ReadInt32();
            }

            if ((flags & BuildEventArgsFieldFlags.Arguments) != 0)
            {
                int count = ReadInt32();
                object[] arguments = new object[count];
                for (int i = 0; i < count; i++)
                {
                    arguments[i] = ReadDeduplicatedString();
                }

                result.Arguments = arguments;
            }

            if ((fileFormatVersion < 13 && readImportance) || (fileFormatVersion >= 13 && (flags & BuildEventArgsFieldFlags.Importance) != 0))
            {
                result.Importance = (MessageImportance)ReadInt32();
            }

            return result;
        }

        private void SetCommonFields(BuildEventArgs buildEventArgs, BuildEventArgsFields fields)
        {
            buildEventArgs.BuildEventContext = fields.BuildEventContext;

            if ((fields.Flags & BuildEventArgsFieldFlags.ThreadId) != 0)
            {
                buildEventArgsFieldThreadId.SetValue(buildEventArgs, fields.ThreadId);
            }

            if ((fields.Flags & BuildEventArgsFieldFlags.SenderName) != 0)
            {
                buildEventArgsFieldSenderName.SetValue(buildEventArgs, fields.SenderName);
            }

            if ((fields.Flags & BuildEventArgsFieldFlags.Timestamp) != 0)
            {
                buildEventArgs.RawTimestamp = fields.Timestamp;
            }
        }

        private IEnumerable ReadPropertyList()
        {
            var properties = ReadStringDictionary();
            if (properties == null || properties.Count == 0)
            {
                return null;
            }

            int count = properties.Count;
            var list = new DictionaryEntry[count];
            int i = 0;
            foreach (var property in properties)
            {
                list[i] = new DictionaryEntry(property.Key, property.Value);
                i++;
            }

            return list;
        }

        private BuildEventContext ReadBuildEventContext()
        {
            int nodeId = ReadInt32();
            int projectContextId = ReadInt32();
            int targetId = ReadInt32();
            int taskId = ReadInt32();
            int submissionId = ReadInt32();
            int projectInstanceId = ReadInt32();

            // evaluationId was introduced in format version 2
            int evaluationId = BuildEventContext.InvalidEvaluationId;
            if (fileFormatVersion > 1)
            {
                evaluationId = ReadInt32();
            }

            var result = new BuildEventContext(
                submissionId,
                nodeId,
                evaluationId,
                projectInstanceId,
                projectContextId,
                targetId,
                taskId);
            return result;
        }

        private IDictionary<string, string> ReadStringDictionary()
        {
            if (fileFormatVersion < 10)
            {
                return ReadLegacyStringDictionary();
            }

            int index = ReadInt32();
            if (index == 0)
            {
                return null;
            }

            var record = GetNameValueList(index);
            return record;
        }

        private IDictionary<string, string> ReadLegacyStringDictionary()
        {
            int count = ReadInt32();
            if (count == 0)
            {
                return null;
            }

            var result = new Dictionary<string, string>(count);

            for (int i = 0; i < count; i++)
            {
                string key = ReadString();
                string value = ReadString();
                result[key] = value;
            }

            return result;
        }

        private ITaskItem ReadTaskItem()
        {
            string itemSpec = ReadDeduplicatedString();
            var metadata = ReadStringDictionary();

            var taskItem = new TaskItemData(itemSpec, metadata);
            return taskItem;
        }

        private IEnumerable ReadProjectItems()
        {
            IList<DictionaryEntry> list;

            // starting with format version 10 project items are grouped by name
            // so we only have to write the name once, and then the count of items
            // with that name. When reading a legacy binlog we need to read the
            // old style flat list where the name is duplicated for each item.
            if (fileFormatVersion < 10)
            {
                int count = ReadInt32();
                if (count == 0)
                {
                    return null;
                }

                list = new DictionaryEntry[count];
                for (int i = 0; i < count; i++)
                {
                    string itemName = ReadString();
                    ITaskItem item = ReadTaskItem();
                    list[i] = new DictionaryEntry(itemName, item);
                }
            }
            else if (fileFormatVersion < 12)
            {
                int count = ReadInt32();
                if (count == 0)
                {
                    return null;
                }

                list = new List<DictionaryEntry>();
                for (int i = 0; i < count; i++)
                {
                    string itemType = ReadDeduplicatedString();
                    var items = ReadTaskItemList();
                    if (items != null)
                    {
                        foreach (var item in items)
                        {
                            list.Add(new DictionaryEntry(itemType, item));
                        }
                    }
                }

                if (list.Count == 0)
                {
                    list = null;
                }
            }
            else
            {
                list = new List<DictionaryEntry>();

                while (true)
                {
                    string itemType = ReadDeduplicatedString();
                    if (string.IsNullOrEmpty(itemType))
                    {
                        break;
                    }

                    var items = ReadTaskItemList();
                    if (items != null)
                    {
                        foreach (var item in items)
                        {
                            list.Add(new DictionaryEntry(itemType, item));
                        }
                    }
                }

                if (list.Count == 0)
                {
                    list = null;
                }
            }

            return list;
        }

        private IEnumerable ReadTaskItemList()
        {
            int count = ReadInt32();
            if (count == 0)
            {
                return null;
            }

            var list = new ITaskItem[count];

            for (int i = 0; i < count; i++)
            {
                ITaskItem item = ReadTaskItem();
                list[i] = item;
            }

            return list;
        }

        private string ReadString()
        {
            return binaryReader.ReadString();
        }

        private string ReadOptionalString()
        {
            if (fileFormatVersion < 10)
            {
                if (ReadBoolean())
                {
                    return ReadString();
                }
                else
                {
                    return null;
                }
            }

            return ReadDeduplicatedString();
        }

        private string ReadDeduplicatedString()
        {
            if (fileFormatVersion < 10)
            {
                return ReadString();
            }

            int index = ReadInt32();
            return GetStringFromRecord(index);
        }

        private string GetStringFromRecord(int index)
        {
            if (index == 0)
            {
                return null;
            }
            else if (index == 1)
            {
                return string.Empty;
            }

            // we reserve numbers 2-9 for future use.
            // the writer assigns 10 as the index of the first string
            index -= BuildEventArgsWriter.StringStartIndex;
            if (index >= 0 && index < this.stringRecords.Count)
            {
                object storedString = stringRecords[index];
                string result = stringStorage.Get(storedString);
                return result;
            }

            // this should never happen for valid binlogs
            throw new InvalidDataException(
                $"String record number {recordNumber} is invalid: string index {index} is not within {stringRecords.Count}.");
        }

        private int ReadInt32()
        {
            // on some platforms (net5) this method was added to BinaryReader
            // but it's not available on others. Call our own extension method
            // explicitly to avoid ambiguity.
            return BinaryReaderExtensions.Read7BitEncodedInt(binaryReader);
        }

        private long ReadInt64()
        {
            return binaryReader.ReadInt64();
        }

        private bool ReadBoolean()
        {
            return binaryReader.ReadBoolean();
        }

        private DateTime ReadDateTime()
        {
            return new DateTime(binaryReader.ReadInt64(), (DateTimeKind)ReadInt32());
        }

        private TimeSpan ReadTimeSpan()
        {
            return new TimeSpan(binaryReader.ReadInt64());
        }

        private ProfiledLocation ReadProfiledLocation()
        {
            var numberOfHits = ReadInt32();
            var exclusiveTime = ReadTimeSpan();
            var inclusiveTime = ReadTimeSpan();

            return new ProfiledLocation(inclusiveTime, exclusiveTime, numberOfHits);
        }

        private EvaluationLocation ReadEvaluationLocation()
        {
            var elementName = ReadOptionalString();
            var description = ReadOptionalString();
            var evaluationDescription = ReadOptionalString();
            var file = ReadOptionalString();
            var kind = (EvaluationLocationKind)ReadInt32();
            var evaluationPass = (EvaluationPass)ReadInt32();

            int? line = null;
            var hasLine = ReadBoolean();
            if (hasLine)
            {
                line = ReadInt32();
            }

            // Id and parent Id were introduced in version 6
            if (fileFormatVersion > 5)
            {
                var id = ReadInt64();
                long? parentId = null;
                var hasParent = ReadBoolean();
                if (hasParent)
                {
                    parentId = ReadInt64();
                }

                return new EvaluationLocation(id, parentId, evaluationPass, evaluationDescription, file, line, elementName, description, kind);
            }

            return new EvaluationLocation(0, null, evaluationPass, evaluationDescription, file, line, elementName, description, kind);
        }

        /// <summary>
        /// Locates the string in the page file.
        /// </summary>
        internal class StringPosition
        {
            /// <summary>
            /// Offset in the file.
            /// </summary>
            public long FilePosition;

            /// <summary>
            /// The length of the string in chars (not bytes).
            /// </summary>
            public int StringLength;
        }

        /// <summary>
        /// Stores large strings in a temp file on disk, to avoid keeping all strings in memory.
        /// Only creates a file for 32-bit MSBuild.exe, just returns the string directly on 64-bit.
        /// </summary>
        internal class StringStorage : IDisposable
        {
            private readonly string filePath;
            private FileStream stream;
            private StreamWriter streamWriter;
            private readonly StreamReader streamReader;
            private readonly StringBuilder stringBuilder;

            public const int StringSizeThreshold = 1024;

            public StringStorage()
            {
                if (!Environment.Is64BitProcess)
                {
                    filePath = Path.GetTempFileName();
                    var utf8noBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                    stream = new FileStream(
                        filePath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        bufferSize: 4096, // 4096 seems to have the best performance on SSD
                        FileOptions.RandomAccess | FileOptions.DeleteOnClose);

                    // 65536 has no particular significance, and maybe could be tuned
                    // but 65536 performs well enough and isn't a lot of memory for a singleton
                    streamWriter = new StreamWriter(stream, utf8noBom, 65536);
                    streamWriter.AutoFlush = true;
                    streamReader = new StreamReader(stream, utf8noBom);
                    stringBuilder = new StringBuilder();
                }
            }

            private long totalAllocatedShortStrings = 0;

            public object Add(string text)
            {
                if (filePath == null)
                {
                    // on 64-bit, we have as much memory as we want
                    // so no need to write to the file at all
                    return text;
                }

                // Tradeoff between not crashing with OOM on large binlogs and
                // keeping the playback of smaller binlogs relatively fast.
                // It is slow to store all small strings in the file and constantly
                // seek to retrieve them. Instead we'll keep storing small strings
                // in memory until we allocate 2 GB. After that, all strings go to
                // the file.
                // Win-win: small binlog playback is fast and large binlog playback
                // doesn't OOM.
                if (text.Length <= StringSizeThreshold && totalAllocatedShortStrings < 1_000_000_000)
                {
                    totalAllocatedShortStrings += text.Length;
                    return text;
                }

                var stringPosition = new StringPosition();

                stringPosition.FilePosition = stream.Position;

                streamWriter.Write(text);

                stringPosition.StringLength = text.Length;
                return stringPosition;
            }

            public string Get(object storedString)
            {
                if (storedString is string text)
                {
                    return text;
                }

                var position = (StringPosition)storedString;

                stream.Position = position.FilePosition;
                stringBuilder.Length = position.StringLength;
                for (int i = 0; i < position.StringLength; i++)
                {
                    char ch = (char)streamReader.Read();
                    stringBuilder[i] = ch;
                }

                stream.Position = stream.Length;
                streamReader.DiscardBufferedData();

                string result = stringBuilder.ToString();
                stringBuilder.Clear();
                return result;
            }

            public void Dispose()
            {
                try
                {
                    if (streamWriter != null)
                    {
                        streamWriter.Dispose();
                        streamWriter = null;
                    }

                    if (stream != null)
                    {
                        stream.Dispose();
                        stream = null;
                    }
                }
                catch
                {
                    // The StringStorage class is not crucial for other functionality and if 
                    // there are exceptions when closing the temp file, it's too late to do anything about it.
                    // Since we don't want to disrupt anything and the file is in the TEMP directory, it will
                    // get cleaned up at some point anyway.
                }
            }
        }
    }
}
