// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
#if FEATURE_APPDOMAIN
using System.Runtime.Remoting.Lifetime;
using System.Runtime.Remoting;
#endif
using System.Threading;
using Microsoft.Build.Framework;
using Microsoft.Build.Shared;
using Microsoft.Build.Execution;
using System.Diagnostics;
using Microsoft.Build.Collections;
using ElementLocation = Microsoft.Build.Construction.ElementLocation;
using TaskItem = Microsoft.Build.Execution.ProjectItemInstance.TaskItem;
using TaskLoggingContext = Microsoft.Build.BackEnd.Logging.TaskLoggingContext;
using System.Threading.Tasks;
using Microsoft.Build.BackEnd.Components.Caching;
using System.Reflection;
using Microsoft.Build.Eventing;
using Microsoft.Build.Utilities;

namespace Microsoft.Build.BackEnd
{
    /// <summary>
    /// The task host object which allows tasks to interface with the rest of the build system.
    /// Implementation of IBuildEngineX is thread-safe, so, for example, tasks can log concurrently on multiple threads.
    /// </summary>
    internal class TaskHost :
#if FEATURE_APPDOMAIN
        MarshalByRefObject,
#endif
        IBuildEngine9
    {
        /// <summary>
        /// True if the "secret" environment variable MSBUILDNOINPROCNODE is set.
        /// </summary>
        private static bool s_disableInprocNodeByEnvironmentVariable = Environment.GetEnvironmentVariable("MSBUILDNOINPROCNODE") == "1";

        /// <summary>
        /// Help diagnose tasks that log after they return.
        /// </summary>
        private static bool s_breakOnLogAfterTaskReturns = Environment.GetEnvironmentVariable("MSBUILDBREAKONLOGAFTERTASKRETURNS") == "1";

        /// <summary>
        /// The build component host
        /// </summary>
        private IBuildComponentHost _host;

        /// <summary>
        /// The build request entry
        /// </summary>
        private BuildRequestEntry _requestEntry;

        /// <summary>
        /// Location of the task node in the original file
        /// </summary>
        private ElementLocation _taskLocation;

        /// <summary>
        /// The task logging context
        /// </summary>
        private TaskLoggingContext _taskLoggingContext;

        /// <summary>
        /// True if the task connected to this proxy is alive
        /// </summary>
        private bool _activeProxy;

        /// <summary>
        /// The callback used to invoke the target builder.
        /// </summary>
        private ITargetBuilderCallback _targetBuilderCallback;

        /// <summary>
        /// This reference type is used to block access to a single entry methods of the interface
        /// </summary>
        private object _callbackMonitor;

#if FEATURE_APPDOMAIN
        /// <summary>
        /// A client sponsor is a class
        /// which will respond to a lease renewal request and will
        /// increase the lease time allowing the object to stay in memory
        /// </summary>
        private ClientSponsor _sponsor;
#endif

        /// <summary>
        /// Legacy continue on error value per batch exposed via IBuildEngine
        /// </summary>
        private bool _continueOnError;

        /// <summary>
        /// Flag indicating if errors should be converted to warnings.
        /// </summary>
        private bool _convertErrorsToWarnings;

        /// <summary>
        /// The thread on which we yielded.
        /// </summary>
        private int _yieldThreadId = -1;

        private bool _disableInprocNode;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="host">The component host</param>
        /// <param name="requestEntry">The build request entry</param>
        /// <param name="taskLocation">The <see cref="ElementLocation"/> of the task.</param>
        /// <param name="targetBuilderCallback">An <see cref="ITargetBuilderCallback"/> to use to invoke targets and build projects.</param>
        public TaskHost(IBuildComponentHost host, BuildRequestEntry requestEntry, ElementLocation taskLocation, ITargetBuilderCallback targetBuilderCallback)
        {
            ErrorUtilities.VerifyThrowArgumentNull(host, nameof(host));
            ErrorUtilities.VerifyThrowArgumentNull(requestEntry, nameof(requestEntry));
            ErrorUtilities.VerifyThrowInternalNull(taskLocation, nameof(taskLocation));

            _host = host;
            _requestEntry = requestEntry;
            _taskLocation = taskLocation;
            _targetBuilderCallback = targetBuilderCallback;
            _continueOnError = false;
            _activeProxy = true;
            _callbackMonitor = new object();
            _disableInprocNode = ChangeWaves.AreFeaturesEnabled(ChangeWaves.Wave17_0)
                ? s_disableInprocNodeByEnvironmentVariable || host.BuildParameters.DisableInProcNode
                : s_disableInprocNodeByEnvironmentVariable;
        }

        /// <summary>
        /// Returns true in the multiproc case
        /// </summary>
        /// <comment>
        /// If MSBUILDNOINPROCNODE is set, then even if there's only one node in the buildparameters, it will be an out-of-proc node.
        /// </comment>
        public bool IsRunningMultipleNodes
        {
            get
            {
                VerifyActiveProxy();
                return _host.BuildParameters.MaxNodeCount > 1 || _disableInprocNode;
            }
        }

        /// <summary>
        /// Reflects the value of the ContinueOnError attribute.
        /// </summary>
        public bool ContinueOnError
        {
            get
            {
                VerifyActiveProxy();
                return _continueOnError;
            }

            internal set
            {
                _continueOnError = value;
            }
        }

        /// <summary>
        /// The line number this task is on
        /// </summary>
        public int LineNumberOfTaskNode
        {
            get
            {
                return _taskLocation.Line;
            }
        }

        /// <summary>
        /// The column number this task is on
        /// </summary>
        public int ColumnNumberOfTaskNode
        {
            get
            {
                return _taskLocation.Column;
            }
        }

        /// <summary>
        /// The project file this task is in.
        /// Typically this is an imported .targets file.
        /// Unfortunately the interface has shipped with a poor name, so we cannot change it.
        /// </summary>
        public string ProjectFileOfTaskNode
        {
            get
            {
                return _taskLocation.File;
            }
        }

        /// <summary>
        /// Indicates whether or not errors should be converted to warnings.
        /// </summary>
        internal bool ConvertErrorsToWarnings
        {
            get { return _convertErrorsToWarnings; }
            set { _convertErrorsToWarnings = value; }
        }

        /// <summary>
        /// Sets or retrieves the logging context
        /// </summary>
        internal TaskLoggingContext LoggingContext
        {
            [DebuggerStepThrough]
            get
            { return _taskLoggingContext; }

            [DebuggerStepThrough]
            set
            { _taskLoggingContext = value; }
        }

#if FEATURE_APPDOMAIN
        /// <summary>
        /// For configuring child AppDomains.
        /// </summary>
        internal AppDomainSetup AppDomainSetup
        {
            get
            {
                return _host.BuildParameters.AppDomainSetup;
            }
        }
#endif

        /// <summary>
        /// Whether or not this is out of proc.
        /// </summary>
        internal bool IsOutOfProc
        {
            get
            {
                return _host.BuildParameters.IsOutOfProc;
            }
        }

        public bool BuildRequestsSucceeded { get; private set; } = true;

        #region IBuildEngine2 Members

        /// <summary>
        /// Builds a single project file
        /// Thread safe.
        /// </summary>
        /// <param name="projectFileName">The project file</param>
        /// <param name="targetNames">The list of targets to build</param>
        /// <param name="globalProperties">The global properties to use</param>
        /// <param name="targetOutputs">The outputs from the targets</param>
        /// <param name="toolsVersion">The tools version to use</param>
        /// <returns>True on success, false otherwise.</returns>
        public bool BuildProjectFile(string projectFileName, string[] targetNames, System.Collections.IDictionary globalProperties, System.Collections.IDictionary targetOutputs, string toolsVersion)
        {
            VerifyActiveProxy();
            return BuildProjectFilesInParallel
            (
                new string[] { projectFileName },
                targetNames,
                new IDictionary[] { globalProperties },
                new IDictionary[] { targetOutputs },
                new string[] { toolsVersion },
                true,
                false
            );
        }

        /// <summary>
        /// Builds multiple project files in parallel.  This is the method the old MSBuild task invokes.
        /// Thread safe.
        /// </summary>
        /// <param name="projectFileNames">The list of projects to build</param>
        /// <param name="targetNames">The set of targets to build</param>
        /// <param name="globalProperties">The global properties to use for each project</param>
        /// <param name="targetOutputsPerProject">The outputs for each target on each project</param>
        /// <param name="toolsVersion">The tools versions to use</param>
        /// <param name="useResultsCache">Whether to use the results cache</param>
        /// <param name="unloadProjectsOnCompletion">Whether to unload projects when we are done.</param>
        /// <returns>True on success, false otherwise.</returns>
        public bool BuildProjectFilesInParallel(string[] projectFileNames, string[] targetNames, System.Collections.IDictionary[] globalProperties, System.Collections.IDictionary[] targetOutputsPerProject, string[] toolsVersion, bool useResultsCache, bool unloadProjectsOnCompletion)
        {
            bool includeTargetOutputs = (targetOutputsPerProject != null);

            // If the caller supplies an array to put the target outputs in, it must have the same length as the array of project file names they provided, too.
            // "MSB3094: "{2}" refers to {0} item(s), and "{3}" refers to {1} item(s). They must have the same number of items."
            ErrorUtilities.VerifyThrowArgument((targetOutputsPerProject == null) || (projectFileNames.Length == targetOutputsPerProject.Length), "General.TwoVectorsMustHaveSameLength", projectFileNames.Length, targetOutputsPerProject?.Length ?? 0, "projectFileNames", "targetOutputsPerProject");

            BuildEngineResult result = BuildProjectFilesInParallel(projectFileNames, targetNames, globalProperties, new List<String>[projectFileNames.Length], toolsVersion, includeTargetOutputs);

            if (includeTargetOutputs)
            {
                // Copy results from result.TargetOutputsPerProject to targetOutputsPerProject
                // We should always have the same number of entries - although an entry might be empty if a project failed.
                ErrorUtilities.VerifyThrow(targetOutputsPerProject.Length == result.TargetOutputsPerProject.Count, "{0} != {1}", targetOutputsPerProject.Length, result.TargetOutputsPerProject.Count);

                for (int i = 0; i < targetOutputsPerProject.Length; i++)
                {
                    if (targetOutputsPerProject[i] != null)
                    {
                        foreach (KeyValuePair<string, ITaskItem[]> output in result.TargetOutputsPerProject[i])
                        {
                            targetOutputsPerProject[i].Add(output.Key, output.Value);
                        }
                    }
                }
            }

            BuildRequestsSucceeded = result.Result;

            return result.Result;
        }

        #endregion

        #region IBuildEngine3 Members

        /// <summary>
        /// Builds multiple project files in parallel.
        /// Thread safe.
        /// </summary>
        /// <param name="projectFileNames">The list of projects to build</param>
        /// <param name="targetNames">The set of targets to build</param>
        /// <param name="globalProperties">The global properties to use for each project</param>
        /// <param name="undefineProperties">The list of global properties to undefine</param>
        /// <param name="toolsVersion">The tools versions to use</param>
        /// <param name="returnTargetOutputs">Should the target outputs be returned in the BuildEngineResult</param>
        /// <returns>A structure containing the result of the build, success or failure and the list of target outputs per project</returns>
        public BuildEngineResult BuildProjectFilesInParallel(string[] projectFileNames, string[] targetNames, System.Collections.IDictionary[] globalProperties, IList<String>[] undefineProperties, string[] toolsVersion, bool returnTargetOutputs)
        {
            lock (_callbackMonitor)
            {
                return BuildProjectFilesInParallelAsync(projectFileNames, targetNames, globalProperties, undefineProperties, toolsVersion, returnTargetOutputs).Result;
            }
        }

        /// <summary>
        /// Requests to yield the node.
        /// Thread safe, however Yield cannot be called unless the
        /// last call to Yield or Reacquire was Reacquire.
        /// </summary>
        public void Yield()
        {
            lock (_callbackMonitor)
            {
                IRequestBuilderCallback builderCallback = _requestEntry.Builder as IRequestBuilderCallback;
                ErrorUtilities.VerifyThrow(_yieldThreadId == -1, "Cannot call Yield() while yielding.");
                _yieldThreadId = Thread.CurrentThread.ManagedThreadId;
                MSBuildEventSource.Log.ExecuteTaskYieldStart(_taskLoggingContext.TaskName, _taskLoggingContext.BuildEventContext.TaskId);
                builderCallback.Yield();
            }
        }

        /// <summary>
        /// Requests to reacquire the node.
        /// Thread safe, however Reacquire cannot be called unless the
        /// last call to Yield or Reacquire was Yield.
        /// </summary>
        public void Reacquire()
        {
            // Release all cores on reacquire. The assumption here is that the task is done with CPU intensive work at this point and forgetting
            // to release explicitly granted cores when reacquiring the node may lead to deadlocks.
            ReleaseAllCores();

            lock (_callbackMonitor)
            {
                IRequestBuilderCallback builderCallback = _requestEntry.Builder as IRequestBuilderCallback;
                ErrorUtilities.VerifyThrow(_yieldThreadId != -1, "Cannot call Reacquire() before Yield().");
                ErrorUtilities.VerifyThrow(_yieldThreadId == Thread.CurrentThread.ManagedThreadId, "Cannot call Reacquire() on thread {0} when Yield() was called on thread {1}", Thread.CurrentThread.ManagedThreadId, _yieldThreadId);
                MSBuildEventSource.Log.ExecuteTaskYieldStop(_taskLoggingContext.TaskName, _taskLoggingContext.BuildEventContext.TaskId);
                MSBuildEventSource.Log.ExecuteTaskReacquireStart(_taskLoggingContext.TaskName, _taskLoggingContext.BuildEventContext.TaskId);
                builderCallback.Reacquire();
                MSBuildEventSource.Log.ExecuteTaskReacquireStop(_taskLoggingContext.TaskName, _taskLoggingContext.BuildEventContext.TaskId);
                _yieldThreadId = -1;
            }
        }

        #endregion

        #region IBuildEngine Members

        /// <summary>
        /// Logs an error event for the current task
        /// Thread safe.
        /// </summary>
        /// <param name="e">The event args</param>
        public void LogErrorEvent(Microsoft.Build.Framework.BuildErrorEventArgs e)
        {
            lock (_callbackMonitor)
            {
                ErrorUtilities.VerifyThrowArgumentNull(e, nameof(e));

                if (!_activeProxy)
                {
                    // The task has been logging on another thread, typically
                    // because of logging a spawned process's output, and has
                    // not terminated this logging before it returned. This is common
                    // enough that we don't want to crash and break the entire build. But
                    // we don't have any good way to log it any more, as not only has this task
                    // finished, the whole build might have finished! The task author will
                    // just have to figure out that their task has a bug by themselves.
                    if (s_breakOnLogAfterTaskReturns)
                    {
                        Trace.Fail(String.Format(CultureInfo.CurrentUICulture, "Task at {0}, after already returning, attempted to log '{1}'", _taskLocation.ToString(), e.Message));
                    }

                    return;
                }

                // If we are in building across process we need the events to be serializable. This method will 
                // check to see if we are building with multiple process and if the event is serializable. It will 
                // also log a warning if the event is not serializable and drop the logging message.
                if (IsRunningMultipleNodes && !IsEventSerializable(e))
                {
                    return;
                }

                if (_convertErrorsToWarnings)
                {
                    // Convert the error into a warning.  We do this because the whole point of 
                    // ContinueOnError is that a project author expects that the task might fail,
                    // but wants to ignore the failures.  This implies that we shouldn't be logging
                    // errors either, because you should never have a successful build with errors.
                    BuildWarningEventArgs warningEvent = new BuildWarningEventArgs
                            (
                                e.Subcategory,
                                e.Code,
                                e.File,
                                e.LineNumber,
                                e.ColumnNumber,
                                e.EndLineNumber,
                                e.EndColumnNumber,
                                e.Message,
                                e.HelpKeyword,
                                e.SenderName
                            );

                    warningEvent.BuildEventContext = _taskLoggingContext.BuildEventContext;
                    _taskLoggingContext.LoggingService.LogBuildEvent(warningEvent);

                    // Log a message explaining why we converted the previous error into a warning.
                    _taskLoggingContext.LoggingService.LogComment(_taskLoggingContext.BuildEventContext, MessageImportance.Normal, "ErrorConvertedIntoWarning");
                }
                else
                {
                    e.BuildEventContext = _taskLoggingContext.BuildEventContext;
                    _taskLoggingContext.LoggingService.LogBuildEvent(e);
                    _taskLoggingContext.HasLoggedErrors = true;
                }
            }
        }

        /// <summary>
        /// Logs a warning event for the current task
        /// Thread safe.
        /// </summary>
        /// <param name="e">The event args</param>
        public void LogWarningEvent(Microsoft.Build.Framework.BuildWarningEventArgs e)
        {
            lock (_callbackMonitor)
            {
                ErrorUtilities.VerifyThrowArgumentNull(e, nameof(e));

                if (!_activeProxy)
                {
                    // The task has been logging on another thread, typically
                    // because of logging a spawned process's output, and has
                    // not terminated this logging before it returned. This is common
                    // enough that we don't want to crash and break the entire build. But
                    // we don't have any good way to log it any more, as not only has this task
                    // finished, the whole build might have finished! The task author will
                    // just have to figure out that their task has a bug by themselves.
                    if (s_breakOnLogAfterTaskReturns)
                    {
                        Trace.Fail(String.Format(CultureInfo.CurrentUICulture, "Task at {0}, after already returning, attempted to log '{1}'", _taskLocation.ToString(), e.Message));
                    }

                    return;
                }

                // If we are in building across process we need the events to be serializable. This method will 
                // check to see if we are building with multiple process and if the event is serializable. It will 
                // also log a warning if the event is not serializable and drop the logging message.
                if (IsRunningMultipleNodes && !IsEventSerializable(e))
                {
                    return;
                }

                e.BuildEventContext = _taskLoggingContext.BuildEventContext;
                _taskLoggingContext.LoggingService.LogBuildEvent(e);
            }
        }

        /// <summary>
        /// Logs a message event for the current task
        /// Thread safe.
        /// </summary>
        /// <param name="e">The event args</param>
        public void LogMessageEvent(Microsoft.Build.Framework.BuildMessageEventArgs e)
        {
            lock (_callbackMonitor)
            {
                ErrorUtilities.VerifyThrowArgumentNull(e, nameof(e));

                if (!_activeProxy)
                {
                    // The task has been logging on another thread, typically
                    // because of logging a spawned process's output, and has
                    // not terminated this logging before it returned. This is common
                    // enough that we don't want to crash and break the entire build. But
                    // we don't have any good way to log it any more, as not only has this task
                    // finished, the whole build might have finished! The task author will
                    // just have to figure out that their task has a bug by themselves.
                    if (s_breakOnLogAfterTaskReturns)
                    {
                        Trace.Fail(String.Format(CultureInfo.CurrentUICulture, "Task at {0}, after already returning, attempted to log '{1}'", _taskLocation.ToString(), e.Message));
                    }

                    return;
                }

                // If we are in building across process we need the events to be serializable. This method will 
                // check to see if we are building with multiple process and if the event is serializable. It will 
                // also log a warning if the event is not serializable and drop the logging message.
                if (IsRunningMultipleNodes && !IsEventSerializable(e))
                {
                    return;
                }

                e.BuildEventContext = _taskLoggingContext.BuildEventContext;
                _taskLoggingContext.LoggingService.LogBuildEvent(e);
            }
        }

        /// <summary>
        /// Logs a custom event for the current task
        /// Thread safe.
        /// </summary>
        /// <param name="e">The event args</param>
        public void LogCustomEvent(Microsoft.Build.Framework.CustomBuildEventArgs e)
        {
            lock (_callbackMonitor)
            {
                ErrorUtilities.VerifyThrowArgumentNull(e, nameof(e));

                if (!_activeProxy)
                {
                    // The task has been logging on another thread, typically
                    // because of logging a spawned process's output, and has
                    // not terminated this logging before it returned. This is common
                    // enough that we don't want to crash and break the entire build. But
                    // we don't have any good way to log it any more, as not only has this task
                    // finished, the whole build might have finished! The task author will
                    // just have to figure out that their task has a bug by themselves.
                    if (s_breakOnLogAfterTaskReturns)
                    {
                        Trace.Fail(String.Format(CultureInfo.CurrentUICulture, "Task at {0}, after already returning, attempted to log '{1}'", _taskLocation.ToString(), e.Message));
                    }

                    return;
                }

                // If we are in building across process we need the events to be serializable. This method will 
                // check to see if we are building with multiple process and if the event is serializable. It will 
                // also log a warning if the event is not serializable and drop the logging message.
                if (IsRunningMultipleNodes && !IsEventSerializable(e))
                {
                    return;
                }

                e.BuildEventContext = _taskLoggingContext.BuildEventContext;
                _taskLoggingContext.LoggingService.LogBuildEvent(e);
            }
        }

        /// <summary>
        /// Builds a single project file
        /// Thread safe.
        /// </summary>
        /// <param name="projectFileName">The project file name</param>
        /// <param name="targetNames">The set of targets to build.</param>
        /// <param name="globalProperties">The global properties to use</param>
        /// <param name="targetOutputs">The outputs from the targets</param>
        /// <returns>True on success, false otherwise.</returns>
        public bool BuildProjectFile(string projectFileName, string[] targetNames, System.Collections.IDictionary globalProperties, System.Collections.IDictionary targetOutputs)
        {
            VerifyActiveProxy();
            return BuildProjectFile(projectFileName, targetNames, globalProperties, targetOutputs, null);
        }

        #endregion

        #region IBuildEngine4 Members

        /// <summary>
        /// Disposes of all of the objects with the specified lifetime.
        /// </summary>
        public void RegisterTaskObject(object key, object obj, RegisteredTaskObjectLifetime lifetime, bool allowEarlyCollection)
        {
            var objectCache = (IRegisteredTaskObjectCache)_host.GetComponent(BuildComponentType.RegisteredTaskObjectCache);
            objectCache.RegisterTaskObject(key, obj, lifetime, allowEarlyCollection);
        }

        /// <summary>
        /// Gets a previously registered task object.
        /// </summary>
        public object GetRegisteredTaskObject(object key, RegisteredTaskObjectLifetime lifetime)
        {
            var objectCache = (IRegisteredTaskObjectCache)_host.GetComponent(BuildComponentType.RegisteredTaskObjectCache);
            return objectCache.GetRegisteredTaskObject(key, lifetime);
        }

        /// <summary>
        /// Unregisters a task object.
        /// </summary>
        public object UnregisterTaskObject(object key, RegisteredTaskObjectLifetime lifetime)
        {
            var objectCache = (IRegisteredTaskObjectCache)_host.GetComponent(BuildComponentType.RegisteredTaskObjectCache);
            return objectCache.UnregisterTaskObject(key, lifetime);
        }

        #endregion

        #region BuildEngine5 Members

        /// <summary>
        /// Logs a telemetry event for the current task.
        /// </summary>
        /// <param name="eventName">The event name.</param>
        /// <param name="properties">The list of properties associated with the event.</param>
        public void LogTelemetry(string eventName, IDictionary<string, string> properties)
        {
            lock (_callbackMonitor)
            {
                ErrorUtilities.VerifyThrowArgumentNull(eventName, nameof(eventName));

                if (!_activeProxy)
                {
                    // The task has been logging on another thread, typically
                    // because of logging a spawned process's output, and has
                    // not terminated this logging before it returned. This is common
                    // enough that we don't want to crash and break the entire build. But
                    // we don't have any good way to log it any more, as not only has this task
                    // finished, the whole build might have finished! The task author will
                    // just have to figure out that their task has a bug by themselves.
                    if (s_breakOnLogAfterTaskReturns)
                    {
                        Trace.Fail(String.Format(CultureInfo.CurrentUICulture, "Task at {0}, after already returning, attempted to log telemetry event '{1}'", _taskLocation.ToString(), eventName));
                    }

                    return;
                }

                _taskLoggingContext.LoggingService.LogTelemetry(_taskLoggingContext.BuildEventContext, eventName, properties);
            }
        }

        #endregion

        #region IBuildEngine6 Members

        /// <summary>
        /// Gets the global properties for the current project.
        /// </summary>
        /// <returns>An <see cref="IReadOnlyDictionary{String, String}" /> containing the global properties of the current project.</returns>
        public IReadOnlyDictionary<string, string> GetGlobalProperties()
        {
            return _requestEntry.RequestConfiguration.GlobalProperties.ToDictionary();
        }

        #endregion

        #region IBuildEngine7 Members

        /// <summary>
        /// Enables or disables emitting a default error when a task fails without logging errors
        /// </summary>
        public bool AllowFailureWithoutError { get; set; } = false;

        #endregion

        #region IBuildEngine8 Members

        private ICollection<string> _warningsAsErrors;

        /// <summary>
        /// Contains all warnings that should be logged as errors.
        /// Non-null empty set when all warnings should be treated as errors.
        /// </summary>
        private ICollection<string> WarningsAsErrors
        {
            get
            {
                // Test compatibility
                if(_taskLoggingContext == null)
                {
                    return null;
                }

                return _warningsAsErrors ??= _taskLoggingContext.GetWarningsAsErrors();
            }
        }

        private ICollection<string> _warningsAsMessages;

        /// <summary>
        /// Contains all warnings that should be logged as errors.
        /// Non-null empty set when all warnings should be treated as errors.
        /// </summary>
        private ICollection<string> WarningsAsMessages
        {
            get
            {
                // Test compatibility
                if (_taskLoggingContext == null)
                {
                    return null;
                }

                return _warningsAsMessages ??= _taskLoggingContext.GetWarningsAsMessages();
            }
        }

        /// <summary>
        /// Determines if the given warning should be treated as an error.
        /// </summary>
        /// <param name="warningCode"></param>
        /// <returns>True if the warning should not be treated as a message and WarningsAsErrors is an empty set or contains the given warning code.</returns>
        public bool ShouldTreatWarningAsError(string warningCode)
        {
            // Warnings as messages overrides warnings as errors.
            if (WarningsAsErrors == null || WarningsAsMessages?.Contains(warningCode) == true)
            {
                return false;
            }

            // An empty set means all warnings are errors.
            return WarningsAsErrors.Count == 0 || WarningsAsErrors.Contains(warningCode);
        }

        #endregion

        #region IBuildEngine9 Members

        /// <summary>
        /// Additional cores granted to the task by the scheduler. Does not include the one implicit core automatically granted to all tasks.
        /// </summary>
        private int _additionalAcquiredCores = 0;

        /// <summary>
        /// True if the one implicit core has been allocated by <see cref="RequestCores"/>, false otherwise.
        /// </summary>
        private bool _isImplicitCoreUsed = false;

        /// <summary>
        /// Total number of cores granted to the task, including the one implicit core.
        /// </summary>
        private int TotalAcquiredCores => _additionalAcquiredCores + (_isImplicitCoreUsed ? 1 : 0);

        /// <summary>
        /// Allocates shared CPU resources. Called by a task when it's about to do potentially multi-threaded/multi-process work.
        /// </summary>
        /// <param name="requestedCores">The number of cores the task wants to use.</param>
        /// <returns>The number of cores the task is allowed to use given the current state of the build. This number is always between
        /// 1 and <paramref name="requestedCores"/>. If the task has allocated its one implicit core, this call may block, waiting for
        /// at least one core to become available.</returns>
        public int RequestCores(int requestedCores)
        {
            ErrorUtilities.VerifyThrowArgumentOutOfRange(requestedCores > 0, nameof(requestedCores));

            lock (_callbackMonitor)
            {
                IRequestBuilderCallback builderCallback = _requestEntry.Builder as IRequestBuilderCallback;

                int coresAcquired = 0;
                bool allocatingImplicitCore = false;
                if (_isImplicitCoreUsed)
                {
                    coresAcquired = builderCallback.RequestCores(_callbackMonitor, requestedCores, waitForCores: true);
                }
                else
                {
                    _isImplicitCoreUsed = true;
                    allocatingImplicitCore = true;
                    if (requestedCores > 1)
                    {
                        coresAcquired = builderCallback.RequestCores(_callbackMonitor, requestedCores - 1, waitForCores: false);
                    }
                }
                _additionalAcquiredCores += coresAcquired;

                if (allocatingImplicitCore)
                {
                    // Pad the result with the one implicit core if it was still available.
                    // This ensures that first call never blocks and always returns >= 1.
                    coresAcquired++;
                }

                Debug.Assert(coresAcquired >= 1);
                if (LoggingContext.IsValid)
                {
                    LoggingContext.LogComment(MessageImportance.Low, "TaskAcquiredCores", _taskLoggingContext.TaskName,
                        requestedCores, coresAcquired, TotalAcquiredCores);
                }
                return coresAcquired;
            }
        }

        /// <summary>
        /// Frees shared CPU resources. Called by a task when it's finished doing multi-threaded/multi-process work.
        /// </summary>
        /// <param name="coresToRelease">The number of cores the task wants to return. This number must be between 0 and the number of cores
        /// granted and not yet released.</param>
        public void ReleaseCores(int coresToRelease)
        {
            ErrorUtilities.VerifyThrowArgumentOutOfRange(coresToRelease > 0, nameof(coresToRelease));

            lock (_callbackMonitor)
            {
                int coresBeingReleased = coresToRelease;
                int previousTotalAcquiredCores = TotalAcquiredCores;

                if (_isImplicitCoreUsed && coresBeingReleased > _additionalAcquiredCores)
                {
                    // Release the implicit core last, i.e. only if we're asked to release everything.
                    coresBeingReleased -= 1;
                    _isImplicitCoreUsed = false;
                }

                coresBeingReleased = Math.Min(coresBeingReleased, _additionalAcquiredCores);
                if (coresBeingReleased >= 1)
                {
                    IRequestBuilderCallback builderCallback = _requestEntry.Builder as IRequestBuilderCallback;
                    builderCallback.ReleaseCores(coresBeingReleased);
                    _additionalAcquiredCores -= coresBeingReleased;
                }

                if (LoggingContext.IsValid)
                {
                    if (TotalAcquiredCores == previousTotalAcquiredCores - coresToRelease)
                    {
                        LoggingContext.LogComment(MessageImportance.Low, "TaskReleasedCores", _taskLoggingContext.TaskName,
                            coresToRelease, TotalAcquiredCores);
                    }
                    else
                    {
                        LoggingContext.LogComment(MessageImportance.Low, "TaskReleasedCoresWarning", _taskLoggingContext.TaskName,
                            coresToRelease, previousTotalAcquiredCores, TotalAcquiredCores);
                    }
                }
            }
        }

        /// <summary>
        /// Frees all CPU resources granted so far.
        /// </summary>
        internal void ReleaseAllCores()
        {
            int coresToRelease = TotalAcquiredCores;
            if (coresToRelease > 0)
            {
                ReleaseCores(coresToRelease);
            }
        }

        #endregion

        /// <summary>
        /// Called by the internal MSBuild task.
        /// Does not take the lock because it is called by another request builder thread.
        /// </summary>
        public async Task<BuildEngineResult> InternalBuildProjects(string[] projectFileNames, string[] targetNames, IDictionary[] globalProperties, IList<String>[] undefineProperties, string[] toolsVersion, bool returnTargetOutputs, bool skipNonexistentTargets = false)
        {
            ErrorUtilities.VerifyThrowArgumentNull(projectFileNames, nameof(projectFileNames));
            ErrorUtilities.VerifyThrowArgumentNull(globalProperties, nameof(globalProperties));
            VerifyActiveProxy();

            BuildEngineResult result;
            if (projectFileNames.Length == 1 && projectFileNames[0] == null && globalProperties[0] == null && (undefineProperties == null || undefineProperties[0] == null) && toolsVersion[0] == null)
            {
                bool overallSuccess = true;
                List<IDictionary<string, ITaskItem[]>> targetOutputsPerProject = null;

                // This is really a legacy CallTarget invocation
                ITargetResult[] results = await _targetBuilderCallback.LegacyCallTarget(targetNames, ContinueOnError, _taskLocation);

                if (returnTargetOutputs)
                {
                    targetOutputsPerProject = new List<IDictionary<string, ITaskItem[]>>(1);
                    targetOutputsPerProject.Add(new Dictionary<string, ITaskItem[]>(StringComparer.OrdinalIgnoreCase));
                }

                for (int i = 0; i < targetNames.Length; i++)
                {
                    targetOutputsPerProject[0][targetNames[i]] = results[i].Items;
                    if (results[i].ResultCode == TargetResultCode.Failure)
                    {
                        overallSuccess = false;
                    }
                }

                result = new BuildEngineResult(overallSuccess, targetOutputsPerProject);
                BuildRequestsSucceeded = overallSuccess;
            }
            else
            {
                // Post the request, then yield up the thread.
                result = await BuildProjectFilesInParallelAsync(projectFileNames, targetNames, globalProperties, undefineProperties, toolsVersion, true /* ask that target outputs are returned in the buildengineresult */, skipNonexistentTargets);
            }

            return result;
        }

#if FEATURE_APPDOMAIN
        /// <inheritdoc />
        /// <summary>
        /// InitializeLifetimeService is called when the remote object is activated.
        /// This method will determine how long the lifetime for the object will be.
        /// </summary>
        /// <returns>The lease object to control this object's lifetime.</returns>
        public override object InitializeLifetimeService()
        {
            lock (_callbackMonitor)
            {
                VerifyActiveProxy();

                // Each MarshalByRef object has a reference to the service which
                // controls how long the remote object will stay around
                ILease lease = (ILease)base.InitializeLifetimeService();

                // Set how long a lease should be initially. Once a lease expires
                // the remote object will be disconnected and it will be marked as being availiable 
                // for garbage collection
                int initialLeaseTime = 1;

                string initialLeaseTimeFromEnvironment = Environment.GetEnvironmentVariable("MSBUILDENGINEPROXYINITIALLEASETIME");

                if (!String.IsNullOrEmpty(initialLeaseTimeFromEnvironment))
                {
                    int leaseTimeFromEnvironment;
                    if (int.TryParse(initialLeaseTimeFromEnvironment, out leaseTimeFromEnvironment) && leaseTimeFromEnvironment > 0)
                    {
                        initialLeaseTime = leaseTimeFromEnvironment;
                    }
                }

                lease.InitialLeaseTime = TimeSpan.FromMinutes(initialLeaseTime);

                // Make a new client sponsor. A client sponsor is a class
                // which will respond to a lease renewal request and will
                // increase the lease time allowing the object to stay in memory
                _sponsor = new ClientSponsor();

                // When a new lease is requested lets make it last 1 minutes longer. 
                int leaseExtensionTime = 1;

                string leaseExtensionTimeFromEnvironment = Environment.GetEnvironmentVariable("MSBUILDENGINEPROXYLEASEEXTENSIONTIME");
                if (!String.IsNullOrEmpty(leaseExtensionTimeFromEnvironment))
                {
                    int leaseExtensionFromEnvironment;
                    if (int.TryParse(leaseExtensionTimeFromEnvironment, out leaseExtensionFromEnvironment) && leaseExtensionFromEnvironment > 0)
                    {
                        leaseExtensionTime = leaseExtensionFromEnvironment;
                    }
                }

                _sponsor.RenewalTime = TimeSpan.FromMinutes(leaseExtensionTime);

                // Register the sponsor which will increase lease timeouts when the lease expires
                lease.Register(_sponsor);

                return lease;
            }
        }
#endif

        /// <summary>
        /// Indicates to the TaskHost that it is no longer needed.
        /// Called by TaskBuilder when the task using the EngineProxy is done.
        /// </summary>
        internal void MarkAsInactive()
        {
            lock (_callbackMonitor)
            {
                VerifyActiveProxy();
                _activeProxy = false;

                ReleaseAllCores();

                // Since the task has a pointer to this class it may store it in a static field. Null out
                // internal data so the leak of this object doesn't lead to a major memory leak.            
                _host = null;
                _requestEntry = null;

                // Don't bother clearing the tiny task location
                _taskLoggingContext = null;
                _targetBuilderCallback = null;

#if FEATURE_APPDOMAIN
                // Clear out the sponsor (who is responsible for keeping the EngineProxy remoting lease alive until the task is done)
                // this will be null if the engine proxy was never sent across an AppDomain boundary.
                if (_sponsor != null)
                {
                    ILease lease = (ILease)RemotingServices.GetLifetimeService(this);

                    lease?.Unregister(_sponsor);

                    _sponsor.Close();
                    _sponsor = null;
                }
#endif
            }
        }

        /// <summary>
        /// Determine if the event is serializable. If we are running with multiple nodes we need to make sure the logging events are serializable. If not
        /// we need to log a warning.
        /// </summary>
        internal bool IsEventSerializable(BuildEventArgs e)
        {
            if (!e.GetType().GetTypeInfo().IsSerializable)
            {
                _taskLoggingContext.LogWarning(null, new BuildEventFileInfo(string.Empty), "ExpectedEventToBeSerializable", e.GetType().Name);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Async version of BuildProjectFilesInParallel.
        /// </summary>
        /// <param name="projectFileNames">The list of projects to build</param>
        /// <param name="targetNames">The set of targets to build</param>
        /// <param name="globalProperties">The global properties to use for each project</param>
        /// <param name="undefineProperties">The list of global properties to undefine</param>
        /// <param name="toolsVersion">The tools versions to use</param>
        /// <param name="returnTargetOutputs">Should the target outputs be returned in the BuildEngineResult</param>
        /// <param name="skipNonexistentTargets">If set, skip targets that are not defined in the projects to be built.</param>
        /// <returns>A Task returning a structure containing the result of the build, success or failure and the list of target outputs per project</returns>
        private async Task<BuildEngineResult> BuildProjectFilesInParallelAsync(string[] projectFileNames, string[] targetNames, IDictionary[] globalProperties, IList<String>[] undefineProperties, string[] toolsVersion, bool returnTargetOutputs, bool skipNonexistentTargets = false)
        {
            ErrorUtilities.VerifyThrowArgumentNull(projectFileNames, nameof(projectFileNames));
            ErrorUtilities.VerifyThrowArgumentNull(globalProperties, nameof(globalProperties));
            VerifyActiveProxy();

            List<IDictionary<string, ITaskItem[]>> targetOutputsPerProject = null;

#if FEATURE_FILE_TRACKER
            using (FullTracking.Suspend())
#endif
            {
                bool overallSuccess = true;

                if (projectFileNames.Length == 1 && projectFileNames[0] == null && globalProperties[0] == null && (undefineProperties == null || undefineProperties[0] == null) && toolsVersion[0] == null)
                {
                    // This is really a legacy CallTarget invocation
                    ITargetResult[] results = await _targetBuilderCallback.LegacyCallTarget(targetNames, ContinueOnError, _taskLocation);

                    if (returnTargetOutputs)
                    {
                        targetOutputsPerProject = new List<IDictionary<string, ITaskItem[]>>(1)
                        {
                            new Dictionary<string, ITaskItem[]>(StringComparer.OrdinalIgnoreCase)
                        };
                    }

                    for (int i = 0; i < targetNames.Length; i++)
                    {
                        targetOutputsPerProject[0][targetNames[i]] = results[i].Items;
                        if (results[i].ResultCode == TargetResultCode.Failure)
                        {
                            overallSuccess = false;
                        }
                    }
                }
                else
                {
                    // UNDONE: (Refactor) Investigate making this a ReadOnly collection of some sort.  
                    PropertyDictionary<ProjectPropertyInstance>[] propertyDictionaries = new PropertyDictionary<ProjectPropertyInstance>[projectFileNames.Length];

                    for (int i = 0; i < projectFileNames.Length; i++)
                    {
                        // Copy in the original project's global properties
                        propertyDictionaries[i] = new PropertyDictionary<ProjectPropertyInstance>(_requestEntry.RequestConfiguration.Project.GlobalPropertiesDictionary);

                        // Now add/replace any which may have been specified.
                        if (globalProperties[i] != null)
                        {
                            foreach (DictionaryEntry entry in globalProperties[i])
                            {
                                propertyDictionaries[i].Set(ProjectPropertyInstance.Create(entry.Key as string, entry.Value as string, _taskLocation));
                            }
                        }

                        // Finally, remove any which were requested to be removed.
                        if (undefineProperties?[i] != null)
                        {
                            foreach (string property in undefineProperties[i])
                            {
                                propertyDictionaries[i].Remove(property);
                            }
                        }
                    }

                    IRequestBuilderCallback builderCallback = _requestEntry.Builder as IRequestBuilderCallback;
                    BuildResult[] results = await builderCallback.BuildProjects(
                        projectFileNames,
                        propertyDictionaries,
                        toolsVersion ?? Array.Empty<string>(),
                        targetNames ?? Array.Empty<string>(),
                        waitForResults: true,
                        skipNonexistentTargets: skipNonexistentTargets);

                    // Even if one of the projects fails to build and therefore has no outputs, it should still have an entry in the results array (albeit with an empty list in it)
                    ErrorUtilities.VerifyThrow(results.Length == projectFileNames.Length, "{0}!={1}.", results.Length, projectFileNames.Length);

                    if (returnTargetOutputs)
                    {
                        targetOutputsPerProject = new List<IDictionary<string, ITaskItem[]>>(results.Length);
                    }

                    // Now walk through the results, and report that subset which was asked for.
                    for (int i = 0; i < results.Length; i++)
                    {
                        targetOutputsPerProject?.Add(new Dictionary<string, ITaskItem[]>(StringComparer.OrdinalIgnoreCase));

                        foreach (KeyValuePair<string, TargetResult> resultEntry in results[i].ResultsByTarget)
                        {
                            if (targetOutputsPerProject != null)
                            {
                                // We need to clone the task items because if we did not then we would be passing live references
                                // out to the caller of the msbuild callback and any modifications they make to those items
                                // would appear in the results cache.
                                ITaskItem[] clonedTaskItem = new ITaskItem[resultEntry.Value.Items.Length];
                                for (int j = 0; j < resultEntry.Value.Items.Length; j++)
                                {
                                    clonedTaskItem[j] = ((TaskItem)resultEntry.Value.Items[j]).DeepClone();
                                }

                                targetOutputsPerProject[i][resultEntry.Key] = clonedTaskItem;
                            }
                        }

                        if (results[i].OverallResult == BuildResultCode.Failure)
                        {
                            overallSuccess = false;
                        }

                        if (!string.IsNullOrEmpty(results[i].SchedulerInducedError))
                        {
                            LoggingContext.LogErrorFromText(
                                subcategoryResourceName: null,
                                errorCode: null,
                                helpKeyword: null,
                                file: new BuildEventFileInfo(ProjectFileOfTaskNode, LineNumberOfTaskNode, ColumnNumberOfTaskNode),
                                message: results[i].SchedulerInducedError);
                        }
                    }

                    ErrorUtilities.VerifyThrow(results.Length == projectFileNames.Length || !overallSuccess, "The number of results returned {0} cannot be less than the number of project files {1} unless one of the results indicated failure.", results.Length, projectFileNames.Length);
                }

                BuildRequestsSucceeded = overallSuccess;

                return new BuildEngineResult(overallSuccess, targetOutputsPerProject);
            }
        }

        /// <summary>
        /// Verify the task host is active or not
        /// Thread safe.
        /// </summary>
        private void VerifyActiveProxy()
        {
            ErrorUtilities.VerifyThrow(_activeProxy, "Attempted to use an inactive task host.");
        }
    }
}
