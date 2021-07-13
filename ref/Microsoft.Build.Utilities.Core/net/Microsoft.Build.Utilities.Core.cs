// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
namespace Microsoft.Build.Utilities
{
    [Microsoft.Build.Framework.LoadInSeparateAppDomainAttribute]
    public abstract partial class AppDomainIsolatedTask : System.MarshalByRefObject, Microsoft.Build.Framework.ITask
    {
        protected AppDomainIsolatedTask() { }
        protected AppDomainIsolatedTask(System.Resources.ResourceManager taskResources) { }
        protected AppDomainIsolatedTask(System.Resources.ResourceManager taskResources, string helpKeywordPrefix) { }
        public Microsoft.Build.Framework.IBuildEngine BuildEngine { get { throw null; } set { } }
        protected string HelpKeywordPrefix { get { throw null; } set { } }
        public Microsoft.Build.Framework.ITaskHost HostObject { get { throw null; } set { } }
        public Microsoft.Build.Utilities.TaskLoggingHelper Log { get { throw null; } }
        protected System.Resources.ResourceManager TaskResources { get { throw null; } set { } }
        public abstract bool Execute();
        [System.Security.SecurityCriticalAttribute]
        public override object InitializeLifetimeService() { throw null; }
    }
    public partial class AssemblyFoldersExInfo
    {
        public AssemblyFoldersExInfo(Microsoft.Win32.RegistryHive hive, Microsoft.Win32.RegistryView view, string registryKey, string directoryPath, System.Version targetFrameworkVersion) { }
        public string DirectoryPath { get { throw null; } }
        public Microsoft.Win32.RegistryHive Hive { get { throw null; } }
        public string Key { get { throw null; } }
        public System.Version TargetFrameworkVersion { get { throw null; } }
        public Microsoft.Win32.RegistryView View { get { throw null; } }
    }
    public partial class AssemblyFoldersFromConfigInfo
    {
        public AssemblyFoldersFromConfigInfo(string directoryPath, System.Version targetFrameworkVersion) { }
        public string DirectoryPath { get { throw null; } }
        public System.Version TargetFrameworkVersion { get { throw null; } }
    }
    public partial class CanonicalTrackedInputFiles
    {
        public CanonicalTrackedInputFiles(Microsoft.Build.Framework.ITask ownerTask, Microsoft.Build.Framework.ITaskItem[] tlogFiles, Microsoft.Build.Framework.ITaskItem sourceFile, Microsoft.Build.Framework.ITaskItem[] excludedInputPaths, Microsoft.Build.Utilities.CanonicalTrackedOutputFiles outputs, bool useMinimalRebuildOptimization, bool maintainCompositeRootingMarkers) { }
        public CanonicalTrackedInputFiles(Microsoft.Build.Framework.ITask ownerTask, Microsoft.Build.Framework.ITaskItem[] tlogFiles, Microsoft.Build.Framework.ITaskItem[] sourceFiles, Microsoft.Build.Framework.ITaskItem[] excludedInputPaths, Microsoft.Build.Framework.ITaskItem[] outputs, bool useMinimalRebuildOptimization, bool maintainCompositeRootingMarkers) { }
        public CanonicalTrackedInputFiles(Microsoft.Build.Framework.ITask ownerTask, Microsoft.Build.Framework.ITaskItem[] tlogFiles, Microsoft.Build.Framework.ITaskItem[] sourceFiles, Microsoft.Build.Framework.ITaskItem[] excludedInputPaths, Microsoft.Build.Utilities.CanonicalTrackedOutputFiles outputs, bool useMinimalRebuildOptimization, bool maintainCompositeRootingMarkers) { }
        public CanonicalTrackedInputFiles(Microsoft.Build.Framework.ITaskItem[] tlogFiles, Microsoft.Build.Framework.ITaskItem[] sourceFiles, Microsoft.Build.Framework.ITaskItem[] excludedInputPaths, Microsoft.Build.Utilities.CanonicalTrackedOutputFiles outputs, bool useMinimalRebuildOptimization, bool maintainCompositeRootingMarkers) { }
        public CanonicalTrackedInputFiles(Microsoft.Build.Framework.ITaskItem[] tlogFiles, Microsoft.Build.Framework.ITaskItem[] sourceFiles, Microsoft.Build.Utilities.CanonicalTrackedOutputFiles outputs, bool useMinimalRebuildOptimization, bool maintainCompositeRootingMarkers) { }
        public System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>> DependencyTable { get { throw null; } }
        public Microsoft.Build.Framework.ITaskItem[] ComputeSourcesNeedingCompilation() { throw null; }
        public Microsoft.Build.Framework.ITaskItem[] ComputeSourcesNeedingCompilation(bool searchForSubRootsInCompositeRootingMarkers) { throw null; }
        public bool FileIsExcludedFromDependencyCheck(string fileName) { throw null; }
        public void RemoveDependenciesFromEntryIfMissing(Microsoft.Build.Framework.ITaskItem source) { }
        public void RemoveDependenciesFromEntryIfMissing(Microsoft.Build.Framework.ITaskItem source, Microsoft.Build.Framework.ITaskItem correspondingOutput) { }
        public void RemoveDependenciesFromEntryIfMissing(Microsoft.Build.Framework.ITaskItem[] source) { }
        public void RemoveDependenciesFromEntryIfMissing(Microsoft.Build.Framework.ITaskItem[] source, Microsoft.Build.Framework.ITaskItem[] correspondingOutputs) { }
        public void RemoveDependencyFromEntry(Microsoft.Build.Framework.ITaskItem source, Microsoft.Build.Framework.ITaskItem dependencyToRemove) { }
        public void RemoveDependencyFromEntry(Microsoft.Build.Framework.ITaskItem[] sources, Microsoft.Build.Framework.ITaskItem dependencyToRemove) { }
        public void RemoveEntriesForSource(Microsoft.Build.Framework.ITaskItem source) { }
        public void RemoveEntriesForSource(Microsoft.Build.Framework.ITaskItem[] source) { }
        public void RemoveEntryForSourceRoot(string rootingMarker) { }
        public void SaveTlog() { }
        public void SaveTlog(Microsoft.Build.Utilities.DependencyFilter includeInTLog) { }
    }
    public partial class CanonicalTrackedOutputFiles
    {
        public CanonicalTrackedOutputFiles(Microsoft.Build.Framework.ITask ownerTask, Microsoft.Build.Framework.ITaskItem[] tlogFiles) { }
        public CanonicalTrackedOutputFiles(Microsoft.Build.Framework.ITask ownerTask, Microsoft.Build.Framework.ITaskItem[] tlogFiles, bool constructOutputsFromTLogs) { }
        public CanonicalTrackedOutputFiles(Microsoft.Build.Framework.ITaskItem[] tlogFiles) { }
        public System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, System.DateTime>> DependencyTable { get { throw null; } }
        public void AddComputedOutputForSourceRoot(string sourceKey, string computedOutput) { }
        public void AddComputedOutputsForSourceRoot(string sourceKey, Microsoft.Build.Framework.ITaskItem[] computedOutputs) { }
        public void AddComputedOutputsForSourceRoot(string sourceKey, string[] computedOutputs) { }
        public Microsoft.Build.Framework.ITaskItem[] OutputsForNonCompositeSource(params Microsoft.Build.Framework.ITaskItem[] sources) { throw null; }
        public Microsoft.Build.Framework.ITaskItem[] OutputsForSource(params Microsoft.Build.Framework.ITaskItem[] sources) { throw null; }
        public Microsoft.Build.Framework.ITaskItem[] OutputsForSource(Microsoft.Build.Framework.ITaskItem[] sources, bool searchForSubRootsInCompositeRootingMarkers) { throw null; }
        public void RemoveDependenciesFromEntryIfMissing(Microsoft.Build.Framework.ITaskItem source) { }
        public void RemoveDependenciesFromEntryIfMissing(Microsoft.Build.Framework.ITaskItem source, Microsoft.Build.Framework.ITaskItem correspondingOutput) { }
        public void RemoveDependenciesFromEntryIfMissing(Microsoft.Build.Framework.ITaskItem[] source) { }
        public void RemoveDependenciesFromEntryIfMissing(Microsoft.Build.Framework.ITaskItem[] source, Microsoft.Build.Framework.ITaskItem[] correspondingOutputs) { }
        public void RemoveDependencyFromEntry(Microsoft.Build.Framework.ITaskItem source, Microsoft.Build.Framework.ITaskItem dependencyToRemove) { }
        public void RemoveDependencyFromEntry(Microsoft.Build.Framework.ITaskItem[] sources, Microsoft.Build.Framework.ITaskItem dependencyToRemove) { }
        public void RemoveEntriesForSource(Microsoft.Build.Framework.ITaskItem source) { }
        public void RemoveEntriesForSource(Microsoft.Build.Framework.ITaskItem source, Microsoft.Build.Framework.ITaskItem correspondingOutput) { }
        public void RemoveEntriesForSource(Microsoft.Build.Framework.ITaskItem[] source) { }
        public void RemoveEntriesForSource(Microsoft.Build.Framework.ITaskItem[] source, Microsoft.Build.Framework.ITaskItem[] correspondingOutputs) { }
        public bool RemoveOutputForSourceRoot(string sourceRoot, string outputPathToRemove) { throw null; }
        public string[] RemoveRootsWithSharedOutputs(Microsoft.Build.Framework.ITaskItem[] sources) { throw null; }
        public void SaveTlog() { }
        public void SaveTlog(Microsoft.Build.Utilities.DependencyFilter includeInTLog) { }
    }
    public partial class CommandLineBuilder
    {
        public CommandLineBuilder() { }
        public CommandLineBuilder(bool quoteHyphensOnCommandLine) { }
        public CommandLineBuilder(bool quoteHyphensOnCommandLine, bool useNewLineSeparator) { }
        protected System.Text.StringBuilder CommandLine { get { throw null; } }
        public int Length { get { throw null; } }
        public void AppendFileNameIfNotNull(Microsoft.Build.Framework.ITaskItem fileItem) { }
        public void AppendFileNameIfNotNull(string fileName) { }
        public void AppendFileNamesIfNotNull(Microsoft.Build.Framework.ITaskItem[] fileItems, string delimiter) { }
        public void AppendFileNamesIfNotNull(string[] fileNames, string delimiter) { }
        protected void AppendFileNameWithQuoting(string fileName) { }
        protected void AppendQuotedTextToBuffer(System.Text.StringBuilder buffer, string unquotedTextToAppend) { }
        protected void AppendSpaceIfNotEmpty() { }
        public void AppendSwitch(string switchName) { }
        public void AppendSwitchIfNotNull(string switchName, Microsoft.Build.Framework.ITaskItem parameter) { }
        public void AppendSwitchIfNotNull(string switchName, Microsoft.Build.Framework.ITaskItem[] parameters, string delimiter) { }
        public void AppendSwitchIfNotNull(string switchName, string parameter) { }
        public void AppendSwitchIfNotNull(string switchName, string[] parameters, string delimiter) { }
        public void AppendSwitchUnquotedIfNotNull(string switchName, Microsoft.Build.Framework.ITaskItem parameter) { }
        public void AppendSwitchUnquotedIfNotNull(string switchName, Microsoft.Build.Framework.ITaskItem[] parameters, string delimiter) { }
        public void AppendSwitchUnquotedIfNotNull(string switchName, string parameter) { }
        public void AppendSwitchUnquotedIfNotNull(string switchName, string[] parameters, string delimiter) { }
        public void AppendTextUnquoted(string textToAppend) { }
        protected void AppendTextWithQuoting(string textToAppend) { }
        protected virtual bool IsQuotingRequired(string parameter) { throw null; }
        public override string ToString() { throw null; }
        protected virtual void VerifyThrowNoEmbeddedDoubleQuotes(string switchName, string parameter) { }
    }
    public delegate bool DependencyFilter(string fullPath);
    public enum DotNetFrameworkArchitecture
    {
        Current = 0,
        Bitness32 = 1,
        Bitness64 = 2,
    }
    public enum ExecutableType
    {
        Native32Bit = 0,
        Native64Bit = 1,
        ManagedIL = 2,
        Managed32Bit = 3,
        Managed64Bit = 4,
        SameAsCurrentProcess = 5,
    }
    public static partial class FileTracker
    {
        public static string CreateRootingMarkerResponseFile(Microsoft.Build.Framework.ITaskItem[] sources) { throw null; }
        public static string CreateRootingMarkerResponseFile(string rootMarker) { throw null; }
        public static void EndTrackingContext() { }
        public static string EnsureFileTrackerOnPath() { throw null; }
        public static string EnsureFileTrackerOnPath(string rootPath) { throw null; }
        public static bool FileIsExcludedFromDependencies(string fileName) { throw null; }
        public static bool FileIsUnderPath(string fileName, string path) { throw null; }
        public static string FindTrackerOnPath() { throw null; }
        public static bool ForceOutOfProcTracking(Microsoft.Build.Utilities.ExecutableType toolType) { throw null; }
        public static bool ForceOutOfProcTracking(Microsoft.Build.Utilities.ExecutableType toolType, string dllName, string cancelEventName) { throw null; }
        public static string FormatRootingMarker(Microsoft.Build.Framework.ITaskItem source) { throw null; }
        public static string FormatRootingMarker(Microsoft.Build.Framework.ITaskItem source, Microsoft.Build.Framework.ITaskItem output) { throw null; }
        public static string FormatRootingMarker(Microsoft.Build.Framework.ITaskItem[] sources) { throw null; }
        public static string FormatRootingMarker(Microsoft.Build.Framework.ITaskItem[] sources, Microsoft.Build.Framework.ITaskItem[] outputs) { throw null; }
        public static string GetFileTrackerPath(Microsoft.Build.Utilities.ExecutableType toolType) { throw null; }
        public static string GetFileTrackerPath(Microsoft.Build.Utilities.ExecutableType toolType, string rootPath) { throw null; }
        public static string GetTrackerPath(Microsoft.Build.Utilities.ExecutableType toolType) { throw null; }
        public static string GetTrackerPath(Microsoft.Build.Utilities.ExecutableType toolType, string rootPath) { throw null; }
        public static void ResumeTracking() { }
        public static void SetThreadCount(int threadCount) { }
        public static System.Diagnostics.Process StartProcess(string command, string arguments, Microsoft.Build.Utilities.ExecutableType toolType) { throw null; }
        public static System.Diagnostics.Process StartProcess(string command, string arguments, Microsoft.Build.Utilities.ExecutableType toolType, string rootFiles) { throw null; }
        public static System.Diagnostics.Process StartProcess(string command, string arguments, Microsoft.Build.Utilities.ExecutableType toolType, string intermediateDirectory, string rootFiles) { throw null; }
        public static System.Diagnostics.Process StartProcess(string command, string arguments, Microsoft.Build.Utilities.ExecutableType toolType, string dllName, string intermediateDirectory, string rootFiles) { throw null; }
        public static System.Diagnostics.Process StartProcess(string command, string arguments, Microsoft.Build.Utilities.ExecutableType toolType, string dllName, string intermediateDirectory, string rootFiles, string cancelEventName) { throw null; }
        public static void StartTrackingContext(string intermediateDirectory, string taskName) { }
        public static void StartTrackingContextWithRoot(string intermediateDirectory, string taskName, string rootMarkerResponseFile) { }
        public static void StopTrackingAndCleanup() { }
        public static void SuspendTracking() { }
        public static string TrackerArguments(string command, string arguments, string dllName, string intermediateDirectory, string rootFiles) { throw null; }
        public static string TrackerArguments(string command, string arguments, string dllName, string intermediateDirectory, string rootFiles, string cancelEventName) { throw null; }
        public static string TrackerCommandArguments(string command, string arguments) { throw null; }
        public static string TrackerResponseFileArguments(string dllName, string intermediateDirectory, string rootFiles) { throw null; }
        public static string TrackerResponseFileArguments(string dllName, string intermediateDirectory, string rootFiles, string cancelEventName) { throw null; }
        public static void WriteAllTLogs(string intermediateDirectory, string taskName) { }
        public static void WriteContextTLogs(string intermediateDirectory, string taskName) { }
    }
    public partial class FlatTrackingData
    {
        public FlatTrackingData(Microsoft.Build.Framework.ITask ownerTask, Microsoft.Build.Framework.ITaskItem[] tlogFiles, bool skipMissingFiles) { }
        public FlatTrackingData(Microsoft.Build.Framework.ITask ownerTask, Microsoft.Build.Framework.ITaskItem[] tlogFiles, System.DateTime missingFileTimeUtc) { }
        public FlatTrackingData(Microsoft.Build.Framework.ITaskItem[] tlogFiles, Microsoft.Build.Framework.ITaskItem[] tlogFilesToIgnore, System.DateTime missingFileTimeUtc) { }
        public FlatTrackingData(Microsoft.Build.Framework.ITaskItem[] tlogFiles, Microsoft.Build.Framework.ITaskItem[] tlogFilesToIgnore, System.DateTime missingFileTimeUtc, string[] excludedInputPaths, System.Collections.Generic.IDictionary<string, System.DateTime> sharedLastWriteTimeUtcCache) { }
        public FlatTrackingData(Microsoft.Build.Framework.ITaskItem[] tlogFiles, Microsoft.Build.Framework.ITaskItem[] tlogFilesToIgnore, System.DateTime missingFileTimeUtc, string[] excludedInputPaths, System.Collections.Generic.IDictionary<string, System.DateTime> sharedLastWriteTimeUtcCache, bool treatRootMarkersAsEntries) { }
        public FlatTrackingData(Microsoft.Build.Framework.ITaskItem[] tlogFiles, bool skipMissingFiles) { }
        public FlatTrackingData(Microsoft.Build.Framework.ITaskItem[] tlogFiles, System.DateTime missingFileTimeUtc) { }
        public System.Collections.Generic.List<string> MissingFiles { get { throw null; } set { } }
        public string NewestFileName { get { throw null; } set { } }
        public System.DateTime NewestFileTime { get { throw null; } set { } }
        public System.DateTime NewestFileTimeUtc { get { throw null; } set { } }
        public string NewestTLogFileName { get { throw null; } set { } }
        public System.DateTime NewestTLogTime { get { throw null; } set { } }
        public System.DateTime NewestTLogTimeUtc { get { throw null; } set { } }
        public string OldestFileName { get { throw null; } set { } }
        public System.DateTime OldestFileTime { get { throw null; } set { } }
        public System.DateTime OldestFileTimeUtc { get { throw null; } set { } }
        public bool SkipMissingFiles { get { throw null; } set { } }
        public Microsoft.Build.Framework.ITaskItem[] TlogFiles { get { throw null; } set { } }
        public bool TlogsAvailable { get { throw null; } set { } }
        public bool TreatRootMarkersAsEntries { get { throw null; } set { } }
        public bool FileIsExcludedFromDependencyCheck(string fileName) { throw null; }
        public static void FinalizeTLogs(bool trackedOperationsSucceeded, Microsoft.Build.Framework.ITaskItem[] readTLogNames, Microsoft.Build.Framework.ITaskItem[] writeTLogNames, Microsoft.Build.Framework.ITaskItem[] trackedFilesToRemoveFromTLogs) { }
        public System.DateTime GetLastWriteTimeUtc(string file) { throw null; }
        public static bool IsUpToDate(Microsoft.Build.Utilities.Task hostTask, Microsoft.Build.Utilities.UpToDateCheckType upToDateCheckType, Microsoft.Build.Framework.ITaskItem[] readTLogNames, Microsoft.Build.Framework.ITaskItem[] writeTLogNames) { throw null; }
        public static bool IsUpToDate(Microsoft.Build.Utilities.TaskLoggingHelper Log, Microsoft.Build.Utilities.UpToDateCheckType upToDateCheckType, Microsoft.Build.Utilities.FlatTrackingData inputs, Microsoft.Build.Utilities.FlatTrackingData outputs) { throw null; }
        public void SaveTlog() { }
        public void SaveTlog(Microsoft.Build.Utilities.DependencyFilter includeInTLog) { }
        public void UpdateFileEntryDetails() { }
    }
    public enum HostObjectInitializationStatus
    {
        UseHostObjectToExecute = 0,
        UseAlternateToolToExecute = 1,
        NoActionReturnSuccess = 2,
        NoActionReturnFailure = 3,
    }
    public abstract partial class Logger : Microsoft.Build.Framework.ILogger
    {
        protected Logger() { }
        public virtual string Parameters { get { throw null; } set { } }
        public virtual Microsoft.Build.Framework.LoggerVerbosity Verbosity { get { throw null; } set { } }
        public virtual string FormatErrorEvent(Microsoft.Build.Framework.BuildErrorEventArgs args) { throw null; }
        public virtual string FormatWarningEvent(Microsoft.Build.Framework.BuildWarningEventArgs args) { throw null; }
        public abstract void Initialize(Microsoft.Build.Framework.IEventSource eventSource);
        public bool IsVerbosityAtLeast(Microsoft.Build.Framework.LoggerVerbosity checkVerbosity) { throw null; }
        public virtual void Shutdown() { }
    }
    public enum MultipleVersionSupport
    {
        Allow = 0,
        Warning = 1,
        Error = 2,
    }
    public partial class MuxLogger : Microsoft.Build.Framework.ILogger, Microsoft.Build.Framework.INodeLogger
    {
        public MuxLogger() { }
        public bool IncludeEvaluationMetaprojects { get { throw null; } set { } }
        public bool IncludeEvaluationProfiles { get { throw null; } set { } }
        public bool IncludeEvaluationPropertiesAndItems { get { throw null; } set { } }
        public bool IncludeTaskInputs { get { throw null; } set { } }
        public string Parameters { get { throw null; } set { } }
        public Microsoft.Build.Framework.LoggerVerbosity Verbosity { get { throw null; } set { } }
        public void Initialize(Microsoft.Build.Framework.IEventSource eventSource) { }
        public void Initialize(Microsoft.Build.Framework.IEventSource eventSource, int maxNodeCount) { }
        public void RegisterLogger(int submissionId, Microsoft.Build.Framework.ILogger logger) { }
        public void Shutdown() { }
        public bool UnregisterLoggers(int submissionId) { throw null; }
    }
    public static partial class ProcessorArchitecture
    {
        public const string AMD64 = "AMD64";
        public const string ARM = "ARM";
        public const string ARM64 = "ARM64";
        public const string IA64 = "IA64";
        public const string MSIL = "MSIL";
        public const string X86 = "x86";
        public static string CurrentProcessArchitecture { get { throw null; } }
    }
    public partial class SDKManifest
    {
        public SDKManifest(string pathToSdk) { }
        public System.Collections.Generic.IDictionary<string, string> AppxLocations { get { throw null; } }
        public string CopyRedistToSubDirectory { get { throw null; } }
        public string DependsOnSDK { get { throw null; } }
        public string DisplayName { get { throw null; } }
        public System.Collections.Generic.IDictionary<string, string> FrameworkIdentities { get { throw null; } }
        public string FrameworkIdentity { get { throw null; } }
        public string MaxOSVersionTested { get { throw null; } }
        public string MaxPlatformVersion { get { throw null; } }
        public string MinOSVersion { get { throw null; } }
        public string MinVSVersion { get { throw null; } }
        public string MoreInfo { get { throw null; } }
        public string PlatformIdentity { get { throw null; } }
        public string ProductFamilyName { get { throw null; } }
        public bool ReadError { get { throw null; } }
        public string ReadErrorMessage { get { throw null; } }
        public Microsoft.Build.Utilities.SDKType SDKType { get { throw null; } }
        public string SupportedArchitectures { get { throw null; } }
        public string SupportPrefer32Bit { get { throw null; } }
        public Microsoft.Build.Utilities.MultipleVersionSupport SupportsMultipleVersions { get { throw null; } }
        public string TargetPlatform { get { throw null; } }
        public string TargetPlatformMinVersion { get { throw null; } }
        public string TargetPlatformVersion { get { throw null; } }
        public static partial class Attributes
        {
            public const string APPX = "APPX";
            public const string AppxLocation = "AppxLocation";
            public const string CopyLocalExpandedReferenceAssemblies = "CopyLocalExpandedReferenceAssemblies";
            public const string CopyRedist = "CopyRedist";
            public const string CopyRedistToSubDirectory = "CopyRedistToSubDirectory";
            public const string DependsOnSDK = "DependsOn";
            public const string DisplayName = "DisplayName";
            public const string ExpandReferenceAssemblies = "ExpandReferenceAssemblies";
            public const string FrameworkIdentity = "FrameworkIdentity";
            public const string MaxOSVersionTested = "MaxOSVersionTested";
            public const string MaxPlatformVersion = "MaxPlatformVersion";
            public const string MinOSVersion = "MinOSVersion";
            public const string MinVSVersion = "MinVSVersion";
            public const string MoreInfo = "MoreInfo";
            public const string PlatformIdentity = "PlatformIdentity";
            public const string ProductFamilyName = "ProductFamilyName";
            public const string SDKType = "SDKType";
            public const string SupportedArchitectures = "SupportedArchitectures";
            public const string SupportPrefer32Bit = "SupportPrefer32Bit";
            public const string SupportsMultipleVersions = "SupportsMultipleVersions";
            public const string TargetedSDK = "TargetedSDKArchitecture";
            public const string TargetedSDKConfiguration = "TargetedSDKConfiguration";
            public const string TargetPlatform = "TargetPlatform";
            public const string TargetPlatformMinVersion = "TargetPlatformMinVersion";
            public const string TargetPlatformVersion = "TargetPlatformVersion";
        }
    }
    public enum SDKType
    {
        Unspecified = 0,
        External = 1,
        Platform = 2,
        Framework = 3,
    }
    public enum TargetDotNetFrameworkVersion
    {
        Version11 = 0,
        Version20 = 1,
        Version30 = 2,
        Version35 = 3,
        Version40 = 4,
        Version45 = 5,
        Version451 = 6,
        Version46 = 7,
        Version461 = 8,
        Version452 = 9,
        Version462 = 10,
        Version47 = 11,
        Version471 = 12,
        Version472 = 13,
        Version48 = 14,
        VersionLatest = 14,
        Latest = 9999,
    }
    public partial class TargetPlatformSDK : System.IEquatable<Microsoft.Build.Utilities.TargetPlatformSDK>
    {
        public TargetPlatformSDK(string targetPlatformIdentifier, System.Version targetPlatformVersion, string path) { }
        public string DisplayName { get { throw null; } }
        public System.Version MinOSVersion { get { throw null; } }
        public System.Version MinVSVersion { get { throw null; } }
        public string Path { get { throw null; } set { } }
        public string TargetPlatformIdentifier { get { throw null; } }
        public System.Version TargetPlatformVersion { get { throw null; } }
        public bool ContainsPlatform(string targetPlatformIdentifier, string targetPlatformVersion) { throw null; }
        public bool Equals(Microsoft.Build.Utilities.TargetPlatformSDK other) { throw null; }
        public override bool Equals(object obj) { throw null; }
        public override int GetHashCode() { throw null; }
    }
    public abstract partial class Task : Microsoft.Build.Framework.ITask
    {
        protected Task() { }
        protected Task(System.Resources.ResourceManager taskResources) { }
        protected Task(System.Resources.ResourceManager taskResources, string helpKeywordPrefix) { }
        public Microsoft.Build.Framework.IBuildEngine BuildEngine { get { throw null; } set { } }
        public Microsoft.Build.Framework.IBuildEngine2 BuildEngine2 { get { throw null; } }
        public Microsoft.Build.Framework.IBuildEngine3 BuildEngine3 { get { throw null; } }
        public Microsoft.Build.Framework.IBuildEngine4 BuildEngine4 { get { throw null; } }
        public Microsoft.Build.Framework.IBuildEngine5 BuildEngine5 { get { throw null; } }
        public Microsoft.Build.Framework.IBuildEngine6 BuildEngine6 { get { throw null; } }
        public Microsoft.Build.Framework.IBuildEngine7 BuildEngine7 { get { throw null; } }
        public Microsoft.Build.Framework.IBuildEngine8 BuildEngine8 { get { throw null; } }
        public Microsoft.Build.Framework.IBuildEngine9 BuildEngine9 { get { throw null; } }
        protected string HelpKeywordPrefix { get { throw null; } set { } }
        public Microsoft.Build.Framework.ITaskHost HostObject { get { throw null; } set { } }
        public Microsoft.Build.Utilities.TaskLoggingHelper Log { get { throw null; } }
        protected System.Resources.ResourceManager TaskResources { get { throw null; } set { } }
        public abstract bool Execute();
    }
    public sealed partial class TaskItem : System.MarshalByRefObject, Microsoft.Build.Framework.ITaskItem, Microsoft.Build.Framework.ITaskItem2
    {
        public TaskItem() { }
        public TaskItem(Microsoft.Build.Framework.ITaskItem sourceItem) { }
        public TaskItem(string itemSpec) { }
        public TaskItem(string itemSpec, System.Collections.IDictionary itemMetadata) { }
        public string ItemSpec { get { throw null; } set { } }
        public int MetadataCount { get { throw null; } }
        public System.Collections.ICollection MetadataNames { get { throw null; } }
        string Microsoft.Build.Framework.ITaskItem2.EvaluatedIncludeEscaped { get { throw null; } set { } }
        public System.Collections.IDictionary CloneCustomMetadata() { throw null; }
        public void CopyMetadataTo(Microsoft.Build.Framework.ITaskItem destinationItem) { }
        public string GetMetadata(string metadataName) { throw null; }
        [System.Security.SecurityCriticalAttribute]
        public override object InitializeLifetimeService() { throw null; }
        System.Collections.IDictionary Microsoft.Build.Framework.ITaskItem2.CloneCustomMetadataEscaped() { throw null; }
        string Microsoft.Build.Framework.ITaskItem2.GetMetadataValueEscaped(string metadataName) { throw null; }
        void Microsoft.Build.Framework.ITaskItem2.SetMetadataValueLiteral(string metadataName, string metadataValue) { }
        public static explicit operator string (Microsoft.Build.Utilities.TaskItem taskItemToCast) { throw null; }
        public void RemoveMetadata(string metadataName) { }
        public void SetMetadata(string metadataName, string metadataValue) { }
        public override string ToString() { throw null; }
    }
    public partial class TaskLoggingHelper : System.MarshalByRefObject
    {
        public TaskLoggingHelper(Microsoft.Build.Framework.IBuildEngine buildEngine, string taskName) { }
        public TaskLoggingHelper(Microsoft.Build.Framework.ITask taskInstance) { }
        protected Microsoft.Build.Framework.IBuildEngine BuildEngine { get { throw null; } }
        public bool HasLoggedErrors { get { throw null; } }
        public string HelpKeywordPrefix { get { throw null; } set { } }
        protected string TaskName { get { throw null; } }
        public System.Resources.ResourceManager TaskResources { get { throw null; } set { } }
        public string ExtractMessageCode(string message, out string messageWithoutCodePrefix) { throw null; }
        public virtual string FormatResourceString(string resourceName, params object[] args) { throw null; }
        public virtual string FormatString(string unformatted, params object[] args) { throw null; }
        public virtual string GetResourceMessage(string resourceName) { throw null; }
        public override object InitializeLifetimeService() { throw null; }
        public void LogCommandLine(Microsoft.Build.Framework.MessageImportance importance, string commandLine) { }
        public void LogCommandLine(string commandLine) { }
        public void LogCriticalMessage(string subcategory, string code, string helpKeyword, string file, int lineNumber, int columnNumber, int endLineNumber, int endColumnNumber, string message, params object[] messageArgs) { }
        public void LogError(string message, params object[] messageArgs) { }
        public void LogError(string subcategory, string errorCode, string helpKeyword, string file, int lineNumber, int columnNumber, int endLineNumber, int endColumnNumber, string message, params object[] messageArgs) { }
        public void LogError(string subcategory, string errorCode, string helpKeyword, string helpLink, string file, int lineNumber, int columnNumber, int endLineNumber, int endColumnNumber, string message, params object[] messageArgs) { }
        public void LogErrorFromException(System.Exception exception) { }
        public void LogErrorFromException(System.Exception exception, bool showStackTrace) { }
        public void LogErrorFromException(System.Exception exception, bool showStackTrace, bool showDetail, string file) { }
        public void LogErrorFromResources(string messageResourceName, params object[] messageArgs) { }
        public void LogErrorFromResources(string subcategoryResourceName, string errorCode, string helpKeyword, string file, int lineNumber, int columnNumber, int endLineNumber, int endColumnNumber, string messageResourceName, params object[] messageArgs) { }
        public void LogErrorWithCodeFromResources(string messageResourceName, params object[] messageArgs) { }
        public void LogErrorWithCodeFromResources(string subcategoryResourceName, string file, int lineNumber, int columnNumber, int endLineNumber, int endColumnNumber, string messageResourceName, params object[] messageArgs) { }
        public void LogExternalProjectFinished(string message, string helpKeyword, string projectFile, bool succeeded) { }
        public void LogExternalProjectStarted(string message, string helpKeyword, string projectFile, string targetNames) { }
        public void LogMessage(Microsoft.Build.Framework.MessageImportance importance, string message, params object[] messageArgs) { }
        public void LogMessage(string message, params object[] messageArgs) { }
        public void LogMessage(string subcategory, string code, string helpKeyword, string file, int lineNumber, int columnNumber, int endLineNumber, int endColumnNumber, Microsoft.Build.Framework.MessageImportance importance, string message, params object[] messageArgs) { }
        public void LogMessageFromResources(Microsoft.Build.Framework.MessageImportance importance, string messageResourceName, params object[] messageArgs) { }
        public void LogMessageFromResources(string messageResourceName, params object[] messageArgs) { }
        public bool LogMessageFromText(string lineOfText, Microsoft.Build.Framework.MessageImportance messageImportance) { throw null; }
        public bool LogMessagesFromFile(string fileName) { throw null; }
        public bool LogMessagesFromFile(string fileName, Microsoft.Build.Framework.MessageImportance messageImportance) { throw null; }
        public bool LogMessagesFromStream(System.IO.TextReader stream, Microsoft.Build.Framework.MessageImportance messageImportance) { throw null; }
        public void LogTelemetry(string eventName, System.Collections.Generic.IDictionary<string, string> properties) { }
        public void LogWarning(string message, params object[] messageArgs) { }
        public void LogWarning(string subcategory, string warningCode, string helpKeyword, string file, int lineNumber, int columnNumber, int endLineNumber, int endColumnNumber, string message, params object[] messageArgs) { }
        public void LogWarning(string subcategory, string warningCode, string helpKeyword, string helpLink, string file, int lineNumber, int columnNumber, int endLineNumber, int endColumnNumber, string message, params object[] messageArgs) { }
        public void LogWarningFromException(System.Exception exception) { }
        public void LogWarningFromException(System.Exception exception, bool showStackTrace) { }
        public void LogWarningFromResources(string messageResourceName, params object[] messageArgs) { }
        public void LogWarningFromResources(string subcategoryResourceName, string warningCode, string helpKeyword, string file, int lineNumber, int columnNumber, int endLineNumber, int endColumnNumber, string messageResourceName, params object[] messageArgs) { }
        public void LogWarningWithCodeFromResources(string messageResourceName, params object[] messageArgs) { }
        public void LogWarningWithCodeFromResources(string subcategoryResourceName, string file, int lineNumber, int columnNumber, int endLineNumber, int endColumnNumber, string messageResourceName, params object[] messageArgs) { }
        public void MarkAsInactive() { }
    }
    public static partial class ToolLocationHelper
    {
        public static string CurrentToolsVersion { get { throw null; } }
        public static string PathToSystem { get { throw null; } }
        public static void ClearSDKStaticCache() { }
        public static System.Collections.Generic.IDictionary<string, string> FilterPlatformExtensionSDKs(System.Version targetPlatformVersion, System.Collections.Generic.IDictionary<string, string> extensionSdks) { throw null; }
        public static System.Collections.Generic.IList<Microsoft.Build.Utilities.TargetPlatformSDK> FilterTargetPlatformSdks(System.Collections.Generic.IList<Microsoft.Build.Utilities.TargetPlatformSDK> targetPlatformSdkList, System.Version osVersion, System.Version vsVersion) { throw null; }
        public static string FindRootFolderWhereAllFilesExist(string possibleRoots, string relativeFilePaths) { throw null; }
        public static System.Collections.Generic.IList<Microsoft.Build.Utilities.AssemblyFoldersExInfo> GetAssemblyFoldersExInfo(string registryRoot, string targetFrameworkVersion, string registryKeySuffix, string osVersion, string platform, System.Reflection.ProcessorArchitecture targetProcessorArchitecture) { throw null; }
        public static System.Collections.Generic.IList<Microsoft.Build.Utilities.AssemblyFoldersFromConfigInfo> GetAssemblyFoldersFromConfigInfo(string configFile, string targetFrameworkVersion, System.Reflection.ProcessorArchitecture targetProcessorArchitecture) { throw null; }
        public static string GetDisplayNameForTargetFrameworkDirectory(string targetFrameworkDirectory, System.Runtime.Versioning.FrameworkName frameworkName) { throw null; }
        public static string GetDotNetFrameworkRootRegistryKey(Microsoft.Build.Utilities.TargetDotNetFrameworkVersion version) { throw null; }
        public static string GetDotNetFrameworkSdkInstallKeyValue(Microsoft.Build.Utilities.TargetDotNetFrameworkVersion version) { throw null; }
        public static string GetDotNetFrameworkSdkInstallKeyValue(Microsoft.Build.Utilities.TargetDotNetFrameworkVersion version, Microsoft.Build.Utilities.VisualStudioVersion visualStudioVersion) { throw null; }
        public static string GetDotNetFrameworkSdkRootRegistryKey(Microsoft.Build.Utilities.TargetDotNetFrameworkVersion version) { throw null; }
        public static string GetDotNetFrameworkSdkRootRegistryKey(Microsoft.Build.Utilities.TargetDotNetFrameworkVersion version, Microsoft.Build.Utilities.VisualStudioVersion visualStudioVersion) { throw null; }
        public static string GetDotNetFrameworkVersionFolderPrefix(Microsoft.Build.Utilities.TargetDotNetFrameworkVersion version) { throw null; }
        public static System.Collections.Generic.IEnumerable<string> GetFoldersInVSInstalls(System.Version minVersion = null, System.Version maxVersion = null, string subFolder = null) { throw null; }
        public static string GetFoldersInVSInstallsAsString(string minVersionString = null, string maxVersionString = null, string subFolder = null) { throw null; }
        public static string GetLatestSDKTargetPlatformVersion(string sdkIdentifier, string sdkVersion) { throw null; }
        public static string GetLatestSDKTargetPlatformVersion(string sdkIdentifier, string sdkVersion, string[] sdkRoots) { throw null; }
        public static string GetPathToBuildTools(string toolsVersion) { throw null; }
        public static string GetPathToBuildTools(string toolsVersion, Microsoft.Build.Utilities.DotNetFrameworkArchitecture architecture) { throw null; }
        public static string GetPathToBuildToolsFile(string fileName, string toolsVersion) { throw null; }
        public static string GetPathToBuildToolsFile(string fileName, string toolsVersion, Microsoft.Build.Utilities.DotNetFrameworkArchitecture architecture) { throw null; }
        public static string GetPathToDotNetFramework(Microsoft.Build.Utilities.TargetDotNetFrameworkVersion version) { throw null; }
        public static string GetPathToDotNetFramework(Microsoft.Build.Utilities.TargetDotNetFrameworkVersion version, Microsoft.Build.Utilities.DotNetFrameworkArchitecture architecture) { throw null; }
        public static string GetPathToDotNetFrameworkFile(string fileName, Microsoft.Build.Utilities.TargetDotNetFrameworkVersion version) { throw null; }
        public static string GetPathToDotNetFrameworkFile(string fileName, Microsoft.Build.Utilities.TargetDotNetFrameworkVersion version, Microsoft.Build.Utilities.DotNetFrameworkArchitecture architecture) { throw null; }
        public static string GetPathToDotNetFrameworkReferenceAssemblies(Microsoft.Build.Utilities.TargetDotNetFrameworkVersion version) { throw null; }
        public static string GetPathToDotNetFrameworkSdk() { throw null; }
        public static string GetPathToDotNetFrameworkSdk(Microsoft.Build.Utilities.TargetDotNetFrameworkVersion version) { throw null; }
        public static string GetPathToDotNetFrameworkSdk(Microsoft.Build.Utilities.TargetDotNetFrameworkVersion version, Microsoft.Build.Utilities.VisualStudioVersion visualStudioVersion) { throw null; }
        public static string GetPathToDotNetFrameworkSdkFile(string fileName) { throw null; }
        public static string GetPathToDotNetFrameworkSdkFile(string fileName, Microsoft.Build.Utilities.TargetDotNetFrameworkVersion version) { throw null; }
        public static string GetPathToDotNetFrameworkSdkFile(string fileName, Microsoft.Build.Utilities.TargetDotNetFrameworkVersion version, Microsoft.Build.Utilities.DotNetFrameworkArchitecture architecture) { throw null; }
        public static string GetPathToDotNetFrameworkSdkFile(string fileName, Microsoft.Build.Utilities.TargetDotNetFrameworkVersion version, Microsoft.Build.Utilities.VisualStudioVersion visualStudioVersion) { throw null; }
        public static string GetPathToDotNetFrameworkSdkFile(string fileName, Microsoft.Build.Utilities.TargetDotNetFrameworkVersion version, Microsoft.Build.Utilities.VisualStudioVersion visualStudioVersion, Microsoft.Build.Utilities.DotNetFrameworkArchitecture architecture) { throw null; }
        public static System.Collections.Generic.IList<string> GetPathToReferenceAssemblies(System.Runtime.Versioning.FrameworkName frameworkName) { throw null; }
        public static System.Collections.Generic.IList<string> GetPathToReferenceAssemblies(string targetFrameworkRootPath, System.Runtime.Versioning.FrameworkName frameworkName) { throw null; }
        public static System.Collections.Generic.IList<string> GetPathToReferenceAssemblies(string targetFrameworkRootPath, string targetFrameworkFallbackSearchPaths, System.Runtime.Versioning.FrameworkName frameworkName) { throw null; }
        public static System.Collections.Generic.IList<string> GetPathToReferenceAssemblies(string targetFrameworkIdentifier, string targetFrameworkVersion, string targetFrameworkProfile) { throw null; }
        public static System.Collections.Generic.IList<string> GetPathToReferenceAssemblies(string targetFrameworkIdentifier, string targetFrameworkVersion, string targetFrameworkProfile, string targetFrameworkRootPath) { throw null; }
        public static System.Collections.Generic.IList<string> GetPathToReferenceAssemblies(string targetFrameworkIdentifier, string targetFrameworkVersion, string targetFrameworkProfile, string targetFrameworkRootPath, string targetFrameworkFallbackSearchPaths) { throw null; }
        public static string GetPathToStandardLibraries(string targetFrameworkIdentifier, string targetFrameworkVersion, string targetFrameworkProfile) { throw null; }
        public static string GetPathToStandardLibraries(string targetFrameworkIdentifier, string targetFrameworkVersion, string targetFrameworkProfile, string platformTarget) { throw null; }
        public static string GetPathToStandardLibraries(string targetFrameworkIdentifier, string targetFrameworkVersion, string targetFrameworkProfile, string platformTarget, string targetFrameworkRootPath) { throw null; }
        public static string GetPathToStandardLibraries(string targetFrameworkIdentifier, string targetFrameworkVersion, string targetFrameworkProfile, string platformTarget, string targetFrameworkRootPath, string targetFrameworkFallbackSearchPaths) { throw null; }
        public static string GetPathToSystemFile(string fileName) { throw null; }
        [System.ObsoleteAttribute("Consider using GetPlatformSDKLocation instead")]
        public static string GetPathToWindowsSdk(Microsoft.Build.Utilities.TargetDotNetFrameworkVersion version, Microsoft.Build.Utilities.VisualStudioVersion visualStudioVersion) { throw null; }
        [System.ObsoleteAttribute("Consider using GetPlatformSDKLocationFile instead")]
        public static string GetPathToWindowsSdkFile(string fileName, Microsoft.Build.Utilities.TargetDotNetFrameworkVersion version, Microsoft.Build.Utilities.VisualStudioVersion visualStudioVersion) { throw null; }
        [System.ObsoleteAttribute("Consider using GetPlatformSDKLocationFile instead")]
        public static string GetPathToWindowsSdkFile(string fileName, Microsoft.Build.Utilities.TargetDotNetFrameworkVersion version, Microsoft.Build.Utilities.VisualStudioVersion visualStudioVersion, Microsoft.Build.Utilities.DotNetFrameworkArchitecture architecture) { throw null; }
        public static string GetPlatformExtensionSDKLocation(string sdkMoniker, string targetPlatformIdentifier, string targetPlatformVersion) { throw null; }
        public static string GetPlatformExtensionSDKLocation(string sdkMoniker, string targetPlatformIdentifier, string targetPlatformVersion, string diskRoots, string registryRoot) { throw null; }
        public static string GetPlatformExtensionSDKLocation(string sdkMoniker, string targetPlatformIdentifier, string targetPlatformVersion, string diskRoots, string extensionDiskRoots, string registryRoot) { throw null; }
        public static string GetPlatformExtensionSDKLocation(string sdkMoniker, string targetPlatformIdentifier, System.Version targetPlatformVersion) { throw null; }
        public static string GetPlatformExtensionSDKLocation(string sdkMoniker, string targetPlatformIdentifier, System.Version targetPlatformVersion, string[] diskRoots, string registryRoot) { throw null; }
        public static string GetPlatformExtensionSDKLocation(string sdkMoniker, string targetPlatformIdentifier, System.Version targetPlatformVersion, string[] diskRoots, string[] extensionDiskRoots, string registryRoot) { throw null; }
        public static System.Collections.Generic.IDictionary<string, string> GetPlatformExtensionSDKLocations(string targetPlatformIdentifier, System.Version targetPlatformVersion) { throw null; }
        public static System.Collections.Generic.IDictionary<string, string> GetPlatformExtensionSDKLocations(string[] diskRoots, string registryRoot, string targetPlatformIdentifier, System.Version targetPlatformVersion) { throw null; }
        public static System.Collections.Generic.IDictionary<string, string> GetPlatformExtensionSDKLocations(string[] diskRoots, string[] extensionDiskRoots, string registryRoot, string targetPlatformIdentifier, System.Version targetPlatformVersion) { throw null; }
        public static System.Collections.Generic.IDictionary<string, System.Tuple<string, string>> GetPlatformExtensionSDKLocationsAndVersions(string targetPlatformIdentifier, System.Version targetPlatformVersion) { throw null; }
        public static System.Collections.Generic.IDictionary<string, System.Tuple<string, string>> GetPlatformExtensionSDKLocationsAndVersions(string[] diskRoots, string registryRoot, string targetPlatformIdentifier, System.Version targetPlatformVersion) { throw null; }
        public static System.Collections.Generic.IDictionary<string, System.Tuple<string, string>> GetPlatformExtensionSDKLocationsAndVersions(string[] diskRoots, string[] multiPlatformDiskRoots, string registryRoot, string targetPlatformIdentifier, System.Version targetPlatformVersion) { throw null; }
        public static string[] GetPlatformOrFrameworkExtensionSdkReferences(string extensionSdkMoniker, string targetSdkIdentifier, string targetSdkVersion, string diskRoots, string extensionDiskRoots, string registryRoot) { throw null; }
        public static string[] GetPlatformOrFrameworkExtensionSdkReferences(string extensionSdkMoniker, string targetSdkIdentifier, string targetSdkVersion, string diskRoots, string extensionDiskRoots, string registryRoot, string targetPlatformIdentifier, string targetPlatformVersion) { throw null; }
        public static string GetPlatformSDKDisplayName(string targetPlatformIdentifier, string targetPlatformVersion) { throw null; }
        public static string GetPlatformSDKDisplayName(string targetPlatformIdentifier, string targetPlatformVersion, string diskRoots, string registryRoot) { throw null; }
        public static string GetPlatformSDKLocation(string targetPlatformIdentifier, string targetPlatformVersion) { throw null; }
        public static string GetPlatformSDKLocation(string targetPlatformIdentifier, string targetPlatformVersion, string diskRoots, string registryRoot) { throw null; }
        public static string GetPlatformSDKLocation(string targetPlatformIdentifier, System.Version targetPlatformVersion) { throw null; }
        public static string GetPlatformSDKLocation(string targetPlatformIdentifier, System.Version targetPlatformVersion, string[] diskRoots, string registryRoot) { throw null; }
        public static string GetPlatformSDKPropsFileLocation(string sdkIdentifier, string sdkVersion, string targetPlatformIdentifier, string targetPlatformMinVersion, string targetPlatformVersion) { throw null; }
        public static string GetPlatformSDKPropsFileLocation(string sdkIdentifier, string sdkVersion, string targetPlatformIdentifier, string targetPlatformMinVersion, string targetPlatformVersion, string diskRoots, string registryRoot) { throw null; }
        public static System.Collections.Generic.IEnumerable<string> GetPlatformsForSDK(string sdkIdentifier, System.Version sdkVersion) { throw null; }
        public static System.Collections.Generic.IEnumerable<string> GetPlatformsForSDK(string sdkIdentifier, System.Version sdkVersion, string[] diskRoots, string registryRoot) { throw null; }
        public static string GetProgramFilesReferenceAssemblyRoot() { throw null; }
        public static string GetSDKContentFolderPath(string sdkIdentifier, string sdkVersion, string targetPlatformIdentifier, string targetPlatformMinVersion, string targetPlatformVersion, string folderName, string diskRoot = null) { throw null; }
        public static System.Collections.Generic.IList<string> GetSDKDesignTimeFolders(string sdkRoot) { throw null; }
        public static System.Collections.Generic.IList<string> GetSDKDesignTimeFolders(string sdkRoot, string targetConfiguration, string targetArchitecture) { throw null; }
        public static System.Collections.Generic.IList<string> GetSDKRedistFolders(string sdkRoot) { throw null; }
        public static System.Collections.Generic.IList<string> GetSDKRedistFolders(string sdkRoot, string targetConfiguration, string targetArchitecture) { throw null; }
        public static System.Collections.Generic.IList<string> GetSDKReferenceFolders(string sdkRoot) { throw null; }
        public static System.Collections.Generic.IList<string> GetSDKReferenceFolders(string sdkRoot, string targetConfiguration, string targetArchitecture) { throw null; }
        public static System.Collections.Generic.IList<string> GetSupportedTargetFrameworks() { throw null; }
        public static string[] GetTargetPlatformReferences(string sdkIdentifier, string sdkVersion, string targetPlatformIdentifier, string targetPlatformMinVersion, string targetPlatformVersion) { throw null; }
        public static string[] GetTargetPlatformReferences(string sdkIdentifier, string sdkVersion, string targetPlatformIdentifier, string targetPlatformMinVersion, string targetPlatformVersion, string diskRoots, string registryRoot) { throw null; }
        public static System.Collections.Generic.IList<Microsoft.Build.Utilities.TargetPlatformSDK> GetTargetPlatformSdks() { throw null; }
        public static System.Collections.Generic.IList<Microsoft.Build.Utilities.TargetPlatformSDK> GetTargetPlatformSdks(string[] diskRoots, string registryRoot) { throw null; }
        public static System.Runtime.Versioning.FrameworkName HighestVersionOfTargetFrameworkIdentifier(string targetFrameworkRootDirectory, string frameworkIdentifier) { throw null; }
    }
    public abstract partial class ToolTask : Microsoft.Build.Utilities.Task, Microsoft.Build.Framework.ICancelableTask, Microsoft.Build.Framework.ITask
    {
        protected ToolTask() { }
        protected ToolTask(System.Resources.ResourceManager taskResources) { }
        protected ToolTask(System.Resources.ResourceManager taskResources, string helpKeywordPrefix) { }
        public bool EchoOff { get { throw null; } set { } }
        [System.ObsoleteAttribute("Use EnvironmentVariables property")]
        protected virtual System.Collections.Generic.Dictionary<string, string> EnvironmentOverride { get { throw null; } }
        public string[] EnvironmentVariables { get { throw null; } set { } }
        [Microsoft.Build.Framework.OutputAttribute]
        public int ExitCode { get { throw null; } }
        protected virtual bool HasLoggedErrors { get { throw null; } }
        public bool LogStandardErrorAsError { get { throw null; } set { } }
        protected virtual System.Text.Encoding ResponseFileEncoding { get { throw null; } }
        protected virtual System.Text.Encoding StandardErrorEncoding { get { throw null; } }
        public string StandardErrorImportance { get { throw null; } set { } }
        protected Microsoft.Build.Framework.MessageImportance StandardErrorImportanceToUse { get { throw null; } }
        protected virtual Microsoft.Build.Framework.MessageImportance StandardErrorLoggingImportance { get { throw null; } }
        protected virtual System.Text.Encoding StandardOutputEncoding { get { throw null; } }
        public string StandardOutputImportance { get { throw null; } set { } }
        protected Microsoft.Build.Framework.MessageImportance StandardOutputImportanceToUse { get { throw null; } }
        protected virtual Microsoft.Build.Framework.MessageImportance StandardOutputLoggingImportance { get { throw null; } }
        protected int TaskProcessTerminationTimeout { get { throw null; } set { } }
        public virtual int Timeout { get { throw null; } set { } }
        protected System.Threading.ManualResetEvent ToolCanceled { get { throw null; } }
        public virtual string ToolExe { get { throw null; } set { } }
        protected abstract string ToolName { get; }
        public string ToolPath { get { throw null; } set { } }
        public bool UseCommandProcessor { get { throw null; } set { } }
        public string UseUtf8Encoding { get { throw null; } set { } }
        public bool YieldDuringToolExecution { get { throw null; } set { } }
        protected virtual string AdjustCommandsForOperatingSystem(string input) { throw null; }
        protected virtual bool CallHostObjectToExecute() { throw null; }
        public virtual void Cancel() { }
        protected void DeleteTempFile(string fileName) { }
        public override bool Execute() { throw null; }
        protected virtual int ExecuteTool(string pathToTool, string responseFileCommands, string commandLineCommands) { throw null; }
        protected virtual string GenerateCommandLineCommands() { throw null; }
        protected abstract string GenerateFullPathToTool();
        protected virtual string GenerateResponseFileCommands() { throw null; }
        protected virtual System.Diagnostics.ProcessStartInfo GetProcessStartInfo(string pathToTool, string commandLineCommands, string responseFileSwitch) { throw null; }
        protected virtual string GetResponseFileSwitch(string responseFilePath) { throw null; }
        protected virtual string GetWorkingDirectory() { throw null; }
        protected virtual bool HandleTaskExecutionErrors() { throw null; }
        protected virtual Microsoft.Build.Utilities.HostObjectInitializationStatus InitializeHostObject() { throw null; }
        protected virtual void LogEventsFromTextOutput(string singleLine, Microsoft.Build.Framework.MessageImportance messageImportance) { }
        protected virtual void LogPathToTool(string toolName, string pathToTool) { }
        protected virtual void LogToolCommand(string message) { }
        protected virtual void ProcessStarted() { }
        protected virtual string ResponseFileEscape(string responseString) { throw null; }
        protected virtual bool SkipTaskExecution() { throw null; }
        protected internal virtual bool ValidateParameters() { throw null; }
    }
    public static partial class TrackedDependencies
    {
        public static Microsoft.Build.Framework.ITaskItem[] ExpandWildcards(Microsoft.Build.Framework.ITaskItem[] expand) { throw null; }
    }
    public enum UpToDateCheckType
    {
        InputNewerThanOutput = 0,
        InputOrOutputNewerThanTracking = 1,
        InputNewerThanTracking = 2,
    }
    public enum VisualStudioVersion
    {
        Version100 = 0,
        Version110 = 1,
        Version120 = 2,
        Version140 = 3,
        Version150 = 4,
        Version160 = 5,
        Version170 = 6,
        VersionLatest = 6,
    }
}
