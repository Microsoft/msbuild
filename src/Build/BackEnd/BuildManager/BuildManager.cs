// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Build.BackEnd;
using Microsoft.Build.BackEnd.Logging;
using Microsoft.Build.BackEnd.SdkResolution;
using Microsoft.Build.Collections;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Eventing;
using Microsoft.Build.Exceptions;
using Microsoft.Build.Experimental.ProjectCache;
using Microsoft.Build.Framework;
using Microsoft.Build.Graph;
using Microsoft.Build.Internal;
using Microsoft.Build.Logging;
using Microsoft.Build.Shared;
using Microsoft.Build.Shared.Debugging;
using Microsoft.Build.Shared.FileSystem;
using Microsoft.Build.Utilities;
using ForwardingLoggerRecord = Microsoft.Build.Logging.ForwardingLoggerRecord;
using LoggerDescription = Microsoft.Build.Logging.LoggerDescription;

using Microsoft.NET.StringTools;

namespace Microsoft.Build.Execution
{
    /// <summary>
    /// This class is the public entry point for executing builds.
    /// </summary>
    [SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling", Justification = "Refactoring at the end of Beta1 is not appropriate.")]
    public class BuildManager : INodePacketHandler, IBuildComponentHost, IDisposable
    {
        // TODO: Remove this when VS gets updated to setup project cache plugins.
        internal static ConcurrentDictionary<string, ProjectCacheItem> ProjectCacheItems { get; } = new ConcurrentDictionary<string, ProjectCacheItem>();

        /// <summary>
        /// The object used for thread-safe synchronization of static members.
        /// </summary>
        private static readonly Object s_staticSyncLock = new Object();

        /// <summary>
        /// The object used for thread-safe synchronization of BuildManager shared data and the Scheduler.
        /// </summary>
        private readonly Object _syncLock = new Object();

        /// <summary>
        /// The singleton instance for the BuildManager.
        /// </summary>
        private static BuildManager s_singletonInstance;

        /// <summary>
        /// The next build id;
        /// </summary>
        private static int s_nextBuildId;

        /// <summary>
        /// The next build request configuration ID to use.
        /// These must be unique across build managers, as they
        /// are used as part of cache file names, for example.
        /// </summary>
        private static int s_nextBuildRequestConfigurationId;

        /// <summary>
        /// The cache for build request configurations.
        /// </summary>
        private IConfigCache _configCache;

        /// <summary>
        /// The cache for build results.
        /// </summary>
        private IResultsCache _resultsCache;

        /// <summary>
        /// The object responsible for creating and managing nodes.
        /// </summary>
        private INodeManager _nodeManager;

        /// <summary>
        /// The object responsible for creating and managing task host nodes.
        /// </summary>
        private INodeManager _taskHostNodeManager;

        /// <summary>
        /// The object which determines which projects to build, and where.
        /// </summary>
        private IScheduler _scheduler;

        /// <summary>
        /// The node configuration to use for spawning new nodes.
        /// </summary>
        private NodeConfiguration _nodeConfiguration;

        /// <summary>
        /// Any exception which occurs on a logging thread will go here.
        /// </summary>
        private ExceptionDispatchInfo _threadException;

        /// <summary>
        /// Set of active nodes in the system.
        /// </summary>
        private readonly HashSet<NGen<int>> _activeNodes;

        /// <summary>
        /// Event signalled when all nodes have shutdown.
        /// </summary>
        private AutoResetEvent _noNodesActiveEvent;

        /// <summary>
        /// Mapping of nodes to the configurations they know about.
        /// </summary>
        private readonly Dictionary<NGen<int>, HashSet<NGen<int>>> _nodeIdToKnownConfigurations;

        /// <summary>
        /// Flag indicating if we are currently shutting down.  When set, we stop processing packets other than NodeShutdown.
        /// </summary>
        private bool _shuttingDown;

        /// <summary>
        /// The current state of the BuildManager.
        /// </summary>
        private BuildManagerState _buildManagerState;

        /// <summary>
        /// The name given to this BuildManager as the component host.
        /// </summary>
        private readonly string _hostName;

        /// <summary>
        /// The parameters with which the build was started.
        /// </summary>
        private BuildParameters _buildParameters;

        /// <summary>
        /// The current pending and active submissions.
        /// </summary>
        /// <remarks>
        /// { submissionId, BuildSubmission }
        /// </remarks>
        private readonly Dictionary<int, BuildSubmission> _buildSubmissions;

        /// <summary>
        /// The current pending and active graph build submissions.
        /// </summary>
        /// <remarks>
        /// { submissionId, GraphBuildSubmission }
        /// </remarks>
        private readonly Dictionary<int, GraphBuildSubmission> _graphBuildSubmissions;

        /// <summary>
        /// Event signalled when all build submissions are complete.
        /// </summary>
        private AutoResetEvent _noActiveSubmissionsEvent;

        /// <summary>
        /// The overall success of the build.
        /// </summary>
        private bool _overallBuildSuccess;

        /// <summary>
        /// The next build submission id.
        /// </summary>
        private int _nextBuildSubmissionId;

        /// <summary>
        /// Mapping of unnamed project instances to the file names assigned to them.
        /// </summary>
        private readonly Dictionary<ProjectInstance, string> _unnamedProjectInstanceToNames;

        /// <summary>
        /// The next ID to assign to a project which has no name.
        /// </summary>
        private int _nextUnnamedProjectId;

        /// <summary>
        /// The build component factories.
        /// </summary>
        private readonly BuildComponentFactoryCollection _componentFactories;

        /// <summary>
        /// Mapping of submission IDs to their first project started events.
        /// </summary>
        private readonly Dictionary<int, BuildEventArgs> _projectStartedEvents;

        /// <summary>
        /// Whether a cache has been provided by a project instance, meaning
        /// we've acquired at least one build submission that included a project instance.
        /// Once that has happened, we use the provided one, rather than our default.
        /// </summary>
        private bool _acquiredProjectRootElementCacheFromProjectInstance;

        /// <summary>
        /// The project started event handler
        /// </summary>
        private readonly ProjectStartedEventHandler _projectStartedEventHandler;

        /// <summary>
        /// The project finished event handler
        /// </summary>
        private readonly ProjectFinishedEventHandler _projectFinishedEventHandler;

        /// <summary>
        /// The logging exception event handler
        /// </summary>
        private readonly LoggingExceptionDelegate _loggingThreadExceptionEventHandler;

        /// <summary>
        /// Legacy threading semantic data associated with this build manager.
        /// </summary>
        private readonly LegacyThreadingData _legacyThreadingData;

        /// <summary>
        /// The worker queue.
        /// </summary>
        private ActionBlock<Action> _workQueue;

        /// <summary>
        /// A cancellation token source used to cancel graph build scheduling
        /// </summary>
        private CancellationTokenSource _graphSchedulingCancellationSource;

        /// <summary>
        /// Flag indicating we have disposed.
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// When the BuildManager was created.
        /// </summary>
        private DateTime _instantiationTimeUtc;

        private IEnumerable<DeferredBuildMessage> _deferredBuildMessages;
        private Task<ProjectCacheService> _projectCacheService;

#if DEBUG
        /// <summary>
        /// <code>true</code> to wait for a debugger to be attached, otherwise <code>false</code>.
        /// </summary>
        public static bool WaitForDebugger { get; set; }
#endif

        /// <summary>
        /// Creates a new unnamed build manager.
        /// Normally there is only one build manager in a process, and it is the default build manager.
        /// Access it with <see cref="BuildManager.DefaultBuildManager"/>
        /// </summary>
        public BuildManager()
            : this("Unnamed")
        {
        }

        /// <summary>
        /// Creates a new build manager with an arbitrary distinct name.
        /// Normally there is only one build manager in a process, and it is the default build manager.
        /// Access it with <see cref="BuildManager.DefaultBuildManager"/>
        /// </summary>
        public BuildManager(string hostName)
        {
            ErrorUtilities.VerifyThrowArgumentNull(hostName, nameof(hostName));
            _hostName = hostName;
            _buildManagerState = BuildManagerState.Idle;
            _buildSubmissions = new Dictionary<int, BuildSubmission>();
            _graphBuildSubmissions = new Dictionary<int, GraphBuildSubmission>();
            _noActiveSubmissionsEvent = new AutoResetEvent(true);
            _activeNodes = new HashSet<NGen<int>>();
            _noNodesActiveEvent = new AutoResetEvent(true);
            _nodeIdToKnownConfigurations = new Dictionary<NGen<int>, HashSet<NGen<int>>>();
            _unnamedProjectInstanceToNames = new Dictionary<ProjectInstance, string>();
            _nextUnnamedProjectId = 1;
            _componentFactories = new BuildComponentFactoryCollection(this);
            _componentFactories.RegisterDefaultFactories();
            _projectStartedEvents = new Dictionary<int, BuildEventArgs>();

            _projectStartedEventHandler = OnProjectStarted;
            _projectFinishedEventHandler = OnProjectFinished;
            _loggingThreadExceptionEventHandler = OnThreadException;
            _legacyThreadingData = new LegacyThreadingData();
            _instantiationTimeUtc = DateTime.UtcNow;
        }

        /// <summary>
        /// Finalizer
        /// </summary>
        ~BuildManager()
        {
            Dispose(false /* disposing */);
        }

        /// <summary>
        /// Enumeration describing the current state of the build manager.
        /// </summary>
        private enum BuildManagerState
        {
            /// <summary>
            /// This is the default state.  <see cref="BeginBuild(BuildParameters)"/> may be called in this state.  All other methods raise InvalidOperationException
            /// </summary>
            Idle,

            /// <summary>
            /// This is the state the BuildManager is in after <see cref="BeginBuild(BuildParameters)"/> has been called but before <see cref="EndBuild"/> has been called.
            /// <see cref="BuildManager.PendBuildRequest(Microsoft.Build.Execution.BuildRequestData)"/>, <see cref="BuildManager.BuildRequest(Microsoft.Build.Execution.BuildRequestData)"/>, <see cref="BuildManager.PendBuildRequest(GraphBuildRequestData)"/>, <see cref="BuildManager.BuildRequest(GraphBuildRequestData)"/>, and <see cref="BuildManager.EndBuild"/> may be called in this state.
            /// </summary>
            Building,

            /// <summary>
            /// This is the state the BuildManager is in after <see cref="BuildManager.EndBuild"/> has been called but before all existing submissions have completed.
            /// </summary>
            WaitingForBuildToComplete
        }

        /// <summary>
        /// Gets the singleton instance of the Build Manager.
        /// </summary>
        public static BuildManager DefaultBuildManager
        {
            get
            {
                if (s_singletonInstance == null)
                {
                    lock (s_staticSyncLock)
                    {
                        if (s_singletonInstance == null)
                        {
                            s_singletonInstance = new BuildManager("Default");
                        }
                    }
                }

                return s_singletonInstance;
            }
        }

        /// <summary>
        /// Retrieves a hosted<see cref="ISdkResolverService"/> instance for resolving SDKs.
        /// </summary>
        private ISdkResolverService SdkResolverService => (this as IBuildComponentHost).GetComponent(BuildComponentType.SdkResolverService) as ISdkResolverService;

        /// <summary>
        /// Retrieves the logging service associated with a particular build
        /// </summary>
        /// <returns>The logging service.</returns>
        ILoggingService IBuildComponentHost.LoggingService => _componentFactories.GetComponent(BuildComponentType.LoggingService) as ILoggingService;

        /// <summary>
        /// Retrieves the name of the component host.
        /// </summary>
        string IBuildComponentHost.Name => _hostName;

        /// <summary>
        /// Retrieves the build parameters associated with this build.
        /// </summary>
        /// <returns>The build parameters.</returns>
        BuildParameters IBuildComponentHost.BuildParameters => _buildParameters;

        /// <summary>
        /// Retrieves the LegacyThreadingData associated with a particular build manager
        /// </summary>
        LegacyThreadingData IBuildComponentHost.LegacyThreadingData => _legacyThreadingData;

        /// <summary>
        /// <see cref="BuildManager.BeginBuild(BuildParameters,IEnumerable{DeferredBuildMessage})"/>
        /// </summary>
        public readonly struct DeferredBuildMessage
        {
            public MessageImportance Importance { get; }

            public string Text { get; }

            public DeferredBuildMessage(string text, MessageImportance importance)
            {
                Importance = importance;
                Text = text;
            }
        }

        /// <summary>
        /// Prepares the BuildManager to receive build requests.
        /// </summary>
        /// <param name="parameters">The build parameters.  May be null.</param>
        /// <param name="deferredBuildMessages"> Build messages to be logged before the build begins. </param>
        /// <exception cref="InvalidOperationException">Thrown if a build is already in progress.</exception>
        public void BeginBuild(BuildParameters parameters, IEnumerable<DeferredBuildMessage> deferredBuildMessages)
        {
            // deferredBuildMessages cannot be an optional parameter on a single BeginBuild method because it would break binary compatibility.
            _deferredBuildMessages = deferredBuildMessages;
            BeginBuild(parameters);
            _deferredBuildMessages = null;
        }

        /// <summary>
        /// Prepares the BuildManager to receive build requests.
        /// </summary>
        /// <param name="parameters">The build parameters.  May be null.</param>
        /// <exception cref="InvalidOperationException">Thrown if a build is already in progress.</exception>
        public void BeginBuild(BuildParameters parameters)
        {
            lock (_syncLock)
            {
                AttachDebugger();

                // Check for build in progress.
                RequireState(BuildManagerState.Idle, "BuildInProgress");

                MSBuildEventSource.Log.BuildStart();

                if (BuildParameters.DumpOpportunisticInternStats)
                {
                    Strings.EnableDiagnostics();
                }

                _overallBuildSuccess = true;

                // Clone off the build parameters.
                _buildParameters = parameters?.Clone() ?? new BuildParameters();

                // Initialize additional build parameters.
                _buildParameters.BuildId = GetNextBuildId();

                if (_buildParameters.UsesCachedResults())
                {
                    _buildParameters.IsolateProjects = true;
                }

                if (_buildParameters.UsesOutputCache() && string.IsNullOrWhiteSpace(_buildParameters.OutputResultsCacheFile))
                {
                    _buildParameters.OutputResultsCacheFile = FileUtilities.NormalizePath("msbuild-cache");
                }

                // Initialize components.
                _nodeManager = ((IBuildComponentHost)this).GetComponent(BuildComponentType.NodeManager) as INodeManager;

                var loggingService = InitializeLoggingService();

                LogDeferredMessages(loggingService, _deferredBuildMessages);

                InitializeCaches();

                if (_buildParameters.ProjectCacheDescriptor != null)
                {
                    // TODO: Implement cancellation.
                    InitializeProjectCacheService(_buildParameters.ProjectCacheDescriptor, CancellationToken.None);
                }

                _taskHostNodeManager = ((IBuildComponentHost)this).GetComponent(BuildComponentType.TaskHostNodeManager) as INodeManager;
                _scheduler = ((IBuildComponentHost)this).GetComponent(BuildComponentType.Scheduler) as IScheduler;

                _nodeManager.RegisterPacketHandler(NodePacketType.BuildRequestBlocker, BuildRequestBlocker.FactoryForDeserialization, this);
                _nodeManager.RegisterPacketHandler(NodePacketType.BuildRequestConfiguration, BuildRequestConfiguration.FactoryForDeserialization, this);
                _nodeManager.RegisterPacketHandler(NodePacketType.BuildRequestConfigurationResponse, BuildRequestConfigurationResponse.FactoryForDeserialization, this);
                _nodeManager.RegisterPacketHandler(NodePacketType.BuildResult, BuildResult.FactoryForDeserialization, this);
                _nodeManager.RegisterPacketHandler(NodePacketType.NodeShutdown, NodeShutdown.FactoryForDeserialization, this);
                _nodeManager.RegisterPacketHandler(NodePacketType.ResolveSdkRequest, SdkResolverRequest.FactoryForDeserialization, SdkResolverService as INodePacketHandler);
                _nodeManager.RegisterPacketHandler(NodePacketType.ResourceRequest, ResourceRequest.FactoryForDeserialization, this);

                if (_threadException != null)
                {
                    ShutdownLoggingService(loggingService);

                    _threadException.Throw();
                }

                if (_workQueue == null)
                {
                    _workQueue = new ActionBlock<Action>(action => ProcessWorkQueue(action));
                }

                _buildManagerState = BuildManagerState.Building;

                _noActiveSubmissionsEvent.Set();
                _noNodesActiveEvent.Set();
            }

            ILoggingService InitializeLoggingService()
            {
                ILoggingService loggingService = CreateLoggingService(
                    (_buildParameters.Loggers ?? Enumerable.Empty<ILogger>()).Concat(GetDebuggingLoggers()),
                    _buildParameters.ForwardingLoggers,
                    _buildParameters.WarningsAsErrors,
                    _buildParameters.WarningsAsMessages);

                _nodeManager.RegisterPacketHandler(NodePacketType.LogMessage, LogMessagePacket.FactoryForDeserialization, loggingService as INodePacketHandler);

                try
                {
                    loggingService.LogBuildStarted();

                    if (_buildParameters.UsesInputCaches())
                    {
                        loggingService.LogComment(BuildEventContext.Invalid, MessageImportance.Normal, "UsingInputCaches", string.Join(";", _buildParameters.InputResultsCacheFiles));
                    }

                    if (_buildParameters.UsesOutputCache())
                    {
                        loggingService.LogComment(BuildEventContext.Invalid, MessageImportance.Normal, "WritingToOutputCache", _buildParameters.OutputResultsCacheFile);
                    }
                }
                catch (Exception)
                {
                    ShutdownLoggingService(loggingService);
                    throw;
                }

                MSBuildEventSource.Log.BuildStop();

                return loggingService;
            }

            // VS builds discard many msbuild events so attach a binlogger to capture them all.
            IEnumerable<ILogger> GetDebuggingLoggers()
            {
                if (CurrentProcessMatchesDebugName() is false ||
                    Traits.Instance.DebugEngine is false)
                {
                    return Enumerable.Empty<ILogger>();
                }

                var binlogPath = DebugUtils.FindNextAvailableDebugFilePath($"{DebugUtils.ProcessInfoString}_BuildManager_{_hostName}_{GetHashCode()}.binlog");

                var logger = new BinaryLogger { Parameters = binlogPath };

                return new []{ logger };
            }

            void InitializeCaches()
            {
                Debug.Assert(Monitor.IsEntered(_syncLock));

                var usesInputCaches = _buildParameters.UsesInputCaches();

                if (usesInputCaches)
                {
                    ReuseOldCaches(_buildParameters.InputResultsCacheFiles);
                }

                _configCache = ((IBuildComponentHost)this).GetComponent(BuildComponentType.ConfigCache) as IConfigCache;
                _resultsCache = ((IBuildComponentHost)this).GetComponent(BuildComponentType.ResultsCache) as IResultsCache;

                if (!usesInputCaches && (_buildParameters.ResetCaches || _configCache.IsConfigCacheSizeLargerThanThreshold()))
                {
                    ResetCaches();
                }
                else
                {
                    if (!usesInputCaches)
                    {
                        List<int> configurationsCleared = _configCache.ClearNonExplicitlyLoadedConfigurations();

                        if (configurationsCleared != null)
                        {
                            foreach (int configurationId in configurationsCleared)
                            {
                                _resultsCache.ClearResultsForConfiguration(configurationId);
                            }
                        }
                    }

                    foreach (var config in _configCache)
                    {
                        config.ResultsNodeId = Scheduler.InvalidNodeId;
                    }

                    _buildParameters.ProjectRootElementCache.DiscardImplicitReferences();
                }
            }
        }

        private static void AttachDebugger()
        {
            if (Debugger.IsAttached)
            {
                return;
            }

            if (!CurrentProcessMatchesDebugName())
            {
                return;
            }

            switch (Environment.GetEnvironmentVariable("MSBuildDebugBuildManagerOnStart"))
            {
#if FEATURE_DEBUG_LAUNCH
                case "1":
                    Debugger.Launch();
                    break;
#endif
                case "2":
                    // Sometimes easier to attach rather than deal with JIT prompt
                    Process currentProcess = Process.GetCurrentProcess();
                    Console.WriteLine($"Waiting for debugger to attach ({currentProcess.MainModule.FileName} PID {currentProcess.Id}).  Press enter to continue...");
                    Console.ReadLine();
                    break;
            }
        }

        private static bool CurrentProcessMatchesDebugName()
        {
            var processNameToBreakInto = Environment.GetEnvironmentVariable("MSBuildDebugBuildManagerOnStartProcessName");
            var thisProcessMatchesName = string.IsNullOrWhiteSpace(processNameToBreakInto) ||
                                         Process.GetCurrentProcess().ProcessName.Contains(processNameToBreakInto);

            return thisProcessMatchesName;
        }

        private void InitializeProjectCacheService(
            ProjectCacheDescriptor pluginDescriptor,
            CancellationToken cancellationToken)
        {
            Debug.Assert(Monitor.IsEntered(_syncLock));

            if (_projectCacheService != null)
            {
                ErrorUtilities.ThrowInternalError("Only one project cache plugin may be set on the BuildManager during a begin / end build session");
            }

            LogMessage(ResourceUtilities.FormatResourceStringIgnoreCodeAndKeyword("LoadingProjectCachePlugin", pluginDescriptor.GetDetailedDescription()));

            _projectCacheService = ProjectCacheService.FromDescriptorAsync(
                pluginDescriptor,
                this,
                ((IBuildComponentHost) this).LoggingService,
                cancellationToken);
        }

        /// <summary>
        /// Cancels all outstanding submissions asynchronously.
        /// </summary>
        public void CancelAllSubmissions()
        {
            CancelAllSubmissions(true);
        }

        private void CancelAllSubmissions(bool async)
        {
            var parentThreadCulture = _buildParameters != null
                ? _buildParameters.Culture
                : CultureInfo.CurrentCulture;
            var parentThreadUICulture = _buildParameters != null
                ? _buildParameters.UICulture
                : CultureInfo.CurrentUICulture;

            void Callback(object state)
            {
                lock (_syncLock)
                {
                    if (_shuttingDown)
                    {
                        return;
                    }

                    // If we are Idle, obviously there is nothing to cancel.  If we are waiting for the build to end, then presumably all requests have already completed
                    // and there is nothing left to cancel.  Putting this here eliminates the possibility of us racing with EndBuild to access the nodeManager before
                    // EndBuild sets it to null.
                    if (_buildManagerState != BuildManagerState.Building)
                    {
                        return;
                    }

                    _overallBuildSuccess = false;

                    foreach (BuildSubmission submission in _buildSubmissions.Values)
                    {
                        if (submission.BuildRequest != null)
                        {
                            BuildResult result = new BuildResult(submission.BuildRequest, new BuildAbortedException());
                            _resultsCache.AddResult(result);
                            submission.CompleteResults(result);
                        }
                    }

                    foreach (GraphBuildSubmission submission in _graphBuildSubmissions.Values)
                    {
                        if (submission.IsStarted)
                        {
                            submission.CompleteResults(new GraphBuildResult(submission.SubmissionId, new BuildAbortedException()));
                        }
                    }

                    ShutdownConnectedNodes(true /* abort */);
                    CheckForActiveNodesAndCleanUpSubmissions();
                }
            }

            ThreadPoolExtensions.QueueThreadPoolWorkItemWithCulture(Callback, parentThreadCulture, parentThreadUICulture);
        }

        /// <summary>
        /// Clears out all of the cached information.
        /// </summary>
        public void ResetCaches()
        {
            lock (_syncLock)
            {
                ErrorIfState(BuildManagerState.WaitingForBuildToComplete, "WaitingForEndOfBuild");
                ErrorIfState(BuildManagerState.Building, "BuildInProgress");

                _configCache = ((IBuildComponentHost)this).GetComponent(BuildComponentType.ConfigCache) as IConfigCache;
                _resultsCache = ((IBuildComponentHost)this).GetComponent(BuildComponentType.ResultsCache) as IResultsCache;
                _resultsCache.ClearResults();

                // This call clears out the directory.
                _configCache.ClearConfigurations();

                _buildParameters?.ProjectRootElementCache.DiscardImplicitReferences();
            }
        }

        /// <summary>
        /// This methods requests the BuildManager to find a matching ProjectInstance in its cache of previously-built projects.
        /// If none exist, a new instance will be created from the specified project.
        /// </summary>
        /// <param name="project">The Project for which an instance should be retrieved.</param>
        /// <returns>The instance.</returns>
        public ProjectInstance GetProjectInstanceForBuild(Project project)
        {
            lock (_syncLock)
            {
                _configCache = ((IBuildComponentHost)this).GetComponent(BuildComponentType.ConfigCache) as IConfigCache;
                BuildRequestConfiguration configuration = _configCache.GetMatchingConfiguration(
                    new ConfigurationMetadata(project),
                    (config, loadProject) => CreateConfiguration(project, config),
                    loadProject: true);
                ErrorUtilities.VerifyThrow(configuration.Project != null, "Configuration should have been loaded.");
                return configuration.Project;
            }
        }

        /// <summary>
        /// Submits a build request to the current build but does not start it immediately.  Allows the user to
        /// perform asynchronous execution or access the submission ID prior to executing the request.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if StartBuild has not been called or if EndBuild has been called.</exception>
        public BuildSubmission PendBuildRequest(BuildRequestData requestData)
        {
            lock (_syncLock)
            {
                ErrorUtilities.VerifyThrowArgumentNull(requestData, nameof(requestData));
                ErrorIfState(BuildManagerState.WaitingForBuildToComplete, "WaitingForEndOfBuild");
                ErrorIfState(BuildManagerState.Idle, "NoBuildInProgress");
                VerifyStateInternal(BuildManagerState.Building);

                var newSubmission = new BuildSubmission(this, GetNextSubmissionId(), requestData, _buildParameters.LegacyThreadingSemantics);
                _buildSubmissions.Add(newSubmission.SubmissionId, newSubmission);
                _noActiveSubmissionsEvent.Reset();
                return newSubmission;
            }
        }

        /// <summary>
        /// Submits a graph build request to the current build but does not start it immediately.  Allows the user to
        /// perform asynchronous execution or access the submission ID prior to executing the request.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if StartBuild has not been called or if EndBuild has been called.</exception>
        public GraphBuildSubmission PendBuildRequest(GraphBuildRequestData requestData)
        {
            lock (_syncLock)
            {
                ErrorUtilities.VerifyThrowArgumentNull(requestData, nameof(requestData));
                ErrorIfState(BuildManagerState.WaitingForBuildToComplete, "WaitingForEndOfBuild");
                ErrorIfState(BuildManagerState.Idle, "NoBuildInProgress");
                VerifyStateInternal(BuildManagerState.Building);

                var newSubmission = new GraphBuildSubmission(this, GetNextSubmissionId(), requestData);
                _graphBuildSubmissions.Add(newSubmission.SubmissionId, newSubmission);
                _noActiveSubmissionsEvent.Reset();
                return newSubmission;
            }
        }

        /// <summary>
        /// Convenience method. Submits a build request and blocks until the results are available.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if StartBuild has not been called or if EndBuild has been called.</exception>
        public BuildResult BuildRequest(BuildRequestData requestData)
        {
            BuildSubmission submission = PendBuildRequest(requestData);
            BuildResult result = submission.Execute();

            return result;
        }

        /// <summary>
        /// Convenience method. Submits a graph build request and blocks until the results are available.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if StartBuild has not been called or if EndBuild has been called.</exception>
        public GraphBuildResult BuildRequest(GraphBuildRequestData requestData) => PendBuildRequest(requestData).Execute();

        /// <summary>
        /// Signals that no more build requests are expected (or allowed) and the BuildManager may clean up.
        /// </summary>
        /// <remarks>
        /// This call blocks until all currently pending requests are complete.
        /// </remarks>
        /// <exception cref="InvalidOperationException">Thrown if there is no build in progress.</exception>
        public void EndBuild()
        {
            lock (_syncLock)
            {
                ErrorIfState(BuildManagerState.WaitingForBuildToComplete, "WaitingForEndOfBuild");
                ErrorIfState(BuildManagerState.Idle, "NoBuildInProgress");
                VerifyStateInternal(BuildManagerState.Building);

                _buildManagerState = BuildManagerState.WaitingForBuildToComplete;
            }

            var exceptionsThrownInEndBuild = false;

            try
            {
                lock (_syncLock)
                {
                    // If there are any submissions which never started, remove them now.
                    var submissionsToCheck = new List<BuildSubmission>(_buildSubmissions.Values);
                    foreach (BuildSubmission submission in submissionsToCheck)
                    {
                        CheckSubmissionCompletenessAndRemove(submission);
                    }

                    var graphSubmissionsToCheck = new List<GraphBuildSubmission>(_graphBuildSubmissions.Values);
                    foreach (GraphBuildSubmission submission in graphSubmissionsToCheck)
                    {
                        CheckSubmissionCompletenessAndRemove(submission);
                    }
                }

                _noActiveSubmissionsEvent.WaitOne();
                ShutdownConnectedNodes(false /* normal termination */);
                _noNodesActiveEvent.WaitOne();

                // Wait for all of the actions in the work queue to drain.  Wait() could throw here if there was an unhandled exception
                // in the work queue, but the top level exception handler there should catch everything and have forwarded it to the
                // OnThreadException method in this class already.
                _workQueue.Complete();

                // Stop the graph scheduling thread(s)
                _graphSchedulingCancellationSource?.Cancel();

                var projectCacheShutdown = _projectCacheService?.Result.ShutDown();

                ErrorUtilities.VerifyThrow(_buildSubmissions.Count == 0 && _graphBuildSubmissions.Count == 0, "All submissions not yet complete.");
                ErrorUtilities.VerifyThrow(_activeNodes.Count == 0, "All nodes not yet shut down.");

                if (_buildParameters.UsesOutputCache())
                {
                    SerializeCaches();
                }

                projectCacheShutdown?.Wait();

#if DEBUG
                if (_projectStartedEvents.Count != 0)
                {
                    bool allMismatchedProjectStartedEventsDueToLoggerErrors = true;

                    foreach (KeyValuePair<int, BuildEventArgs> projectStartedEvent in _projectStartedEvents)
                    {
                        BuildResult result = _resultsCache.GetResultsForConfiguration(projectStartedEvent.Value.BuildEventContext.ProjectInstanceId);

                        // It's valid to have a mismatched project started event IFF that particular
                        // project had some sort of unhandled exception.  If there is no result, we
                        // can't tell for sure one way or the other, so err on the side of throwing
                        // the assert, but if there is a result, make sure that it actually has an
                        // exception attached.
                        if (result?.Exception == null)
                        {
                            allMismatchedProjectStartedEventsDueToLoggerErrors = false;
                            break;
                        }
                    }

                    Debug.Assert(allMismatchedProjectStartedEventsDueToLoggerErrors, "There was a mismatched project started event not caused by an exception result");
                }
#endif

                if (_buildParameters.DiscardBuildResults)
                {
                    _resultsCache.ClearResults();
                }
            }
            catch (Exception e)
            {
                exceptionsThrownInEndBuild = true;

                if (e is AggregateException ae && ae.InnerExceptions.Count == 1)
                {
                    e = ae.InnerExceptions.First();
                }

                throw e;
            }
            finally
            {
                try
                {
                    ILoggingService loggingService = ((IBuildComponentHost)this).LoggingService;

                    if (loggingService != null)
                    {
                        // Override the build success if the user specified /warnaserror and any errors were logged outside of a build submission.
                        if (exceptionsThrownInEndBuild ||
                            _overallBuildSuccess && loggingService.HasBuildSubmissionLoggedErrors(BuildEventContext.InvalidSubmissionId))
                        {
                            _overallBuildSuccess = false;
                        }

                        loggingService.LogBuildFinished(_overallBuildSuccess);
                    }

                    ShutdownLoggingService(loggingService);
                }
                finally
                {
                    if (_buildParameters.LegacyThreadingSemantics)
                    {
                        _legacyThreadingData.MainThreadSubmissionId = -1;
                    }

                    Reset();
                    _buildManagerState = BuildManagerState.Idle;

                    _threadException?.Throw();

                    if (BuildParameters.DumpOpportunisticInternStats)
                    {
                        Console.WriteLine(Strings.CreateDiagnosticReport());
                    }
                }
            }

            void SerializeCaches()
            {
                var errorMessage = CacheSerialization.SerializeCaches(_configCache, _resultsCache, _buildParameters.OutputResultsCacheFile);

                if (!string.IsNullOrEmpty(errorMessage))
                {
                    LogErrorAndShutdown(errorMessage);
                }
            }
        }

        /// <summary>
        /// Convenience method.  Submits a lone build request and blocks until results are available.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if a build is already in progress.</exception>
        public BuildResult Build(BuildParameters parameters, BuildRequestData requestData)
        {
            BuildResult result;
            BeginBuild(parameters);
            try
            {
                result = BuildRequest(requestData);
                if (result.Exception == null && _threadException != null)
                {
                    result.Exception = _threadException.SourceException;
                    _threadException = null;
                }
            }
            finally
            {
                EndBuild();
            }

            return result;
        }

        /// <summary>
        /// Convenience method.  Submits a lone graph build request and blocks until results are available.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if a build is already in progress.</exception>
        public GraphBuildResult Build(BuildParameters parameters, GraphBuildRequestData requestData)
        {
            GraphBuildResult result;
            BeginBuild(parameters);
            try
            {
                result = BuildRequest(requestData);
                if (result.Exception == null && _threadException != null)
                {
                    result.Exception = _threadException.SourceException;
                    _threadException = null;
                }
            }
            finally
            {
                EndBuild();
            }

            return result;
        }

        /// <summary>
        /// Shuts down all idle MSBuild nodes on the machine
        /// </summary>
        public void ShutdownAllNodes()
        {
            if (_nodeManager == null)
            {
                _nodeManager = ((IBuildComponentHost)this).GetComponent(BuildComponentType.NodeManager) as INodeManager;
            }

            _nodeManager.ShutdownAllNodes();
        }

        /// <summary>
        /// Dispose of the build manager.
        /// </summary>
        public void Dispose()
        {
            Dispose(true /* disposing */);
            GC.SuppressFinalize(this);
        }

        #region INodePacketHandler Members

        /// <summary>
        /// This method is invoked by the NodePacketRouter when a packet is received and is intended for
        /// this recipient.
        /// </summary>
        /// <param name="node">The node from which the packet was received.</param>
        /// <param name="packet">The packet.</param>
        void INodePacketHandler.PacketReceived(int node, INodePacket packet)
        {
            _workQueue.Post(() => ProcessPacket(node, packet));
        }

        #endregion

        #region IBuildComponentHost Members

        /// <summary>
        /// Registers a factory which will be used to create the necessary components of the build
        /// system.
        /// </summary>
        /// <param name="componentType">The type which is created by this factory.</param>
        /// <param name="factory">The factory to be registered.</param>
        /// <remarks>
        /// It is not necessary to register any factories.  If no factory is registered for a specific kind
        /// of object, the system will use the default factory.
        /// </remarks>
        void IBuildComponentHost.RegisterFactory(BuildComponentType componentType, BuildComponentFactoryDelegate factory)
        {
            _componentFactories.ReplaceFactory(componentType, factory);
        }

        /// <summary>
        /// Gets an instance of the specified component type from the host.
        /// </summary>
        /// <param name="type">The component type to be retrieved</param>
        /// <returns>The component</returns>
        IBuildComponent IBuildComponentHost.GetComponent(BuildComponentType type)
        {
            return _componentFactories.GetComponent(type);
        }

        #endregion

        /// <summary>
        /// This method adds the request in the specified submission to the set of requests being handled by the scheduler.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "Standard ExpectedException pattern used")]
        [SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling", Justification = "Complex class might need refactoring to separate scheduling elements from submission elements.")]
        internal void ExecuteSubmission(BuildSubmission submission, bool allowMainThreadBuild)
        {
            ErrorUtilities.VerifyThrowArgumentNull(submission, nameof(submission));
            ErrorUtilities.VerifyThrow(!submission.IsCompleted, "Submission already complete.");

            lock (_syncLock)
            {
                ProjectInstance projectInstance = submission.BuildRequestData.ProjectInstance;
                if (projectInstance != null)
                {
                    if (_acquiredProjectRootElementCacheFromProjectInstance)
                    {
                        ErrorUtilities.VerifyThrowArgument(
                            _buildParameters.ProjectRootElementCache == projectInstance.ProjectRootElementCache,
                            "OM_BuildSubmissionsMultipleProjectCollections");
                    }
                    else
                    {
                        _buildParameters.ProjectRootElementCache = projectInstance.ProjectRootElementCache;
                        _acquiredProjectRootElementCacheFromProjectInstance = true;
                    }
                }
                else if (_buildParameters.ProjectRootElementCache == null)
                {
                    // Create our own cache; if we subsequently get a build submission with a project instance attached,
                    // we'll dump our cache and use that one.
                    _buildParameters.ProjectRootElementCache =
                        new ProjectRootElementCache(false /* do not automatically reload from disk */);
                }

                VerifyStateInternal(BuildManagerState.Building);

                BuildRequestConfiguration resolvedConfiguration = null;
                try
                {
                    // If we have an unnamed project, assign it a temporary name.
                    if (string.IsNullOrEmpty(submission.BuildRequestData.ProjectFullPath))
                    {
                        ErrorUtilities.VerifyThrow(
                            submission.BuildRequestData.ProjectInstance != null,
                            "Unexpected null path for a submission with no ProjectInstance.");

                        // If we have already named this instance when it was submitted previously during this build, use the same
                        // name so that we get the same configuration (and thus don't cause it to rebuild.)
                        if (!_unnamedProjectInstanceToNames.TryGetValue(submission.BuildRequestData.ProjectInstance, out var tempName))
                        {
                            tempName = "Unnamed_" + _nextUnnamedProjectId++;
                            _unnamedProjectInstanceToNames[submission.BuildRequestData.ProjectInstance] = tempName;
                        }

                        submission.BuildRequestData.ProjectFullPath = Path.Combine(
                            submission.BuildRequestData.ProjectInstance.GetProperty(ReservedPropertyNames.projectDirectory).EvaluatedValue,
                            tempName);
                    }

                    // Create/Retrieve a configuration for each request
                    var buildRequestConfiguration = new BuildRequestConfiguration(submission.BuildRequestData, _buildParameters.DefaultToolsVersion);
                    var matchingConfiguration = _configCache.GetMatchingConfiguration(buildRequestConfiguration);
                    resolvedConfiguration = ResolveConfiguration(
                        buildRequestConfiguration,
                        matchingConfiguration,
                        submission.BuildRequestData.Flags.HasFlag(BuildRequestDataFlags.ReplaceExistingProjectInstance));

                    resolvedConfiguration.ExplicitlyLoaded = true;

                    if (_shuttingDown)
                    {
                        // We were already canceled!
                        AddBuildRequestToSubmission(submission, resolvedConfiguration.ConfigurationId);
                        BuildResult result = new BuildResult(submission.BuildRequest, new BuildAbortedException());
                        submission.CompleteResults(result);
                        submission.CompleteLogging(true);
                        CheckSubmissionCompletenessAndRemove(submission);
                        return;
                    }

                    if (ProjectCacheIsPresent())
                    {
                        IssueCacheRequestForBuildSubmission(new CacheRequest(submission, resolvedConfiguration));
                    }
                    else
                    {
                        AddBuildRequestToSubmission(submission, resolvedConfiguration.ConfigurationId);
                        IssueBuildRequestForBuildSubmission(submission, allowMainThreadBuild);
                    }
                }
                catch (ProjectCacheException ex)
                {
                    ErrorUtilities.VerifyThrow(resolvedConfiguration is not null, "Cannot call project cache without having ");
                    CompleteSubmissionWithException(submission, resolvedConfiguration, ex);
                }
                catch (Exception ex) when (!ExceptionHandling.IsCriticalException(ex))
                {
                    if (resolvedConfiguration is not null)
                    {
                        CompleteSubmissionWithException(submission, resolvedConfiguration, ex);
                    }
                    else
                    {
                        HandleSubmissionException(submission, ex);
                        throw;
                    }
                }
            }
        }

        bool ProjectCacheIsPresent()
        {
            // TODO: remove after we change VS to set the cache descriptor via build parameters.
            // TODO: no need to access the service when there's no design time builds.
            var projectCacheService = GetProjectCacheService();

            if (projectCacheService != null && projectCacheService.DesignTimeBuildsDetected)
            {
                return false;
            }

            return
                projectCacheService != null ||
                _buildParameters.ProjectCacheDescriptor != null ||
                ProjectCachePresentViaVisualStudioWorkaround();
        }

        private static bool ProjectCachePresentViaVisualStudioWorkaround()
        {
            return BuildEnvironmentHelper.Instance.RunningInVisualStudio && ProjectCacheItems.Count > 0;
        }

        // Cache requests on configuration N do not block future build submissions depending on configuration N.
        // It is assumed that the higher level build orchestrator (static graph scheduler, VS, quickbuild) submits a
        // project build request only when its references have finished building.
        private void IssueCacheRequestForBuildSubmission(CacheRequest cacheRequest)
        {
            Debug.Assert(Monitor.IsEntered(_syncLock));

            _workQueue.Post(() =>
            {
                try
                {
                    var projectCacheService = GetProjectCacheService();

                    ErrorUtilities.VerifyThrow(
                        projectCacheService != null,
                        "This method should not get called if there's no project cache.");

                    ErrorUtilities.VerifyThrow(
                        !projectCacheService.DesignTimeBuildsDetected,
                        "This method should not get called if design time builds are detected.");

                    projectCacheService.PostCacheRequest(cacheRequest);
                }
                catch (Exception e)
                {
                    CompleteSubmissionWithException(cacheRequest.Submission, cacheRequest.Configuration, e);
                }
            });
        }

        private ProjectCacheService GetProjectCacheService()
        {
            // TODO: remove after we change VS to set the cache descriptor via build parameters.
            AutomaticallyDetectAndInstantiateProjectCacheServiceForVisualStudio();

            try
            {
                return _projectCacheService?.Result;
            }
            catch(Exception ex)
            {
                if (ex is AggregateException ae && ae.InnerExceptions.Count == 1)
                {
                    ex = ae.InnerExceptions.First();
                }

                // These are exceptions thrown during project cache startup (assembly load issues or cache BeginBuild exceptions).
                // Set to null so that EndBuild does not try to shut it down and thus rethrow the exception.
                Interlocked.Exchange(ref _projectCacheService, null);
                throw ex;
            }
        }

        private void AutomaticallyDetectAndInstantiateProjectCacheServiceForVisualStudio()
        {
            if (BuildEnvironmentHelper.Instance.RunningInVisualStudio &&
                ProjectCacheItems.Count > 0 &&
                _projectCacheService == null &&
                _buildParameters.ProjectCacheDescriptor == null)
            {
                lock (_syncLock)
                {
                    if (_projectCacheService != null)
                    {
                        return;
                    }

                    if (ProjectCacheItems.Count != 1)
                    {
                        ProjectCacheException.ThrowForMSBuildIssueWithTheProjectCache(
                            "OnlyOneCachePluginMustBeSpecified",
                            string.Join("; ", ProjectCacheItems.Values.Select(c => c.PluginPath)));
                    }

                    var projectCacheItem = ProjectCacheItems.First().Value;

                    InitializeProjectCacheService(ProjectCacheDescriptor.FromVisualStudioWorkaround(projectCacheItem), CancellationToken.None);
                }
            }
        }

        /// <summary>
        /// This method adds the graph build request in the specified submission to the set of requests being handled by the scheduler.
        /// </summary>
        internal void ExecuteSubmission(GraphBuildSubmission submission)
        {
            lock (_syncLock)
            {
                VerifyStateInternal(BuildManagerState.Building);

                try
                {
                    submission.IsStarted = true;

                    if (_shuttingDown)
                    {
                        // We were already canceled!
                        var result = new GraphBuildResult(submission.SubmissionId, new BuildAbortedException());
                        submission.CompleteResults(result);
                        CheckSubmissionCompletenessAndRemove(submission);
                        return;
                    }

                    // Lazily create a cancellation token source to be used for all graph scheduling tasks running from this build manager.
                    if (_graphSchedulingCancellationSource == null)
                    {
                        _graphSchedulingCancellationSource = new CancellationTokenSource();
                    }

                    // Do the scheduling in a separate thread to unblock the calling thread
                    Task.Factory.StartNew(
                        () =>
                        {
                            try
                            {
                                ExecuteGraphBuildScheduler(submission);
                            }
                            catch (Exception ex) when (!ExceptionHandling.IsCriticalException(ex))
                            {
                                HandleSubmissionException(submission, ex);
                            }
                        },
                        _graphSchedulingCancellationSource.Token,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Default);
                }
                catch (Exception ex) when (!ExceptionHandling.IsCriticalException(ex))
                {
                    HandleSubmissionException(submission, ex);
                    throw;
                }
            }
        }

        private void LoadSubmissionProjectIntoConfiguration(BuildSubmission submission, BuildRequestConfiguration config)
        {
            if (!config.IsLoaded)
            {
                config.LoadProjectIntoConfiguration(
                    this,
                    submission.BuildRequestData.Flags,
                    submission.SubmissionId,
                    Scheduler.InProcNodeId
                );

                // If we're taking the time to evaluate, avoid having other nodes to repeat the same evaluation.
                // Based on the assumption that ProjectInstance serialization is faster than evaluating from scratch.
                config.Project.TranslateEntireState = true;
            }
        }

        /// <summary>
        /// Creates the traversal and metaproject instances necessary to represent the solution and populates new configurations with them.
        /// </summary>
        private void LoadSolutionIntoConfiguration(BuildRequestConfiguration config, BuildRequest request)
        {
            Debug.Assert(Monitor.IsEntered(_syncLock));

            if (config.IsLoaded)
            {
                // We've already processed it, nothing to do.
                return;
            }

            ErrorUtilities.VerifyThrow(FileUtilities.IsSolutionFilename(config.ProjectFullPath), "{0} is not a solution", config.ProjectFullPath);
            var instances = ProjectInstance.LoadSolutionForBuild(
                config.ProjectFullPath,
                config.GlobalProperties,
                config.ExplicitToolsVersionSpecified ? config.ToolsVersion : null,
                _buildParameters,
                ((IBuildComponentHost) this).LoggingService,
                request.BuildEventContext,
                false /* loaded by solution parser*/,
                config.TargetNames,
                SdkResolverService,
                request.SubmissionId);

            // The first instance is the traversal project, which goes into this configuration
            config.Project = instances[0];

            // The remaining instances are the metaprojects which describe the dependencies for each project as well as how to invoke the project itself.
            for (int i = 1; i < instances.Length; i++)
            {
                // Create new configurations for each of these if they don't already exist.  That could happen if there are multiple
                // solutions in this build which refer to the same project, in which case we want them to refer to the same
                // metaproject as well.
                var newConfig = new BuildRequestConfiguration(
                    GetNewConfigurationId(),
                    instances[i]) { ExplicitlyLoaded = config.ExplicitlyLoaded };
                if (_configCache.GetMatchingConfiguration(newConfig) == null)
                {
                    _configCache.AddConfiguration(newConfig);
                }
            }
        }

        /// <summary>
        /// Gets the next build id.
        /// </summary>
        private static int GetNextBuildId()
        {
            return Interlocked.Increment(ref s_nextBuildId);
        }

        /// <summary>
        /// Creates and optionally populates a new configuration.
        /// </summary>
        private BuildRequestConfiguration CreateConfiguration(Project project, BuildRequestConfiguration existingConfiguration)
        {
            ProjectInstance newInstance = project.CreateProjectInstance();

            if (existingConfiguration == null)
            {
                existingConfiguration = new BuildRequestConfiguration(GetNewConfigurationId(), new BuildRequestData(newInstance, Array.Empty<string>()), null /* use the instance's tools version */);
            }
            else
            {
                existingConfiguration.Project = newInstance;
            }

            return existingConfiguration;
        }

        /// <summary>
        /// Processes the next action in the work queue.
        /// </summary>
        /// <param name="action">The action to be processed.</param>
        private void ProcessWorkQueue(Action action)
        {
            try
            {
                var oldCulture = CultureInfo.CurrentCulture;
                var oldUICulture = CultureInfo.CurrentUICulture;

                try
                {
                    if (!Equals(CultureInfo.CurrentCulture, _buildParameters.Culture))
                    {
                        CultureInfo.CurrentCulture = _buildParameters.Culture;
                    }

                    if (!Equals(CultureInfo.CurrentUICulture, _buildParameters.UICulture))
                    {
                        CultureInfo.CurrentUICulture = _buildParameters.UICulture;
                    }

                    action();
                }
                catch (Exception ex)
                {
                    // These need to go to the main thread exception handler.  We can't rethrow here because that will just silently stop the
                    // action block.  Instead, send them over to the main handler for the BuildManager.
                    OnThreadException(ex);
                }
                finally
                {
                    // Set the culture back to the original one so that if something else reuses this thread then it will not have a culture which it was not expecting.
                    if (!Equals(CultureInfo.CurrentCulture, oldCulture))
                    {
                        CultureInfo.CurrentCulture = oldCulture;
                    }

                    if (!Equals(CultureInfo.CurrentUICulture, oldUICulture))
                    {
                        CultureInfo.CurrentUICulture = oldUICulture;
                    }
                }
            }
            catch (Exception e)
            {
                // On the off chance we get an exception from our exception handler (oh, the irony!), we want to know about it (and still not kill this block
                // which could lead to a somewhat mysterious hang.)
                ExceptionHandling.DumpExceptionToFile(e);
            }
        }

        /// <summary>
        /// Processes a packet
        /// </summary>
        private void ProcessPacket(int node, INodePacket packet)
        {
            lock (_syncLock)
            {
                if (_shuttingDown && packet.Type != NodePacketType.NodeShutdown)
                {
                    // Console.WriteLine("Discarding packet {0} from node {1} because we are shutting down.", packet.Type, node);
                    return;
                }

                switch (packet.Type)
                {
                    case NodePacketType.BuildRequestBlocker:
                        BuildRequestBlocker blocker = ExpectPacketType<BuildRequestBlocker>(packet, NodePacketType.BuildRequestBlocker);
                        HandleNewRequest(node, blocker);
                        break;

                    case NodePacketType.BuildRequestConfiguration:
                        BuildRequestConfiguration requestConfiguration = ExpectPacketType<BuildRequestConfiguration>(packet, NodePacketType.BuildRequestConfiguration);
                        HandleConfigurationRequest(node, requestConfiguration);
                        break;

                    case NodePacketType.BuildResult:
                        BuildResult result = ExpectPacketType<BuildResult>(packet, NodePacketType.BuildResult);
                        HandleResult(node, result);
                        break;

                    case NodePacketType.ResourceRequest:
                        ResourceRequest request = ExpectPacketType<ResourceRequest>(packet, NodePacketType.ResourceRequest);
                        HandleResourceRequest(node, request);
                        break;

                    case NodePacketType.NodeShutdown:
                        // Remove the node from the list of active nodes.  When they are all done, we have shut down fully
                        NodeShutdown shutdownPacket = ExpectPacketType<NodeShutdown>(packet, NodePacketType.NodeShutdown);
                        HandleNodeShutdown(node, shutdownPacket);
                        break;

                    default:
                        ErrorUtilities.ThrowInternalError("Unexpected packet received by BuildManager: {0}", packet.Type);
                        break;
                }
            }
        }

        private void CompleteSubmissionWithException(BuildSubmission submission, BuildRequestConfiguration configuration, Exception exception)
        {
            lock (_syncLock)
            {
                if (submission.BuildRequest is null)
                {
                    AddBuildRequestToSubmission(submission, configuration.ConfigurationId);
                }

                HandleSubmissionException(submission, exception);
            }
        }

        /// <summary>
        /// Deals with exceptions that may be thrown when handling a submission.
        /// </summary>
        private void HandleSubmissionException(BuildSubmission submission, Exception ex)
        {
            if (ex is AggregateException ae && ae.InnerExceptions.Count == 1)
            {
                ex = ae.InnerExceptions.First();
            }
            if (ex is InvalidProjectFileException projectException)
            {
                if (!projectException.HasBeenLogged)
                {
                    BuildEventContext buildEventContext = new BuildEventContext(submission.SubmissionId, 1, BuildEventContext.InvalidProjectInstanceId, BuildEventContext.InvalidProjectContextId, BuildEventContext.InvalidTargetId, BuildEventContext.InvalidTaskId);
                    ((IBuildComponentHost)this).LoggingService.LogInvalidProjectFileError(buildEventContext, projectException);
                    projectException.HasBeenLogged = true;
                }
            }

            lock(_syncLock)
            {
                // BuildRequest may be null if the submission fails early on.
                if (submission.BuildRequest != null)
                {
                    var result = new BuildResult(submission.BuildRequest, ex);
                    submission.CompleteResults(result);
                    submission.CompleteLogging(true);
                }

                _overallBuildSuccess = false;
                CheckSubmissionCompletenessAndRemove(submission);
            }
        }

        /// <summary>
        /// Deals with exceptions that may be thrown as a result of ExecuteSubmission.
        /// </summary>
        private void HandleSubmissionException(GraphBuildSubmission submission, Exception ex)
        {
            if (ex is InvalidProjectFileException projectException)
            {
                if (!projectException.HasBeenLogged)
                {
                    BuildEventContext buildEventContext = new BuildEventContext(submission.SubmissionId, 1, BuildEventContext.InvalidProjectInstanceId, BuildEventContext.InvalidProjectContextId, BuildEventContext.InvalidTargetId, BuildEventContext.InvalidTaskId);
                    ((IBuildComponentHost)this).LoggingService.LogInvalidProjectFileError(buildEventContext, projectException);
                    projectException.HasBeenLogged = true;
                }
            }

            ex = ex is AggregateException ae && ae.InnerExceptions.Count == 1
                ? ae.InnerExceptions.First()
                : ex;

            lock (_syncLock)
            {
                if (submission.IsStarted)
                {
                    submission.CompleteResults(new GraphBuildResult(submission.SubmissionId, ex));
                }

                _overallBuildSuccess = false;
                CheckSubmissionCompletenessAndRemove(submission);
            }
        }

        private static void AddBuildRequestToSubmission(BuildSubmission submission, int configurationId)
        {
            submission.BuildRequest = new BuildRequest(
                submission.SubmissionId,
                BackEnd.BuildRequest.InvalidNodeRequestId,
                configurationId,
                submission.BuildRequestData.TargetNames,
                submission.BuildRequestData.HostServices,
                BuildEventContext.Invalid,
                null,
                submission.BuildRequestData.Flags,
                submission.BuildRequestData.RequestedProjectState);
        }

        private static void AddProxyBuildRequestToSubmission(BuildSubmission submission, int configurationId, ProxyTargets proxyTargets)
        {
            submission.BuildRequest = new BuildRequest(
                submission.SubmissionId,
                BackEnd.BuildRequest.InvalidNodeRequestId,
                configurationId,
                proxyTargets,
                submission.BuildRequestData.HostServices,
                submission.BuildRequestData.Flags,
                submission.BuildRequestData.RequestedProjectState);
        }

        /// <summary>
        /// The submission is a top level build request entering the BuildManager.
        /// Sends the request to the scheduler with optional legacy threading semantics behavior.
        /// </summary>
        private void IssueBuildRequestForBuildSubmission(BuildSubmission submission, bool allowMainThreadBuild = false)
        {
            _workQueue.Post(
                () =>
                {
                    try
                    {
                        IssueBuildSubmissionToSchedulerImpl(submission, allowMainThreadBuild);
                    }
                    catch (BuildAbortedException bae)
                    {
                        // We were canceled before we got issued by the work queue.
                        var result = new BuildResult(submission.BuildRequest, bae);
                        submission.CompleteResults(result);
                        submission.CompleteLogging(true);
                        CheckSubmissionCompletenessAndRemove(submission);
                    }
                    catch (Exception ex) when (!ExceptionHandling.IsCriticalException(ex))
                    {
                        HandleSubmissionException(submission, ex);
                    }
                });

            void IssueBuildSubmissionToSchedulerImpl(BuildSubmission submission, bool allowMainThreadBuild)
            {
                var resetMainThreadOnFailure = false;
                try
                {
                    lock (_syncLock)
                    {
                        if (_shuttingDown)
                        {
                            throw new BuildAbortedException();
                        }

                        if (allowMainThreadBuild && _buildParameters.LegacyThreadingSemantics)
                        {
                            if (_legacyThreadingData.MainThreadSubmissionId == -1)
                            {
                                resetMainThreadOnFailure = true;
                                _legacyThreadingData.MainThreadSubmissionId = submission.SubmissionId;
                            }
                        }

                        BuildRequestBlocker blocker = new BuildRequestBlocker(-1, Array.Empty<string>(), new[] {submission.BuildRequest});

                        HandleNewRequest(Scheduler.VirtualNode, blocker);
                    }
                }
                catch (Exception ex) when (!ExceptionHandling.IsCriticalException(ex))
                {
                    var projectException = ex as InvalidProjectFileException;
                    if (projectException != null)
                    {
                        if (!projectException.HasBeenLogged)
                        {
                            BuildEventContext projectBuildEventContext = new BuildEventContext(submission.SubmissionId, 1, BuildEventContext.InvalidProjectInstanceId, BuildEventContext.InvalidProjectContextId, BuildEventContext.InvalidTargetId, BuildEventContext.InvalidTaskId);
                            ((IBuildComponentHost)this).LoggingService.LogInvalidProjectFileError(projectBuildEventContext, projectException);
                            projectException.HasBeenLogged = true;
                        }
                    }
                    else if ((ex is BuildAbortedException) || ExceptionHandling.NotExpectedException(ex))
                    {
                        throw;
                    }

                    lock (_syncLock)
                    {

                        if (resetMainThreadOnFailure)
                        {
                            _legacyThreadingData.MainThreadSubmissionId = -1;
                        }

                        if (projectException == null)
                        {
                            var buildEventContext = new BuildEventContext(submission.SubmissionId, 1, BuildEventContext.InvalidProjectInstanceId, BuildEventContext.InvalidProjectContextId, BuildEventContext.InvalidTargetId, BuildEventContext.InvalidTaskId);
                            ((IBuildComponentHost)this).LoggingService.LogFatalBuildError(buildEventContext, ex, new BuildEventFileInfo(submission.BuildRequestData.ProjectFullPath));
                        }

                        submission.CompleteLogging(true);
                        ReportResultsToSubmission(new BuildResult(submission.BuildRequest, ex));
                        _overallBuildSuccess = false;
                    }
                }
            }
        }

        private void ExecuteGraphBuildScheduler(GraphBuildSubmission submission)
        {
            try
            {
                if (_shuttingDown)
                {
                    throw new BuildAbortedException();
                }

                var projectGraph = submission.BuildRequestData.ProjectGraph;
                if (projectGraph == null)
                {
                    projectGraph = new ProjectGraph(
                        submission.BuildRequestData.ProjectGraphEntryPoints,
                        ProjectCollection.GlobalProjectCollection,
                        (path, properties, collection) =>
                        {
                            ProjectLoadSettings projectLoadSettings = _buildParameters.ProjectLoadSettings;
                            if (submission.BuildRequestData.Flags.HasFlag(BuildRequestDataFlags.IgnoreMissingEmptyAndInvalidImports))
                            {
                                projectLoadSettings |= ProjectLoadSettings.IgnoreMissingImports | ProjectLoadSettings.IgnoreInvalidImports | ProjectLoadSettings.IgnoreEmptyImports;
                            }

                            if (submission.BuildRequestData.Flags.HasFlag(BuildRequestDataFlags.FailOnUnresolvedSdk))
                            {
                                projectLoadSettings |= ProjectLoadSettings.FailOnUnresolvedSdk;
                            }

                            return new ProjectInstance(
                                path,
                                properties,
                                null,
                                _buildParameters,
                                ((IBuildComponentHost)this).LoggingService,
                                new BuildEventContext(
                                    submission.SubmissionId,
                                    _buildParameters.NodeId,
                                    BuildEventContext.InvalidEvaluationId,
                                    BuildEventContext.InvalidProjectInstanceId,
                                    BuildEventContext.InvalidProjectContextId,
                                    BuildEventContext.InvalidTargetId,
                                    BuildEventContext.InvalidTaskId),
                                SdkResolverService,
                                submission.SubmissionId,
                                projectLoadSettings);
                        });
                }

                LogMessage(
                    ResourceUtilities.FormatResourceStringIgnoreCodeAndKeyword(
                        "StaticGraphConstructionMetrics",
                        Math.Round(projectGraph.ConstructionMetrics.ConstructionTime.TotalSeconds, 3),
                        projectGraph.ConstructionMetrics.NodeCount,
                        projectGraph.ConstructionMetrics.EdgeCount));

                Dictionary<ProjectGraphNode, BuildResult> resultsPerNode = null;

                if (submission.BuildRequestData.GraphBuildOptions.Build)
                {
                    var cacheServiceTask = Task.Run(() => SearchAndInitializeProjectCachePluginFromGraph(projectGraph));
                    var targetListTask = projectGraph.GetTargetLists(submission.BuildRequestData.TargetNames);

                    DumpGraph(projectGraph, targetListTask);

                    using DisposablePluginService cacheService = cacheServiceTask.Result;

                    resultsPerNode = BuildGraph(projectGraph, targetListTask, submission.BuildRequestData);
                }
                else
                {
                    DumpGraph(projectGraph);
                }

                ErrorUtilities.VerifyThrow(
                    submission.BuildResult?.Exception == null,
                    "Exceptions only get set when the graph submission gets completed with an exception in OnThreadException. That should not happen during graph builds.");

                // The overall submission is complete, so report it as complete
                ReportResultsToSubmission(
                    new GraphBuildResult(
                        submission.SubmissionId,
                        new ReadOnlyDictionary<ProjectGraphNode, BuildResult>(resultsPerNode ?? new Dictionary<ProjectGraphNode, BuildResult>())));
            }
            catch (Exception ex) when (!ExceptionHandling.IsCriticalException(ex))
            {
                GraphBuildResult result = null;

                // ProjectGraph throws an aggregate exception with InvalidProjectFileException inside when evaluation fails
                if (ex is AggregateException aggregateException && aggregateException.InnerExceptions.All(innerException => innerException is InvalidProjectFileException))
                {
                    // Log each InvalidProjectFileException encountered during ProjectGraph creation
                    foreach (var innerException in aggregateException.InnerExceptions)
                    {
                        var projectException = (InvalidProjectFileException) innerException;
                        if (!projectException.HasBeenLogged)
                        {
                            BuildEventContext projectBuildEventContext = new BuildEventContext(submission.SubmissionId, 1, BuildEventContext.InvalidProjectInstanceId, BuildEventContext.InvalidProjectContextId, BuildEventContext.InvalidTargetId, BuildEventContext.InvalidTaskId);
                            ((IBuildComponentHost)this).LoggingService.LogInvalidProjectFileError(projectBuildEventContext, projectException);
                            projectException.HasBeenLogged = true;
                        }
                    }
                }
                else if (ex is CircularDependencyException)
                {
                    result = new GraphBuildResult(submission.SubmissionId, true);

                    BuildEventContext projectBuildEventContext = new BuildEventContext(submission.SubmissionId, 1, BuildEventContext.InvalidProjectInstanceId, BuildEventContext.InvalidProjectContextId, BuildEventContext.InvalidTargetId, BuildEventContext.InvalidTaskId);
                    ((IBuildComponentHost)this).LoggingService.LogInvalidProjectFileError(projectBuildEventContext, new InvalidProjectFileException(ex.Message, ex));
                }
                else if (ex is BuildAbortedException || ExceptionHandling.NotExpectedException(ex))
                {
                    throw;
                }
                else
                {
                    // Arbitrarily just choose the first entry point project's path
                    var projectFile = submission.BuildRequestData.ProjectGraph?.EntryPointNodes.First().ProjectInstance.FullPath
                        ?? submission.BuildRequestData.ProjectGraphEntryPoints?.First().ProjectFile;
                    BuildEventContext buildEventContext = new BuildEventContext(submission.SubmissionId, 1, BuildEventContext.InvalidProjectInstanceId, BuildEventContext.InvalidProjectContextId, BuildEventContext.InvalidTargetId, BuildEventContext.InvalidTaskId);
                    ((IBuildComponentHost)this).LoggingService.LogFatalBuildError(buildEventContext, ex, new BuildEventFileInfo(projectFile));
                }

                if (result == null)
                {
                    result = new GraphBuildResult(submission.SubmissionId, ex);
                }

                ReportResultsToSubmission(result);

                lock (_syncLock)
                {
                    _overallBuildSuccess = false;
                }
            }

            static void DumpGraph(ProjectGraph graph, IReadOnlyDictionary<ProjectGraphNode, ImmutableList<string>> targetList = null)
            {
                if (Traits.Instance.DebugEngine is false)
                {
                    return;
                }

                var logPath = DebugUtils.FindNextAvailableDebugFilePath($"{DebugUtils.ProcessInfoString}_ProjectGraph.dot");

                File.WriteAllText(logPath, graph.ToDot(targetList));
            }
        }

        private Dictionary<ProjectGraphNode, BuildResult> BuildGraph(
            ProjectGraph projectGraph,
            IReadOnlyDictionary<ProjectGraphNode, ImmutableList<string>> targetsPerNode,
            GraphBuildRequestData graphBuildRequestData
        )
        {
            var waitHandle = new AutoResetEvent(true);
            var graphBuildStateLock = new object();

            var blockedNodes = new HashSet<ProjectGraphNode>(projectGraph.ProjectNodes);
            var finishedNodes = new HashSet<ProjectGraphNode>(projectGraph.ProjectNodes.Count);
            var buildingNodes = new Dictionary<BuildSubmission, ProjectGraphNode>();
            var resultsPerNode = new Dictionary<ProjectGraphNode, BuildResult>(projectGraph.ProjectNodes.Count);
            Exception submissionException = null;

            while (blockedNodes.Count > 0 || buildingNodes.Count > 0)
            {
                waitHandle.WaitOne();

                // When a cache plugin is present, ExecuteSubmission(BuildSubmission) executes on a separate thread whose exceptions do not get observed.
                // Observe them here to keep the same exception flow with the case when there's no plugins and ExecuteSubmission(BuildSubmission) does not run on a separate thread.
                if (submissionException != null)
                {
                    throw submissionException;
                }

                lock (graphBuildStateLock)
                {
                    var unblockedNodes = blockedNodes
                        .Where(node => node.ProjectReferences.All(projectReference => finishedNodes.Contains(projectReference)))
                        .ToList();
                    foreach (var node in unblockedNodes)
                    {
                        var targetList = targetsPerNode[node];
                        if (targetList.Count == 0)
                        {
                            // An empty target list here means "no targets" instead of "default targets", so don't even build it.
                            finishedNodes.Add(node);
                            blockedNodes.Remove(node);

                            waitHandle.Set();

                            continue;
                        }

                        var request = new BuildRequestData(
                            node.ProjectInstance,
                            targetList.ToArray(),
                            graphBuildRequestData.HostServices,
                            graphBuildRequestData.Flags);

                        // TODO Tack onto the existing submission instead of pending a whole new submission for every node
                        // Among other things, this makes BuildParameters.DetailedSummary produce a summary for each node, which is not desirable.
                        // We basically want to submit all requests to the scheduler all at once and describe dependencies by requests being blocked by other requests.
                        // However today the scheduler only keeps track of MSBuild nodes being blocked by other MSBuild nodes, and MSBuild nodes haven't been assigned to the graph nodes yet.
                        var innerBuildSubmission = PendBuildRequest(request);
                        buildingNodes.Add(innerBuildSubmission, node);
                        blockedNodes.Remove(node);
                        innerBuildSubmission.ExecuteAsync(finishedBuildSubmission =>
                        {
                            lock (graphBuildStateLock)
                            {
                                if (submissionException == null && finishedBuildSubmission.BuildResult.Exception != null)
                                {
                                    submissionException = finishedBuildSubmission.BuildResult.Exception;
                                }

                                ProjectGraphNode finishedNode = buildingNodes[finishedBuildSubmission];

                                finishedNodes.Add(finishedNode);
                                buildingNodes.Remove(finishedBuildSubmission);

                                resultsPerNode.Add(finishedNode, finishedBuildSubmission.BuildResult);
                            }

                            waitHandle.Set();
                        }, null);
                    }
                }
            }

            return resultsPerNode;
        }

        private DisposablePluginService SearchAndInitializeProjectCachePluginFromGraph(ProjectGraph projectGraph)
        {
            // TODO: Consider allowing parallel graph submissions, each with its own separate cache plugin. Right now the second graph submission with a cache will fail.

            if (_buildParameters.ProjectCacheDescriptor != null)
            {
                // Build parameter specified project cache takes precedence.
                return new DisposablePluginService(null);
            }

            var nodeToCacheItems = projectGraph.ProjectNodes.ToDictionary(
                n => n,
                n => n.ProjectInstance.GetItems(ItemTypeNames.ProjectCachePlugin)
                    .Select(
                        i =>
                        {
                            var metadataDictionary = i.Metadata.ToDictionary(
                                m => ((IKeyed) m).Key,
                                m => ((IValued) m).EscapedValue);

                            var pluginPath = Path.Combine(i.Project.Directory, i.EvaluatedInclude);

                            var projectCacheItem = new ProjectCacheItem(pluginPath, metadataDictionary);

                            return projectCacheItem;
                        })
                    .ToArray());

            var cacheItems = nodeToCacheItems.Values.SelectMany(i => i).ToHashSet();

            if (cacheItems.Count == 0)
            {
                return new DisposablePluginService(null);
            }

            if (cacheItems.Count != 1)
            {
                ProjectCacheException.ThrowForMSBuildIssueWithTheProjectCache(
                    "OnlyOneCachePluginMustBeSpecified",
                    string.Join("; ", cacheItems.Select(ci => ci.PluginPath)));
            }

            var nodesWithoutCacheItems = nodeToCacheItems.Where(kvp => kvp.Value.Length == 0).ToArray();

            if (nodesWithoutCacheItems.Length > 0)
            {
                ProjectCacheException.ThrowForMSBuildIssueWithTheProjectCache(
                    "NotAllNodesDefineACacheItem",
                    ItemTypeNames.ProjectCachePlugin,
                    string.Join(", ", nodesWithoutCacheItems.Select(kvp => kvp.Key.ProjectInstance.FullPath)));
            }

            var cacheItem = cacheItems.First();

            lock (_syncLock)
            {
                InitializeProjectCacheService(
                    ProjectCacheDescriptor.FromAssemblyPath(
                        cacheItem.PluginPath,
                        entryPoints: null,
                        projectGraph,
                        cacheItem.PluginSettings),
                    _graphSchedulingCancellationSource.Token);
            }

            return new DisposablePluginService(this);
        }

        private class DisposablePluginService : IDisposable
        {
            private readonly BuildManager _buildManager;

            public DisposablePluginService(BuildManager buildManager)
            {
                _buildManager = buildManager;
            }

            public void Dispose()
            {
                if (_buildManager == null)
                {
                    return;
                }

                lock (_buildManager._syncLock)
                {
                    try
                    {
                        _buildManager._projectCacheService?.Result.ShutDown().GetAwaiter().GetResult();
                    }
                    finally
                    {
                        _buildManager._projectCacheService = null;
                    }
                }
            }
        }

        /// <summary>
        /// Asks the nodeManager to tell the currently connected nodes to shut down and sets a flag preventing all non-shutdown-related packets from
        /// being processed.
        /// </summary>
        private void ShutdownConnectedNodes(bool abort)
        {
            lock (_syncLock)
            {
                _shuttingDown = true;

                // If we are aborting, we will NOT reuse the nodes because their state may be compromised by attempts to shut down while the build is in-progress.
                _nodeManager.ShutdownConnectedNodes(!abort && _buildParameters.EnableNodeReuse);

                // if we are aborting, the task host will hear about it in time through the task building infrastructure;
                // so only shut down the task host nodes if we're shutting down tidily (in which case, it is assumed that all
                // tasks are finished building and thus that there's no risk of a race between the two shutdown pathways).
                if (!abort)
                {
                    _taskHostNodeManager.ShutdownConnectedNodes(_buildParameters.EnableNodeReuse);
                }
            }
        }

        /// <summary>
        /// Retrieves the next build submission id.
        /// </summary>
        private int GetNextSubmissionId()
        {
            return _nextBuildSubmissionId++;
        }

        /// <summary>
        /// Errors if the BuildManager is in the specified state.
        /// </summary>
        private void ErrorIfState(BuildManagerState disallowedState, string exceptionResouorce)
        {
            if (_buildManagerState == disallowedState)
            {
                ErrorUtilities.ThrowInvalidOperation(exceptionResouorce);
            }
        }

        /// <summary>
        /// Verifies the BuildManager is in the required state, and throws a <see cref="System.InvalidOperationException"/> if it is not.
        /// </summary>
        private void RequireState(BuildManagerState requiredState, string exceptionResouorce)
        {
            ErrorUtilities.VerifyThrowInvalidOperation(_buildManagerState == requiredState, exceptionResouorce);
        }

        /// <summary>
        /// Verifies the BuildManager is in the required state, and throws a <see cref="System.InvalidOperationException"/> if it is not.
        /// </summary>
        private void VerifyStateInternal(BuildManagerState requiredState)
        {
            if (_buildManagerState != requiredState)
            {
                ErrorUtilities.ThrowInternalError("Expected state {0}, actual state {1}", requiredState, _buildManagerState);
            }
        }

        /// <summary>
        /// Method called to reset the state of the system after a build.
        /// </summary>
        private void Reset()
        {
            _nodeManager.UnregisterPacketHandler(NodePacketType.BuildRequestBlocker);
            _nodeManager.UnregisterPacketHandler(NodePacketType.BuildRequestConfiguration);
            _nodeManager.UnregisterPacketHandler(NodePacketType.BuildRequestConfigurationResponse);
            _nodeManager.UnregisterPacketHandler(NodePacketType.BuildResult);
            _nodeManager.UnregisterPacketHandler(NodePacketType.NodeShutdown);

            _nodeManager.ClearPerBuildState();
            _nodeManager = null;

            _shuttingDown = false;
            _nodeConfiguration = null;
            _buildSubmissions.Clear();
            _graphBuildSubmissions.Clear();

            _scheduler.Reset();
            _scheduler = null;
            _workQueue = null;
            _graphSchedulingCancellationSource = null;
            _projectCacheService = null;
            _acquiredProjectRootElementCacheFromProjectInstance = false;

            _unnamedProjectInstanceToNames.Clear();
            _projectStartedEvents.Clear();
            _nodeIdToKnownConfigurations.Clear();
            _nextUnnamedProjectId = 1;

            if (_configCache != null)
            {
                foreach (BuildRequestConfiguration config in _configCache)
                {
                    config.ActivelyBuildingTargets.Clear();
                }
            }

            if (Environment.GetEnvironmentVariable("MSBUILDCLEARXMLCACHEONBUILDMANAGER") == "1")
            {
                // Optionally clear out the cache. This has the advantage of releasing memory,
                // but the disadvantage of causing the next build to repeat the load and parse.
                // We'll experiment here and ship with the best default.
                _buildParameters.ProjectRootElementCache.Clear();
            }
        }

        /// <summary>
        /// Returns a new, valid configuration id.
        /// </summary>
        private int GetNewConfigurationId()
        {
            int newId = Interlocked.Increment(ref s_nextBuildRequestConfigurationId);

            if (_scheduler != null)
            {
                // Minimum configuration id is always the lowest valid configuration id available, so increment after returning.
                while (newId <= _scheduler.MinimumAssignableConfigurationId) // Currently this minimum is one
                {
                    newId = Interlocked.Increment(ref s_nextBuildRequestConfigurationId);
                }
            }

            return newId;
        }

        /// <summary>
        /// Finds a matching configuration in the cache and returns it, or stores the configuration passed in.
        /// </summary>
        private BuildRequestConfiguration ResolveConfiguration(BuildRequestConfiguration unresolvedConfiguration, BuildRequestConfiguration matchingConfigurationFromCache, bool replaceProjectInstance)
        {
            Debug.Assert(Monitor.IsEntered(_syncLock));

            BuildRequestConfiguration resolvedConfiguration = matchingConfigurationFromCache ?? _configCache.GetMatchingConfiguration(unresolvedConfiguration);
            if (resolvedConfiguration == null)
            {
                resolvedConfiguration = AddNewConfiguration(unresolvedConfiguration);
            }
            else if (unresolvedConfiguration.Project != null && replaceProjectInstance)
            {
                ReplaceExistingProjectInstance(unresolvedConfiguration, resolvedConfiguration);
            }
            else if (unresolvedConfiguration.Project != null && resolvedConfiguration.Project != null && !ReferenceEquals(unresolvedConfiguration.Project, resolvedConfiguration.Project))
            {
                // The user passed in a different instance than the one we already had. Throw away any corresponding results.
                ReplaceExistingProjectInstance(unresolvedConfiguration, resolvedConfiguration);
            }
            else if (unresolvedConfiguration.Project != null && resolvedConfiguration.Project == null)
            {
                // Workaround for https://github.com/Microsoft/msbuild/issues/1748
                // If the submission has a project instance but the existing configuration does not, it probably means that the project was
                // built on another node (e.g. the project was encountered as a p2p reference and scheduled to a node).
                // Add a dummy property to force cache invalidation in the scheduler and the nodes.
                // TODO find a better solution than a dummy property
                unresolvedConfiguration.CreateUniqueGlobalProperty();

                resolvedConfiguration = AddNewConfiguration(unresolvedConfiguration);
            }

            return resolvedConfiguration;
        }

        private void ReplaceExistingProjectInstance(BuildRequestConfiguration newConfiguration, BuildRequestConfiguration existingConfiguration)
        {
            Debug.Assert(Monitor.IsEntered(_syncLock));

            existingConfiguration.Project = newConfiguration.Project;
            _resultsCache.ClearResultsForConfiguration(existingConfiguration.ConfigurationId);
        }

        private BuildRequestConfiguration AddNewConfiguration(BuildRequestConfiguration unresolvedConfiguration)
        {
            Debug.Assert(Monitor.IsEntered(_syncLock));

            var newConfigurationId = _scheduler.GetConfigurationIdFromPlan(unresolvedConfiguration.ProjectFullPath);

            if (_configCache.HasConfiguration(newConfigurationId) || (newConfigurationId == BuildRequestConfiguration.InvalidConfigurationId))
            {
                // There is already a configuration like this one or one didn't exist in a plan, so generate a new ID.
                newConfigurationId = GetNewConfigurationId();
            }

            var newConfiguration = unresolvedConfiguration.ShallowCloneWithNewId(newConfigurationId);

            _configCache.AddConfiguration(newConfiguration);

            return newConfiguration;
        }

        internal void PostCacheResult(CacheRequest cacheRequest, CacheResult cacheResult)
        {
            _workQueue.Post(() =>
            {
                if (cacheResult.Exception is not null)
                {
                    CompleteSubmissionWithException(cacheRequest.Submission, cacheRequest.Configuration, cacheResult.Exception);
                    return;
                }

                HandleCacheResult();
            });

            void HandleCacheResult()
            {
                lock (_syncLock)
                {
                    try
                    {
                        var submission = cacheRequest.Submission;
                        var configuration = cacheRequest.Configuration;

                        if (cacheResult.ResultType != CacheResultType.CacheHit)
                        {
                            // Issue the real build request.
                            AddBuildRequestToSubmission(submission, configuration.ConfigurationId);
                            IssueBuildRequestForBuildSubmission(submission, allowMainThreadBuild: false);
                        }
                        else if (cacheResult.ResultType == CacheResultType.CacheHit && cacheResult.ProxyTargets != null)
                        {
                            // Setup submission.BuildRequest with proxy targets. The proxy request is built on the inproc node (to avoid
                            // ProjectInstance serialization). The proxy target results are used as results for the real targets.
                            AddProxyBuildRequestToSubmission(submission, configuration.ConfigurationId, cacheResult.ProxyTargets);
                            IssueBuildRequestForBuildSubmission(submission, allowMainThreadBuild: false);
                        }
                        else if (cacheResult.ResultType == CacheResultType.CacheHit && cacheResult.BuildResult != null)
                        {
                            // Mark the build submission as complete with the provided results and return.

                            // There must be a build request for the results, so fake one.
                            AddBuildRequestToSubmission(submission, configuration.ConfigurationId);
                            var result = new BuildResult(submission.BuildRequest);

                            foreach (var cacheResult in cacheResult.BuildResult.ResultsByTarget)
                            {
                                result.AddResultsForTarget(cacheResult.Key, cacheResult.Value);
                            }

                            _resultsCache.AddResult(result);
                            submission.CompleteLogging(false);
                            ReportResultsToSubmission(result);
                        }
                    }
                    catch (Exception e)
                    {
                        CompleteSubmissionWithException(cacheRequest.Submission, cacheRequest.Configuration, e);
                    }
                }
            }
        }

        /// <summary>
        /// Handles a new request coming from a node.
        /// </summary>
        private void HandleNewRequest(int node, BuildRequestBlocker blocker)
        {
            // If we received any solution files, populate their configurations now.
            if (blocker.BuildRequests != null)
            {
                foreach (BuildRequest request in blocker.BuildRequests)
                {
                    BuildRequestConfiguration config = _configCache[request.ConfigurationId];
                    if (FileUtilities.IsSolutionFilename(config.ProjectFullPath))
                    {
                        try
                        {
                            LoadSolutionIntoConfiguration(config, request);
                        }
                        catch (InvalidProjectFileException e)
                        {
                            // Throw the error in the cache.  The Scheduler will pick it up and return the results correctly.
                            _resultsCache.AddResult(new BuildResult(request, e));
                            if (node == Scheduler.VirtualNode)
                            {
                                throw;
                            }
                        }
                    }
                }
            }

            IEnumerable<ScheduleResponse> response = _scheduler.ReportRequestBlocked(node, blocker);
            PerformSchedulingActions(response);
        }

        /// <summary>
        /// Handles a resource request coming from a node.
        /// </summary>
        private void HandleResourceRequest(int node, ResourceRequest request)
        {
            Debug.Assert(Monitor.IsEntered(_syncLock));

            if (request.IsResourceAcquire)
            {
                // Resource request requires a response and may be blocking. Our continuation is effectively a callback
                // to be called once at least one core becomes available.
                _scheduler.RequestCores(request.GlobalRequestId, request.NumCores, request.IsBlocking).ContinueWith((Task<int> task) =>
                {
                    var response = new ResourceResponse(request.GlobalRequestId, task.Result);
                    _nodeManager.SendData(node, response);
                }, TaskContinuationOptions.ExecuteSynchronously);
            }
            else
            {
                // Resource release is a one-way call, no response is expected. We release the cores as instructed
                // and kick the scheduler because there may be work waiting for cores to become available.
                IEnumerable<ScheduleResponse> response = _scheduler.ReleaseCores(request.GlobalRequestId, request.NumCores);
                PerformSchedulingActions(response);
            }
        }

        /// <summary>
        /// Handles a configuration request coming from a node.
        /// </summary>
        private void HandleConfigurationRequest(int node, BuildRequestConfiguration unresolvedConfiguration)
        {
            Debug.Assert(Monitor.IsEntered(_syncLock));

            BuildRequestConfiguration resolvedConfiguration = ResolveConfiguration(unresolvedConfiguration, null, false);

            var response = new BuildRequestConfigurationResponse(unresolvedConfiguration.ConfigurationId, resolvedConfiguration.ConfigurationId, resolvedConfiguration.ResultsNodeId);

            if (!_nodeIdToKnownConfigurations.TryGetValue(node, out HashSet<NGen<int>> configurationsOnNode))
            {
                configurationsOnNode = new HashSet<NGen<int>>();
                _nodeIdToKnownConfigurations[node] = configurationsOnNode;
            }

            configurationsOnNode.Add(resolvedConfiguration.ConfigurationId);

            _nodeManager.SendData(node, response);
        }

        /// <summary>
        /// Handles a build result coming from a node.
        /// </summary>
        private void HandleResult(int node, BuildResult result)
        {
            // Update cache with the default and initial targets, as needed.
            BuildRequestConfiguration configuration = _configCache[result.ConfigurationId];
            if (result.DefaultTargets != null)
            {
                // If the result has Default and Initial targets, we populate the configuration cache with them if it
                // doesn't already have entries.  This can happen if we created a configuration based on a request from
                // an external node, but hadn't yet received a result since we may not have loaded the Project locally
                // and thus wouldn't know what the default and initial targets were.
                if (configuration.ProjectDefaultTargets == null)
                {
                    configuration.ProjectDefaultTargets = result.DefaultTargets;
                }

                if (configuration.ProjectInitialTargets == null)
                {
                    configuration.ProjectInitialTargets = result.InitialTargets;
                }
            }

            IEnumerable<ScheduleResponse> response = _scheduler.ReportResult(node, result);
            PerformSchedulingActions(response);
        }

        /// <summary>
        /// Handles the NodeShutdown packet
        /// </summary>
        private void HandleNodeShutdown(int node, NodeShutdown shutdownPacket)
        {
            Debug.Assert(Monitor.IsEntered(_syncLock));

            _shuttingDown = true;
            ErrorUtilities.VerifyThrow(_activeNodes.Contains(node), "Unexpected shutdown from node {0} which shouldn't exist.", node);
            _activeNodes.Remove(node);

            if (shutdownPacket.Reason != NodeShutdownReason.Requested)
            {
                if (shutdownPacket.Reason == NodeShutdownReason.ConnectionFailed)
                {
                    ILoggingService loggingService = ((IBuildComponentHost)this).GetComponent(BuildComponentType.LoggingService) as ILoggingService;
                    foreach (BuildSubmission submission in _buildSubmissions.Values)
                    {
                        BuildEventContext buildEventContext = new BuildEventContext(submission.SubmissionId, BuildEventContext.InvalidNodeId, BuildEventContext.InvalidProjectInstanceId, BuildEventContext.InvalidProjectContextId, BuildEventContext.InvalidTargetId, BuildEventContext.InvalidTaskId);
                        string exception = ExceptionHandling.ReadAnyExceptionFromFile(_instantiationTimeUtc);
                        loggingService.LogError(buildEventContext, new BuildEventFileInfo(String.Empty) /* no project file */, "ChildExitedPrematurely", node, ExceptionHandling.DebugDumpPath, exception);
                    }

                    foreach (GraphBuildSubmission submission in _graphBuildSubmissions.Values)
                    {
                        BuildEventContext buildEventContext = new BuildEventContext(submission.SubmissionId, BuildEventContext.InvalidNodeId, BuildEventContext.InvalidProjectInstanceId, BuildEventContext.InvalidProjectContextId, BuildEventContext.InvalidTargetId, BuildEventContext.InvalidTaskId);
                        string exception = ExceptionHandling.ReadAnyExceptionFromFile(_instantiationTimeUtc);
                        loggingService.LogError(buildEventContext, new BuildEventFileInfo(String.Empty) /* no project file */, "ChildExitedPrematurely", node, ExceptionHandling.DebugDumpPath, exception);
                    }
                }
                else if (shutdownPacket.Reason == NodeShutdownReason.Error && _buildSubmissions.Values.Count == 0 && _graphBuildSubmissions.Values.Count == 0)
                {
                    // We have no submissions to attach any exceptions to, lets just log it here.
                    if (shutdownPacket.Exception != null)
                    {
                        ILoggingService loggingService = ((IBuildComponentHost)this).GetComponent(BuildComponentType.LoggingService) as ILoggingService;
                        loggingService.LogError(BuildEventContext.Invalid, new BuildEventFileInfo(String.Empty) /* no project file */, "ChildExitedPrematurely", node, ExceptionHandling.DebugDumpPath, shutdownPacket.Exception.ToString());
                        OnThreadException(shutdownPacket.Exception);
                    }
                }

                _nodeManager.ShutdownConnectedNodes(_buildParameters.EnableNodeReuse);
                _taskHostNodeManager.ShutdownConnectedNodes(_buildParameters.EnableNodeReuse);

                foreach (BuildSubmission submission in _buildSubmissions.Values)
                {
                    // The submission has not started
                    if (submission.BuildRequest == null)
                    {
                        continue;
                    }

                    _resultsCache.AddResult(new BuildResult(submission.BuildRequest, shutdownPacket.Exception ?? new BuildAbortedException()));
                }

                _scheduler.ReportBuildAborted(node);
            }

            CheckForActiveNodesAndCleanUpSubmissions();
        }

        /// <summary>
        /// If there are no more active nodes, cleans up any remaining submissions.
        /// </summary>
        /// <remarks>
        /// Must only be called from within the sync lock.
        /// </remarks>
        private void CheckForActiveNodesAndCleanUpSubmissions()
        {
            Debug.Assert(Monitor.IsEntered(_syncLock));

            if (_activeNodes.Count == 0)
            {
                var submissions = new List<BuildSubmission>(_buildSubmissions.Values);
                foreach (BuildSubmission submission in submissions)
                {
                    // The submission has not started do not add it to the results cache
                    if (submission.BuildRequest == null)
                    {
                        continue;
                    }

                    // UNDONE: (stability) It might be best to trigger the logging service to shut down here,
                    //         since the full build is complete.  This would allow us to ensure all logging messages have been
                    //         drained and all submissions can complete their logging requirements.
                    BuildResult result = _resultsCache.GetResultsForConfiguration(submission.BuildRequest.ConfigurationId) ??
                                         new BuildResult(submission.BuildRequest, new BuildAbortedException());

                    submission.CompleteResults(result);

                    // If we never received a project started event, consider logging complete anyhow, since the nodes have
                    // shut down.
                    submission.CompleteLogging(waitForLoggingThread: false);

                    _overallBuildSuccess = _overallBuildSuccess && (submission.BuildResult.OverallResult == BuildResultCode.Success);
                    CheckSubmissionCompletenessAndRemove(submission);
                }

                var graphSubmissions = new List<GraphBuildSubmission>(_graphBuildSubmissions.Values);
                foreach (GraphBuildSubmission submission in graphSubmissions)
                {
                    if (submission.IsStarted)
                    {
                        continue;
                    }

                    submission.CompleteResults(new GraphBuildResult(submission.SubmissionId, new BuildAbortedException()));

                    _overallBuildSuccess &= submission.BuildResult.OverallResult == BuildResultCode.Success;
                    CheckSubmissionCompletenessAndRemove(submission);
                }

                _noNodesActiveEvent.Set();
            }
        }

        /// <summary>
        /// Carries out the actions specified by the scheduler.
        /// </summary>
        private void PerformSchedulingActions(IEnumerable<ScheduleResponse> responses)
        {
            Debug.Assert(Monitor.IsEntered(_syncLock));

            foreach (ScheduleResponse response in responses)
            {
                switch (response.Action)
                {
                    case ScheduleActionType.NoAction:
                        break;

                    case ScheduleActionType.SubmissionComplete:
                        if (_buildParameters.DetailedSummary)
                        {
                            _scheduler.WriteDetailedSummary(response.BuildResult.SubmissionId);
                        }

                        ReportResultsToSubmission(response.BuildResult);
                        break;

                    case ScheduleActionType.CircularDependency:
                    case ScheduleActionType.ResumeExecution:
                    case ScheduleActionType.ReportResults:
                        _nodeManager.SendData(response.NodeId, response.Unblocker);
                        break;

                    case ScheduleActionType.CreateNode:
                        var newNodes = new List<NodeInfo>();

                        for (int i = 0; i < response.NumberOfNodesToCreate; i++)
                        {
                            NodeInfo createdNode = _nodeManager.CreateNode(GetNodeConfiguration(), response.RequiredNodeType);

                            if (createdNode != null)
                            {
                                _noNodesActiveEvent.Reset();
                                _activeNodes.Add(createdNode.NodeId);
                                newNodes.Add(createdNode);
                                ErrorUtilities.VerifyThrow(_activeNodes.Count != 0, "Still 0 nodes after asking for a new node.  Build cannot proceed.");
                            }
                            else
                            {
                                BuildEventContext buildEventContext = new BuildEventContext(0, Scheduler.VirtualNode, BuildEventContext.InvalidProjectInstanceId, BuildEventContext.InvalidProjectContextId, BuildEventContext.InvalidTargetId, BuildEventContext.InvalidTaskId);
                                ((IBuildComponentHost)this).LoggingService.LogError(buildEventContext, new BuildEventFileInfo(String.Empty), "UnableToCreateNode", response.RequiredNodeType.ToString("G"));

                                throw new BuildAbortedException(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("UnableToCreateNode", response.RequiredNodeType.ToString("G")));
                            }
                        }

                        IEnumerable<ScheduleResponse> newResponses = _scheduler.ReportNodesCreated(newNodes);
                        PerformSchedulingActions(newResponses);

                        break;

                    case ScheduleActionType.Schedule:
                    case ScheduleActionType.ScheduleWithConfiguration:
                        if (response.Action == ScheduleActionType.ScheduleWithConfiguration)
                        {
                            // Only actually send the configuration if the node doesn't know about it.  The scheduler only keeps track
                            // of which nodes have had configurations specifically assigned to them for building.  However, a node may
                            // have created a configuration based on a build request it needs to wait on.  In this
                            // case we need not send the configuration since it will already have been mapped earlier.
                            if (!_nodeIdToKnownConfigurations.TryGetValue(response.NodeId, out HashSet<NGen<int>> configurationsOnNode) ||
                               !configurationsOnNode.Contains(response.BuildRequest.ConfigurationId))
                            {
                                IConfigCache configCache = _componentFactories.GetComponent(BuildComponentType.ConfigCache) as IConfigCache;
                                _nodeManager.SendData(response.NodeId, configCache[response.BuildRequest.ConfigurationId]);
                            }
                        }

                        _nodeManager.SendData(response.NodeId, response.BuildRequest);
                        break;

                    default:
                        ErrorUtilities.ThrowInternalError("Scheduling action {0} not handled.", response.Action);
                        break;
                }
            }
        }

        /// <summary>
        /// Completes a submission using the specified overall results.
        /// </summary>
        private void ReportResultsToSubmission(BuildResult result)
        {
            lock (_syncLock)
            {
                // The build submission has not already been completed.
                if (_buildSubmissions.TryGetValue(result.SubmissionId, out BuildSubmission submission))
                {
                    /* If the request failed because we caught an exception from the loggers, we can assume we will receive no more logging messages for
                     * this submission, therefore set the logging as complete. InternalLoggerExceptions are unhandled exceptions from the logger. If the logger author does
                     * not handle an exception the eventsource wraps all exceptions (except a logging exception) into an internal logging exception.
                     * These exceptions will have their stack logged on the commandline as an unexpected failure. If a logger author wants the logger
                     * to fail gracefully then can catch an exception and log a LoggerException. This has the same effect of stopping the build but it logs only
                     * the exception error message rather than the whole stack trace.
                     *
                     * If any other exception happened and logging is not completed, then go ahead and complete it now since this is the last place to do it.
                     * Otherwise the submission would remain uncompleted, potentially causing hangs (EndBuild waiting on all BuildSubmissions, users waiting on BuildSubmission, or expecting a callback, etc)
                    */
                    if (!submission.LoggingCompleted && result.Exception != null)
                    {
                        submission.CompleteLogging(waitForLoggingThread: false);
                    }

                    submission.CompleteResults(result);

                    _overallBuildSuccess = _overallBuildSuccess && (_buildSubmissions[result.SubmissionId].BuildResult.OverallResult == BuildResultCode.Success);

                    CheckSubmissionCompletenessAndRemove(submission);
                }
            }
        }

        /// <summary>
        /// Completes a submission using the specified overall results.
        /// </summary>
        private void ReportResultsToSubmission(GraphBuildResult result)
        {
            lock (_syncLock)
            {
                // The build submission has not already been completed.
                if (_graphBuildSubmissions.TryGetValue(result.SubmissionId, out GraphBuildSubmission submission))
                {
                    submission.CompleteResults(result);

                    _overallBuildSuccess &= submission.BuildResult.OverallResult == BuildResultCode.Success;

                    CheckSubmissionCompletenessAndRemove(submission);
                }
            }
        }

        /// <summary>
        /// Determines if the submission is fully completed.
        /// </summary>
        private void CheckSubmissionCompletenessAndRemove(BuildSubmission submission)
        {
            lock (_syncLock)
            {
                // If the submission has completed or never started, remove it.
                if (submission.IsCompleted || submission.BuildRequest == null)
                {
                    _buildSubmissions.Remove(submission.SubmissionId);

                    // Clear all cached SDKs for the submission
                    SdkResolverService.ClearCache(submission.SubmissionId);
                }

                CheckAllSubmissionsComplete(submission.BuildRequestData?.Flags);
            }
        }

        /// <summary>
        /// Determines if the submission is fully completed.
        /// </summary>
        private void CheckSubmissionCompletenessAndRemove(GraphBuildSubmission submission)
        {
            lock (_syncLock)
            {
                // If the submission has completed or never started, remove it.
                if (submission.IsCompleted || !submission.IsStarted)
                {
                    _graphBuildSubmissions.Remove(submission.SubmissionId);

                    // Clear all cached SDKs for the submission
                    SdkResolverService.ClearCache(submission.SubmissionId);
                }

                CheckAllSubmissionsComplete(submission.BuildRequestData?.Flags);
            }
        }

        private void CheckAllSubmissionsComplete(BuildRequestDataFlags? flags)
        {
            Debug.Assert(Monitor.IsEntered(_syncLock));

            if (_buildSubmissions.Count == 0 && _graphBuildSubmissions.Count == 0)
            {
                if (flags.HasValue && flags.Value.HasFlag(BuildRequestDataFlags.ClearCachesAfterBuild))
                {
                    // Reset the project root element cache if specified which ensures that projects will be re-loaded from disk.  We do not need to reset the
                    // cache on child nodes because the OutOfProcNode class sets "autoReloadFromDisk" to "true" which handles the case when a restore modifies
                    // part of the import graph.
                    _buildParameters?.ProjectRootElementCache?.Clear();

                    FileMatcher.ClearFileEnumerationsCache();
#if !CLR2COMPATIBILITY
                    FileUtilities.ClearFileExistenceCache();
#endif
                }

                _noActiveSubmissionsEvent.Set();
            }
        }

        /// <summary>
        /// Retrieves the configuration structure for a node.
        /// </summary>
        private NodeConfiguration GetNodeConfiguration()
        {
            Debug.Assert(Monitor.IsEntered(_syncLock));

            if (_nodeConfiguration == null)
            {
                // Get the remote loggers
                ILoggingService loggingService = ((IBuildComponentHost)this).GetComponent(BuildComponentType.LoggingService) as ILoggingService;
                var remoteLoggers = new List<LoggerDescription>(loggingService.LoggerDescriptions);

                _nodeConfiguration = new NodeConfiguration
                (
                -1, /* must be assigned by the NodeManager */
                _buildParameters,
                remoteLoggers.ToArray()
#if FEATURE_APPDOMAIN
                , AppDomain.CurrentDomain.SetupInformation
#endif
                , new LoggingNodeConfiguration(
                    loggingService.IncludeEvaluationMetaprojects,
                    loggingService.IncludeEvaluationProfile,
                    loggingService.IncludeEvaluationPropertiesAndItems,
                    loggingService.IncludeTaskInputs)
                );
            }

            return _nodeConfiguration;
        }

        /// <summary>
        /// Handler for thread exceptions (logging thread, communications thread).  This handler will only get called if the exception did not previously
        /// get handled by a node exception handlers (for instance because the build is complete for the node.)  In this case we
        /// get the exception and will put it into the OverallBuildResult so that the host can see what happened.
        /// </summary>
        private void OnThreadException(Exception e)
        {
            lock (_syncLock)
            {
                if (_threadException == null)
                {
                    if (e is AggregateException ae && ae.InnerExceptions.Count == 1)
                    {
                        e = ae.InnerExceptions.First();
                    }

                    _threadException = ExceptionDispatchInfo.Capture(e);
                    var submissions = new List<BuildSubmission>(_buildSubmissions.Values);
                    foreach (BuildSubmission submission in submissions)
                    {
                        // Submission has not started
                        if (submission.BuildRequest == null)
                        {
                            continue;
                        }

                        // Attach the exception to this submission if it does not already have an exception associated with it
                        if (!submission.IsCompleted && submission.BuildResult != null && submission.BuildResult.Exception == null)
                        {
                            submission.BuildResult.Exception = e;
                        }
                        submission.CompleteLogging(waitForLoggingThread: false);
                        submission.CompleteResults(new BuildResult(submission.BuildRequest, e));

                        CheckSubmissionCompletenessAndRemove(submission);
                    }

                    var graphSubmissions = new List<GraphBuildSubmission>(_graphBuildSubmissions.Values);
                    foreach (GraphBuildSubmission submission in graphSubmissions)
                    {
                        if (!submission.IsStarted)
                        {
                            continue;
                        }

                        // Attach the exception to this submission if it does not already have an exception associated with it
                        if (!submission.IsCompleted && submission.BuildResult != null && submission.BuildResult.Exception == null)
                        {
                            submission.BuildResult.Exception = e;
                        }
                        submission.CompleteResults(submission.BuildResult ?? new GraphBuildResult(submission.SubmissionId, e));

                        CheckSubmissionCompletenessAndRemove(submission);
                    }
                }
            }
        }

        /// <summary>
        /// Raised when a project finished logging message has been processed.
        /// </summary>
        private void OnProjectFinished(object sender, ProjectFinishedEventArgs e)
        {
            lock (_syncLock)
            {
                if (_projectStartedEvents.TryGetValue(e.BuildEventContext.SubmissionId, out var originalArgs))
                {
                    if (originalArgs.BuildEventContext.Equals(e.BuildEventContext))
                    {
                        _projectStartedEvents.Remove(e.BuildEventContext.SubmissionId);
                        if (_buildSubmissions.TryGetValue(e.BuildEventContext.SubmissionId, out var submission))
                        {
                            submission.CompleteLogging(false);
                            CheckSubmissionCompletenessAndRemove(submission);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Raised when a project started logging message is about to be processed.
        /// </summary>
        private void OnProjectStarted(object sender, ProjectStartedEventArgs e)
        {
            lock (_syncLock)
            {
                if (!_projectStartedEvents.ContainsKey(e.BuildEventContext.SubmissionId))
                {
                    _projectStartedEvents[e.BuildEventContext.SubmissionId] = e;
                }
            }
        }

        /// <summary>
        /// Creates a logging service around the specified set of loggers.
        /// </summary>
        private ILoggingService CreateLoggingService(IEnumerable<ILogger> loggers, IEnumerable<ForwardingLoggerRecord> forwardingLoggers, ISet<string> warningsAsErrors, ISet<string> warningsAsMessages)
        {
            Debug.Assert(Monitor.IsEntered(_syncLock));

            int cpuCount = _buildParameters.MaxNodeCount;

            LoggerMode loggerMode = cpuCount == 1 && _buildParameters.UseSynchronousLogging
                                        ? LoggerMode.Synchronous
                                        : LoggerMode.Asynchronous;

            ILoggingService loggingService = LoggingService.CreateLoggingService(loggerMode,
                1 /*This logging service is used for the build manager and the inproc node, therefore it should have the first nodeId*/);

            ((IBuildComponent)loggingService).InitializeComponent(this);
            _componentFactories.ReplaceFactory(BuildComponentType.LoggingService, loggingService as IBuildComponent);

            _threadException = null;
            loggingService.OnLoggingThreadException += _loggingThreadExceptionEventHandler;
            loggingService.OnProjectStarted += _projectStartedEventHandler;
            loggingService.OnProjectFinished += _projectFinishedEventHandler;
            loggingService.WarningsAsErrors = warningsAsErrors;
            loggingService.WarningsAsMessages = warningsAsMessages;

            try
            {
                if (loggers != null)
                {
                    foreach (ILogger logger in loggers)
                    {
                        loggingService.RegisterLogger(logger);
                    }
                }

                if (loggingService.Loggers.Count == 0)
                {
                    // We need to register SOME logger if we don't have any. This ensures the out of proc nodes will still send us message,
                    // ensuring we receive project started and finished events.
                    LoggerDescription forwardingLoggerDescription = new LoggerDescription(
                        loggerClassName: typeof(ConfigurableForwardingLogger).FullName,
                        loggerAssemblyName: typeof(ConfigurableForwardingLogger).GetTypeInfo().Assembly.GetName().FullName,
                        loggerAssemblyFile: null,
                        loggerSwitchParameters: "PROJECTSTARTEDEVENT;PROJECTFINISHEDEVENT",
                        verbosity: LoggerVerbosity.Quiet);

                    ForwardingLoggerRecord[] forwardingLogger = { new ForwardingLoggerRecord(new NullLogger(), forwardingLoggerDescription) };
                    forwardingLoggers = forwardingLoggers?.Concat(forwardingLogger) ?? forwardingLogger;
                }

                if (forwardingLoggers != null)
                {
                    foreach (ForwardingLoggerRecord forwardingLoggerRecord in forwardingLoggers)
                    {
                        loggingService.RegisterDistributedLogger(forwardingLoggerRecord.CentralLogger, forwardingLoggerRecord.ForwardingLoggerDescription);
                    }
                }
            }
            catch (Exception ex) when (!ExceptionHandling.IsCriticalException(ex))
            {
                ShutdownLoggingService(loggingService);
                throw;
            }

            return loggingService;
        }

        private static void LogDeferredMessages(ILoggingService loggingService, IEnumerable<DeferredBuildMessage> deferredBuildMessages)
        {
            if (deferredBuildMessages == null)
            {
                return;
            }

            foreach (var message in deferredBuildMessages)
            {
                loggingService.LogCommentFromText(BuildEventContext.Invalid, message.Importance, message.Text);
            }
        }

        /// <summary>
        /// Ensures that the packet type matches the expected type
        /// </summary>
        /// <typeparam name="I">The instance-type of packet being expected</typeparam>
        private static I ExpectPacketType<I>(INodePacket packet, NodePacketType expectedType) where I : class, INodePacket
        {
            I castPacket = packet as I;

            // PERF: Not using VerifyThrow here to avoid boxing of expectedType.
            if (castPacket == null)
            {
                ErrorUtilities.ThrowInternalError("Incorrect packet type: {0} should have been {1}", packet.Type, expectedType);
            }

            return castPacket;
        }

        /// <summary>
        ///  Shutdown the logging service
        /// </summary>
        private void ShutdownLoggingService(ILoggingService loggingService)
        {
            try
            {
                if (loggingService != null)
                {
                    loggingService.OnLoggingThreadException -= _loggingThreadExceptionEventHandler;
                    loggingService.OnProjectFinished -= _projectFinishedEventHandler;
                    loggingService.OnProjectStarted -= _projectStartedEventHandler;
                    _componentFactories.ShutdownComponent(BuildComponentType.LoggingService);
                }
            }
            finally
            {
                // Even if an exception is thrown, we want to make sure we null out the logging service so that
                // we don't try to shut it down again in some other cleanup code.
                _componentFactories.ReplaceFactory(BuildComponentType.LoggingService, (IBuildComponent)null);
            }
        }

        /// <summary>
        /// Dispose implementation
        /// </summary>
        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    lock (_syncLock)
                    {
                        // We should always have finished cleaning up before calling Dispose.
                        RequireState(BuildManagerState.Idle, "ShouldNotDisposeWhenBuildManagerActive");

                        _componentFactories?.ShutdownComponents();

                        if (_workQueue != null)
                        {
                            _workQueue.Complete();
                            _workQueue = null;
                        }

                        if (_graphSchedulingCancellationSource != null)
                        {
                            _graphSchedulingCancellationSource.Cancel();
                            _graphSchedulingCancellationSource = null;
                        }

                        if (_noActiveSubmissionsEvent != null)
                        {
                            _noActiveSubmissionsEvent.Dispose();
                            _noActiveSubmissionsEvent = null;
                        }

                        if (_noNodesActiveEvent != null)
                        {
                            _noNodesActiveEvent.Dispose();
                            _noNodesActiveEvent = null;
                        }

                        if (ReferenceEquals(this, s_singletonInstance))
                        {
                            s_singletonInstance = null;
                        }

                        _disposed = true;
                    }
                }
            }
        }

        private bool ReuseOldCaches(string[] inputCacheFiles)
        {
            Debug.Assert(Monitor.IsEntered(_syncLock));

            ErrorUtilities.VerifyThrowInternalNull(inputCacheFiles, nameof(inputCacheFiles));
            ErrorUtilities.VerifyThrow(_configCache == null, "caches must not be set at this point");
            ErrorUtilities.VerifyThrow(_resultsCache == null, "caches must not be set at this point");

            try
            {
                if (inputCacheFiles.Length == 0)
                {
                    return false;
                }

                if (inputCacheFiles.Any(f => !File.Exists(f)))
                {
                    LogErrorAndShutdown(ResourceUtilities.FormatResourceStringIgnoreCodeAndKeyword("InputCacheFilesDoNotExist", string.Join(";", inputCacheFiles.Where(f => !File.Exists(f)))));
                    return false;
                }

                var cacheAggregator = new CacheAggregator(() => GetNewConfigurationId());

                foreach (var inputCacheFile in inputCacheFiles)
                {
                    var (configCache, resultsCache, exception) = CacheSerialization.DeserializeCaches(inputCacheFile);

                    if (exception != null)
                    {
                        LogErrorAndShutdown(ResourceUtilities.FormatResourceStringIgnoreCodeAndKeyword("ErrorReadingCacheFile", inputCacheFile, exception.Message));
                        return false;
                    }

                    cacheAggregator.Add(configCache, resultsCache);
                }

                var cacheAggregation = cacheAggregator.Aggregate();

                // using caches with override (override queried first before current cache) based on the assumption that during single project cached builds
                // there's many old results, but just one single actively building project.

                _componentFactories.ReplaceFactory(BuildComponentType.ConfigCache, new ConfigCacheWithOverride(cacheAggregation.ConfigCache));
                _componentFactories.ReplaceFactory(BuildComponentType.ResultsCache, new ResultsCacheWithOverride(cacheAggregation.ResultsCache));

                return true;
            }
            catch
            {
                CancelAndMarkAsFailure();
                throw;
            }
        }

        private void LogMessage(string message)
        {
            var loggingService = ((IBuildComponentHost)this).LoggingService;

            loggingService?.LogCommentFromText(BuildEventContext.Invalid, MessageImportance.High, message);
        }

        private void LogErrorAndShutdown(string message)
        {
            var loggingService = ((IBuildComponentHost)this).LoggingService;

            loggingService?.LogErrorFromText(
                BuildEventContext.Invalid,
                null,
                null,
                null,
                BuildEventFileInfo.Empty,
                message);

            CancelAndMarkAsFailure();

            if (loggingService == null)
            {
                // todo should we write this to temp file instead (like failing nodes do)
                throw new Exception(message);
            }
        }

        private void CancelAndMarkAsFailure()
        {
            Debug.Assert(Monitor.IsEntered(_syncLock));

            CancelAllSubmissions();

            // CancelAllSubmissions also ends up setting _shuttingDown and _overallBuildSuccess but it does so in a separate thread to avoid deadlocks.
            // This might cause a race with the first builds which might miss the shutdown update and succeed instead of fail.
            _shuttingDown = true;
            _overallBuildSuccess = false;
        }

        /// <summary>
        /// The logger registered to the logging service when no other one is.
        /// </summary>
        private class NullLogger : ILogger
        {
            #region ILogger Members

            /// <summary>
            /// The logger verbosity.
            /// </summary>
            public LoggerVerbosity Verbosity
            {
                get => LoggerVerbosity.Normal;
                set { }
            }

            /// <summary>
            /// The logger parameters.
            /// </summary>
            public string Parameters
            {
                get => String.Empty;
                set{ }
            }

            /// <summary>
            /// Initialize.
            /// </summary>
            public void Initialize(IEventSource eventSource)
            {
            }

            /// <summary>
            /// Shutdown.
            /// </summary>
            public void Shutdown()
            {
            }

            #endregion
        }
    }
}
