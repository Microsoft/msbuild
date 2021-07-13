// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Xml;
using System.Collections.Generic;
using System.IO;
using Microsoft.Build.Framework;
using Microsoft.Build.BackEnd;
using Microsoft.Build.Shared;
using Microsoft.Build.Execution;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Experimental.ProjectCache;
using Shouldly;
using TaskItem = Microsoft.Build.Execution.ProjectItemInstance.TaskItem;
using Xunit;

namespace Microsoft.Build.UnitTests.BackEnd
{
    using Microsoft.Build.Unittest;

    /// <summary>
    /// Tests of the scheduler.
    /// </summary>
    // Ignore: Causing issues with other tests
    // NOTE: marked as "internal" to disable the entire test class, as was done for MSTest.
    public class Scheduler_Tests : IDisposable
    {
        /// <summary>
        /// The host object.
        /// </summary>
        private MockHost _host;

        /// <summary>
        /// The scheduler used in each test.
        /// </summary>
        private Scheduler _scheduler;

        /// <summary>
        /// The default parent request
        /// </summary>
        private BuildRequest _defaultParentRequest;

        /// <summary>
        /// The mock logger for testing.
        /// </summary>
        private MockLogger _logger;

        /// <summary>
        /// The standard build manager for each test.
        /// </summary>
        private BuildManager _buildManager;

        /// <summary>
        /// The build parameters.
        /// </summary>
        private BuildParameters _parameters;

        /// <summary>
        /// Set up
        /// </summary>
        public Scheduler_Tests()
        {
            // Since we're creating our own BuildManager, we need to make sure that the default
            // one has properly relinquished the inproc node
            NodeProviderInProc nodeProviderInProc = ((IBuildComponentHost)BuildManager.DefaultBuildManager).GetComponent(BuildComponentType.InProcNodeProvider) as NodeProviderInProc;
            nodeProviderInProc?.Dispose();

            _host = new MockHost();
            _scheduler = new Scheduler();
            _scheduler.InitializeComponent(_host);
            CreateConfiguration(99, "parent.proj");
            _defaultParentRequest = CreateBuildRequest(99, 99, new string[] { }, null);

            // Set up the scheduler with one node to start with.
            _scheduler.ReportNodesCreated(new NodeInfo[] { new NodeInfo(1, NodeProviderType.InProc) });
            _scheduler.ReportRequestBlocked(1, new BuildRequestBlocker(-1, new string[] { }, new BuildRequest[] { _defaultParentRequest }));

            _logger = new MockLogger();
            _parameters = new BuildParameters();
            _parameters.Loggers = new ILogger[] { _logger };
            _parameters.ShutdownInProcNodeOnBuildFinish = true;
            _buildManager = new BuildManager();
        }

        /// <summary>
        /// Tear down
        /// </summary>
        public void Dispose()
        {
            if (_buildManager != null)
            {
                NodeProviderInProc nodeProviderInProc = ((IBuildComponentHost)_buildManager).GetComponent(BuildComponentType.InProcNodeProvider) as NodeProviderInProc;
                nodeProviderInProc.Dispose();

                _buildManager.Dispose();
            }
        }

        /// <summary>
        /// Verify that when a single request is submitted, we get a request assigned back out.
        /// </summary>
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/515")]
        public void TestSimpleRequest()
        {
            CreateConfiguration(1, "foo.proj");
            BuildRequest request = CreateBuildRequest(1, 1);
            BuildRequestBlocker blocker = new BuildRequestBlocker(request.ParentGlobalRequestId, new string[] { }, new BuildRequest[] { request });
            List<ScheduleResponse> response = new List<ScheduleResponse>(_scheduler.ReportRequestBlocked(1, blocker));

            Assert.Single(response);
            Assert.Equal(ScheduleActionType.ScheduleWithConfiguration, response[0].Action);
            Assert.Equal(request, response[0].BuildRequest);
        }

        /// <summary>
        /// Verify that when we submit a request and we already have results, we get the results back.
        /// </summary>
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/515")]
        public void TestSimpleRequestWithCachedResultsSuccess()
        {
            CreateConfiguration(1, "foo.proj");
            BuildRequest request = CreateBuildRequest(1, 1, new string[] { "foo" });
            BuildResult result = CacheBuildResult(request, "foo", BuildResultUtilities.GetSuccessResult());

            BuildRequestBlocker blocker = new BuildRequestBlocker(request.ParentGlobalRequestId, new string[] { }, new BuildRequest[] { request });
            List<ScheduleResponse> response = new List<ScheduleResponse>(_scheduler.ReportRequestBlocked(1, blocker));

            Assert.Equal(2, response.Count);

            // First response tells the parent of the results.
            Assert.Equal(ScheduleActionType.ReportResults, response[0].Action);
            Assert.True(ResultsCache_Tests.AreResultsIdentical(result, response[0].Unblocker.Result));

            // Second response tells the parent to continue.
            Assert.Equal(ScheduleActionType.ResumeExecution, response[1].Action);
            Assert.Null(response[1].Unblocker.Result);
        }

        /// <summary>
        /// Verify that when we submit a request with failing results, we get the results back.
        /// </summary>
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/515")]
        public void TestSimpleRequestWithCachedResultsFail()
        {
            CreateConfiguration(1, "foo.proj");
            BuildRequest request = CreateBuildRequest(1, 1, new string[] { "foo" });
            BuildResult result = CacheBuildResult(request, "foo", BuildResultUtilities.GetStopWithErrorResult());

            BuildRequestBlocker blocker = new BuildRequestBlocker(request.ParentGlobalRequestId, new string[] { }, new BuildRequest[] { request });
            List<ScheduleResponse> response = new List<ScheduleResponse>(_scheduler.ReportRequestBlocked(1, blocker));

            Assert.Equal(2, response.Count);

            // First response tells the parent of the results.
            Assert.Equal(ScheduleActionType.ReportResults, response[0].Action);
            Assert.True(ResultsCache_Tests.AreResultsIdentical(result, response[0].Unblocker.Result));

            // Second response tells the parent to continue.
            Assert.Equal(ScheduleActionType.ResumeExecution, response[1].Action);
            Assert.Null(response[1].Unblocker.Result);
        }

        /// <summary>
        /// Verify that when we submit a child request with results cached, we get those results back.
        /// </summary>
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/515")]
        public void TestChildRequest()
        {
            CreateConfiguration(1, "foo.proj");
            BuildRequest request = CreateBuildRequest(1, 1, new string[] { "foo" });

            BuildRequestBlocker blocker = new BuildRequestBlocker(-1, new string[] { }, new BuildRequest[] { request });
            List<ScheduleResponse> response = new List<ScheduleResponse>(_scheduler.ReportRequestBlocked(1, blocker));

            CreateConfiguration(2, "bar.proj");
            BuildRequest childRequest = CreateBuildRequest(2, 2, new string[] { "foo" }, request);
            BuildResult childResult = CacheBuildResult(childRequest, "foo", BuildResultUtilities.GetSuccessResult());

            blocker = new BuildRequestBlocker(0, new string[] { "foo" }, new BuildRequest[] { childRequest });
            response = new List<ScheduleResponse>(_scheduler.ReportRequestBlocked(1, blocker));

            Assert.Equal(2, response.Count);

            // The first response will be to report the results back to the node.
            Assert.Equal(ScheduleActionType.ReportResults, response[0].Action);
            Assert.True(ResultsCache_Tests.AreResultsIdentical(childResult, response[0].Unblocker.Result));

            // The second response will be to continue building the original request.
            Assert.Equal(ScheduleActionType.ResumeExecution, response[1].Action);
            Assert.Null(response[1].Unblocker.Result);
        }

        /// <summary>
        /// Verify that when multiple requests are submitted, the first one in is the first one out.
        /// </summary>
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/515")]
        public void TestMultipleRequests()
        {
            CreateConfiguration(1, "foo.proj");
            BuildRequest request1 = CreateBuildRequest(1, 1, new string[] { "foo" });
            BuildRequest request2 = CreateBuildRequest(2, 1, new string[] { "bar" });

            BuildRequestBlocker blocker = new BuildRequestBlocker(request1.ParentGlobalRequestId, new string[] { }, new BuildRequest[] { request1, request2 });
            List<ScheduleResponse> response = new List<ScheduleResponse>(_scheduler.ReportRequestBlocked(1, blocker));

            Assert.Single(response);
            Assert.Equal(ScheduleActionType.ScheduleWithConfiguration, response[0].Action);
            Assert.Equal(request1, response[0].BuildRequest);
        }

        /// <summary>
        /// Verify that when multiple requests are submitted with results cached, we get the results back.
        /// </summary>
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/515")]
        public void TestMultipleRequestsWithSomeResults()
        {
            CreateConfiguration(1, "foo.proj");
            BuildRequest request1 = CreateBuildRequest(1, 1, new string[] { "foo" });
            CreateConfiguration(2, "bar.proj");
            BuildRequest request2 = CreateBuildRequest(2, 2, new string[] { "bar" });
            BuildResult result2 = CacheBuildResult(request2, "bar", BuildResultUtilities.GetSuccessResult());

            BuildRequestBlocker blocker = new BuildRequestBlocker(request1.ParentGlobalRequestId, new string[] { }, new BuildRequest[] { request1, request2 });
            List<ScheduleResponse> response = new List<ScheduleResponse>(_scheduler.ReportRequestBlocked(1, blocker));

            Assert.Equal(2, response.Count);
            Assert.Equal(ScheduleActionType.ReportResults, response[0].Action);
            Assert.True(ResultsCache_Tests.AreResultsIdentical(result2, response[0].Unblocker.Result));
            Assert.Equal(ScheduleActionType.ScheduleWithConfiguration, response[1].Action);
            Assert.Equal(request1, response[1].BuildRequest);
        }

        /// <summary>
        /// Verify that when multiple requests are submitted with results cached, we get the results back.
        /// </summary>
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/515")]
        public void TestMultipleRequestsWithAllResults()
        {
            CreateConfiguration(1, "foo.proj");
            BuildRequest request1 = CreateBuildRequest(1, 1, new string[] { "foo" });
            BuildResult result1 = CacheBuildResult(request1, "foo", BuildResultUtilities.GetSuccessResult());
            CreateConfiguration(2, "bar.proj");
            BuildRequest request2 = CreateBuildRequest(2, 2, new string[] { "bar" });
            BuildResult result2 = CacheBuildResult(request2, "bar", BuildResultUtilities.GetSuccessResult());

            BuildRequestBlocker blocker = new BuildRequestBlocker(request1.ParentGlobalRequestId, new string[] { }, new BuildRequest[] { request1, request2 });
            List<ScheduleResponse> response = new List<ScheduleResponse>(_scheduler.ReportRequestBlocked(1, blocker));

            Assert.Equal(3, response.Count);

            // First two are the results which were cached.
            Assert.Equal(ScheduleActionType.ReportResults, response[0].Action);
            Assert.True(ResultsCache_Tests.AreResultsIdentical(result1, response[0].Unblocker.Result));
            Assert.Equal(ScheduleActionType.ReportResults, response[1].Action);
            Assert.True(ResultsCache_Tests.AreResultsIdentical(result2, response[1].Unblocker.Result));

            // Last response is to continue the parent.
            Assert.Equal(ScheduleActionType.ResumeExecution, response[2].Action);
            Assert.Equal(request1.ParentGlobalRequestId, response[2].Unblocker.BlockedRequestId);
            Assert.Null(response[2].Unblocker.Result);
        }

        /// <summary>
        /// Verify that if the affinity of one of the requests is out-of-proc, we create an out-of-proc node (but only one)
        /// even if the max node count = 1.
        /// </summary>
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/515")]
        public void TestOutOfProcNodeCreatedWhenAffinityIsOutOfProc()
        {
            CreateConfiguration(1, "foo.proj");
            BuildRequest request1 = CreateBuildRequest(1, 1, new string[] { "foo" }, NodeAffinity.OutOfProc, _defaultParentRequest);
            BuildRequest request2 = CreateBuildRequest(2, 1, new string[] { "bar" }, NodeAffinity.OutOfProc, _defaultParentRequest);

            BuildRequestBlocker blocker = new BuildRequestBlocker(request1.ParentGlobalRequestId, new string[] { }, new BuildRequest[] { request1, request2 });
            List<ScheduleResponse> response = new List<ScheduleResponse>(_scheduler.ReportRequestBlocked(1, blocker));

            // Parent request is blocked by the fact that both child requests require the out-of-proc node that doesn't
            // exist yet.
            Assert.Single(response);
            Assert.Equal(ScheduleActionType.CreateNode, response[0].Action);
            Assert.Equal(NodeAffinity.OutOfProc, response[0].RequiredNodeType);
            Assert.Equal(1, response[0].NumberOfNodesToCreate);
        }

        /// <summary>
        /// Verify that if the affinity of our requests is out-of-proc, that many out-of-proc nodes will
        /// be made (assuming it does not exceed MaxNodeCount)
        /// </summary>
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/515")]
        public void TestOutOfProcNodesCreatedWhenAffinityIsOutOfProc()
        {
            _host.BuildParameters.MaxNodeCount = 4;

            CreateConfiguration(1, "foo.proj");
            BuildRequest request1 = CreateBuildRequest(1, 1, new string[] { "foo" }, NodeAffinity.OutOfProc, _defaultParentRequest);
            BuildRequest request2 = CreateBuildRequest(2, 1, new string[] { "bar" }, NodeAffinity.OutOfProc, _defaultParentRequest);

            BuildRequestBlocker blocker = new BuildRequestBlocker(request1.ParentGlobalRequestId, new string[] { }, new BuildRequest[] { request1, request2 });
            List<ScheduleResponse> response = new List<ScheduleResponse>(_scheduler.ReportRequestBlocked(1, blocker));

            // Parent request is blocked by the fact that both child requests require the out-of-proc node that doesn't
            // exist yet.
            Assert.Single(response);
            Assert.Equal(ScheduleActionType.CreateNode, response[0].Action);
            Assert.Equal(NodeAffinity.OutOfProc, response[0].RequiredNodeType);
            Assert.Equal(2, response[0].NumberOfNodesToCreate);
        }

        /// <summary>
        /// Verify that if we have multiple requests and the max node count to fulfill them,
        /// we still won't create any new nodes if they're all for the same configuration --
        /// they'd end up all being assigned to the same node.
        /// </summary>
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/515")]
        public void TestNoNewNodesCreatedForMultipleRequestsWithSameConfiguration()
        {
            _host.BuildParameters.MaxNodeCount = 3;

            CreateConfiguration(1, "foo.proj");
            BuildRequest request1 = CreateBuildRequest(1, 1, new string[] { "foo" });
            BuildRequest request2 = CreateBuildRequest(2, 1, new string[] { "bar" });
            BuildRequest request3 = CreateBuildRequest(3, 1, new string[] { "baz" });
            BuildRequest request4 = CreateBuildRequest(4, 1, new string[] { "qux" });

            BuildRequestBlocker blocker = new BuildRequestBlocker(request1.ParentGlobalRequestId, new string[] { }, new BuildRequest[] { request1, request2, request3, request4 });
            List<ScheduleResponse> response = new List<ScheduleResponse>(_scheduler.ReportRequestBlocked(1, blocker));

            Assert.Single(response);
            Assert.Equal(ScheduleActionType.ScheduleWithConfiguration, response[0].Action);
            Assert.Equal(request1, response[0].BuildRequest);
        }

        /// <summary>
        /// Verify that if the affinity of our requests is "any", we will not create more than
        /// MaxNodeCount nodes (1 IP node + MaxNodeCount - 1 OOP nodes)
        /// </summary>
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/515")]
        public void TestMaxNodeCountNotExceededWithRequestsOfAffinityAny()
        {
            _host.BuildParameters.MaxNodeCount = 3;

            CreateConfiguration(1, "foo.proj");
            CreateConfiguration(2, "bar.proj");
            CreateConfiguration(3, "baz.proj");
            CreateConfiguration(4, "quz.proj");
            BuildRequest request1 = CreateBuildRequest(1, 1, new string[] { "foo" });
            BuildRequest request2 = CreateBuildRequest(2, 2, new string[] { "bar" });
            BuildRequest request3 = CreateBuildRequest(3, 3, new string[] { "baz" });
            BuildRequest request4 = CreateBuildRequest(4, 4, new string[] { "qux" });

            BuildRequestBlocker blocker = new BuildRequestBlocker(request1.ParentGlobalRequestId, new string[] { }, new BuildRequest[] { request1, request2, request3, request4 });
            List<ScheduleResponse> response = new List<ScheduleResponse>(_scheduler.ReportRequestBlocked(1, blocker));

            Assert.Equal(2, response.Count);
            Assert.Equal(ScheduleActionType.ScheduleWithConfiguration, response[0].Action);
            Assert.Equal(request1, response[0].BuildRequest);
            Assert.Equal(ScheduleActionType.CreateNode, response[1].Action);
            Assert.Equal(NodeAffinity.OutOfProc, response[1].RequiredNodeType);
            Assert.Equal(2, response[1].NumberOfNodesToCreate);
        }

        /// <summary>
        /// Verify that if we get 2 Any and 2 inproc requests, in that order, we will only create 2 nodes, since the initial inproc
        /// node will service an Any request instead of an inproc request, leaving only one non-inproc request for the second round
        /// of node creation.
        /// </summary>
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/515")]
        public void VerifyRequestOrderingDoesNotAffectNodeCreationCountWithInProcAndAnyRequests()
        {
            // Since we're creating our own BuildManager, we need to make sure that the default
            // one has properly relinquished the inproc node
            NodeProviderInProc nodeProviderInProc = ((IBuildComponentHost)BuildManager.DefaultBuildManager).GetComponent(BuildComponentType.InProcNodeProvider) as NodeProviderInProc;
            nodeProviderInProc?.Dispose();

            _host = new MockHost();
            _host.BuildParameters.MaxNodeCount = 3;

            _scheduler = new Scheduler();
            _scheduler.InitializeComponent(_host);

            _logger = new MockLogger();
            _parameters = new BuildParameters();
            _parameters.Loggers = new ILogger[] { _logger };
            _parameters.ShutdownInProcNodeOnBuildFinish = true;
            _buildManager = new BuildManager();

            CreateConfiguration(99, "parent.proj");
            _defaultParentRequest = CreateBuildRequest(99, 99, new string[] { }, null);

            CreateConfiguration(1, "foo.proj");
            BuildRequest request1 = CreateBuildRequest(1, 1, new string[] { "foo" }, NodeAffinity.Any, _defaultParentRequest);
            BuildRequest request2 = CreateBuildRequest(2, 1, new string[] { "bar" }, NodeAffinity.InProc, _defaultParentRequest);
            BuildRequest request3 = CreateBuildRequest(3, 1, new string[] { "bar" }, NodeAffinity.InProc, _defaultParentRequest);

            List<ScheduleResponse> response = new List<ScheduleResponse>(_scheduler.ReportRequestBlocked(1, new BuildRequestBlocker(-1, new string[] { }, new BuildRequest[] { _defaultParentRequest, request1, request2, request3 })));
            Assert.Single(response);
            Assert.Equal(ScheduleActionType.CreateNode, response[0].Action);
            Assert.Equal(NodeAffinity.InProc, response[0].RequiredNodeType);
            Assert.Equal(1, response[0].NumberOfNodesToCreate);

            List<NodeInfo> nodeInfos = new List<NodeInfo>(new NodeInfo[] { new NodeInfo(1, NodeProviderType.InProc) });
            List<ScheduleResponse> moreResponses = new List<ScheduleResponse>(_scheduler.ReportNodesCreated(nodeInfos));

            Assert.Equal(2, moreResponses.Count);
            Assert.Equal(ScheduleActionType.ScheduleWithConfiguration, moreResponses[0].Action);
            Assert.Equal(ScheduleActionType.CreateNode, moreResponses[1].Action);
            Assert.Equal(NodeAffinity.OutOfProc, moreResponses[1].RequiredNodeType);
            Assert.Equal(1, moreResponses[1].NumberOfNodesToCreate);
        }

        /// <summary>
        /// Verify that if the affinity of our requests is out-of-proc, we will create as many as
        /// MaxNodeCount out-of-proc nodes
        /// </summary>
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/515")]
        public void TestMaxNodeCountOOPNodesCreatedForOOPAffinitizedRequests()
        {
            _host.BuildParameters.MaxNodeCount = 3;

            CreateConfiguration(1, "foo.proj");
            CreateConfiguration(2, "bar.proj");
            CreateConfiguration(3, "baz.proj");
            CreateConfiguration(4, "quz.proj");
            BuildRequest request1 = CreateBuildRequest(1, 1, new string[] { "foo" }, NodeAffinity.OutOfProc, _defaultParentRequest);
            BuildRequest request2 = CreateBuildRequest(2, 2, new string[] { "bar" }, NodeAffinity.OutOfProc, _defaultParentRequest);
            BuildRequest request3 = CreateBuildRequest(3, 3, new string[] { "baz" }, NodeAffinity.OutOfProc, _defaultParentRequest);
            BuildRequest request4 = CreateBuildRequest(4, 4, new string[] { "qux" }, NodeAffinity.OutOfProc, _defaultParentRequest);

            BuildRequestBlocker blocker = new BuildRequestBlocker(request1.ParentGlobalRequestId, new string[] { }, new BuildRequest[] { request1, request2, request3, request4 });
            List<ScheduleResponse> response = new List<ScheduleResponse>(_scheduler.ReportRequestBlocked(1, blocker));

            // Parent request is blocked by the fact that both child requests require the out-of-proc node that doesn't
            // exist yet.
            Assert.Single(response);
            Assert.Equal(ScheduleActionType.CreateNode, response[0].Action);
            Assert.Equal(NodeAffinity.OutOfProc, response[0].RequiredNodeType);
            Assert.Equal(3, response[0].NumberOfNodesToCreate);
        }

        /// <summary>
        /// Verify that if only some of our requests are explicitly affinitized to out-of-proc, and that number
        /// is less than MaxNodeCount, that we only create MaxNodeCount - 1 OOP nodes (for a total of MaxNodeCount
        /// nodes, when the inproc node is included)
        /// </summary>
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/515")]
        public void TestMaxNodeCountNodesNotExceededWithSomeOOPRequests1()
        {
            _host.BuildParameters.MaxNodeCount = 3;

            CreateConfiguration(1, "foo.proj");
            CreateConfiguration(2, "bar.proj");
            CreateConfiguration(3, "baz.proj");
            CreateConfiguration(4, "quz.proj");
            BuildRequest request1 = CreateBuildRequest(1, 1, new string[] { "foo" }, NodeAffinity.OutOfProc, _defaultParentRequest);
            BuildRequest request2 = CreateBuildRequest(2, 2, new string[] { "bar" });
            BuildRequest request3 = CreateBuildRequest(3, 3, new string[] { "baz" });
            BuildRequest request4 = CreateBuildRequest(4, 4, new string[] { "qux" });

            BuildRequestBlocker blocker = new BuildRequestBlocker(request1.ParentGlobalRequestId, new string[] { }, new BuildRequest[] { request1, request2, request3, request4 });
            List<ScheduleResponse> response = new List<ScheduleResponse>(_scheduler.ReportRequestBlocked(1, blocker));

            Assert.Equal(2, response.Count);
            Assert.Equal(ScheduleActionType.ScheduleWithConfiguration, response[0].Action);
            Assert.Equal(request2, response[0].BuildRequest);
            Assert.Equal(ScheduleActionType.CreateNode, response[1].Action);
            Assert.Equal(NodeAffinity.OutOfProc, response[1].RequiredNodeType);
            Assert.Equal(2, response[1].NumberOfNodesToCreate);
        }

        /// <summary>
        /// Verify that if only some of our requests are explicitly affinitized to out-of-proc, and that number
        /// is less than MaxNodeCount, that we only create MaxNodeCount - 1 OOP nodes (for a total of MaxNodeCount
        /// nodes, when the inproc node is included)
        /// </summary>
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/515")]
        public void TestMaxNodeCountNodesNotExceededWithSomeOOPRequests2()
        {
            _host.BuildParameters.MaxNodeCount = 3;

            CreateConfiguration(1, "foo.proj");
            CreateConfiguration(2, "bar.proj");
            CreateConfiguration(3, "baz.proj");
            CreateConfiguration(4, "quz.proj");
            BuildRequest request1 = CreateBuildRequest(1, 1, new string[] { "foo" }, NodeAffinity.OutOfProc, _defaultParentRequest);
            BuildRequest request2 = CreateBuildRequest(2, 2, new string[] { "bar" }, NodeAffinity.OutOfProc, _defaultParentRequest);
            BuildRequest request3 = CreateBuildRequest(3, 3, new string[] { "baz" });
            BuildRequest request4 = CreateBuildRequest(4, 4, new string[] { "qux" });

            BuildRequestBlocker blocker = new BuildRequestBlocker(request1.ParentGlobalRequestId, new string[] { }, new BuildRequest[] { request1, request2, request3, request4 });
            List<ScheduleResponse> response = new List<ScheduleResponse>(_scheduler.ReportRequestBlocked(1, blocker));

            Assert.Equal(2, response.Count);
            Assert.Equal(ScheduleActionType.ScheduleWithConfiguration, response[0].Action);
            Assert.Equal(request3, response[0].BuildRequest);
            Assert.Equal(ScheduleActionType.CreateNode, response[1].Action);
            Assert.Equal(NodeAffinity.OutOfProc, response[1].RequiredNodeType);
            Assert.Equal(2, response[1].NumberOfNodesToCreate);
        }

        [Fact]
        public void SchedulerShouldHonorDisableInprocNode()
        {
            var s = new Scheduler();
            s.InitializeComponent(new MockHost(new BuildParameters {DisableInProcNode = true}));
            s.ForceAffinityOutOfProc.ShouldBeTrue();
        }

        /// <summary>
        /// Make sure that traversal projects are marked with an affinity of "InProc", which means that
        /// even if multiple are available, we should still only have the single inproc node.
        /// </summary>
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/515")]
        public void TestTraversalAffinityIsInProc()
        {
            _host.BuildParameters.MaxNodeCount = 3;

            CreateConfiguration(1, "dirs.proj");
            CreateConfiguration(2, "abc.metaproj");
            BuildRequest request1 = CreateBuildRequest(1, 1, new string[] { "foo" }, _defaultParentRequest);
            BuildRequest request2 = CreateBuildRequest(2, 2, new string[] { "bar" }, _defaultParentRequest);

            BuildRequestBlocker blocker = new BuildRequestBlocker(request1.ParentGlobalRequestId, new string[] { }, new BuildRequest[] { request1, request2 });
            List<ScheduleResponse> response = new List<ScheduleResponse>(_scheduler.ReportRequestBlocked(1, blocker));

            // There will be no request to create a new node, because both of the above requests are traversals,
            // which have an affinity of "inproc", and the inproc node already exists.
            Assert.Single(response);
            Assert.Equal(ScheduleActionType.ScheduleWithConfiguration, response[0].Action);
            Assert.Equal(request1, response[0].BuildRequest);
        }

        /// <summary>
        /// Make sure that traversal projects are marked with an affinity of "InProc", which means that
        /// even if multiple are available, we should still only have the single inproc node.
        /// </summary>
        [Fact]
        public void TestProxyAffinityIsInProc()
        {
            _host.BuildParameters.MaxNodeCount = 4;
            ReportDefaultParentRequestIsFinished();

            CreateConfiguration(1, "foo.csproj");

            BuildRequest request1 = CreateProxyBuildRequest(1, 1, new ProxyTargets(new Dictionary<string, string> {{"foo", "bar"}}), null);

            BuildRequestBlocker blocker = new BuildRequestBlocker(-1, new string[] { }, new[] { request1 });
            List<ScheduleResponse> response = new List<ScheduleResponse>(_scheduler.ReportRequestBlocked(1, blocker));

            // There will be no request to create a new node, because both of the above requests are proxy build requests,
            // which have an affinity of "inproc", and the inproc node already exists.
            Assert.Single(response);
            Assert.Equal(ScheduleActionType.ScheduleWithConfiguration, response[0].Action);
            Assert.Equal(request1, response[0].BuildRequest);
            Assert.Equal(Scheduler.InProcNodeId, response[0].NodeId);
        }

        /// <summary>
        /// With something approximating the BuildManager's build loop, make sure that we don't end up
        /// trying to create more nodes than we can actually support.
        /// </summary>
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/515")]
        public void VerifyNoOverCreationOfNodesWithBuildLoop()
        {
            // Since we're creating our own BuildManager, we need to make sure that the default
            // one has properly relinquished the inproc node
            NodeProviderInProc nodeProviderInProc = ((IBuildComponentHost)BuildManager.DefaultBuildManager).GetComponent(BuildComponentType.InProcNodeProvider) as NodeProviderInProc;
            nodeProviderInProc?.Dispose();

            _host = new MockHost();
            _host.BuildParameters.MaxNodeCount = 3;

            _scheduler = new Scheduler();
            _scheduler.InitializeComponent(_host);

            _parameters = new BuildParameters();
            _parameters.ShutdownInProcNodeOnBuildFinish = true;
            _buildManager = new BuildManager();

            CreateConfiguration(99, "parent.proj");
            _defaultParentRequest = CreateBuildRequest(99, 99, new string[] { }, null);

            CreateConfiguration(1, "foo.proj");
            BuildRequest request1 = CreateBuildRequest(1, 1, new string[] { "foo" }, NodeAffinity.OutOfProc, _defaultParentRequest);
            CreateConfiguration(2, "foo2.proj");
            BuildRequest request2 = CreateBuildRequest(2, 2, new string[] { "bar" }, NodeAffinity.OutOfProc, _defaultParentRequest);
            CreateConfiguration(3, "foo3.proj");
            BuildRequest request3 = CreateBuildRequest(3, 3, new string[] { "bar" }, NodeAffinity.InProc, _defaultParentRequest);

            List<ScheduleResponse> responses = new List<ScheduleResponse>(_scheduler.ReportRequestBlocked(1, new BuildRequestBlocker(-1, new string[] { }, new BuildRequest[] { _defaultParentRequest, request1, request2, request3 })));

            int nextNodeId = 1;
            bool inProcNodeExists = false;
            MockPerformSchedulingActions(responses, ref nextNodeId, ref inProcNodeExists);
            Assert.Equal(4, nextNodeId); // 3 nodes
        }

        /// <summary>
        /// Verify that if we get two requests but one of them is a failure, we only get the failure result back.
        /// </summary>
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/515")]
        public void TestTwoRequestsWithFirstFailure()
        {
            CreateConfiguration(1, "foo.proj");
            BuildRequest request1 = CreateBuildRequest(1, 1, new string[] { "foo" });
            BuildResult result1 = CacheBuildResult(request1, "foo", BuildResultUtilities.GetStopWithErrorResult());
            BuildRequest request2 = CreateBuildRequest(2, 1, new string[] { "bar" });

            BuildRequestBlocker blocker = new BuildRequestBlocker(request1.ParentGlobalRequestId, new string[] { }, new BuildRequest[] { request1, request2 });
            List<ScheduleResponse> response = new List<ScheduleResponse>(_scheduler.ReportRequestBlocked(1, blocker));

            Assert.Equal(2, response.Count);
            Assert.True(ResultsCache_Tests.AreResultsIdentical(result1, response[0].Unblocker.Result));
            Assert.Equal(ScheduleActionType.ScheduleWithConfiguration, response[1].Action);
        }

        /// <summary>
        /// Verify that if we get two requests but one of them is a failure, we only get the failure result back.
        /// </summary>
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/515")]
        public void TestTwoRequestsWithSecondFailure()
        {
            CreateConfiguration(1, "foo.proj");
            BuildRequest request1 = CreateBuildRequest(1, 1, new string[] { "foo" });
            BuildRequest request2 = CreateBuildRequest(2, 1, new string[] { "bar" });
            BuildResult result2 = CacheBuildResult(request2, "bar", BuildResultUtilities.GetStopWithErrorResult());

            BuildRequestBlocker blocker = new BuildRequestBlocker(request1.ParentGlobalRequestId, new string[] { }, new BuildRequest[] { request1, request2 });
            List<ScheduleResponse> response = new List<ScheduleResponse>(_scheduler.ReportRequestBlocked(1, blocker));

            Assert.Equal(2, response.Count);
            Assert.True(ResultsCache_Tests.AreResultsIdentical(result2, response[0].Unblocker.Result));
            Assert.Equal(ScheduleActionType.ScheduleWithConfiguration, response[1].Action);
        }

        /// <summary>
        /// Verify that if we get three requests but one of them is a failure, we only get the failure result back.
        /// </summary>
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/515")]
        public void TestThreeRequestsWithOneFailure()
        {
            CreateConfiguration(1, "foo.proj");
            BuildRequest request1 = CreateBuildRequest(1, 1, new string[] { "foo" });
            BuildRequest request2 = CreateBuildRequest(2, 1, new string[] { "bar" });
            BuildResult result2 = CacheBuildResult(request2, "bar", BuildResultUtilities.GetStopWithErrorResult());
            BuildRequest request3 = CreateBuildRequest(3, 1, new string[] { "baz" });

            BuildRequestBlocker blocker = new BuildRequestBlocker(request1.ParentGlobalRequestId, new string[] { }, new BuildRequest[] { request1, request2, request3 });
            List<ScheduleResponse> response = new List<ScheduleResponse>(_scheduler.ReportRequestBlocked(1, blocker));

            Assert.Equal(2, response.Count);
            Assert.True(ResultsCache_Tests.AreResultsIdentical(result2, response[0].Unblocker.Result));
            Assert.Equal(ScheduleActionType.ScheduleWithConfiguration, response[1].Action);
        }

        /// <summary>
        /// Verify that providing a result to the only outstanding request results in build complete.
        /// </summary>
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/515")]
        public void TestResult()
        {
            CreateConfiguration(1, "foo.proj");
            BuildRequest request = CreateBuildRequest(1, 1);
            BuildRequestBlocker blocker = new BuildRequestBlocker(request.ParentGlobalRequestId, new string[] { }, new BuildRequest[] { request });
            List<ScheduleResponse> response = new List<ScheduleResponse>(_scheduler.ReportRequestBlocked(1, blocker));

            BuildResult result = CreateBuildResult(request, "foo", BuildResultUtilities.GetSuccessResult());
            response = new List<ScheduleResponse>(_scheduler.ReportResult(1, result));

            Assert.Equal(2, response.Count);

            // First response is reporting the results for this request to the parent
            Assert.Equal(ScheduleActionType.ReportResults, response[0].Action);

            // Second response is continuing execution of the parent
            Assert.Equal(ScheduleActionType.ResumeExecution, response[1].Action);
            Assert.Equal(request.ParentGlobalRequestId, response[1].Unblocker.BlockedRequestId);
        }

        /// <summary>
        /// Tests that the detailed summary setting causes the summary to be produced.
        /// </summary>
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/515")]
        public void TestDetailedSummary()
        {
            string contents = ObjectModelHelpers.CleanupFileContents(@"
<Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
 <Target Name='test'>
	<Message Text='[success]'/>
 </Target>
</Project>
");

            _parameters.DetailedSummary = true;
            Project project = new Project(new XmlTextReader(new StringReader(contents)));
            BuildRequestData data = new BuildRequestData(project.CreateProjectInstance(), new string[] { "test" });
            BuildResult result = _buildManager.Build(_parameters, data);
            Assert.Equal(BuildResultCode.Success, result.OverallResult);
            _logger.AssertLogContains("[success]");

            // Verify the existence of the first line of the header.
            StringReader reader = new StringReader(ResourceUtilities.GetResourceString("BuildHierarchyHeader"));
            _logger.AssertLogContains(reader.ReadLine());
        }

        /// <summary>
        /// Creates a configuration and stores it in the cache.
        /// </summary>
        private void CreateConfiguration(int configId, string file)
        {
            BuildRequestData data = new BuildRequestData(file, new Dictionary<string, string>(), "4.0", new string[] { }, null);
            BuildRequestConfiguration config = new BuildRequestConfiguration(configId, data, "4.0");
            config.ProjectInitialTargets = new List<string>();
            config.ProjectDefaultTargets = new List<string>();

            (_host.GetComponent(BuildComponentType.ConfigCache) as IConfigCache).AddConfiguration(config);
        }

        /// <summary>
        /// Creates and caches a built result.
        /// </summary>
        private BuildResult CacheBuildResult(BuildRequest request, string target, WorkUnitResult workUnitResult)
        {
            BuildResult result = CreateBuildResult(request, target, workUnitResult);
            IResultsCache resultsCache = _host.GetComponent(BuildComponentType.ResultsCache) as IResultsCache;
            resultsCache.AddResult(result);
            return result;
        }

        /// <summary>
        /// Creates a build result for a request
        /// </summary>
        private BuildResult CreateBuildResult(BuildRequest request, string target, WorkUnitResult workUnitResult)
        {
            BuildResult result = new BuildResult(request);
            result.AddResultsForTarget(target, new TargetResult(new TaskItem[] { }, workUnitResult));
            return result;
        }

        /// <summary>
        /// Creates a build request.
        /// </summary>
        private BuildRequest CreateBuildRequest(int nodeRequestId, int configId)
        {
            return CreateBuildRequest(nodeRequestId, configId, new string[] { });
        }

        /// <summary>
        /// Creates a build request.
        /// </summary>
        private BuildRequest CreateBuildRequest(int nodeRequestId, int configId, string[] targets)
        {
            return CreateBuildRequest(nodeRequestId, configId, targets, _defaultParentRequest);
        }

        /// <summary>
        /// Creates a build request.
        /// </summary>
        private BuildRequest CreateBuildRequest(int nodeRequestId, int configId, string[] targets, BuildRequest parentRequest)
        {
            return CreateBuildRequest(nodeRequestId, configId, targets, NodeAffinity.Any, parentRequest);
        }

        /// <summary>
        /// Creates a build request.
        /// </summary>
        private BuildRequest CreateBuildRequest(int nodeRequestId, int configId, string[] targets, NodeAffinity nodeAffinity, BuildRequest parentRequest, ProxyTargets proxyTargets = null)
        {
            (targets == null ^ proxyTargets == null).ShouldBeTrue();

            HostServices hostServices = null;

            if (nodeAffinity != NodeAffinity.Any)
            {
                hostServices = new HostServices();
                hostServices.SetNodeAffinity(String.Empty, nodeAffinity);
            }

            if (targets != null)
            {
                return new BuildRequest(
                    submissionId: 1,
                    nodeRequestId,
                    configId,
                    targets,
                    hostServices,
                    BuildEventContext.Invalid,
                    parentRequest);
            }

            parentRequest.ShouldBeNull();
            return new BuildRequest(
                submissionId: 1,
                nodeRequestId,
                configId,
                proxyTargets,
                hostServices);
        }

        private BuildRequest CreateProxyBuildRequest(int nodeRequestId, int configId, ProxyTargets proxyTargets, BuildRequest parentRequest)
        {
            return CreateBuildRequest(
                nodeRequestId,
                configId,
                null,
                NodeAffinity.Any,
                parentRequest,
                proxyTargets);
        }

        /// <summary>
        /// Method that fakes the actions done by BuildManager.PerformSchedulingActions
        /// </summary>
        private void MockPerformSchedulingActions(IEnumerable<ScheduleResponse> responses, ref int nodeId, ref bool inProcNodeExists)
        {
            List<NodeInfo> nodeInfos = new List<NodeInfo>();
            foreach (ScheduleResponse response in responses)
            {
                if (response.Action == ScheduleActionType.CreateNode)
                {
                    NodeProviderType nodeType;
                    if (response.RequiredNodeType == NodeAffinity.InProc || (response.RequiredNodeType == NodeAffinity.Any && !inProcNodeExists))
                    {
                        nodeType = NodeProviderType.InProc;
                        inProcNodeExists = true;
                    }
                    else
                    {
                        nodeType = NodeProviderType.OutOfProc;
                    }

                    for (int i = 0; i < response.NumberOfNodesToCreate; i++)
                    {
                        nodeInfos.Add(new NodeInfo(nodeId, nodeType));
                        nodeId++;
                    }
                }
            }

            if (nodeInfos.Count > 0)
            {
                List<ScheduleResponse> moreResponses = new List<ScheduleResponse>(_scheduler.ReportNodesCreated(nodeInfos));
                MockPerformSchedulingActions(moreResponses, ref nodeId, ref inProcNodeExists);
            }
        }

        private void ReportDefaultParentRequestIsFinished()
        {
            var buildResult = new BuildResult(_defaultParentRequest);
            _scheduler.ReportResult(_defaultParentRequest.NodeRequestId, buildResult);
        }
    }
}
