// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
namespace Microsoft.Build.Framework
{
    public delegate void AnyEventHandler(object sender, Microsoft.Build.Framework.BuildEventArgs e);
    [System.Runtime.InteropServices.StructLayoutAttribute(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public partial struct BuildEngineResult
    {
        private object _dummy;
        private int _dummyPrimitive;
        public BuildEngineResult(bool result, System.Collections.Generic.List<System.Collections.Generic.IDictionary<string, Microsoft.Build.Framework.ITaskItem[]>> targetOutputsPerProject) { throw null; }
        public bool Result { get { throw null; } }
        public System.Collections.Generic.IList<System.Collections.Generic.IDictionary<string, Microsoft.Build.Framework.ITaskItem[]>> TargetOutputsPerProject { get { throw null; } }
    }
    public partial class BuildErrorEventArgs : Microsoft.Build.Framework.LazyFormattedBuildEventArgs
    {
        protected BuildErrorEventArgs() { }
        public BuildErrorEventArgs(string subcategory, string code, string file, int lineNumber, int columnNumber, int endLineNumber, int endColumnNumber, string message, string helpKeyword, string senderName) { }
        public BuildErrorEventArgs(string subcategory, string code, string file, int lineNumber, int columnNumber, int endLineNumber, int endColumnNumber, string message, string helpKeyword, string senderName, System.DateTime eventTimestamp) { }
        public BuildErrorEventArgs(string subcategory, string code, string file, int lineNumber, int columnNumber, int endLineNumber, int endColumnNumber, string message, string helpKeyword, string senderName, System.DateTime eventTimestamp, params object[] messageArgs) { }
        public BuildErrorEventArgs(string subcategory, string code, string file, int lineNumber, int columnNumber, int endLineNumber, int endColumnNumber, string message, string helpKeyword, string senderName, string helpLink, System.DateTime eventTimestamp, params object[] messageArgs) { }
        public string Code { get { throw null; } }
        public int ColumnNumber { get { throw null; } }
        public int EndColumnNumber { get { throw null; } }
        public int EndLineNumber { get { throw null; } }
        public string File { get { throw null; } }
        public string HelpLink { get { throw null; } }
        public int LineNumber { get { throw null; } }
        public string ProjectFile { get { throw null; } set { } }
        public string Subcategory { get { throw null; } }
    }
    public delegate void BuildErrorEventHandler(object sender, Microsoft.Build.Framework.BuildErrorEventArgs e);
    public abstract partial class BuildEventArgs : System.EventArgs
    {
        protected BuildEventArgs() { }
        protected BuildEventArgs(string message, string helpKeyword, string senderName) { }
        protected BuildEventArgs(string message, string helpKeyword, string senderName, System.DateTime eventTimestamp) { }
        public Microsoft.Build.Framework.BuildEventContext BuildEventContext { get { throw null; } set { } }
        public string HelpKeyword { get { throw null; } }
        public virtual string Message { get { throw null; } protected set { } }
        protected internal string RawMessage { get { throw null; } set { } }
        protected internal System.DateTime RawTimestamp { get { throw null; } set { } }
        public string SenderName { get { throw null; } }
        public int ThreadId { get { throw null; } }
        public System.DateTime Timestamp { get { throw null; } }
    }
    public partial class BuildEventContext
    {
        public const int InvalidEvaluationId = -1;
        public const int InvalidNodeId = -2;
        public const int InvalidProjectContextId = -2;
        public const int InvalidProjectInstanceId = -1;
        public const int InvalidSubmissionId = -1;
        public const int InvalidTargetId = -1;
        public const int InvalidTaskId = -1;
        public BuildEventContext(int nodeId, int targetId, int projectContextId, int taskId) { }
        public BuildEventContext(int nodeId, int projectInstanceId, int projectContextId, int targetId, int taskId) { }
        public BuildEventContext(int submissionId, int nodeId, int projectInstanceId, int projectContextId, int targetId, int taskId) { }
        public BuildEventContext(int submissionId, int nodeId, int evaluationId, int projectInstanceId, int projectContextId, int targetId, int taskId) { }
        public long BuildRequestId { get { throw null; } }
        public int EvaluationId { get { throw null; } }
        public static Microsoft.Build.Framework.BuildEventContext Invalid { get { throw null; } }
        public int NodeId { get { throw null; } }
        public int ProjectContextId { get { throw null; } }
        public int ProjectInstanceId { get { throw null; } }
        public int SubmissionId { get { throw null; } }
        public int TargetId { get { throw null; } }
        public int TaskId { get { throw null; } }
        public override bool Equals(object obj) { throw null; }
        public override int GetHashCode() { throw null; }
        public static bool operator ==(Microsoft.Build.Framework.BuildEventContext left, Microsoft.Build.Framework.BuildEventContext right) { throw null; }
        public static bool operator !=(Microsoft.Build.Framework.BuildEventContext left, Microsoft.Build.Framework.BuildEventContext right) { throw null; }
        public override string ToString() { throw null; }
    }
    public partial class BuildFinishedEventArgs : Microsoft.Build.Framework.BuildStatusEventArgs
    {
        protected BuildFinishedEventArgs() { }
        public BuildFinishedEventArgs(string message, string helpKeyword, bool succeeded) { }
        public BuildFinishedEventArgs(string message, string helpKeyword, bool succeeded, System.DateTime eventTimestamp) { }
        public BuildFinishedEventArgs(string message, string helpKeyword, bool succeeded, System.DateTime eventTimestamp, params object[] messageArgs) { }
        public bool Succeeded { get { throw null; } }
    }
    public delegate void BuildFinishedEventHandler(object sender, Microsoft.Build.Framework.BuildFinishedEventArgs e);
    public partial class BuildMessageEventArgs : Microsoft.Build.Framework.LazyFormattedBuildEventArgs
    {
        protected BuildMessageEventArgs() { }
        public BuildMessageEventArgs(string message, string helpKeyword, string senderName, Microsoft.Build.Framework.MessageImportance importance) { }
        public BuildMessageEventArgs(string message, string helpKeyword, string senderName, Microsoft.Build.Framework.MessageImportance importance, System.DateTime eventTimestamp) { }
        public BuildMessageEventArgs(string message, string helpKeyword, string senderName, Microsoft.Build.Framework.MessageImportance importance, System.DateTime eventTimestamp, params object[] messageArgs) { }
        public BuildMessageEventArgs(string subcategory, string code, string file, int lineNumber, int columnNumber, int endLineNumber, int endColumnNumber, string message, string helpKeyword, string senderName, Microsoft.Build.Framework.MessageImportance importance) { }
        public BuildMessageEventArgs(string subcategory, string code, string file, int lineNumber, int columnNumber, int endLineNumber, int endColumnNumber, string message, string helpKeyword, string senderName, Microsoft.Build.Framework.MessageImportance importance, System.DateTime eventTimestamp) { }
        public BuildMessageEventArgs(string subcategory, string code, string file, int lineNumber, int columnNumber, int endLineNumber, int endColumnNumber, string message, string helpKeyword, string senderName, Microsoft.Build.Framework.MessageImportance importance, System.DateTime eventTimestamp, params object[] messageArgs) { }
        public string Code { get { throw null; } }
        public int ColumnNumber { get { throw null; } }
        public int EndColumnNumber { get { throw null; } }
        public int EndLineNumber { get { throw null; } }
        public string File { get { throw null; } }
        public Microsoft.Build.Framework.MessageImportance Importance { get { throw null; } }
        public int LineNumber { get { throw null; } }
        public string ProjectFile { get { throw null; } set { } }
        public string Subcategory { get { throw null; } }
    }
    public delegate void BuildMessageEventHandler(object sender, Microsoft.Build.Framework.BuildMessageEventArgs e);
    public partial class BuildStartedEventArgs : Microsoft.Build.Framework.BuildStatusEventArgs
    {
        protected BuildStartedEventArgs() { }
        public BuildStartedEventArgs(string message, string helpKeyword) { }
        public BuildStartedEventArgs(string message, string helpKeyword, System.Collections.Generic.IDictionary<string, string> environmentOfBuild) { }
        public BuildStartedEventArgs(string message, string helpKeyword, System.DateTime eventTimestamp) { }
        public BuildStartedEventArgs(string message, string helpKeyword, System.DateTime eventTimestamp, params object[] messageArgs) { }
        public System.Collections.Generic.IDictionary<string, string> BuildEnvironment { get { throw null; } }
    }
    public delegate void BuildStartedEventHandler(object sender, Microsoft.Build.Framework.BuildStartedEventArgs e);
    public abstract partial class BuildStatusEventArgs : Microsoft.Build.Framework.LazyFormattedBuildEventArgs
    {
        protected BuildStatusEventArgs() { }
        protected BuildStatusEventArgs(string message, string helpKeyword, string senderName) { }
        protected BuildStatusEventArgs(string message, string helpKeyword, string senderName, System.DateTime eventTimestamp) { }
        protected BuildStatusEventArgs(string message, string helpKeyword, string senderName, System.DateTime eventTimestamp, params object[] messageArgs) { }
    }
    public delegate void BuildStatusEventHandler(object sender, Microsoft.Build.Framework.BuildStatusEventArgs e);
    public partial class BuildWarningEventArgs : Microsoft.Build.Framework.LazyFormattedBuildEventArgs
    {
        protected BuildWarningEventArgs() { }
        public BuildWarningEventArgs(string subcategory, string code, string file, int lineNumber, int columnNumber, int endLineNumber, int endColumnNumber, string message, string helpKeyword, string senderName) { }
        public BuildWarningEventArgs(string subcategory, string code, string file, int lineNumber, int columnNumber, int endLineNumber, int endColumnNumber, string message, string helpKeyword, string senderName, System.DateTime eventTimestamp) { }
        public BuildWarningEventArgs(string subcategory, string code, string file, int lineNumber, int columnNumber, int endLineNumber, int endColumnNumber, string message, string helpKeyword, string senderName, System.DateTime eventTimestamp, params object[] messageArgs) { }
        public BuildWarningEventArgs(string subcategory, string code, string file, int lineNumber, int columnNumber, int endLineNumber, int endColumnNumber, string message, string helpKeyword, string senderName, string helpLink, System.DateTime eventTimestamp, params object[] messageArgs) { }
        public string Code { get { throw null; } }
        public int ColumnNumber { get { throw null; } }
        public int EndColumnNumber { get { throw null; } }
        public int EndLineNumber { get { throw null; } }
        public string File { get { throw null; } }
        public string HelpLink { get { throw null; } }
        public int LineNumber { get { throw null; } }
        public string ProjectFile { get { throw null; } set { } }
        public string Subcategory { get { throw null; } }
    }
    public delegate void BuildWarningEventHandler(object sender, Microsoft.Build.Framework.BuildWarningEventArgs e);
    public partial class CriticalBuildMessageEventArgs : Microsoft.Build.Framework.BuildMessageEventArgs
    {
        protected CriticalBuildMessageEventArgs() { }
        public CriticalBuildMessageEventArgs(string subcategory, string code, string file, int lineNumber, int columnNumber, int endLineNumber, int endColumnNumber, string message, string helpKeyword, string senderName) { }
        public CriticalBuildMessageEventArgs(string subcategory, string code, string file, int lineNumber, int columnNumber, int endLineNumber, int endColumnNumber, string message, string helpKeyword, string senderName, System.DateTime eventTimestamp) { }
        public CriticalBuildMessageEventArgs(string subcategory, string code, string file, int lineNumber, int columnNumber, int endLineNumber, int endColumnNumber, string message, string helpKeyword, string senderName, System.DateTime eventTimestamp, params object[] messageArgs) { }
    }
    public abstract partial class CustomBuildEventArgs : Microsoft.Build.Framework.LazyFormattedBuildEventArgs
    {
        protected CustomBuildEventArgs() { }
        protected CustomBuildEventArgs(string message, string helpKeyword, string senderName) { }
        protected CustomBuildEventArgs(string message, string helpKeyword, string senderName, System.DateTime eventTimestamp) { }
        protected CustomBuildEventArgs(string message, string helpKeyword, string senderName, System.DateTime eventTimestamp, params object[] messageArgs) { }
    }
    public delegate void CustomBuildEventHandler(object sender, Microsoft.Build.Framework.CustomBuildEventArgs e);
    public partial class EnvironmentVariableReadEventArgs : Microsoft.Build.Framework.BuildMessageEventArgs
    {
        public EnvironmentVariableReadEventArgs() { }
        public EnvironmentVariableReadEventArgs(string environmentVariableName, string message, string helpKeyword = null, string senderName = null, Microsoft.Build.Framework.MessageImportance importance = Microsoft.Build.Framework.MessageImportance.Low) { }
        public string EnvironmentVariableName { get { throw null; } set { } }
    }
    public partial class ExternalProjectFinishedEventArgs : Microsoft.Build.Framework.CustomBuildEventArgs
    {
        protected ExternalProjectFinishedEventArgs() { }
        public ExternalProjectFinishedEventArgs(string message, string helpKeyword, string senderName, string projectFile, bool succeeded) { }
        public ExternalProjectFinishedEventArgs(string message, string helpKeyword, string senderName, string projectFile, bool succeeded, System.DateTime eventTimestamp) { }
        public string ProjectFile { get { throw null; } }
        public bool Succeeded { get { throw null; } }
    }
    public partial class ExternalProjectStartedEventArgs : Microsoft.Build.Framework.CustomBuildEventArgs
    {
        protected ExternalProjectStartedEventArgs() { }
        public ExternalProjectStartedEventArgs(string message, string helpKeyword, string senderName, string projectFile, string targetNames) { }
        public ExternalProjectStartedEventArgs(string message, string helpKeyword, string senderName, string projectFile, string targetNames, System.DateTime eventTimestamp) { }
        public string ProjectFile { get { throw null; } }
        public string TargetNames { get { throw null; } }
    }
    public partial interface IBuildEngine
    {
        int ColumnNumberOfTaskNode { get; }
        bool ContinueOnError { get; }
        int LineNumberOfTaskNode { get; }
        string ProjectFileOfTaskNode { get; }
        bool BuildProjectFile(string projectFileName, string[] targetNames, System.Collections.IDictionary globalProperties, System.Collections.IDictionary targetOutputs);
        void LogCustomEvent(Microsoft.Build.Framework.CustomBuildEventArgs e);
        void LogErrorEvent(Microsoft.Build.Framework.BuildErrorEventArgs e);
        void LogMessageEvent(Microsoft.Build.Framework.BuildMessageEventArgs e);
        void LogWarningEvent(Microsoft.Build.Framework.BuildWarningEventArgs e);
    }
    public partial interface IBuildEngine2 : Microsoft.Build.Framework.IBuildEngine
    {
        bool IsRunningMultipleNodes { get; }
        bool BuildProjectFile(string projectFileName, string[] targetNames, System.Collections.IDictionary globalProperties, System.Collections.IDictionary targetOutputs, string toolsVersion);
        bool BuildProjectFilesInParallel(string[] projectFileNames, string[] targetNames, System.Collections.IDictionary[] globalProperties, System.Collections.IDictionary[] targetOutputsPerProject, string[] toolsVersion, bool useResultsCache, bool unloadProjectsOnCompletion);
    }
    public partial interface IBuildEngine3 : Microsoft.Build.Framework.IBuildEngine, Microsoft.Build.Framework.IBuildEngine2
    {
        Microsoft.Build.Framework.BuildEngineResult BuildProjectFilesInParallel(string[] projectFileNames, string[] targetNames, System.Collections.IDictionary[] globalProperties, System.Collections.Generic.IList<string>[] removeGlobalProperties, string[] toolsVersion, bool returnTargetOutputs);
        void Reacquire();
        void Yield();
    }
    public partial interface IBuildEngine4 : Microsoft.Build.Framework.IBuildEngine, Microsoft.Build.Framework.IBuildEngine2, Microsoft.Build.Framework.IBuildEngine3
    {
        object GetRegisteredTaskObject(object key, Microsoft.Build.Framework.RegisteredTaskObjectLifetime lifetime);
        void RegisterTaskObject(object key, object obj, Microsoft.Build.Framework.RegisteredTaskObjectLifetime lifetime, bool allowEarlyCollection);
        object UnregisterTaskObject(object key, Microsoft.Build.Framework.RegisteredTaskObjectLifetime lifetime);
    }
    public partial interface IBuildEngine5 : Microsoft.Build.Framework.IBuildEngine, Microsoft.Build.Framework.IBuildEngine2, Microsoft.Build.Framework.IBuildEngine3, Microsoft.Build.Framework.IBuildEngine4
    {
        void LogTelemetry(string eventName, System.Collections.Generic.IDictionary<string, string> properties);
    }
    public partial interface IBuildEngine6 : Microsoft.Build.Framework.IBuildEngine, Microsoft.Build.Framework.IBuildEngine2, Microsoft.Build.Framework.IBuildEngine3, Microsoft.Build.Framework.IBuildEngine4, Microsoft.Build.Framework.IBuildEngine5
    {
        System.Collections.Generic.IReadOnlyDictionary<string, string> GetGlobalProperties();
    }
    public partial interface IBuildEngine7 : Microsoft.Build.Framework.IBuildEngine, Microsoft.Build.Framework.IBuildEngine2, Microsoft.Build.Framework.IBuildEngine3, Microsoft.Build.Framework.IBuildEngine4, Microsoft.Build.Framework.IBuildEngine5, Microsoft.Build.Framework.IBuildEngine6
    {
        bool AllowFailureWithoutError { get; set; }
    }
    public partial interface IBuildEngine8 : Microsoft.Build.Framework.IBuildEngine, Microsoft.Build.Framework.IBuildEngine2, Microsoft.Build.Framework.IBuildEngine3, Microsoft.Build.Framework.IBuildEngine4, Microsoft.Build.Framework.IBuildEngine5, Microsoft.Build.Framework.IBuildEngine6, Microsoft.Build.Framework.IBuildEngine7
    {
        bool ShouldTreatWarningAsError(string warningCode);
    }
    public partial interface IBuildEngine9 : Microsoft.Build.Framework.IBuildEngine, Microsoft.Build.Framework.IBuildEngine2, Microsoft.Build.Framework.IBuildEngine3, Microsoft.Build.Framework.IBuildEngine4, Microsoft.Build.Framework.IBuildEngine5, Microsoft.Build.Framework.IBuildEngine6, Microsoft.Build.Framework.IBuildEngine7, Microsoft.Build.Framework.IBuildEngine8
    {
        void ReleaseCores(int coresToRelease);
        int RequestCores(int requestedCores);
    }
    public partial interface ICancelableTask : Microsoft.Build.Framework.ITask
    {
        void Cancel();
    }
    public partial interface IEventRedirector
    {
        void ForwardEvent(Microsoft.Build.Framework.BuildEventArgs buildEvent);
    }
    public partial interface IEventSource
    {
        event Microsoft.Build.Framework.AnyEventHandler AnyEventRaised;
        event Microsoft.Build.Framework.BuildFinishedEventHandler BuildFinished;
        event Microsoft.Build.Framework.BuildStartedEventHandler BuildStarted;
        event Microsoft.Build.Framework.CustomBuildEventHandler CustomEventRaised;
        event Microsoft.Build.Framework.BuildErrorEventHandler ErrorRaised;
        event Microsoft.Build.Framework.BuildMessageEventHandler MessageRaised;
        event Microsoft.Build.Framework.ProjectFinishedEventHandler ProjectFinished;
        event Microsoft.Build.Framework.ProjectStartedEventHandler ProjectStarted;
        event Microsoft.Build.Framework.BuildStatusEventHandler StatusEventRaised;
        event Microsoft.Build.Framework.TargetFinishedEventHandler TargetFinished;
        event Microsoft.Build.Framework.TargetStartedEventHandler TargetStarted;
        event Microsoft.Build.Framework.TaskFinishedEventHandler TaskFinished;
        event Microsoft.Build.Framework.TaskStartedEventHandler TaskStarted;
        event Microsoft.Build.Framework.BuildWarningEventHandler WarningRaised;
    }
    public partial interface IEventSource2 : Microsoft.Build.Framework.IEventSource
    {
        event Microsoft.Build.Framework.TelemetryEventHandler TelemetryLogged;
    }
    public partial interface IEventSource3 : Microsoft.Build.Framework.IEventSource, Microsoft.Build.Framework.IEventSource2
    {
        void IncludeEvaluationMetaprojects();
        void IncludeEvaluationProfiles();
        void IncludeTaskInputs();
    }
    public partial interface IEventSource4 : Microsoft.Build.Framework.IEventSource, Microsoft.Build.Framework.IEventSource2, Microsoft.Build.Framework.IEventSource3
    {
        void IncludeEvaluationPropertiesAndItems();
    }
    public partial interface IForwardingLogger : Microsoft.Build.Framework.ILogger, Microsoft.Build.Framework.INodeLogger
    {
        Microsoft.Build.Framework.IEventRedirector BuildEventRedirector { get; set; }
        int NodeId { get; set; }
    }
    public partial interface IGeneratedTask : Microsoft.Build.Framework.ITask
    {
        object GetPropertyValue(Microsoft.Build.Framework.TaskPropertyInfo property);
        void SetPropertyValue(Microsoft.Build.Framework.TaskPropertyInfo property, object value);
    }
    [System.Runtime.InteropServices.ComVisibleAttribute(true)]
    public partial interface ILogger
    {
        string Parameters { get; set; }
        Microsoft.Build.Framework.LoggerVerbosity Verbosity { get; set; }
        void Initialize(Microsoft.Build.Framework.IEventSource eventSource);
        void Shutdown();
    }
    [System.Runtime.InteropServices.ComVisibleAttribute(true)]
    public partial interface INodeLogger : Microsoft.Build.Framework.ILogger
    {
        void Initialize(Microsoft.Build.Framework.IEventSource eventSource, int nodeCount);
    }
    public partial interface IProjectElement
    {
        string ElementName { get; }
        string OuterElement { get; }
    }
    public partial interface ITask
    {
        Microsoft.Build.Framework.IBuildEngine BuildEngine { get; set; }
        Microsoft.Build.Framework.ITaskHost HostObject { get; set; }
        bool Execute();
    }
    public partial interface ITaskFactory
    {
        string FactoryName { get; }
        System.Type TaskType { get; }
        void CleanupTask(Microsoft.Build.Framework.ITask task);
        Microsoft.Build.Framework.ITask CreateTask(Microsoft.Build.Framework.IBuildEngine taskFactoryLoggingHost);
        Microsoft.Build.Framework.TaskPropertyInfo[] GetTaskParameters();
        bool Initialize(string taskName, System.Collections.Generic.IDictionary<string, Microsoft.Build.Framework.TaskPropertyInfo> parameterGroup, string taskBody, Microsoft.Build.Framework.IBuildEngine taskFactoryLoggingHost);
    }
    public partial interface ITaskFactory2 : Microsoft.Build.Framework.ITaskFactory
    {
        Microsoft.Build.Framework.ITask CreateTask(Microsoft.Build.Framework.IBuildEngine taskFactoryLoggingHost, System.Collections.Generic.IDictionary<string, string> taskIdentityParameters);
        bool Initialize(string taskName, System.Collections.Generic.IDictionary<string, string> factoryIdentityParameters, System.Collections.Generic.IDictionary<string, Microsoft.Build.Framework.TaskPropertyInfo> parameterGroup, string taskBody, Microsoft.Build.Framework.IBuildEngine taskFactoryLoggingHost);
    }
    [System.Runtime.InteropServices.ComVisibleAttribute(true)]
    [System.Runtime.InteropServices.GuidAttribute("9049A481-D0E9-414f-8F92-D4F67A0359A6")]
    [System.Runtime.InteropServices.InterfaceTypeAttribute(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
    public partial interface ITaskHost
    {
    }
    [System.Runtime.InteropServices.ComVisibleAttribute(true)]
    [System.Runtime.InteropServices.GuidAttribute("8661674F-2148-4F71-A92A-49875511C528")]
    public partial interface ITaskItem
    {
        string ItemSpec { get; set; }
        int MetadataCount { get; }
        System.Collections.ICollection MetadataNames { get; }
        System.Collections.IDictionary CloneCustomMetadata();
        void CopyMetadataTo(Microsoft.Build.Framework.ITaskItem destinationItem);
        string GetMetadata(string metadataName);
        void RemoveMetadata(string metadataName);
        void SetMetadata(string metadataName, string metadataValue);
    }
    [System.Runtime.InteropServices.ComVisibleAttribute(true)]
    [System.Runtime.InteropServices.GuidAttribute("ac6d5a59-f877-461b-88e3-b2f06fce0cb9")]
    public partial interface ITaskItem2 : Microsoft.Build.Framework.ITaskItem
    {
        string EvaluatedIncludeEscaped { get; set; }
        System.Collections.IDictionary CloneCustomMetadataEscaped();
        string GetMetadataValueEscaped(string metadataName);
        void SetMetadataValueLiteral(string metadataName, string metadataValue);
    }
    public partial class LazyFormattedBuildEventArgs : Microsoft.Build.Framework.BuildEventArgs
    {
        [System.NonSerializedAttribute]
        protected object locker;
        protected LazyFormattedBuildEventArgs() { }
        public LazyFormattedBuildEventArgs(string message, string helpKeyword, string senderName) { }
        public LazyFormattedBuildEventArgs(string message, string helpKeyword, string senderName, System.DateTime eventTimestamp, params object[] messageArgs) { }
        public override string Message { get { throw null; } }
    }
    [System.AttributeUsageAttribute(System.AttributeTargets.Class, AllowMultiple=false, Inherited=true)]
    public sealed partial class LoadInSeparateAppDomainAttribute : System.Attribute
    {
        public LoadInSeparateAppDomainAttribute() { }
    }
    public partial class LoggerException : System.Exception
    {
        public LoggerException() { }
        protected LoggerException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) { }
        public LoggerException(string message) { }
        public LoggerException(string message, System.Exception innerException) { }
        public LoggerException(string message, System.Exception innerException, string errorCode, string helpKeyword) { }
        public string ErrorCode { get { throw null; } }
        public string HelpKeyword { get { throw null; } }
        [System.Security.Permissions.SecurityPermissionAttribute(System.Security.Permissions.SecurityAction.Demand, SerializationFormatter=true)]
        public override void GetObjectData(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) { }
    }
    [System.Runtime.InteropServices.ComVisibleAttribute(true)]
    public enum LoggerVerbosity
    {
        Quiet = 0,
        Minimal = 1,
        Normal = 2,
        Detailed = 3,
        Diagnostic = 4,
    }
    public enum MessageImportance
    {
        High = 0,
        Normal = 1,
        Low = 2,
    }
    public partial class MetaprojectGeneratedEventArgs : Microsoft.Build.Framework.BuildMessageEventArgs
    {
        public string metaprojectXml;
        public MetaprojectGeneratedEventArgs(string metaprojectXml, string metaprojectPath, string message) { }
    }
    [System.AttributeUsageAttribute(System.AttributeTargets.Property, AllowMultiple=false, Inherited=false)]
    public sealed partial class OutputAttribute : System.Attribute
    {
        public OutputAttribute() { }
    }
    public sealed partial class ProjectEvaluationFinishedEventArgs : Microsoft.Build.Framework.BuildStatusEventArgs
    {
        public ProjectEvaluationFinishedEventArgs() { }
        public ProjectEvaluationFinishedEventArgs(string message, params object[] messageArgs) { }
        public System.Collections.IEnumerable GlobalProperties { get { throw null; } set { } }
        public System.Collections.IEnumerable Items { get { throw null; } set { } }
        public Microsoft.Build.Framework.Profiler.ProfilerResult? ProfilerResult { get { throw null; } set { } }
        public string ProjectFile { get { throw null; } set { } }
        public System.Collections.IEnumerable Properties { get { throw null; } set { } }
    }
    public partial class ProjectEvaluationStartedEventArgs : Microsoft.Build.Framework.BuildStatusEventArgs
    {
        public ProjectEvaluationStartedEventArgs() { }
        public ProjectEvaluationStartedEventArgs(string message, params object[] messageArgs) { }
        public string ProjectFile { get { throw null; } set { } }
    }
    public partial class ProjectFinishedEventArgs : Microsoft.Build.Framework.BuildStatusEventArgs
    {
        protected ProjectFinishedEventArgs() { }
        public ProjectFinishedEventArgs(string message, string helpKeyword, string projectFile, bool succeeded) { }
        public ProjectFinishedEventArgs(string message, string helpKeyword, string projectFile, bool succeeded, System.DateTime eventTimestamp) { }
        public override string Message { get { throw null; } }
        public string ProjectFile { get { throw null; } }
        public bool Succeeded { get { throw null; } }
    }
    public delegate void ProjectFinishedEventHandler(object sender, Microsoft.Build.Framework.ProjectFinishedEventArgs e);
    public partial class ProjectImportedEventArgs : Microsoft.Build.Framework.BuildMessageEventArgs
    {
        public ProjectImportedEventArgs() { }
        public ProjectImportedEventArgs(int lineNumber, int columnNumber, string message, params object[] messageArgs) { }
        public string ImportedProjectFile { get { throw null; } set { } }
        public bool ImportIgnored { get { throw null; } set { } }
        public string UnexpandedProject { get { throw null; } set { } }
    }
    public partial class ProjectStartedEventArgs : Microsoft.Build.Framework.BuildStatusEventArgs
    {
        public const int InvalidProjectId = -1;
        protected ProjectStartedEventArgs() { }
        public ProjectStartedEventArgs(int projectId, string message, string helpKeyword, string projectFile, string targetNames, System.Collections.IEnumerable properties, System.Collections.IEnumerable items, Microsoft.Build.Framework.BuildEventContext parentBuildEventContext) { }
        public ProjectStartedEventArgs(int projectId, string message, string helpKeyword, string projectFile, string targetNames, System.Collections.IEnumerable properties, System.Collections.IEnumerable items, Microsoft.Build.Framework.BuildEventContext parentBuildEventContext, System.Collections.Generic.IDictionary<string, string> globalProperties, string toolsVersion) { }
        public ProjectStartedEventArgs(int projectId, string message, string helpKeyword, string projectFile, string targetNames, System.Collections.IEnumerable properties, System.Collections.IEnumerable items, Microsoft.Build.Framework.BuildEventContext parentBuildEventContext, System.DateTime eventTimestamp) { }
        public ProjectStartedEventArgs(string message, string helpKeyword, string projectFile, string targetNames, System.Collections.IEnumerable properties, System.Collections.IEnumerable items) { }
        public ProjectStartedEventArgs(string message, string helpKeyword, string projectFile, string targetNames, System.Collections.IEnumerable properties, System.Collections.IEnumerable items, System.DateTime eventTimestamp) { }
        public System.Collections.Generic.IDictionary<string, string> GlobalProperties { get { throw null; } }
        public System.Collections.IEnumerable Items { get { throw null; } }
        public override string Message { get { throw null; } }
        public Microsoft.Build.Framework.BuildEventContext ParentProjectBuildEventContext { get { throw null; } }
        public string ProjectFile { get { throw null; } }
        public int ProjectId { get { throw null; } }
        public System.Collections.IEnumerable Properties { get { throw null; } }
        public string TargetNames { get { throw null; } }
        public string ToolsVersion { get { throw null; } }
    }
    public delegate void ProjectStartedEventHandler(object sender, Microsoft.Build.Framework.ProjectStartedEventArgs e);
    public partial class PropertyInitialValueSetEventArgs : Microsoft.Build.Framework.BuildMessageEventArgs
    {
        public PropertyInitialValueSetEventArgs() { }
        public PropertyInitialValueSetEventArgs(string propertyName, string propertyValue, string propertySource, string message, string helpKeyword = null, string senderName = null, Microsoft.Build.Framework.MessageImportance importance = Microsoft.Build.Framework.MessageImportance.Low) { }
        public string PropertyName { get { throw null; } set { } }
        public string PropertySource { get { throw null; } set { } }
        public string PropertyValue { get { throw null; } set { } }
    }
    public partial class PropertyReassignmentEventArgs : Microsoft.Build.Framework.BuildMessageEventArgs
    {
        public PropertyReassignmentEventArgs() { }
        public PropertyReassignmentEventArgs(string propertyName, string previousValue, string newValue, string location, string message, string helpKeyword = null, string senderName = null, Microsoft.Build.Framework.MessageImportance importance = Microsoft.Build.Framework.MessageImportance.Low) { }
        public string Location { get { throw null; } set { } }
        public override string Message { get { throw null; } }
        public string NewValue { get { throw null; } set { } }
        public string PreviousValue { get { throw null; } set { } }
        public string PropertyName { get { throw null; } set { } }
    }
    public enum RegisteredTaskObjectLifetime
    {
        Build = 0,
        AppDomain = 1,
    }
    [System.AttributeUsageAttribute(System.AttributeTargets.Property, AllowMultiple=false, Inherited=false)]
    public sealed partial class RequiredAttribute : System.Attribute
    {
        public RequiredAttribute() { }
    }
    [System.AttributeUsageAttribute(System.AttributeTargets.Class, AllowMultiple=false, Inherited=false)]
    public sealed partial class RequiredRuntimeAttribute : System.Attribute
    {
        public RequiredRuntimeAttribute(string runtimeVersion) { }
        public string RuntimeVersion { get { throw null; } }
    }
    [System.AttributeUsageAttribute(System.AttributeTargets.Class, AllowMultiple=false, Inherited=false)]
    public sealed partial class RunInMTAAttribute : System.Attribute
    {
        public RunInMTAAttribute() { }
    }
    [System.AttributeUsageAttribute(System.AttributeTargets.Class, AllowMultiple=false, Inherited=false)]
    public sealed partial class RunInSTAAttribute : System.Attribute
    {
        public RunInSTAAttribute() { }
    }
    public abstract partial class SdkLogger
    {
        protected SdkLogger() { }
        public abstract void LogMessage(string message, Microsoft.Build.Framework.MessageImportance messageImportance = Microsoft.Build.Framework.MessageImportance.Low);
    }
    public sealed partial class SdkReference : System.IEquatable<Microsoft.Build.Framework.SdkReference>
    {
        public SdkReference(string name, string version, string minimumVersion) { }
        public string MinimumVersion { get { throw null; } }
        public string Name { get { throw null; } }
        public string Version { get { throw null; } }
        public bool Equals(Microsoft.Build.Framework.SdkReference other) { throw null; }
        public override bool Equals(object obj) { throw null; }
        public override int GetHashCode() { throw null; }
        public override string ToString() { throw null; }
        public static bool TryParse(string sdk, out Microsoft.Build.Framework.SdkReference sdkReference) { throw null; }
    }
    public abstract partial class SdkResolver
    {
        protected SdkResolver() { }
        public abstract string Name { get; }
        public abstract int Priority { get; }
        public abstract Microsoft.Build.Framework.SdkResult Resolve(Microsoft.Build.Framework.SdkReference sdkReference, Microsoft.Build.Framework.SdkResolverContext resolverContext, Microsoft.Build.Framework.SdkResultFactory factory);
    }
    public abstract partial class SdkResolverContext
    {
        protected SdkResolverContext() { }
        public virtual bool Interactive { get { throw null; } protected set { } }
        public virtual bool IsRunningInVisualStudio { get { throw null; } protected set { } }
        public virtual Microsoft.Build.Framework.SdkLogger Logger { get { throw null; } protected set { } }
        public virtual System.Version MSBuildVersion { get { throw null; } protected set { } }
        public virtual string ProjectFilePath { get { throw null; } protected set { } }
        public virtual string SolutionFilePath { get { throw null; } protected set { } }
        public virtual object State { get { throw null; } set { } }
    }
    public abstract partial class SdkResult
    {
        protected SdkResult() { }
        public virtual System.Collections.Generic.IList<string> AdditionalPaths { get { throw null; } set { } }
        public virtual System.Collections.Generic.IDictionary<string, Microsoft.Build.Framework.SdkResultItem> ItemsToAdd { get { throw null; } protected set { } }
        public virtual string Path { get { throw null; } protected set { } }
        public virtual System.Collections.Generic.IDictionary<string, string> PropertiesToAdd { get { throw null; } protected set { } }
        public virtual Microsoft.Build.Framework.SdkReference SdkReference { get { throw null; } protected set { } }
        public virtual bool Success { get { throw null; } protected set { } }
        public virtual string Version { get { throw null; } protected set { } }
    }
    public abstract partial class SdkResultFactory
    {
        protected SdkResultFactory() { }
        public abstract Microsoft.Build.Framework.SdkResult IndicateFailure(System.Collections.Generic.IEnumerable<string> errors, System.Collections.Generic.IEnumerable<string> warnings = null);
        public virtual Microsoft.Build.Framework.SdkResult IndicateSuccess(System.Collections.Generic.IEnumerable<string> paths, string version, System.Collections.Generic.IDictionary<string, string> propertiesToAdd = null, System.Collections.Generic.IDictionary<string, Microsoft.Build.Framework.SdkResultItem> itemsToAdd = null, System.Collections.Generic.IEnumerable<string> warnings = null) { throw null; }
        public virtual Microsoft.Build.Framework.SdkResult IndicateSuccess(string path, string version, System.Collections.Generic.IDictionary<string, string> propertiesToAdd, System.Collections.Generic.IDictionary<string, Microsoft.Build.Framework.SdkResultItem> itemsToAdd, System.Collections.Generic.IEnumerable<string> warnings = null) { throw null; }
        public abstract Microsoft.Build.Framework.SdkResult IndicateSuccess(string path, string version, System.Collections.Generic.IEnumerable<string> warnings = null);
    }
    public partial class SdkResultItem
    {
        public SdkResultItem() { }
        public SdkResultItem(string itemSpec, System.Collections.Generic.Dictionary<string, string> metadata) { }
        public string ItemSpec { get { throw null; } set { } }
        public System.Collections.Generic.Dictionary<string, string> Metadata { get { throw null; } }
        public override bool Equals(object obj) { throw null; }
        public override int GetHashCode() { throw null; }
    }
    public enum TargetBuiltReason
    {
        None = 0,
        BeforeTargets = 1,
        DependsOn = 2,
        AfterTargets = 3,
    }
    public partial class TargetFinishedEventArgs : Microsoft.Build.Framework.BuildStatusEventArgs
    {
        protected TargetFinishedEventArgs() { }
        public TargetFinishedEventArgs(string message, string helpKeyword, string targetName, string projectFile, string targetFile, bool succeeded) { }
        public TargetFinishedEventArgs(string message, string helpKeyword, string targetName, string projectFile, string targetFile, bool succeeded, System.Collections.IEnumerable targetOutputs) { }
        public TargetFinishedEventArgs(string message, string helpKeyword, string targetName, string projectFile, string targetFile, bool succeeded, System.DateTime eventTimestamp, System.Collections.IEnumerable targetOutputs) { }
        public override string Message { get { throw null; } }
        public string ProjectFile { get { throw null; } }
        public bool Succeeded { get { throw null; } }
        public string TargetFile { get { throw null; } }
        public string TargetName { get { throw null; } }
        public System.Collections.IEnumerable TargetOutputs { get { throw null; } set { } }
    }
    public delegate void TargetFinishedEventHandler(object sender, Microsoft.Build.Framework.TargetFinishedEventArgs e);
    public partial class TargetSkippedEventArgs : Microsoft.Build.Framework.BuildMessageEventArgs
    {
        public TargetSkippedEventArgs() { }
        public TargetSkippedEventArgs(string message, params object[] messageArgs) { }
        public Microsoft.Build.Framework.TargetBuiltReason BuildReason { get { throw null; } set { } }
        public string Condition { get { throw null; } set { } }
        public string EvaluatedCondition { get { throw null; } set { } }
        public override string Message { get { throw null; } }
        public Microsoft.Build.Framework.BuildEventContext OriginalBuildEventContext { get { throw null; } set { } }
        public bool OriginallySucceeded { get { throw null; } set { } }
        public string ParentTarget { get { throw null; } set { } }
        public Microsoft.Build.Framework.TargetSkipReason SkipReason { get { throw null; } set { } }
        public string TargetFile { get { throw null; } set { } }
        public string TargetName { get { throw null; } set { } }
    }
    public enum TargetSkipReason
    {
        None = 0,
        PreviouslyBuiltSuccessfully = 1,
        PreviouslyBuiltUnsuccessfully = 2,
        OutputsUpToDate = 3,
        ConditionWasFalse = 4,
    }
    public partial class TargetStartedEventArgs : Microsoft.Build.Framework.BuildStatusEventArgs
    {
        protected TargetStartedEventArgs() { }
        public TargetStartedEventArgs(string message, string helpKeyword, string targetName, string projectFile, string targetFile) { }
        public TargetStartedEventArgs(string message, string helpKeyword, string targetName, string projectFile, string targetFile, string parentTarget, Microsoft.Build.Framework.TargetBuiltReason buildReason, System.DateTime eventTimestamp) { }
        public TargetStartedEventArgs(string message, string helpKeyword, string targetName, string projectFile, string targetFile, string parentTarget, System.DateTime eventTimestamp) { }
        public Microsoft.Build.Framework.TargetBuiltReason BuildReason { get { throw null; } }
        public override string Message { get { throw null; } }
        public string ParentTarget { get { throw null; } }
        public string ProjectFile { get { throw null; } }
        public string TargetFile { get { throw null; } }
        public string TargetName { get { throw null; } }
    }
    public delegate void TargetStartedEventHandler(object sender, Microsoft.Build.Framework.TargetStartedEventArgs e);
    public partial class TaskCommandLineEventArgs : Microsoft.Build.Framework.BuildMessageEventArgs
    {
        protected TaskCommandLineEventArgs() { }
        public TaskCommandLineEventArgs(string commandLine, string taskName, Microsoft.Build.Framework.MessageImportance importance) { }
        public TaskCommandLineEventArgs(string commandLine, string taskName, Microsoft.Build.Framework.MessageImportance importance, System.DateTime eventTimestamp) { }
        public string CommandLine { get { throw null; } }
        public string TaskName { get { throw null; } }
    }
    public partial class TaskFinishedEventArgs : Microsoft.Build.Framework.BuildStatusEventArgs
    {
        protected TaskFinishedEventArgs() { }
        public TaskFinishedEventArgs(string message, string helpKeyword, string projectFile, string taskFile, string taskName, bool succeeded) { }
        public TaskFinishedEventArgs(string message, string helpKeyword, string projectFile, string taskFile, string taskName, bool succeeded, System.DateTime eventTimestamp) { }
        public override string Message { get { throw null; } }
        public string ProjectFile { get { throw null; } }
        public bool Succeeded { get { throw null; } }
        public string TaskFile { get { throw null; } }
        public string TaskName { get { throw null; } }
    }
    public delegate void TaskFinishedEventHandler(object sender, Microsoft.Build.Framework.TaskFinishedEventArgs e);
    public partial class TaskParameterEventArgs : Microsoft.Build.Framework.BuildMessageEventArgs
    {
        public TaskParameterEventArgs(Microsoft.Build.Framework.TaskParameterMessageKind kind, string itemType, System.Collections.IList items, bool logItemMetadata, System.DateTime eventTimestamp) { }
        public System.Collections.IList Items { get { throw null; } }
        public string ItemType { get { throw null; } }
        public Microsoft.Build.Framework.TaskParameterMessageKind Kind { get { throw null; } }
        public bool LogItemMetadata { get { throw null; } }
        public override string Message { get { throw null; } }
    }
    public enum TaskParameterMessageKind
    {
        TaskInput = 0,
        TaskOutput = 1,
        AddItem = 2,
        RemoveItem = 3,
        SkippedTargetInputs = 4,
        SkippedTargetOutputs = 5,
    }
    public partial class TaskPropertyInfo
    {
        public TaskPropertyInfo(string name, System.Type typeOfParameter, bool output, bool required) { }
        public bool Log { get { throw null; } set { } }
        public bool LogItemMetadata { get { throw null; } set { } }
        public string Name { get { throw null; } }
        public bool Output { get { throw null; } }
        public System.Type PropertyType { get { throw null; } }
        public bool Required { get { throw null; } }
    }
    public partial class TaskStartedEventArgs : Microsoft.Build.Framework.BuildStatusEventArgs
    {
        protected TaskStartedEventArgs() { }
        public TaskStartedEventArgs(string message, string helpKeyword, string projectFile, string taskFile, string taskName) { }
        public TaskStartedEventArgs(string message, string helpKeyword, string projectFile, string taskFile, string taskName, System.DateTime eventTimestamp) { }
        public int ColumnNumber { get { throw null; } }
        public int LineNumber { get { throw null; } }
        public override string Message { get { throw null; } }
        public string ProjectFile { get { throw null; } }
        public string TaskFile { get { throw null; } }
        public string TaskName { get { throw null; } }
    }
    public delegate void TaskStartedEventHandler(object sender, Microsoft.Build.Framework.TaskStartedEventArgs e);
    public sealed partial class TelemetryEventArgs : Microsoft.Build.Framework.BuildEventArgs
    {
        public TelemetryEventArgs() { }
        public string EventName { get { throw null; } set { } }
        public System.Collections.Generic.IDictionary<string, string> Properties { get { throw null; } set { } }
    }
    public delegate void TelemetryEventHandler(object sender, Microsoft.Build.Framework.TelemetryEventArgs e);
    public partial class UninitializedPropertyReadEventArgs : Microsoft.Build.Framework.BuildMessageEventArgs
    {
        public UninitializedPropertyReadEventArgs() { }
        public UninitializedPropertyReadEventArgs(string propertyName, string message, string helpKeyword = null, string senderName = null, Microsoft.Build.Framework.MessageImportance importance = Microsoft.Build.Framework.MessageImportance.Low) { }
        public string PropertyName { get { throw null; } set { } }
    }
}
namespace Microsoft.Build.Framework.Profiler
{
    [System.Runtime.InteropServices.StructLayoutAttribute(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public partial struct EvaluationLocation
    {
        private object _dummy;
        private int _dummyPrimitive;
        public EvaluationLocation(Microsoft.Build.Framework.Profiler.EvaluationPass evaluationPass, string evaluationPassDescription, string file, int? line, string elementName, string elementDescription, Microsoft.Build.Framework.Profiler.EvaluationLocationKind kind) { throw null; }
        public EvaluationLocation(long id, long? parentId, Microsoft.Build.Framework.Profiler.EvaluationPass evaluationPass, string evaluationPassDescription, string file, int? line, string elementName, string elementDescription, Microsoft.Build.Framework.Profiler.EvaluationLocationKind kind) { throw null; }
        public EvaluationLocation(long? parentId, Microsoft.Build.Framework.Profiler.EvaluationPass evaluationPass, string evaluationPassDescription, string file, int? line, string elementName, string elementDescription, Microsoft.Build.Framework.Profiler.EvaluationLocationKind kind) { throw null; }
        public string ElementDescription { get { throw null; } }
        public string ElementName { get { throw null; } }
        public static Microsoft.Build.Framework.Profiler.EvaluationLocation EmptyLocation { get { throw null; } }
        public Microsoft.Build.Framework.Profiler.EvaluationPass EvaluationPass { get { throw null; } }
        public string EvaluationPassDescription { get { throw null; } }
        public string File { get { throw null; } }
        public long Id { get { throw null; } }
        public bool IsEvaluationPass { get { throw null; } }
        public Microsoft.Build.Framework.Profiler.EvaluationLocationKind Kind { get { throw null; } }
        public int? Line { get { throw null; } }
        public long? ParentId { get { throw null; } }
        public static Microsoft.Build.Framework.Profiler.EvaluationLocation CreateLocationForAggregatedGlob() { throw null; }
        public static Microsoft.Build.Framework.Profiler.EvaluationLocation CreateLocationForCondition(long? parentId, Microsoft.Build.Framework.Profiler.EvaluationPass evaluationPass, string evaluationDescription, string file, int? line, string condition) { throw null; }
        public static Microsoft.Build.Framework.Profiler.EvaluationLocation CreateLocationForGlob(long? parentId, Microsoft.Build.Framework.Profiler.EvaluationPass evaluationPass, string evaluationDescription, string file, int? line, string globDescription) { throw null; }
        public static Microsoft.Build.Framework.Profiler.EvaluationLocation CreateLocationForProject(long? parentId, Microsoft.Build.Framework.Profiler.EvaluationPass evaluationPass, string evaluationDescription, string file, int? line, Microsoft.Build.Framework.IProjectElement element) { throw null; }
        public override bool Equals(object obj) { throw null; }
        public override int GetHashCode() { throw null; }
        public override string ToString() { throw null; }
        public Microsoft.Build.Framework.Profiler.EvaluationLocation WithEvaluationPass(Microsoft.Build.Framework.Profiler.EvaluationPass evaluationPass, string passDescription = null) { throw null; }
        public Microsoft.Build.Framework.Profiler.EvaluationLocation WithFile(string file) { throw null; }
        public Microsoft.Build.Framework.Profiler.EvaluationLocation WithFileLineAndCondition(string file, int? line, string condition) { throw null; }
        public Microsoft.Build.Framework.Profiler.EvaluationLocation WithFileLineAndElement(string file, int? line, Microsoft.Build.Framework.IProjectElement element) { throw null; }
        public Microsoft.Build.Framework.Profiler.EvaluationLocation WithGlob(string globDescription) { throw null; }
        public Microsoft.Build.Framework.Profiler.EvaluationLocation WithParentId(long? parentId) { throw null; }
    }
    public enum EvaluationLocationKind : byte
    {
        Element = (byte)0,
        Condition = (byte)1,
        Glob = (byte)2,
    }
    public enum EvaluationPass : byte
    {
        TotalEvaluation = (byte)0,
        TotalGlobbing = (byte)1,
        InitialProperties = (byte)2,
        Properties = (byte)3,
        ItemDefinitionGroups = (byte)4,
        Items = (byte)5,
        LazyItems = (byte)6,
        UsingTasks = (byte)7,
        Targets = (byte)8,
    }
    [System.Runtime.InteropServices.StructLayoutAttribute(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public partial struct ProfiledLocation
    {
        private int _dummyPrimitive;
        public ProfiledLocation(System.TimeSpan inclusiveTime, System.TimeSpan exclusiveTime, int numberOfHits) { throw null; }
        public System.TimeSpan ExclusiveTime { get { throw null; } }
        public System.TimeSpan InclusiveTime { get { throw null; } }
        public int NumberOfHits { get { throw null; } }
        public override bool Equals(object obj) { throw null; }
        public override int GetHashCode() { throw null; }
        public override string ToString() { throw null; }
    }
    [System.Runtime.InteropServices.StructLayoutAttribute(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public partial struct ProfilerResult
    {
        private object _dummy;
        public ProfilerResult(System.Collections.Generic.IDictionary<Microsoft.Build.Framework.Profiler.EvaluationLocation, Microsoft.Build.Framework.Profiler.ProfiledLocation> profiledLocations) { throw null; }
        public System.Collections.Generic.IReadOnlyDictionary<Microsoft.Build.Framework.Profiler.EvaluationLocation, Microsoft.Build.Framework.Profiler.ProfiledLocation> ProfiledLocations { get { throw null; } }
        public override bool Equals(object obj) { throw null; }
        public override int GetHashCode() { throw null; }
    }
}
namespace Microsoft.Build.Framework.XamlTypes
{
    public sealed partial class Argument : System.ComponentModel.ISupportInitialize
    {
        public Argument() { }
        public bool IsRequired { get { throw null; } set { } }
        public string Property { get { throw null; } set { } }
        public string Separator { get { throw null; } set { } }
        public void BeginInit() { }
        public void EndInit() { }
    }
    [System.Windows.Markup.ContentPropertyAttribute("Arguments")]
    public abstract partial class BaseProperty : System.ComponentModel.ISupportInitialize
    {
        protected BaseProperty() { }
        public System.Collections.Generic.List<Microsoft.Build.Framework.XamlTypes.Argument> Arguments { get { throw null; } set { } }
        public string Category { get { throw null; } set { } }
        public Microsoft.Build.Framework.XamlTypes.Rule ContainingRule { get { throw null; } }
        public Microsoft.Build.Framework.XamlTypes.DataSource DataSource { get { throw null; } set { } }
        [System.ComponentModel.LocalizableAttribute(true)]
        public string Default { get { throw null; } set { } }
        [System.ComponentModel.LocalizableAttribute(true)]
        public string Description { get { throw null; } set { } }
        [System.ComponentModel.LocalizableAttribute(true)]
        public string DisplayName { get { throw null; } set { } }
        [System.ComponentModel.LocalizableAttribute(false)]
        public string F1Keyword { get { throw null; } set { } }
        public int HelpContext { get { throw null; } set { } }
        [System.ComponentModel.LocalizableAttribute(false)]
        public string HelpFile { get { throw null; } set { } }
        [System.ComponentModel.LocalizableAttribute(false)]
        public string HelpUrl { get { throw null; } set { } }
        public bool IncludeInCommandLine { get { throw null; } set { } }
        public bool IsRequired { get { throw null; } set { } }
        public System.Collections.Generic.List<Microsoft.Build.Framework.XamlTypes.NameValuePair> Metadata { get { throw null; } set { } }
        public bool MultipleValuesAllowed { get { throw null; } set { } }
        public string Name { get { throw null; } set { } }
        public bool ReadOnly { get { throw null; } set { } }
        public string Separator { get { throw null; } set { } }
        public string Subcategory { get { throw null; } set { } }
        public string Switch { get { throw null; } set { } }
        public string SwitchPrefix { get { throw null; } set { } }
        public System.Collections.Generic.List<Microsoft.Build.Framework.XamlTypes.ValueEditor> ValueEditors { get { throw null; } set { } }
        public bool Visible { get { throw null; } set { } }
        public virtual void BeginInit() { }
        public virtual void EndInit() { }
    }
    public sealed partial class BoolProperty : Microsoft.Build.Framework.XamlTypes.BaseProperty
    {
        public BoolProperty() { }
        public string ReverseSwitch { get { throw null; } set { } }
    }
    public sealed partial class Category : Microsoft.Build.Framework.XamlTypes.CategorySchema, System.ComponentModel.ISupportInitialize
    {
        public Category() { }
        [System.ComponentModel.LocalizableAttribute(true)]
        public string Description { get { throw null; } set { } }
        [System.ComponentModel.LocalizableAttribute(true)]
        public string DisplayName { get { throw null; } set { } }
        [System.ComponentModel.LocalizableAttribute(true)]
        public string HelpString { get { throw null; } set { } }
        public string Name { get { throw null; } set { } }
        public string Subtype { get { throw null; } set { } }
        public void BeginInit() { }
        public void EndInit() { }
    }
    public abstract partial class CategorySchema
    {
        protected CategorySchema() { }
    }
    [System.Windows.Markup.ContentPropertyAttribute("Metadata")]
    public sealed partial class ContentType : Microsoft.Build.Framework.XamlTypes.IProjectSchemaNode, System.ComponentModel.ISupportInitialize
    {
        public ContentType() { }
        public bool DefaultContentTypeForItemType { get { throw null; } set { } }
        [System.ComponentModel.LocalizableAttribute(true)]
        public string DisplayName { get { throw null; } set { } }
        [System.ObsoleteAttribute("Unused.  Use ItemType property instead.", true)]
        public string ItemGroupName { get { throw null; } set { } }
        public string ItemType { get { throw null; } set { } }
        public System.Collections.Generic.List<Microsoft.Build.Framework.XamlTypes.NameValuePair> Metadata { get { throw null; } set { } }
        public string Name { get { throw null; } set { } }
        public void BeginInit() { }
        public void EndInit() { }
        public string GetMetadata(string metadataName) { throw null; }
        public System.Collections.Generic.IEnumerable<object> GetSchemaObjects(System.Type type) { throw null; }
        public System.Collections.Generic.IEnumerable<System.Type> GetSchemaObjectTypes() { throw null; }
    }
    public sealed partial class DataSource : System.ComponentModel.ISupportInitialize
    {
        public DataSource() { }
        public bool HasConfigurationCondition { get { throw null; } set { } }
        public string ItemType { get { throw null; } set { } }
        public string Label { get { throw null; } set { } }
        public string MSBuildTarget { get { throw null; } set { } }
        public string PersistedName { get { throw null; } set { } }
        public string Persistence { get { throw null; } set { } }
        public string PersistenceStyle { get { throw null; } set { } }
        public Microsoft.Build.Framework.XamlTypes.DefaultValueSourceLocation SourceOfDefaultValue { get { throw null; } set { } }
        public string SourceType { get { throw null; } set { } }
        public void BeginInit() { }
        public void EndInit() { }
    }
    public enum DefaultValueSourceLocation
    {
        BeforeContext = 0,
        AfterContext = 1,
    }
    public sealed partial class DynamicEnumProperty : Microsoft.Build.Framework.XamlTypes.BaseProperty
    {
        public DynamicEnumProperty() { }
        public string EnumProvider { get { throw null; } set { } }
        public System.Collections.Generic.List<Microsoft.Build.Framework.XamlTypes.NameValuePair> ProviderSettings { get { throw null; } set { } }
    }
    [System.Windows.Markup.ContentPropertyAttribute("AdmissibleValues")]
    public sealed partial class EnumProperty : Microsoft.Build.Framework.XamlTypes.BaseProperty
    {
        public EnumProperty() { }
        public System.Collections.Generic.List<Microsoft.Build.Framework.XamlTypes.EnumValue> AdmissibleValues { get { throw null; } set { } }
        public override void EndInit() { }
    }
    [System.Windows.Markup.ContentPropertyAttribute("Arguments")]
    public sealed partial class EnumValue
    {
        public EnumValue() { }
        public System.Collections.Generic.List<Microsoft.Build.Framework.XamlTypes.Argument> Arguments { get { throw null; } set { } }
        [System.ComponentModel.LocalizableAttribute(true)]
        public string Description { get { throw null; } set { } }
        [System.ComponentModel.LocalizableAttribute(true)]
        public string DisplayName { get { throw null; } set { } }
        [System.ComponentModel.LocalizableAttribute(true)]
        public string HelpString { get { throw null; } set { } }
        public bool IsDefault { get { throw null; } set { } }
        public System.Collections.Generic.List<Microsoft.Build.Framework.XamlTypes.NameValuePair> Metadata { get { throw null; } set { } }
        public string Name { get { throw null; } set { } }
        public string Switch { get { throw null; } set { } }
        public string SwitchPrefix { get { throw null; } set { } }
    }
    public sealed partial class FileExtension : Microsoft.Build.Framework.XamlTypes.IProjectSchemaNode
    {
        public FileExtension() { }
        public string ContentType { get { throw null; } set { } }
        public string Name { get { throw null; } set { } }
        public System.Collections.Generic.IEnumerable<object> GetSchemaObjects(System.Type type) { throw null; }
        public System.Collections.Generic.IEnumerable<System.Type> GetSchemaObjectTypes() { throw null; }
    }
    public sealed partial class IntProperty : Microsoft.Build.Framework.XamlTypes.BaseProperty
    {
        public IntProperty() { }
        public int? MaxValue { get { throw null; } set { } }
        public int? MinValue { get { throw null; } set { } }
        public override void EndInit() { }
    }
    public partial interface IProjectSchemaNode
    {
        System.Collections.Generic.IEnumerable<object> GetSchemaObjects(System.Type type);
        System.Collections.Generic.IEnumerable<System.Type> GetSchemaObjectTypes();
    }
    public sealed partial class ItemType : Microsoft.Build.Framework.XamlTypes.IProjectSchemaNode, System.ComponentModel.ISupportInitialize
    {
        public ItemType() { }
        public string DefaultContentType { get { throw null; } set { } }
        [System.ComponentModel.LocalizableAttribute(true)]
        public string DisplayName { get { throw null; } set { } }
        public string Name { get { throw null; } set { } }
        public bool UpToDateCheckInput { get { throw null; } set { } }
        public void BeginInit() { }
        public void EndInit() { }
        public System.Collections.Generic.IEnumerable<object> GetSchemaObjects(System.Type type) { throw null; }
        public System.Collections.Generic.IEnumerable<System.Type> GetSchemaObjectTypes() { throw null; }
    }
    public partial class NameValuePair
    {
        public NameValuePair() { }
        public string Name { get { throw null; } set { } }
        [System.ComponentModel.LocalizableAttribute(true)]
        public string Value { get { throw null; } set { } }
    }
    [System.Windows.Markup.ContentPropertyAttribute("Nodes")]
    public sealed partial class ProjectSchemaDefinitions : Microsoft.Build.Framework.XamlTypes.IProjectSchemaNode
    {
        public ProjectSchemaDefinitions() { }
        public System.Collections.Generic.List<Microsoft.Build.Framework.XamlTypes.IProjectSchemaNode> Nodes { get { throw null; } set { } }
        public System.Collections.Generic.IEnumerable<object> GetSchemaObjects(System.Type type) { throw null; }
        public System.Collections.Generic.IEnumerable<System.Type> GetSchemaObjectTypes() { throw null; }
    }
    [System.Windows.Markup.ContentPropertyAttribute("Properties")]
    public sealed partial class Rule : Microsoft.Build.Framework.XamlTypes.RuleSchema, Microsoft.Build.Framework.XamlTypes.IProjectSchemaNode, System.ComponentModel.ISupportInitialize
    {
        public Rule() { }
        public string AdditionalInputs { get { throw null; } set { } }
        public System.Collections.Generic.List<Microsoft.Build.Framework.XamlTypes.Category> Categories { get { throw null; } set { } }
        public string CommandLine { get { throw null; } set { } }
        public Microsoft.Build.Framework.XamlTypes.DataSource DataSource { get { throw null; } set { } }
        [System.ComponentModel.LocalizableAttribute(true)]
        public string Description { get { throw null; } set { } }
        [System.ComponentModel.LocalizableAttribute(true)]
        public string DisplayName { get { throw null; } set { } }
        public System.Collections.Generic.List<Microsoft.Build.Framework.XamlTypes.Category> EvaluatedCategories { get { throw null; } }
        public string ExecutionDescription { get { throw null; } set { } }
        public string FileExtension { get { throw null; } set { } }
        [System.ComponentModel.LocalizableAttribute(true)]
        public string HelpString { get { throw null; } set { } }
        public System.Collections.Generic.Dictionary<string, object> Metadata { get { throw null; } set { } }
        public string Name { get { throw null; } set { } }
        public int Order { get { throw null; } set { } }
        public string Outputs { get { throw null; } set { } }
        public Microsoft.Build.Framework.XamlTypes.RuleOverrideMode OverrideMode { get { throw null; } set { } }
        public string PageTemplate { get { throw null; } set { } }
        public System.Collections.Generic.List<Microsoft.Build.Framework.XamlTypes.BaseProperty> Properties { get { throw null; } set { } }
        public bool PropertyPagesHidden { get { throw null; } set { } }
        public string Separator { get { throw null; } set { } }
        public bool ShowOnlyRuleProperties { get { throw null; } set { } }
        public bool SupportsFileBatching { get { throw null; } set { } }
        public string SwitchPrefix { get { throw null; } set { } }
        public string ToolName { get { throw null; } set { } }
        public void BeginInit() { }
        public void EndInit() { }
        public System.Collections.Specialized.OrderedDictionary GetPropertiesByCategory() { throw null; }
        public System.Collections.Generic.IList<Microsoft.Build.Framework.XamlTypes.BaseProperty> GetPropertiesInCategory(string categoryName) { throw null; }
        public Microsoft.Build.Framework.XamlTypes.BaseProperty GetProperty(string propertyName) { throw null; }
        public System.Collections.Generic.IEnumerable<object> GetSchemaObjects(System.Type type) { throw null; }
        public System.Collections.Generic.IEnumerable<System.Type> GetSchemaObjectTypes() { throw null; }
    }
    [System.Windows.Markup.ContentPropertyAttribute("Rules")]
    public sealed partial class RuleBag : Microsoft.Build.Framework.XamlTypes.IProjectSchemaNode, System.ComponentModel.ISupportInitialize
    {
        public RuleBag() { }
        public System.Collections.Generic.List<Microsoft.Build.Framework.XamlTypes.Rule> Rules { get { throw null; } set { } }
        public void BeginInit() { }
        public void EndInit() { }
        public System.Collections.Generic.IEnumerable<object> GetSchemaObjects(System.Type type) { throw null; }
        public System.Collections.Generic.IEnumerable<System.Type> GetSchemaObjectTypes() { throw null; }
    }
    public enum RuleOverrideMode
    {
        Replace = 0,
        Extend = 1,
    }
    public abstract partial class RuleSchema
    {
        protected RuleSchema() { }
    }
    public sealed partial class StringListProperty : Microsoft.Build.Framework.XamlTypes.BaseProperty
    {
        public StringListProperty() { }
        public string CommandLineValueSeparator { get { throw null; } set { } }
        public string RendererValueSeparator { get { throw null; } set { } }
        public string Subtype { get { throw null; } set { } }
    }
    public sealed partial class StringProperty : Microsoft.Build.Framework.XamlTypes.BaseProperty
    {
        public StringProperty() { }
        public string Subtype { get { throw null; } set { } }
    }
    [System.Windows.Markup.ContentPropertyAttribute("Metadata")]
    public sealed partial class ValueEditor : System.ComponentModel.ISupportInitialize
    {
        public ValueEditor() { }
        [System.ComponentModel.LocalizableAttribute(true)]
        public string DisplayName { get { throw null; } set { } }
        public string EditorType { get { throw null; } set { } }
        public System.Collections.Generic.List<Microsoft.Build.Framework.XamlTypes.NameValuePair> Metadata { get { throw null; } set { } }
        public void BeginInit() { }
        public void EndInit() { }
    }
}
