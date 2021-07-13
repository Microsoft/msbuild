// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;

using Microsoft.Build.Framework;
using Microsoft.Build.BuildEngine.Shared;
using System.Threading;

namespace Microsoft.Build.BuildEngine
{
    internal class Introspector
    {
        #region Constructors
        internal Introspector(Engine parentEngine, ProjectManager projectManager, NodeManager nodeManager)
        {
            this.parentEngine   = parentEngine;
            this.projectManager = projectManager;
            this.nodeManager    = nodeManager;
            this.ignoreTimeout  = 0;
        }
        #endregion

        #region Methods
        /// <summary>
        /// This method is called when the parent engine doesn't see activity for a preset time period to
        /// determine if the whole system is making forward progress. In order to that, status is collected
        /// from every node in the system. If no node is making forward progress then the graph of all the
        /// inprogress targets is analyzed for cycles. If a cycle is found the appropriate node is instructed
        /// to break it. If no cause for deadlock can be determined the system is shutdown.
        /// </summary>
        /// <returns>New inactivity timeout</returns>
        internal int DetectDeadlock( int queueCounts, long lastLoopActivity, int currentTimeout)
        {
            // Don't try to detect deadlock in single threaded mode or on a child node
            if (parentEngine.Router.ChildMode || parentEngine.Router.SingleThreadedMode)
            {
                return Timeout.Infinite;
            }

            // Calculate time since last loop activity
            TimeSpan timeSinceLastLoopActivity =
                            new TimeSpan(DateTime.Now.Ticks - lastLoopActivity);

            // If there are items in the queue waiting to be processed or there was loop activity
            // not so long ago - continue
            if (queueCounts > 0 || timeSinceLastLoopActivity.TotalMilliseconds < currentTimeout)
            {
                return currentTimeout;
            }

            if (nodeManager.TaskExecutionModule == null)
            {
                return currentTimeout;
            }

            // Calculate the time since the last task activity
            TimeSpan timeSinceLastTEMActivity =
                            new TimeSpan(DateTime.Now.Ticks - nodeManager.TaskExecutionModule.LastTaskActivity());

            // If there was not task activity for the whole time period - check with individual nodes
            // to see if there was activity there
            if (timeSinceLastTEMActivity.TotalMilliseconds < currentTimeout)
            {
                // Increase the timeout since tasks are taking a long time
                return calculateNewLoopTimeout(currentTimeout);
            }

            // Check if we are waiting on an outcome of an operation
            if ((ignoreTimeout - DateTime.Now.Ticks) > 0)
            {
                return currentTimeout;
            }

            long requestStartTime = DateTime.Now.Ticks;
            NodeStatus[] nodeStatus = nodeManager.RequestStatusForNodes(nodeStatusReplyTimeout);
            long requestDurationTime = DateTime.Now.Ticks - requestStartTime;

            for (int i = 0; i < nodeStatus.Length; i++)
            {
                if (nodeStatus[i] == null)
                {
                    // A node failed to respond to the request for status. The only option is to shutdown
                    // the build and error out
                    LogOrDumpError("FailedToReceiveChildStatus", i + 1, nodeStatusReplyTimeout);

                    SystemShutdown();
                    return currentTimeout;
                }
                else if (nodeStatus[i].HasExited)
                {
                    // A node has exited prematurely. The only option is to shutdown
                    LogOrDumpError("ChildExitedPrematurely", i + 1);

                    SystemShutdown();
                    return currentTimeout;
                }
                else if (nodeStatus[i].IsActive)
                {
                    // Calculate the time since last node activity
                    TimeSpan timeSinceLastNodeTaskActivity = new TimeSpan(nodeStatus[i].TimeSinceLastTaskActivity);
                    TimeSpan timeSinceLastNodeLoopActivity = new TimeSpan(nodeStatus[i].TimeSinceLastLoopActivity);

                    // Check if there was activity on the node within the timeout
                    if (nodeStatus[i].QueueDepth > 0 ||
                        timeSinceLastNodeTaskActivity.TotalMilliseconds < currentTimeout ||
                        timeSinceLastNodeLoopActivity.TotalMilliseconds < currentTimeout)
                    {
                        // If the time out has been exceeded while one of the nodes was
                        // active lets increase the timeout
                        return calculateNewLoopTimeout(currentTimeout);
                    }
                }
                else if (nodeStatus[i].IsLaunchInProgress)
                {
                    // If there is a node in process of being launched, only the NodeProvider
                    // knows how long that should take so the decision to error out can
                    // only be made by the node provider.
                    return currentTimeout;
                }
            }

            // There was no detected activity within the system for the whole time period. Check
            // if there is a cycle in the in progress targets
            TargetCycleDetector cycleDetector = new TargetCycleDetector(parentEngine.LoggingServices, parentEngine.EngineCallback);
            AddTargetStatesToCycleDetector(nodeStatus, cycleDetector);
            NodeStatus localStatus = parentEngine.RequestStatus(0);
            cycleDetector.AddTargetsToGraph(localStatus.StateOfInProgressTargets);


            if (cycleDetector.FindCycles())
            {
                if (Engine.debugMode)
                {
                    Console.WriteLine("Breaking cycle between " + cycleDetector.CycleEdgeChild.TargetId.name + " and " +
                                  cycleDetector.CycleEdgeParent.TargetId.name);
                }
                // A cycle has been detected - it needs to be broken for the build to continue
                nodeManager.PostCycleNotification(cycleDetector.CycleEdgeChild.TargetId.nodeId,
                                                  cycleDetector.CycleEdgeChild,
                                                  cycleDetector.CycleEdgeParent);
                // Use the amount of time it took us to receive the NodeStatus and buffer it a little because node status is sent via a faster code path
                ignoreTimeout = DateTime.Now.Ticks + requestDurationTime + (cycleBreakTimeout * TimeSpan.TicksPerMillisecond);
                return currentTimeout;
            }

            // The system doesn't appear to be making progress. Switch to a largest sampling interval.
            if (currentTimeout != maxLoopTimeout)
            {
                return maxLoopTimeout;
            }

            // Should make at least two observations before assuming that no forward progress is being made
            if (previousStatus == null || previousLocalStatus == null || nodeStatus.Length != previousStatus.Length)
            {
                previousStatus = nodeStatus;
                previousLocalStatus = localStatus;
                return currentTimeout;
            }

            // There was some activity between previous and current status checks on the local node
            if (localStatus.LastLoopActivity != previousLocalStatus.LastLoopActivity ||
                localStatus.LastTaskActivity != previousLocalStatus.LastTaskActivity )
            {
                previousStatus = nodeStatus;
                previousLocalStatus = localStatus;
                return currentTimeout;
            }

            for (int i = 0; i < nodeStatus.Length; i++)
            {
                // There was some activity between previous and current status checks on the child node
                if (nodeStatus[i].LastTaskActivity != previousStatus[i].LastTaskActivity ||
                    nodeStatus[i].LastLoopActivity != previousStatus[i].LastLoopActivity)
                {
                    previousStatus = nodeStatus;
                    previousLocalStatus = localStatus;
                    return currentTimeout;
                }
            }

            // The system is not making forward progress for an unknown reason. The
            // only recourse to is to collect as much data as possible and shutdown with
            // an error message
            // UNDONE - using logging and resource string to output the state dump

            GatherNodeInformationForShutdown(nodeStatus, localStatus);
            SystemShutdown();
            return currentTimeout;
        }

        /// <summary>
        /// Logs an error, or if the loggers are not available, writes it to the console
        /// </summary>
        private void LogOrDumpError(string resourceName, params object[] args)
        {
            if (parentEngine.LoggingServices != null)
            {
                parentEngine.LoggingServices.LogError(BuildEventContext.Invalid, new BuildEventFileInfo(String.Empty) /* no project file */, resourceName, args);
            }
            else
            {
                // Can't log it -- we can only log to the console instead
                string message = ResourceUtilities.FormatResourceString(resourceName, args);
                Console.WriteLine(message);
            }
        }

        /// <summary>
        /// Adds a set of nodeStatus's to the cycle graph
        /// </summary>
        private void AddTargetStatesToCycleDetector(NodeStatus[] nodeStatus, TargetCycleDetector cycleDetector)
        {
            for (int i = 0; i < nodeStatus.Length; i++)
            {
               cycleDetector.AddTargetsToGraph(nodeStatus[i].StateOfInProgressTargets);
            }
        }

        /// <summary>
        /// The system is not making forward progress for an unknown reason. The
        /// only recourse to is to collect as much data as possible and shutdown with
        /// an error message
        /// </summary>
        private void GatherNodeInformationForShutdown(NodeStatus[] nodeStatus, NodeStatus localStatus)
        {
            for (int i = 0; i < nodeStatus.Length; i++)
            {
                TimeSpan timeSinceLastNodeTaskActivity = new TimeSpan(nodeStatus[i].TimeSinceLastTaskActivity);
                TimeSpan timeSinceLastNodeLoopActivity = new TimeSpan(nodeStatus[i].TimeSinceLastLoopActivity);

                Console.WriteLine("Status: " + i + " Task Activity " + timeSinceLastNodeTaskActivity.TotalMilliseconds +
                                  " Loop Activity " + timeSinceLastNodeLoopActivity.TotalMilliseconds + " Queue depth " +
                                  nodeStatus[i].QueueDepth);
                for (int j = 0; j < nodeStatus[i].StateOfInProgressTargets.Length; j++)
                {
                    Console.WriteLine(nodeStatus[i].StateOfInProgressTargets[j].ProjectName + ":" + nodeStatus[i].StateOfInProgressTargets[j].TargetId.name);
                }
            }

            Console.WriteLine("Status: LocalNode Task Activity " + localStatus.TimeSinceLastTaskActivity +
                                  " Loop Activity " + localStatus.TimeSinceLastLoopActivity + " Queue depth " +
                                  localStatus.QueueDepth);

            for (int j = 0; j < localStatus.StateOfInProgressTargets.Length; j++)
            {
                Console.WriteLine(localStatus.StateOfInProgressTargets[j].ProjectName + ":" + localStatus.StateOfInProgressTargets[j].TargetId.name);
            }

            parentEngine.Scheduler.DumpState();
        }

        /// <summary>
        /// This method is called to shutdown the system in case of fatal error
        /// </summary>
        internal void SystemShutdown()
        {
	    ErrorUtilities.LaunchMsBuildDebuggerOnFatalError();
            nodeManager.ShutdownNodes(Node.NodeShutdownLevel.ErrorShutdown);
        }


        /// <summary>
        /// This function is called to break the link between two targets that creates a cycle. The link could be
        /// due to depends/onerror relationship between parent and child. In that case both parent and child are
        /// on the same node and within the same project. Or the link could be formed by an IBuildEngine callback
        /// (made such by tasks such as MSBuild or CallTarget) in which case there maybe multiple requests forming
        /// same link between parent and child. Also in that case parent and child maybe on different nodes and/or in
        /// different projects. In either case the break is forced by finding the correct builds states and causing
        /// them to fail.
        /// </summary>
        internal void BreakCycle(TargetInProgessState child, TargetInProgessState parent)
        {
            ErrorUtilities.VerifyThrow( child.TargetId.nodeId == parentEngine.NodeId,
                                        "Expect the child target to be on the node");

            Project parentProject = projectManager.GetProject(child.TargetId.projectId);

            ErrorUtilities.VerifyThrow(parentProject  != null,
                                        "Expect the parent project to be on the node");

            Target childTarget = parentProject.Targets[child.TargetId.name];

            List<ProjectBuildState> parentStates = FindConnectingContexts(child, parent, childTarget, childTarget.ExecutionState.GetWaitingBuildContexts(),
                                      childTarget.ExecutionState.InitiatingBuildContext);

            ErrorUtilities.VerifyThrow(parentStates.Count > 0, "Must find at least one matching context");

            for (int i = 0; i < parentStates.Count; i++)
            {
                parentStates[i].CurrentBuildContextState = ProjectBuildState.BuildContextState.CycleDetected;
                TaskExecutionContext taskExecutionContext =
                    new TaskExecutionContext(parentProject, childTarget, null, parentStates[i], EngineCallback.invalidEngineHandle,
                                             EngineCallback.inProcNode, null);

                parentEngine.PostTaskOutputUpdates(taskExecutionContext);
            }
        }

        /// <summary>
        /// Find all the build contexts that connects child to parent. The only time multiple contexts are possible
        /// is if the connection is formed by an IBuildEngine callback which requests the same target in the
        /// same project to be build in parallel multiple times.
        /// </summary>
        internal List<ProjectBuildState> FindConnectingContexts
        (
            TargetInProgessState child,
            TargetInProgessState parent,
            Target childTarget,
            List<ProjectBuildState> waitingStates,
            ProjectBuildState initiatingBuildContext
        )
        {
            List<ProjectBuildState> connectingContexts = new List<ProjectBuildState>();

            // Since the there is a cycle formed at the child there must be at least two requests
            // since the edge between the parent and the child is a backward edge
            ErrorUtilities.VerifyThrow(waitingStates != null, "There must be a at least two requests at the child");


            for (int i = 0; i < waitingStates.Count; i++)
            {
                if (child.CheckBuildContextForParentMatch(parentEngine.EngineCallback, parent.TargetId, childTarget, waitingStates[i]))
                {
                    connectingContexts.Add(waitingStates[i]);
                }
            }


            if (child.CheckBuildContextForParentMatch(parentEngine.EngineCallback, parent.TargetId, childTarget, initiatingBuildContext))
            {
                connectingContexts.Add(initiatingBuildContext);
            }

            return connectingContexts;
        }

        /// <summary>
        /// Increase the inactivity time out
        /// </summary>
        /// <param name="currentTimeout">current inactivity timeout</param>
        /// <returns>new inactivity timeout</returns>
        private int calculateNewLoopTimeout(int currentTimeout)
        {
            if (currentTimeout < maxLoopTimeout)
            {
                currentTimeout = 2*currentTimeout;
            }

            return currentTimeout;
        }

        #endregion

        #region Data
        private Engine parentEngine;
        private ProjectManager projectManager;
        private NodeManager nodeManager;
        private NodeStatus[] previousStatus;
        private NodeStatus previousLocalStatus;
        private long ignoreTimeout;

        internal const int initialLoopTimeout = 1000; // Start with a 1 sec of inactivity delay before asking for status
        internal const int cycleBreakTimeout = 5000; // Allow 5 seconds for the cycle break to reach the child node
        internal const int maxLoopTimeout = 50000; // Allow at most 50 sec of inactivity before asking for status
        internal const int nodeStatusReplyTimeout = 300000; // Give the node 5 minutes to reply to a status request

        #endregion
    }
}
