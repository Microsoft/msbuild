using System;
using System.IO;
using System.IO.Compression;
using Microsoft.Build.Framework;
using Microsoft.Build.Shared;
using Microsoft.Build.Utilities;

namespace Microsoft.Build.Logging
{
    /// <summary>
    /// A logger that serializes all incoming BuildEventArgs in a compressed binary file (*.binlog). The file
    /// can later be played back and piped into other loggers (file, console, etc) to reconstruct the log contents
    /// as if a real build was happening. Additionally, this format can be read by tools for
    /// analysis or visualization. Since the file format preserves structure, tools don't have to parse
    /// text logs that erase a lot of useful information.
    /// </summary>
    /// <remarks>The logger is public so that it can be instantiated from MSBuild.exe via command-line switch.</remarks>
    public sealed class BinaryLogger : ILogger
    {
        // version 2: 
        //   - new BuildEventContext.EvaluationId
        //   - new record kinds: ProjectEvaluationStarted, ProjectEvaluationFinished
        // version 3:
        //   - new ProjectImportedEventArgs.ImportIgnored
        // version 4:
        //   - new TargetSkippedEventArgs
        //   - new TargetStartedEventArgs.BuildReason
        // version 5:
        //   - new EvaluationFinished.ProfilerResult
        // version 6:
        //   - Ids and parent ids for the evaluation locations
        // version 7:
        //   - Include ProjectStartedEventArgs.GlobalProperties
        // version 8:
        //   - This was used in a now-reverted change but is the same as 9.
        // version 9:
        //   - new record kinds: EnvironmentVariableRead, PropertyReassignment, UninitializedPropertyRead
        // version 10:
        //   - new record kinds:
        //      * String - deduplicate strings by hashing and write a string record before it's used
        //      * NameValueList - deduplicate arrays of name-value pairs such as properties, items and metadata
        //                        in a separate record and refer to those records from regular records
        //                        where a list used to be written in-place
        // version 11:
        //   - new record kind: TaskParameterEventArgs
        // version 12:
        //   - add GlobalProperties, Properties and Items on ProjectEvaluationFinished
        // version 13:
        //   - don't log Message where it can be recovered
        //   - log arguments for LazyFormattedBuildEventArgs
        //   - TargetSkippedEventArgs: added OriginallySucceeded, Condition, EvaluatedCondition
        // version 14:
        //   - TargetSkippedEventArgs: added SkipReason, OriginalBuildEventContext
        internal const int FileFormatVersion = 14;

        private Stream stream;
        private BinaryWriter binaryWriter;
        private BuildEventArgsWriter eventArgsWriter;
        private ProjectImportsCollector projectImportsCollector;
        private string _initialTargetOutputLogging;
        private bool _initialLogImports;

        /// <summary>
        /// Describes whether to collect the project files (including imported project files) used during the build.
        /// If the project files are collected they can be embedded in the log file or as a separate zip archive.
        /// </summary>
        public enum ProjectImportsCollectionMode
        {
            /// <summary>
            /// Don't collect any files during the build.
            /// </summary>
            None,

            /// <summary>
            /// Embed all project files directly in the log file.
            /// </summary>
            Embed,

            /// <summary>
            /// Create an external .ProjectImports.zip archive for the project files.
            /// </summary>
            ZipFile
        }

        /// <summary>
        /// Gets or sets whether to capture and embed project and target source files used during the build.
        /// </summary>
        public ProjectImportsCollectionMode CollectProjectImports { get; set; } = ProjectImportsCollectionMode.Embed;

        private string FilePath { get; set; }

        /// <summary>
        /// The binary logger Verbosity is always maximum (Diagnostic). It tries to capture as much
        /// information as possible.
        /// </summary>
        public LoggerVerbosity Verbosity { get; set; } = LoggerVerbosity.Diagnostic;

        /// <summary>
        /// The only supported parameter is the output log file path (e.g. "msbuild.binlog") 
        /// </summary>
        public string Parameters { get; set; }

        /// <summary>
        /// Initializes the logger by subscribing to events of IEventSource
        /// </summary>
        public void Initialize(IEventSource eventSource)
        {
            _initialTargetOutputLogging = Environment.GetEnvironmentVariable("MSBUILDTARGETOUTPUTLOGGING");
            _initialLogImports = Traits.Instance.EscapeHatches.LogProjectImports;

            Environment.SetEnvironmentVariable("MSBUILDTARGETOUTPUTLOGGING", "true");
            Environment.SetEnvironmentVariable("MSBUILDLOGIMPORTS", "1");
            Traits.Instance.EscapeHatches.LogProjectImports = true;
            bool logPropertiesAndItemsAfterEvaluation = Traits.Instance.EscapeHatches.LogPropertiesAndItemsAfterEvaluation ?? true;

            ProcessParameters();

            try
            {
                string logDirectory = null;
                try
                {
                    logDirectory = Path.GetDirectoryName(FilePath);
                }
                catch (Exception)
                {
                    // Directory creation is best-effort; if finding its path fails don't create the directory
                    // and possibly let the FileStream constructor below report the failure
                }

                if (logDirectory != null)
                {
                    Directory.CreateDirectory(logDirectory);
                }

                stream = new FileStream(FilePath, FileMode.Create);

                if (CollectProjectImports != ProjectImportsCollectionMode.None)
                {
                    projectImportsCollector = new ProjectImportsCollector(FilePath, CollectProjectImports == ProjectImportsCollectionMode.ZipFile);
                }

                if (eventSource is IEventSource3 eventSource3)
                {
                    eventSource3.IncludeEvaluationMetaprojects();
                }

                if (logPropertiesAndItemsAfterEvaluation && eventSource is IEventSource4 eventSource4)
                {
                    eventSource4.IncludeEvaluationPropertiesAndItems();
                }
            }
            catch (Exception e)
            {
                string errorCode;
                string helpKeyword;
                string message = ResourceUtilities.FormatResourceStringStripCodeAndKeyword(out errorCode, out helpKeyword, "InvalidFileLoggerFile", FilePath, e.Message);
                throw new LoggerException(message, e, errorCode, helpKeyword);
            }

            stream = new GZipStream(stream, CompressionLevel.Optimal);

            // wrapping the GZipStream in a buffered stream significantly improves performance
            // and the max throughput is reached with a 32K buffer. See details here:
            // https://github.com/dotnet/runtime/issues/39233#issuecomment-745598847
            stream = new BufferedStream(stream, bufferSize: 32768);
            binaryWriter = new BinaryWriter(stream);
            eventArgsWriter = new BuildEventArgsWriter(binaryWriter);

            if (projectImportsCollector != null)
            {
                eventArgsWriter.EmbedFile += EventArgsWriter_EmbedFile;
            }

            binaryWriter.Write(FileFormatVersion);

            LogInitialInfo();

            eventSource.AnyEventRaised += EventSource_AnyEventRaised;
        }

        private void EventArgsWriter_EmbedFile(string filePath)
        {
            if (projectImportsCollector != null)
            {
                projectImportsCollector.AddFile(filePath);
            }
        }

        private void LogInitialInfo()
        {
            LogMessage("BinLogFilePath=" + FilePath);
            LogMessage("CurrentUICulture=" + System.Globalization.CultureInfo.CurrentUICulture.Name);
        }

        private void LogMessage(string text)
        {
            var args = new BuildMessageEventArgs(text, helpKeyword: null, senderName: "BinaryLogger", MessageImportance.Normal);
            args.BuildEventContext = BuildEventContext.Invalid;
            Write(args);
        }

        /// <summary>
        /// Closes the underlying file stream.
        /// </summary>
        public void Shutdown()
        {
            Environment.SetEnvironmentVariable("MSBUILDTARGETOUTPUTLOGGING", _initialTargetOutputLogging);
            Environment.SetEnvironmentVariable("MSBUILDLOGIMPORTS", _initialLogImports ? "1" : "");
            Traits.Instance.EscapeHatches.LogProjectImports = _initialLogImports;

            if (projectImportsCollector != null)
            {
                if (CollectProjectImports == ProjectImportsCollectionMode.Embed)
                {
                    eventArgsWriter.WriteBlob(BinaryLogRecordKind.ProjectImportArchive, projectImportsCollector.GetAllBytes());
                }

                projectImportsCollector.Close();
                projectImportsCollector = null;
            }

            if (stream != null)
            {
                // It's hard to determine whether we're at the end of decoding GZipStream
                // so add an explicit 0 at the end to signify end of file
                stream.WriteByte((byte)BinaryLogRecordKind.EndOfFile);
                stream.Flush();
                stream.Dispose();
                stream = null;
            }
        }

        private void EventSource_AnyEventRaised(object sender, BuildEventArgs e)
        {
            Write(e);
        }

        private void Write(BuildEventArgs e)
        {
            if (stream != null)
            {
                // TODO: think about queuing to avoid contention
                lock (eventArgsWriter)
                {
                    eventArgsWriter.Write(e);
                }

                if (projectImportsCollector != null)
                {
                    CollectImports(e);
                }
            }
        }

        private void CollectImports(BuildEventArgs e)
        {
            if (e is ProjectImportedEventArgs importArgs && importArgs.ImportedProjectFile != null)
            {
                projectImportsCollector.AddFile(importArgs.ImportedProjectFile);
            }
            else if (e is ProjectStartedEventArgs projectArgs)
            {
                projectImportsCollector.AddFile(projectArgs.ProjectFile);
            }
            else if (e is MetaprojectGeneratedEventArgs metaprojectArgs)
            {
                projectImportsCollector.AddFileFromMemory(metaprojectArgs.ProjectFile, metaprojectArgs.metaprojectXml);
            }
        }

        /// <summary>
        /// Processes the parameters given to the logger from MSBuild.
        /// </summary>
        /// <exception cref="LoggerException">
        /// </exception>
        private void ProcessParameters()
        {
            if (Parameters == null)
            {
                throw new LoggerException(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("InvalidBinaryLoggerParameters", ""));
            }

            var parameters = Parameters.Split(MSBuildConstants.SemicolonChar, StringSplitOptions.RemoveEmptyEntries);
            foreach (var parameter in parameters)
            {
                if (string.Equals(parameter, "ProjectImports=None", StringComparison.OrdinalIgnoreCase))
                {
                    CollectProjectImports = ProjectImportsCollectionMode.None;
                }
                else if (string.Equals(parameter, "ProjectImports=Embed", StringComparison.OrdinalIgnoreCase))
                {
                    CollectProjectImports = ProjectImportsCollectionMode.Embed;
                }
                else if (string.Equals(parameter, "ProjectImports=ZipFile", StringComparison.OrdinalIgnoreCase))
                {
                    CollectProjectImports = ProjectImportsCollectionMode.ZipFile;
                }
                else if (parameter.EndsWith(".binlog", StringComparison.OrdinalIgnoreCase))
                {
                    FilePath = parameter;
                    if (FilePath.StartsWith("LogFile=", StringComparison.OrdinalIgnoreCase))
                    {
                        FilePath = FilePath.Substring("LogFile=".Length);
                    }

                    FilePath = FilePath.Trim('"');
                }
                else
                {
                    throw new LoggerException(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("InvalidBinaryLoggerParameters", parameter));
                }
            }

            if (FilePath == null)
            {
                FilePath = "msbuild.binlog";
            }

            try
            {
                FilePath = Path.GetFullPath(FilePath);
            }
            catch (Exception e)
            {
                string errorCode;
                string helpKeyword;
                string message = ResourceUtilities.FormatResourceStringStripCodeAndKeyword(out errorCode, out helpKeyword, "InvalidFileLoggerFile", FilePath, e.Message);
                throw new LoggerException(message, e, errorCode, helpKeyword);
            }
        }
    }
}
