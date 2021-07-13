// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;

using Microsoft.Build.Framework;
using Microsoft.Build.BuildEngine.Shared;

namespace Microsoft.Build.BuildEngine
{
    /// <summary>
    /// This class is a representation of a possible remote work item processing subsystem.
    /// Currently wrapped by LocalNode.
    /// Owns the Engine on a child node.
    /// </summary>
    internal class Node
    {
        #region Constructors
        /// <summary>
        /// Initialize the node with the id and the callback object
        /// </summary>
        internal Node
        (
            int nodeId,
            LoggerDescription[] nodeLoggers,
            IEngineCallback parentCallback,
            BuildPropertyGroup parentGlobalProperties,
            ToolsetDefinitionLocations toolsetSearchLocations,
            string parentStartupDirectory
        )
        {
            this.nodeId = nodeId;
            this.parentCallback = parentCallback;

            this.exitNodeEvent = new ManualResetEvent(false);
            this.buildRequests = new Queue<BuildRequest>();

            this.requestToLocalIdMapping = new Hashtable();
            this.lastRequestIdUsed = 0;

            this.centralizedLogging = false;
            this.nodeLoggers = nodeLoggers;

            this.localEngine = null;
            this.launchedEngineLoopThread = false;
            this.nodeShutdown = false;
            this.parentGlobalProperties = parentGlobalProperties;
            this.toolsetSearchLocations = toolsetSearchLocations;
            this.parentStartupDirectory = parentStartupDirectory;
        }
        #endregion

        #region Properties
        internal Introspector Introspector
        {
            get
            {
                if (localEngine != null)
                {
                    return localEngine.Introspector;
                }
                return null;
            }
        }
        /// <summary>
        /// This property returns 0 if profiling is not enabled and otherwise returns the
        /// total time spent inside user task code
        /// </summary>
        internal int TotalTaskTime
        {
            get
            {
                return totalTaskTime;
            }
        }

        internal int NodeId
        {
            get { return nodeId; }
        }
        #endregion

        #region Method used to call from local engine to the host (and parent engine)

        /// <summary>
        /// This method posts outputs of a build to the node that made the request
        /// </summary>
        /// <param name="buildResult"></param>
        internal void PostBuildResultToHost(BuildResult buildResult)
        {
            try
            {
                outProcLoggingService.ProcessPostedLoggingEvents();
                parentCallback.PostBuildResultToHost(buildResult);
            }
            catch (Exception e)
            {
                ReportUnhandledError(e);
            }
        }

        internal void PostBuildRequestToHost(BuildRequest currentRequest)
        {
            // Since this request is going back to the host the local node should not have a project object for it
            ErrorUtilities.VerifyThrow(currentRequest.ProjectToBuild == null, "Should not have a project object");
            // Add the request to the mapping hashtable so that we can recognize the outputs
            CacheScope cacheScope = localEngine.CacheManager.GetCacheScope(currentRequest.ProjectFileName, currentRequest.GlobalProperties, currentRequest.ToolsetVersion, CacheContentType.BuildResults);
            NodeRequestMapping nodeRequestMapping =
                new NodeRequestMapping(currentRequest.HandleId, currentRequest.RequestId, cacheScope );
            lock (requestToLocalIdMapping)
            {
                requestToLocalIdMapping.Add(lastRequestIdUsed, nodeRequestMapping);
                // Keep the request id local for this node
                currentRequest.RequestId = lastRequestIdUsed;
                lastRequestIdUsed++;
            }

            // Find the original external request that caused us to run this task
            TaskExecutionContext taskExecutionContext = localEngine.EngineCallback.GetTaskContextFromHandleId(currentRequest.HandleId);
            while (!taskExecutionContext.BuildContext.BuildRequest.IsExternalRequest)
            {
                ErrorUtilities.VerifyThrow(taskExecutionContext.BuildContext.BuildRequest.IsGeneratedRequest,
                                           "Must be a generated request");

                taskExecutionContext =
                   localEngine.EngineCallback.GetTaskContextFromHandleId(taskExecutionContext.BuildContext.BuildRequest.HandleId);
            }

            //Modify the request with the data that is expected by the parent engine
            currentRequest.HandleId = taskExecutionContext.BuildContext.BuildRequest.HandleId;
            currentRequest.NodeIndex = taskExecutionContext.BuildContext.BuildRequest.NodeIndex;

            try
            {
                outProcLoggingService.ProcessPostedLoggingEvents();
                parentCallback.PostBuildRequestsToHost(new BuildRequest[] { currentRequest });
            }
            catch (Exception e)
            {
                ReportUnhandledError(e);
            }
        }

        /// <summary>
        /// Posts the given set of cache entries to the parent engine.
        /// </summary>
        /// <param name="entries"></param>
        /// <param name="scopeName"></param>
        /// <param name="scopeProperties"></param>
        internal Exception PostCacheEntriesToHost(CacheEntry[] entries, string scopeName, BuildPropertyGroup scopeProperties, string scopeToolsVersion, CacheContentType cacheContentType)
        {
            try
            {
                return parentCallback.PostCacheEntriesToHost(this.nodeId /* ignored */, entries, scopeName, scopeProperties, scopeToolsVersion, cacheContentType);
            }
            catch (Exception e)
            {
                ReportUnhandledError(e);
                return null;
            }
        }

        /// <summary>
        /// Retrieves the requested set of cache entries from the engine.
        /// </summary>
        /// <param name="names"></param>
        /// <param name="scopeName"></param>
        /// <param name="scopeProperties"></param>
        /// <returns></returns>
        internal CacheEntry[] GetCachedEntriesFromHost(string[] names, string scopeName, BuildPropertyGroup scopeProperties, string scopeToolsVersion, CacheContentType cacheContentType)
        {
            try
            {
                return parentCallback.GetCachedEntriesFromHost(this.nodeId /* ignored */, names, scopeName, scopeProperties, scopeToolsVersion, cacheContentType);
            }
            catch (Exception e)
            {
                ReportUnhandledError(e);
                return new CacheEntry[0];
            }
        }

        internal void PostLoggingMessagesToHost(NodeLoggingEvent[] nodeLoggingEvents)
        {
            try
            {
                parentCallback.PostLoggingMessagesToHost(nodeId, nodeLoggingEvents);
            }
            catch (Exception e)
            {
                ReportUnhandledError(e);
            }
        }

        internal void PostStatus(NodeStatus nodeStatus, bool blockUntilSent)
        {
            try
            {
                PostStatusThrow(nodeStatus, blockUntilSent);
            }
            catch (Exception e)
            {
                ReportUnhandledError(e);
            }
        }

        /// <summary>
        /// A variation of PostStatus that throws instead of calling ReportUnhandledError
        /// if there's a problem. This allows ReportUnhandledError itself to post status
        /// without the possibility of a loop.
        /// </summary>
        internal void PostStatusThrow(NodeStatus nodeStatus, bool blockUntilSent)
        {
            parentCallback.PostStatus(nodeId, nodeStatus, blockUntilSent);
        }
        #endregion

        #region Methods called from the host (and parent engine)

        /// <summary>
        /// This method lets the host request an evaluation of a build request. A local engine
        /// will be created if necessary.
        /// </summary>
        internal void PostBuildRequest
        (
            BuildRequest buildRequest
        )
        {
            buildRequest.RestoreNonSerializedDefaults();
            buildRequest.NodeIndex = EngineCallback.inProcNode;
            // A request has come from the parent engine, so mark it as external
            buildRequest.IsExternalRequest = true;

            if (localEngine == null)
            {
                // Check if an an engine has been allocated and if not start the thread that will do that
                lock (buildRequests)
                {
                    if (localEngine == null)
                    {
                        if (!launchedEngineLoopThread)
                        {
                            launchedEngineLoopThread = true;
                            ThreadStart threadState = new ThreadStart(this.NodeLocalEngineLoop);
                            Thread taskThread = new Thread(threadState);
                            taskThread.Name = "MSBuild Child Engine";
                            taskThread.SetApartmentState(ApartmentState.STA);
                            taskThread.Start();
                        }
                        buildRequests.Enqueue(buildRequest);
                    }
                    else
                    {
                        localEngine.PostBuildRequest(buildRequest);
                    }
                }
            }
            else
            {
                if (!buildInProgress)
                {
                    buildInProgress = true;
                    // If there are forwarding loggers registered - generate a custom  build started
                    if (nodeLoggers.Length > 0)
                    {
                        localEngine.LoggingServices.LogBuildStarted(EngineLoggingServicesInProc.CENTRAL_ENGINE_EVENTSOURCE);
                        localEngine.LoggingServices.ProcessPostedLoggingEvents();
                    }
                }
                localEngine.PostBuildRequest(buildRequest);
            }
        }

        /// <summary>
        /// This method lets the engine provide node with results of an evaluation it was waiting on.
        /// </summary>
        internal void PostBuildResult(BuildResult buildResult)
        {
            ErrorUtilities.VerifyThrow(localEngine != null, "Local engine should be initialized");

            // Figure out the local handle id
            NodeRequestMapping nodeRequestMapping = null;
            try
            {
                lock (requestToLocalIdMapping)
                {
                    nodeRequestMapping = (NodeRequestMapping)requestToLocalIdMapping[buildResult.RequestId];
                    requestToLocalIdMapping.Remove(buildResult.RequestId);
                }

                if (Engine.debugMode)
                {
                    Console.WriteLine(nodeId + ": Got result for Request Id " + buildResult.RequestId);
                    Console.WriteLine(nodeId + ": Received result for HandleId " + buildResult.HandleId + ":" + buildResult.RequestId + " mapped to " + nodeRequestMapping.HandleId + ":" + nodeRequestMapping.RequestId);
                }

                buildResult.HandleId = nodeRequestMapping.HandleId;
                buildResult.RequestId = nodeRequestMapping.RequestId;
                nodeRequestMapping.AddResultToCache(buildResult);

                // posts the result to the inproc node
                localEngine.Router.PostDoneNotice(0, buildResult);
            }
            catch (Exception ex)
            {
                ReportUnhandledError(ex);
            }
        }

        /// <summary>
        /// Causes the node to safely shutdown and exit
        /// </summary>
        internal void ShutdownNode(NodeShutdownLevel shutdownLevel)
        {
            if (shutdownLevel == NodeShutdownLevel.BuildCompleteSuccess ||
                shutdownLevel == NodeShutdownLevel.BuildCompleteFailure )
            {
                if (Engine.debugMode)
                {
                    Console.WriteLine("Node.Shutdown: NodeId " + nodeId + ": Clearing engine state and intern table ready for node re-use.");
                }

                if (localEngine != null)
                {
                    localEngine.EndingEngineExecution
                        (shutdownLevel == NodeShutdownLevel.BuildCompleteSuccess ? true : false, false);
                    localEngine.ResetPerBuildDataStructures();
                    outProcLoggingService.ProcessPostedLoggingEvents();
                    buildInProgress = false;
                }

                // Clear the static table of interned strings
                BuildProperty.ClearInternTable();
            }
            else
            {
                if (Engine.debugMode)
                {
                    Console.WriteLine("Node.Shutdown: NodeId " + nodeId + ": Shutting down node due to error.");
                }

                // The thread starting the build loop will acquire this lock before starting the loop
                // in order to read the build requests. Acquiring it here ensures that build loop doesn't
                // get started between the check for null and setting of the flag
                lock (buildRequests)
                {
                    if (localEngine == null)
                    {
                        nodeShutdown = true;
                    }
                }

                if (localEngine != null)
                {
                    // Indicate to the child engine that we need to exit
                    localEngine.SetEngineAbortTo(true);
                    // Wait for the exit event which will be raised once the child engine exits
                    exitNodeEvent.WaitOne();
                }
            }
        }

        /// <summary>
        /// This function is used to update the settings of the engine running on the node
        /// </summary>
        internal void UpdateNodeSettings
        (
            bool shouldLogOnlyCriticalEvents,
            bool enableCentralizedLogging,
            bool useBreadthFirstTraversal
        )
        {
            this.logOnlyCriticalEvents = shouldLogOnlyCriticalEvents;
            this.centralizedLogging = enableCentralizedLogging;
            this.useBreadthFirstTraversal = useBreadthFirstTraversal;

            if (localEngine != null)
            {
                localEngine.LoggingServices.OnlyLogCriticalEvents = this.logOnlyCriticalEvents;
                localEngine.PostEngineCommand( new ChangeTraversalTypeCommand( useBreadthFirstTraversal, true ));
            }
        }

        /// <summary>
        /// The coordinating engine is requesting status
        /// </summary>
        internal void RequestStatus(int requestId)
        {
            // Check if the status has been requested before the local
            // engine has been started.
            if (localEngine == null)
            {
                NodeStatus nodeStatus = null;

                lock (buildRequests)
                {
                    nodeStatus = new NodeStatus(requestId, true, buildRequests.Count, 0, 0, false);
                }

                parentCallback.PostStatus(nodeId, nodeStatus, false);
            }
            else
            {
                // Since the local engine has been started - ask it for status
                RequestStatusEngineCommand requestStatus = new RequestStatusEngineCommand(requestId);
                localEngine.PostEngineCommand(requestStatus);
            }
        }

        /// <summary>
        /// This function can be used by the node provider to report a failure which doesn't prevent further
        /// communication with the parent node. The node will attempt to notify the parent of the failure,
        /// send all outstanding logging events and shutdown.
        /// </summary>
        /// <param name="originalException"></param>
        /// <exception cref="Exception">Throws exception (with nested original exception) if reporting to parent fails.</exception>
        internal void ReportUnhandledError(Exception originalException)
        {
            NodeStatus nodeStatus = new NodeStatus(originalException);

            if (Engine.debugMode)
            {
                Console.WriteLine("Node.ReportUnhandledError: " + originalException.Message);
            }

            try
            {
                try
                {
                    PostStatusThrow(nodeStatus, true /* wait for the message to be sent before returning */);
                }
                catch (Exception ex)
                {
                    // If an error occurred while trying to send the original exception to the parent
                    // rethrow the original exception
                    string message = ResourceUtilities.FormatResourceString("FatalErrorOnChildNode", nodeId, ex.Message);

                    ErrorUtilities.LaunchMsBuildDebuggerOnFatalError();

                    throw new Exception(message, originalException);
                }
            }
            finally
            {
                // Makesure we write the exception to a file so even if something goes wrong with the logging or transfer to the parent
                // then we will atleast get the message on disk.
                LocalNode.DumpExceptionToFile(originalException);
            }

            localEngine?.Shutdown();
        }

        /// <summary>
        /// This method can be used by the node provider to report a fatal communication error, after
        /// which further communication with the parent node is no longer possible. The node provider
        /// can optionally provide a stream to which the current node state will be logged in order
        /// to assist with debugging of the problem.
        /// </summary>
        /// <param name="originalException"></param>
        /// <param name="loggingStream"></param>
        /// <exception cref="Exception">Re-throws exception passed in</exception>
        internal void ReportFatalCommunicationError(Exception originalException, TextWriter loggingStream)
        {
            loggingStream?.WriteLine(originalException.ToString());

            string message = ResourceUtilities.FormatResourceString("FatalErrorOnChildNode", nodeId, originalException.Message);

            ErrorUtilities.LaunchMsBuildDebuggerOnFatalError();

            throw new Exception(message, originalException);
        }

        #endregion

        #region Thread procedure for Engine BuildLoop

        private void NodeLocalEngineLoop()
        {
            buildInProgress = true;

            // Create a logging service for this build request
            localEngine =
                new Engine(parentGlobalProperties, toolsetSearchLocations, 1 /* cpus */, true /* child node */, this.nodeId, parentStartupDirectory, null);
            localEngine.Router.ChildMode = true;
            localEngine.Router.ParentNode = this;

            this.outProcLoggingService = new EngineLoggingServicesOutProc(this, localEngine.FlushRequestEvent);

            if (nodeLoggers.Length != 0)
            {
                foreach (LoggerDescription loggerDescription in nodeLoggers)
                {
                    IForwardingLogger newLogger = null;
                    bool exitedDueToError = true;
                    try
                    {
                        newLogger = loggerDescription.CreateForwardingLogger();
                        // Check if the class was not found in the assembly
                        if (newLogger == null)
                        {
                            InternalLoggerException.Throw(null, null, "FatalErrorWhileInitializingLogger", true, loggerDescription.Name);
                        }
                        newLogger.Verbosity = loggerDescription.Verbosity;
                        newLogger.Parameters = loggerDescription.LoggerSwitchParameters;
                        newLogger.NodeId = nodeId;
                        EventRedirector newRedirector = new EventRedirector(loggerDescription.LoggerId, outProcLoggingService);
                        newLogger.BuildEventRedirector = newRedirector;
                        exitedDueToError = false;
                    }
                    // Polite logger failure
                    catch (LoggerException e)
                    {
                        ReportUnhandledError(e);
                    }
                    // Logger class was not found
                    catch (InternalLoggerException e)
                    {
                        ReportUnhandledError(e);
                    }
                    catch (Exception e)
                    {
                        // Wrap the exception in a InternalLoggerException and send it to the parent node
                        string errorCode;
                        string helpKeyword;
                        string message = ResourceUtilities.FormatResourceString(out errorCode, out helpKeyword, "FatalErrorWhileInitializingLogger", loggerDescription.Name);
                        ReportUnhandledError(new InternalLoggerException(message, e, null, errorCode, helpKeyword, true));
                    }

                    // If there was a failure registering loggers, null out the engine pointer
                    if (exitedDueToError)
                    {
                        localEngine = null;
                        return;
                    }

                    localEngine.RegisterLogger(newLogger);
                }

                localEngine.ExternalLoggingServices = outProcLoggingService;
            }

            // Hook up logging service to forward all events to the central engine if necessary
            if (centralizedLogging)
            {
                if (nodeLoggers.Length != 0)
                {
                    localEngine.LoggingServices.ForwardingService = outProcLoggingService;
                    localEngine.ExternalLoggingServices = outProcLoggingService;
                }
                else
                {
                    localEngine.LoggingServices = outProcLoggingService;
                }
            }

            localEngine.LoggingServices.OnlyLogCriticalEvents = this.logOnlyCriticalEvents;

            if (!useBreadthFirstTraversal)
            {
                localEngine.PostEngineCommand(new ChangeTraversalTypeCommand(useBreadthFirstTraversal, true));
            }

            // Post all the requests that passed in while the engine was being constructed
            // into the engine queue
            lock (buildRequests)
            {
                while (buildRequests.Count != 0)
                {
                    BuildRequest buildRequest = buildRequests.Dequeue();
                    localEngine.PostBuildRequest(buildRequest);
                }
            }

            try
            {
                // If there are forwarding loggers registered - generate a custom  build started
                if (nodeLoggers.Length > 0)
                {
                    localEngine.LoggingServices.LogBuildStarted(EngineLoggingServicesInProc.CENTRAL_ENGINE_EVENTSOURCE);
                    localEngine.LoggingServices.ProcessPostedLoggingEvents();
                }

                // Trigger the actual build if shutdown was not called while the engine was being initialized
                if (!nodeShutdown)
                {
                    localEngine.EngineBuildLoop(null);
                }
            }
            catch (Exception e)
            {
                // Unhandled exception during execution. The node has to be shutdown.
                ReportUnhandledError(e);
            }
            finally
            {
                if (localEngine != null)
                {
                    // Flush all the messages associated before shutting down
                    localEngine.LoggingServices.ProcessPostedLoggingEvents();

                    NodeManager nodeManager = localEngine.NodeManager;

                    // If the local engine is already shutting down, the TEM will be nulled out
                    if (nodeManager.TaskExecutionModule != null && nodeManager.TaskExecutionModule.TaskExecutionTime != 0)
                    {
                        TimeSpan taskTimeSpan = new TimeSpan(localEngine.NodeManager.TaskExecutionModule.TaskExecutionTime);
                        totalTaskTime = (int)taskTimeSpan.TotalMilliseconds;
                    }
                    localEngine.Shutdown();
                }
                // Flush all the events to the parent engine
                outProcLoggingService.ProcessPostedLoggingEvents();
                // Indicate that the node logger thread should exit
                exitNodeEvent.Set();
            }
        }

        #endregion

        #region Member data
        // Interface provided by the host that is used to communicate with the parent engine
        private IEngineCallback parentCallback;
        // Id of this node
        private int nodeId;
        // This event is used to communicate between the thread calling shutdown method and the thread running the Engine.BuildLoop.
        private ManualResetEvent exitNodeEvent;
        // The engine being used to process build requests
        private Engine localEngine;
        // The queue of build requests arriving from the parent. The queue is needed to buffer the requests while the local engine is
        // being created and initialized
        private Queue<BuildRequest> buildRequests;
        // This flag is true if the thread that will be running the Engine.BuildLoop has been launched
        private bool launchedEngineLoopThread;
        // The logging service that is used to send the event to the parent engine. It maybe hooked up directly to the local engine or cascaded with
        // another logging service depending on configuration
        private EngineLoggingServicesOutProc outProcLoggingService;
        private Hashtable requestToLocalIdMapping;
        private int lastRequestIdUsed;

        // Initializes to false by default
        private bool logOnlyCriticalEvents;
        private bool centralizedLogging;
        private bool useBreadthFirstTraversal;
        private LoggerDescription[] nodeLoggers;
        private bool buildInProgress;
        private bool nodeShutdown;
        private BuildPropertyGroup parentGlobalProperties;
        private ToolsetDefinitionLocations toolsetSearchLocations;
        private string parentStartupDirectory;
        private int totalTaskTime;

        #endregion

        #region Enums

        internal enum NodeShutdownLevel
        {
            /// <summary>
            /// Notify the engine that a build has completed an reset all data structures
            /// that should be reset between builds
            /// </summary>
            BuildCompleteSuccess = 0,
            BuildCompleteFailure = 1,
            /// <summary>
            /// Wait for in progress operations to finish before returning
            /// </summary>
            PoliteShutdown = 2,
            /// <summary>
            /// Cancel all in progress operations and return
            /// </summary>
            ErrorShutdown = 3
        }

        #endregion
    }
}
