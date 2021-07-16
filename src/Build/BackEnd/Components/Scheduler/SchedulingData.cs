// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Build.Shared;
using Microsoft.Build.Collections;

namespace Microsoft.Build.BackEnd
{
    /// <summary>
    /// This class manages the set of schedulable requests.  In concert with SchedulableRequest, it tracks all relationships
    /// between requests in the system, verifies state change validity and provides efficient methods for querying request relationships.
    /// </summary>
    internal class SchedulingData
    {
        #region Requests By State

        /// <summary>
        /// Maps global request Id to an executing request.
        /// </summary>
        private readonly Dictionary<int, SchedulableRequest> _executingRequests = new Dictionary<int, SchedulableRequest>(32);

        /// <summary>
        /// Maps global request Id to a blocked request.
        /// </summary>
        private readonly Dictionary<int, SchedulableRequest> _blockedRequests = new Dictionary<int, SchedulableRequest>(32);

        /// <summary>
        /// Maps global request Id to a blocked request.
        /// </summary>
        private readonly Dictionary<int, SchedulableRequest> _yieldingRequests = new Dictionary<int, SchedulableRequest>(32);

        /// <summary>
        /// Maps global request Id to a ready request.
        /// </summary>
        private readonly Dictionary<int, SchedulableRequest> _readyRequests = new Dictionary<int, SchedulableRequest>(32);

        /// <summary>
        /// Holds all of the unscheduled requests.
        /// </summary>
        private readonly LinkedList<SchedulableRequest> _unscheduledRequests = new LinkedList<SchedulableRequest>();

        /// <summary>
        /// Maps a schedulable request directly to the node holding it in the linked list.  This allows us to perform an O(1) operation to
        /// remove the node from the linked list without exposing the list directly.
        /// </summary>
        private readonly Dictionary<SchedulableRequest, LinkedListNode<SchedulableRequest>> _unscheduledRequestNodesByRequest = new Dictionary<SchedulableRequest, LinkedListNode<SchedulableRequest>>(32);

        #endregion

        #region Requests By Node

        /// <summary>
        /// Maps node id to the requests scheduled on it.
        /// </summary>
        private readonly Dictionary<int, HashSet<SchedulableRequest>> _scheduledRequestsByNode = new Dictionary<int, HashSet<SchedulableRequest>>(32);

        /// <summary>
        /// Maps a node id to the currently executing request, if any.
        /// </summary>
        private readonly Dictionary<int, SchedulableRequest> _executingRequestByNode = new Dictionary<int, SchedulableRequest>(32);

        /// <summary>
        /// Maps a node id to those requests which are ready to execute, if any.
        /// </summary>
        private readonly Dictionary<int, HashSet<SchedulableRequest>> _readyRequestsByNode = new Dictionary<int, HashSet<SchedulableRequest>>(32);

        /// <summary>
        /// Maps a node id to the set of configurations assigned to it.
        /// </summary>
        private readonly Dictionary<int, HashSet<int>> _configurationsByNode = new Dictionary<int, HashSet<int>>(32);

        #endregion

        #region Configuration-related Information
        /// <summary>
        /// Maps a configuration id to the node to which it is assigned.
        /// </summary>
        private readonly Dictionary<int, int> _configurationToNode = new Dictionary<int, int>(32);

        /// <summary>
        /// Maps a configuration id to the requests which apply to it.
        /// </summary>
        private readonly Dictionary<int, HashSet<SchedulableRequest>> _configurationToRequests = new Dictionary<int, HashSet<SchedulableRequest>>(32);

        #endregion

        #region Resource management

        /// <summary>
        /// The sum of number of cores explicitly granted to all build requests.
        /// </summary>
        private int _grantedCores;

        #endregion

        #region Diagnostic Information

        /// <summary>
        /// This is the hierarchy of build requests as they were created.
        /// </summary>
        private readonly Dictionary<SchedulableRequest, List<SchedulableRequest>> _buildHierarchy = new Dictionary<SchedulableRequest, List<SchedulableRequest>>(32);

        /// <summary>
        /// The sequence of events which have taken place during this build.
        /// </summary>
        private readonly List<SchedulingEvent> _buildEvents = new List<SchedulingEvent>(64);

        /// <summary>
        /// The current time for events.  This is set by the scheduler when it does a scheduling cycle in response to an event.
        /// </summary>
        private DateTime _currentEventTime;

        #endregion

        /// <summary>
        /// Constructor.
        /// </summary>
        public SchedulingData()
        {
        }

        /// <summary>
        /// Retrieves all of the build events.
        /// </summary>
        public IEnumerable<SchedulingEvent> BuildEvents
        {
            get { return _buildEvents; }
        }

        /// <summary>
        /// Retrieves all of the executing requests.
        /// </summary>
        public IEnumerable<SchedulableRequest> ExecutingRequests
        {
            get { return _executingRequests.Values; }
        }

        /// <summary>
        /// Gets a count of all executing requests.
        /// </summary>
        public int ExecutingRequestsCount
        {
            get { return _executingRequests.Count; }
        }

        /// <summary>
        /// Retrieves all of the ready requests.
        /// </summary>
        public IEnumerable<SchedulableRequest> ReadyRequests
        {
            get { return _readyRequests.Values; }
        }

        /// <summary>
        /// Gets a count of all the ready requests.
        /// </summary>
        public int ReadyRequestsCount
        {
            get { return _readyRequests.Count; }
        }

        /// <summary>
        /// Gets the total number of cores granted to executing and yielding build requests.
        /// </summary>
        public int ExplicitlyGrantedCores
        {
            get { return _grantedCores; }
        }

        /// <summary>
        /// Retrieves all of the blocked requests.
        /// </summary>
        public IEnumerable<SchedulableRequest> BlockedRequests
        {
            get { return _blockedRequests.Values; }
        }

        /// <summary>
        /// Gets a count of all of the blocked requests.
        /// </summary>
        public int BlockedRequestsCount
        {
            get { return _blockedRequests.Count; }
        }

        /// <summary>
        /// Retrieves all of the yielded requests.
        /// </summary>
        public IEnumerable<SchedulableRequest> YieldingRequests
        {
            get { return _yieldingRequests.Values; }
        }

        /// <summary>
        /// Gets a count of all of the yielded requests.
        /// </summary>
        public int YieldingRequestsCount
        {
            get { return _yieldingRequests.Count; }
        }

        /// <summary>
        /// Retrieves all of the unscheduled requests.
        /// </summary>
        public IEnumerable<SchedulableRequest> UnscheduledRequests
        {
            get { return _unscheduledRequests; }
        }

        /// <summary>
        /// Gets a count of all the unscheduled requests.
        /// </summary>
        public int UnscheduledRequestsCount
        {
            get { return _unscheduledRequests.Count; }
        }

        /// <summary>
        /// Enumerates the unscheduled requests which don't have other instances scheduled already.
        /// </summary>
        public IEnumerable<SchedulableRequest> UnscheduledRequestsWhichCanBeScheduled
        {
            get
            {
                foreach (SchedulableRequest request in _unscheduledRequests)
                {
                    if (!IsRequestScheduled(request))
                    {
                        yield return request;
                    }
                }
            }
        }

        /// <summary>
        /// Gets all of the configurations for this build.
        /// </summary>
        /// <returns></returns>
        public IEnumerable<int> Configurations
        {
            get
            {
                return _configurationToNode.Keys;
            }
        }

        /// <summary>
        /// Gets or sets the current event time.
        /// </summary>
        public DateTime EventTime
        {
            get { return _currentEventTime; }
            set { _currentEventTime = value; }
        }

        /// <summary>
        /// Creates a new request and adds it to the system
        /// </summary>
        /// <remarks>
        /// New requests always go on the front of the queue, because we prefer to build the projects we just received first (depth first, absent
        /// any particular scheduling algorithm such as in the single-proc case.)
        /// </remarks>
        public SchedulableRequest CreateRequest(BuildRequest buildRequest, SchedulableRequest parent)
        {
            SchedulableRequest request = new SchedulableRequest(this, buildRequest, parent);
            request.CreationTime = EventTime;

            LinkedListNode<SchedulableRequest> requestNode = _unscheduledRequests.AddFirst(request);
            _unscheduledRequestNodesByRequest[request] = requestNode;

            // Update the configuration information.
            HashSet<SchedulableRequest> requests;
            if (!_configurationToRequests.TryGetValue(request.BuildRequest.ConfigurationId, out requests))
            {
                requests = new HashSet<SchedulableRequest>();
                _configurationToRequests[request.BuildRequest.ConfigurationId] = requests;
            }

            requests.Add(request);

            // Update the build hierarchy.
            if (!_buildHierarchy.ContainsKey(request))
            {
                _buildHierarchy[request] = new List<SchedulableRequest>(8);
            }

            if (parent != null)
            {
                ErrorUtilities.VerifyThrow(_buildHierarchy.ContainsKey(parent), "Parent doesn't exist in build hierarchy for request {0}", request.BuildRequest.GlobalRequestId);
                _buildHierarchy[parent].Add(request);
            }

            return request;
        }

        /// <summary>
        /// Updates the state of the specified request.
        /// </summary>
        public void UpdateFromState(SchedulableRequest request, SchedulableRequestState previousState)
        {
            // Remove from its old collection
            switch (previousState)
            {
                case SchedulableRequestState.Blocked:
                    _blockedRequests.Remove(request.BuildRequest.GlobalRequestId);
                    break;

                case SchedulableRequestState.Yielding:
                    _yieldingRequests.Remove(request.BuildRequest.GlobalRequestId);
                    break;

                case SchedulableRequestState.Completed:
                    ErrorUtilities.ThrowInternalError("Should not be updating a request after it has reached the Completed state.");
                    break;

                case SchedulableRequestState.Executing:
                    _executingRequests.Remove(request.BuildRequest.GlobalRequestId);
                    _executingRequestByNode[request.AssignedNode] = null;
                    break;

                case SchedulableRequestState.Ready:
                    _readyRequests.Remove(request.BuildRequest.GlobalRequestId);
                    _readyRequestsByNode[request.AssignedNode].Remove(request);
                    break;

                case SchedulableRequestState.Unscheduled:
                    LinkedListNode<SchedulableRequest> requestNode = _unscheduledRequestNodesByRequest[request];
                    _unscheduledRequestNodesByRequest.Remove(request);
                    _unscheduledRequests.Remove(requestNode);

                    if (request.State != SchedulableRequestState.Completed)
                    {
                        // Map the request to the node.
                        HashSet<SchedulableRequest> requestsAssignedToNode;
                        if (!_scheduledRequestsByNode.TryGetValue(request.AssignedNode, out requestsAssignedToNode))
                        {
                            requestsAssignedToNode = new HashSet<SchedulableRequest>();
                            _scheduledRequestsByNode[request.AssignedNode] = requestsAssignedToNode;
                        }

                        ErrorUtilities.VerifyThrow(!requestsAssignedToNode.Contains(request), "Request {0} is already scheduled to node {1}", request.BuildRequest.GlobalRequestId, request.AssignedNode);
                        requestsAssignedToNode.Add(request);

                        // Map the configuration to the node.
                        HashSet<int> configurationsAssignedToNode;
                        if (!_configurationsByNode.TryGetValue(request.AssignedNode, out configurationsAssignedToNode))
                        {
                            configurationsAssignedToNode = new HashSet<int>();
                            _configurationsByNode[request.AssignedNode] = configurationsAssignedToNode;
                        }

                        if (!configurationsAssignedToNode.Contains(request.BuildRequest.ConfigurationId))
                        {
                            configurationsAssignedToNode.Add(request.BuildRequest.ConfigurationId);
                        }
                    }

                    break;
            }

            // Add it to its new location
            switch (request.State)
            {
                case SchedulableRequestState.Blocked:
                    ErrorUtilities.VerifyThrow(!_blockedRequests.ContainsKey(request.BuildRequest.GlobalRequestId), "Request with global id {0} is already blocked!");
                    _blockedRequests[request.BuildRequest.GlobalRequestId] = request;
                    break;

                case SchedulableRequestState.Yielding:
                    ErrorUtilities.VerifyThrow(!_yieldingRequests.ContainsKey(request.BuildRequest.GlobalRequestId), "Request with global id {0} is already yielded!");
                    _yieldingRequests[request.BuildRequest.GlobalRequestId] = request;
                    break;

                case SchedulableRequestState.Completed:
                    ErrorUtilities.VerifyThrow(_configurationToRequests.ContainsKey(request.BuildRequest.ConfigurationId), "Configuration {0} never had requests assigned to it.", request.BuildRequest.ConfigurationId);
                    ErrorUtilities.VerifyThrow(_configurationToRequests[request.BuildRequest.ConfigurationId].Count > 0, "Configuration {0} has no requests assigned to it.", request.BuildRequest.ConfigurationId);
                    _configurationToRequests[request.BuildRequest.ConfigurationId].Remove(request);
                    if (_scheduledRequestsByNode.TryGetValue(request.AssignedNode, out var requests))
                    {
                        requests.Remove(request);
                    }

                    request.EndTime = EventTime;
                    break;

                case SchedulableRequestState.Executing:
                    ErrorUtilities.VerifyThrow(!_executingRequests.ContainsKey(request.BuildRequest.GlobalRequestId), "Request with global id {0} is already executing!");
                    ErrorUtilities.VerifyThrow(!_executingRequestByNode.ContainsKey(request.AssignedNode) || _executingRequestByNode[request.AssignedNode] == null, "Node {0} is currently executing a request.", request.AssignedNode);

                    _executingRequests[request.BuildRequest.GlobalRequestId] = request;
                    _executingRequestByNode[request.AssignedNode] = request;
                    _configurationToNode[request.BuildRequest.ConfigurationId] = request.AssignedNode;
                    if (previousState == SchedulableRequestState.Unscheduled)
                    {
                        request.StartTime = EventTime;
                    }

                    break;

                case SchedulableRequestState.Ready:
                    ErrorUtilities.VerifyThrow(!_readyRequests.ContainsKey(request.BuildRequest.GlobalRequestId), "Request with global id {0} is already ready!");
                    _readyRequests[request.BuildRequest.GlobalRequestId] = request;
                    HashSet<SchedulableRequest> readyRequestsOnNode;
                    if (!_readyRequestsByNode.TryGetValue(request.AssignedNode, out readyRequestsOnNode))
                    {
                        readyRequestsOnNode = new HashSet<SchedulableRequest>();
                        _readyRequestsByNode[request.AssignedNode] = readyRequestsOnNode;
                    }

                    ErrorUtilities.VerifyThrow(!readyRequestsOnNode.Contains(request), "Request with global id {0} is already marked as ready on node {1}", request.BuildRequest.GlobalRequestId, request.AssignedNode);
                    readyRequestsOnNode.Add(request);
                    break;

                case SchedulableRequestState.Unscheduled:
                    ErrorUtilities.ThrowInternalError("Request with global id {0} cannot transition to the Unscheduled state", request.BuildRequest.GlobalRequestId);
                    break;
            }

            _buildEvents.Add(new SchedulingEvent(EventTime, request, previousState, request.State));
        }

        /// <summary>
        /// Gets the requests assigned to a particular configuration.
        /// </summary>
        public IEnumerable<SchedulableRequest> GetRequestsAssignedToConfiguration(int configurationId)
        {
            return _configurationToRequests[configurationId];
        }

        /// <summary>
        /// Retrieves the number of requests which exist in the system that are attributed to the specified configuration.
        /// </summary>
        public int GetRequestsAssignedToConfigurationCount(int configurationId)
        {
            HashSet<SchedulableRequest> requests;
            if (!_configurationToRequests.TryGetValue(configurationId, out requests))
            {
                return 0;
            }

            return requests.Count;
        }

        /// <summary>
        /// Retrieves a request which is currently executing.
        /// </summary>
        public SchedulableRequest GetExecutingRequest(int globalRequestId)
        {
            ExpectScheduledRequestState(globalRequestId, SchedulableRequestState.Executing);
            return _executingRequests[globalRequestId];
        }

        /// <summary>
        /// Retrieves a request which is currently blocked.
        /// </summary>
        public SchedulableRequest GetBlockedRequest(int globalRequestId)
        {
            ExpectScheduledRequestState(globalRequestId, SchedulableRequestState.Blocked);
            return _blockedRequests[globalRequestId];
        }

        /// <summary>
        /// Retrieves a request which is currently blocked, or null if there is none.
        /// </summary>
        public SchedulableRequest GetBlockedRequestIfAny(int globalRequestId)
        {
            SchedulableRequest request;
            if (_blockedRequests.TryGetValue(globalRequestId, out request))
            {
                return request;
            }

            return null;
        }

        /// <summary>
        /// Retrieves a request which is currently yielding.
        /// </summary>
        public SchedulableRequest GetYieldingRequest(int globalRequestId)
        {
            ExpectScheduledRequestState(globalRequestId, SchedulableRequestState.Yielding);
            return _yieldingRequests[globalRequestId];
        }

        /// <summary>
        /// Retrieves a request which is ready to continue executing.
        /// </summary>
        public SchedulableRequest GetReadyRequest(int globalRequestId)
        {
            ExpectScheduledRequestState(globalRequestId, SchedulableRequestState.Ready);
            return _readyRequests[globalRequestId];
        }

        /// <summary>
        /// Retrieves a request which has been assigned to a node and is in the executing, yielding, blocked, or ready states.
        /// </summary>
        public SchedulableRequest GetScheduledRequest(int globalRequestId)
        {
            SchedulableRequest returnValue = InternalGetScheduledRequestByGlobalRequestId(globalRequestId);
            ErrorUtilities.VerifyThrow(returnValue != null, "Global Request Id {0} has not been assigned and cannot be retrieved.", globalRequestId);
            return returnValue;
        }

        /// <summary>
        /// Returns true if the specified node has an executing request, false otherwise.
        /// </summary>
        public bool IsNodeWorking(int nodeId)
        {
            SchedulableRequest request;
            if (!_executingRequestByNode.TryGetValue(nodeId, out request))
            {
                return false;
            }

            return request != null;
        }

        /// <summary>
        /// Returns the number of configurations assigned to the specified node.
        /// </summary>
        public int GetConfigurationsCountByNode(int nodeId, bool excludeTraversals, IConfigCache configCache)
        {
            HashSet<int> configurationsAssignedToNode;

            if (!_configurationsByNode.TryGetValue(nodeId, out configurationsAssignedToNode))
            {
                return 0;
            }

            int excludeCount = 0;
            if (excludeTraversals && (configCache != null))
            {
                foreach (int config in configurationsAssignedToNode)
                {
                    if (configCache[config].IsTraversal)
                    {
                        excludeCount++;
                    }
                }
            }

            return configurationsAssignedToNode.Count - excludeCount;
        }

        /// <summary>
        /// Gets the request currently executing on the node.
        /// </summary>
        public SchedulableRequest GetExecutingRequestByNode(int nodeId)
        {
            return _executingRequestByNode[nodeId];
        }

        /// <summary>
        /// Determines if the specified request is currently scheduled.
        /// </summary>
        public bool IsRequestScheduled(SchedulableRequest request)
        {
            return InternalGetScheduledRequestByGlobalRequestId(request.BuildRequest.GlobalRequestId) != null;
        }

        /// <summary>
        /// Retrieves the count all of the requests scheduled to the specified node.
        /// </summary>
        public int GetScheduledRequestsCountByNode(int nodeId)
        {
            HashSet<SchedulableRequest> requests;
            if (!_scheduledRequestsByNode.TryGetValue(nodeId, out requests))
            {
                return 0;
            }

            return requests.Count;
        }

        /// <summary>
        /// Retrieves all of the requests scheduled to the specified node.
        /// </summary>
        public IEnumerable<SchedulableRequest> GetScheduledRequestsByNode(int nodeId)
        {
            HashSet<SchedulableRequest> requests;
            if (!_scheduledRequestsByNode.TryGetValue(nodeId, out requests))
            {
                return ReadOnlyEmptyCollection<SchedulableRequest>.Instance;
            }

            return requests;
        }

        /// <summary>
        /// Retrieves all of the ready requests on the specified node.
        /// </summary>
        public IEnumerable<SchedulableRequest> GetReadyRequestsByNode(int nodeId)
        {
            HashSet<SchedulableRequest> requests;
            if (!_readyRequestsByNode.TryGetValue(nodeId, out requests))
            {
                return ReadOnlyEmptyCollection<SchedulableRequest>.Instance;
            }

            return requests;
        }

        /// <summary>
        /// Retrieves a set of build requests which have the specified parent.  If root is null, this will retrieve all of the 
        /// top-level requests.
        /// </summary>
        public IEnumerable<SchedulableRequest> GetRequestsByHierarchy(SchedulableRequest root)
        {
            if (root == null)
            {
                // Retrieve all requests which are roots of the tree.
                List<SchedulableRequest> roots = new List<SchedulableRequest>();
                foreach (SchedulableRequest key in _buildHierarchy.Keys)
                {
                    if (key.Parent == null)
                    {
                        roots.Add(key);
                    }
                }

                return roots;
            }

            return _buildHierarchy[root];
        }

        /// <summary>
        /// Returns the node id to which this request should be assigned based on its configuration.
        /// </summary>
        /// <returns>The node if one has been assigned for this configuration, otherwise -1.</returns>
        public int GetAssignedNodeForRequestConfiguration(int configurationId)
        {
            int assignedNode;
            if (!_configurationToNode.TryGetValue(configurationId, out assignedNode))
            {
                return Scheduler.InvalidNodeId;
            }

            return assignedNode;
        }

        /// <summary>
        /// Returns true if the request can be scheduled to the specified node.
        /// </summary>
        public bool CanScheduleRequestToNode(SchedulableRequest request, int nodeId)
        {
            return CanScheduleConfigurationToNode(request.BuildRequest.ConfigurationId, nodeId);
        }

        public bool CanScheduleConfigurationToNode(int configurationId, int nodeId)
        {
            int requiredNodeId = GetAssignedNodeForRequestConfiguration(configurationId);
            return requiredNodeId == Scheduler.InvalidNodeId || requiredNodeId == nodeId;
        }

        /// <summary>
        /// Explicitly grants CPU cores to a request.
        /// </summary>
        public void GrantCoresToRequest(int globalRequestId, int coresToGrant)
        {
            // Update per-request state.
            SchedulableRequest request = GetScheduledRequest(globalRequestId);
            request.GrantedCores += coresToGrant;

            // Update global state.
            _grantedCores += coresToGrant;
        }

        /// <summary>
        /// Explicitly removes previously granted CPU cores from a request.
        /// </summary>
        public void RemoveCoresFromRequest(int globalRequestId, int coresToRemove)
        {
            // Update per-request state.
            SchedulableRequest request = GetScheduledRequest(globalRequestId);
            coresToRemove = Math.Min(request.GrantedCores, coresToRemove);
            request.GrantedCores -= coresToRemove;

            // Update global state.
            _grantedCores -= coresToRemove;
        }

        /// <summary>
        /// Unassigns the node associated with a particular configuration.
        /// </summary>
        /// <remarks>
        /// The operation is only valid when there are no scheduled requests for this configuration.
        /// </remarks>
        internal void UnassignNodeForRequestConfiguration(int configurationId)
        {
            ErrorUtilities.VerifyThrow(
                GetRequestsAssignedToConfigurationCount(configurationId) == 0,
                "Configuration with ID {0} cannot be unassigned from a node, because there are requests scheduled with that configuration.",
                configurationId);

            _configurationToNode.Remove(configurationId);
        }

        /// <summary>
        /// Gets a schedulable request with the specified global request id if it is currently scheduled.
        /// </summary>
        private SchedulableRequest InternalGetScheduledRequestByGlobalRequestId(int globalRequestId)
        {
            SchedulableRequest returnValue;
            if (_executingRequests.TryGetValue(globalRequestId, out returnValue))
            {
                return returnValue;
            }

            if (_blockedRequests.TryGetValue(globalRequestId, out returnValue))
            {
                return returnValue;
            }

            if (_yieldingRequests.TryGetValue(globalRequestId, out returnValue))
            {
                return returnValue;
            }

            if (_readyRequests.TryGetValue(globalRequestId, out returnValue))
            {
                return returnValue;
            }

            return null;
        }

        /// <summary>
        /// Verifies that the request is scheduled and in the expected state.
        /// </summary>
        private void ExpectScheduledRequestState(int globalRequestId, SchedulableRequestState state)
        {
            SchedulableRequest request = InternalGetScheduledRequestByGlobalRequestId(globalRequestId);
            if (request == null)
            {
                ErrorUtilities.ThrowInternalError("Request {0} was expected to be in state {1} but is not scheduled at all (it may be unscheduled or may be unknown to the system.)", globalRequestId, state);
            }
            else
            {
                request.VerifyState(state);
            }
        }

        /// <summary>
        /// A scheduling event.
        /// </summary>
        internal class SchedulingEvent
        {
            /// <summary>
            /// The time the event took place.
            /// </summary>
            private DateTime _eventTime;

            /// <summary>
            /// The request involved in the event.
            /// </summary>
            private SchedulableRequest _request;

            /// <summary>
            /// The state of the request before the event.
            /// </summary>
            private SchedulableRequestState _oldState;

            /// <summary>
            /// The state of the request as a result of the event.
            /// </summary>
            private SchedulableRequestState _newState;

            /// <summary>
            /// Constructor.
            /// </summary>
            public SchedulingEvent(DateTime eventTime, SchedulableRequest request, SchedulableRequestState oldState, SchedulableRequestState newState)
            {
                _eventTime = eventTime;
                _request = request;
                _oldState = oldState;
                _newState = newState;
            }

            /// <summary>
            /// The time the event took place.
            /// </summary>
            public DateTime EventTime
            {
                get { return _eventTime; }
            }

            /// <summary>
            /// The request involved in the event.
            /// </summary>
            public SchedulableRequest Request
            {
                get { return _request; }
            }

            /// <summary>
            /// The state of the request before the event.
            /// </summary>
            public SchedulableRequestState OldState
            {
                get { return _oldState; }
            }

            /// <summary>
            /// The state of the request as a result of the event.
            /// </summary>
            public SchedulableRequestState NewState
            {
                get { return _newState; }
            }
        }
    }
}
