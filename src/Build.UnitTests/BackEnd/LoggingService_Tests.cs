// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using Microsoft.Build.Framework;
using Microsoft.Build.BackEnd.Logging;
using Microsoft.Build.BackEnd;
using Microsoft.Build.Logging;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Exceptions;
using Microsoft.Build.Shared;
using Microsoft.Build.UnitTests.BackEnd;
using System.Threading;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using Shouldly;
using Xunit;

namespace Microsoft.Build.UnitTests.Logging
{
    /// <summary>
    /// Test the logging service component
    /// </summary>
    public class LoggingService_Tests
    {
        #region Data
        /// <summary>
        /// An already instantiated and initialized service.
        /// This is used so the host object does not need to be
        /// used in every test method.
        /// </summary>
        private LoggingService _initializedService;

        #endregion

        #region Setup

        /// <summary>
        /// This method is run before each test case is run.
        /// We instantiate and initialize a new logging service each time
        /// </summary>
        public LoggingService_Tests()
        {
            InitializeLoggingService();
        }

        #endregion

        #region Test BuildComponent Methods

        /// <summary>
        /// Verify the CreateLogger method create a LoggingService in both Synchronous mode
        /// and Asynchronous mode.
        /// </summary>
        [Fact]
        public void CreateLogger()
        {
            // Generic host which has some default properties set inside of it
            IBuildComponent logServiceComponent = (IBuildComponent)LoggingService.CreateLoggingService(LoggerMode.Synchronous, 1);

            // Create a synchronous logging service and do some quick checks
            Assert.NotNull(logServiceComponent);
            LoggingService logService = (LoggingService)logServiceComponent;
            Assert.Equal(LoggerMode.Synchronous, logService.LoggingMode);
            Assert.Equal(LoggingServiceState.Instantiated, logService.ServiceState);

            // Create an asynchronous logging service
            logServiceComponent = (IBuildComponent)LoggingService.CreateLoggingService(LoggerMode.Asynchronous, 1);
            Assert.NotNull(logServiceComponent);
            logService = (LoggingService)logServiceComponent;
            Assert.Equal(LoggerMode.Asynchronous, logService.LoggingMode);
            Assert.Equal(LoggingServiceState.Instantiated, logService.ServiceState);

            // Shutdown logging thread
            logServiceComponent.InitializeComponent(new MockHost());
            logServiceComponent.ShutdownComponent();
            Assert.Equal(LoggingServiceState.Shutdown, logService.ServiceState);
        }

        /// <summary>
        /// Test the IBuildComponent method InitializeComponent, make sure the component gets the parameters it expects
        /// </summary>
        [Fact]
        public void InitializeComponent()
        {
            IBuildComponent logServiceComponent = (IBuildComponent)LoggingService.CreateLoggingService(LoggerMode.Synchronous, 1);

            BuildParameters parameters = new BuildParameters();
            parameters.MaxNodeCount = 4;
            parameters.OnlyLogCriticalEvents = true;

            IBuildComponentHost loggingHost = new MockHost(parameters);

            // Make sure we are in the Instantiated state before initializing
            Assert.Equal(LoggingServiceState.Instantiated, ((LoggingService)logServiceComponent).ServiceState);

            logServiceComponent.InitializeComponent(loggingHost);

            // Make sure that the parameters in the host are set in the logging service
            LoggingService service = (LoggingService)logServiceComponent;
            Assert.Equal(LoggingServiceState.Initialized, service.ServiceState);
            Assert.Equal(4, service.MaxCPUCount);
            Assert.True(service.OnlyLogCriticalEvents);
        }

        /// <summary>
        /// Verify the correct exception is thrown when a null Component host is passed in
        /// </summary>
        [Fact]
        public void InitializeComponentNullHost()
        {
            Assert.Throws<InternalErrorException>(() =>
            {
                IBuildComponent logServiceComponent = (IBuildComponent)LoggingService.CreateLoggingService(LoggerMode.Synchronous, 1);
                logServiceComponent.InitializeComponent(null);
            }
           );
        }
        /// <summary>
        /// Verify an exception is thrown if in initialized is called after the service has been shutdown
        /// </summary>
        [Fact]
        public void InitializeComponentAfterShutdown()
        {
            Assert.Throws<InternalErrorException>(() =>
            {
                _initializedService.ShutdownComponent();
                _initializedService.InitializeComponent(new MockHost());
            }
           );
        }
        /// <summary>
        /// Verify the correct exceptions are thrown if the loggers crash
        /// when they are shutdown
        /// </summary>
        [Fact]
        public void ShutDownComponentExceptionsInForwardingLogger()
        {
            // Cause a logger exception in the shutdown of the logger
            string className = "Microsoft.Build.UnitTests.Logging.LoggingService_Tests+ShutdownLoggerExceptionFL";
            Type exceptionType = typeof(LoggerException);
            VerifyShutdownExceptions(null, className, exceptionType);
            Assert.Equal(LoggingServiceState.Shutdown, _initializedService.ServiceState);

            // Cause a general exception which should result in an InternalLoggerException
            className = "Microsoft.Build.UnitTests.Logging.LoggingService_Tests+ShutdownGeneralExceptionFL";
            exceptionType = typeof(InternalLoggerException);
            VerifyShutdownExceptions(null, className, exceptionType);
            Assert.Equal(LoggingServiceState.Shutdown, _initializedService.ServiceState);

            // Cause a StackOverflow exception in the shutdown of the logger
            // this kind of exception should not be caught
            className = "Microsoft.Build.UnitTests.Logging.LoggingService_Tests+ShutdownStackoverflowExceptionFL";
            exceptionType = typeof(StackOverflowException);
            VerifyShutdownExceptions(null, className, exceptionType);

            Assert.Equal(LoggingServiceState.Shutdown, _initializedService.ServiceState);
        }

        /// <summary>
        /// Verify the correct exceptions are thrown when ILoggers
        /// throw exceptions during shutdown
        /// </summary>
        [Fact]
        public void ShutDownComponentExceptionsInLogger()
        {
            LoggerThrowException logger = new LoggerThrowException(true, false, new LoggerException("Hello"));
            VerifyShutdownExceptions(logger, null, typeof(LoggerException));

            logger = new LoggerThrowException(true, false, new Exception("boo"));
            VerifyShutdownExceptions(logger, null, typeof(InternalLoggerException));

            logger = new LoggerThrowException(true, false, new StackOverflowException());
            VerifyShutdownExceptions(logger, null, typeof(StackOverflowException));

            Assert.Equal(LoggingServiceState.Shutdown, _initializedService.ServiceState);
        }

        /// <summary>
        /// Make sure an exception is thrown if shutdown is called
        /// more than once
        /// </summary>
        [Fact]
        public void DoubleShutdown()
        {
            Assert.Throws<InternalErrorException>(() =>
            {
                _initializedService.ShutdownComponent();
                _initializedService.ShutdownComponent();
            }
           );
        }
        #endregion

        #region RegisterLogger
        /// <summary>
        /// Verify we get an exception when a null logger is passed in
        /// </summary>
        [Fact]
        public void NullLogger()
        {
            Assert.Throws<InternalErrorException>(() =>
            {
                _initializedService.RegisterLogger(null);
            }
           );
        }
        /// <summary>
        /// Verify we get an exception when we try and register a logger
        /// and the system has already shutdown
        /// </summary>
        [Fact]
        public void RegisterLoggerServiceShutdown()
        {
            Assert.Throws<InternalErrorException>(() =>
            {
                _initializedService.ShutdownComponent();
                RegularILogger regularILogger = new RegularILogger();
                _initializedService.RegisterLogger(regularILogger);
            }
           );
        }
        /// <summary>
        /// Verify a logger exception when initializing a logger is rethrown
        /// as a logger exception
        /// </summary>
        [Fact]
        public void LoggerExceptionInInitialize()
        {
            Assert.Throws<LoggerException>(() =>
            {
                LoggerThrowException exceptionLogger = new LoggerThrowException(false, true, new LoggerException());
                _initializedService.RegisterLogger(exceptionLogger);
            }
           );
        }
        /// <summary>
        /// Verify a general exception when initializing a logger is wrapped
        /// as a InternalLogger exception
        /// </summary>
        [Fact]
        public void GeneralExceptionInInitialize()
        {
            Assert.Throws<InternalLoggerException>(() =>
            {
                LoggerThrowException exceptionLogger = new LoggerThrowException(false, true, new Exception());
                _initializedService.RegisterLogger(exceptionLogger);
            }
           );
        }

        /// <summary>
        /// Verify a critical exception is not wrapped
        /// </summary>
        [Fact]
        public void ILoggerExceptionInInitialize()
        {
            Assert.Throws<StackOverflowException>(() =>
            {
                LoggerThrowException exceptionLogger = new LoggerThrowException(false, true, new StackOverflowException());
                _initializedService.RegisterLogger(exceptionLogger);
            }
           );
        }

        /// <summary>
        /// Register an good Logger and verify it was registered.
        /// </summary>
        [Fact]
        public void RegisterILoggerAndINodeLoggerGood()
        {
            ConsoleLogger consoleLogger = new ConsoleLogger();
            RegularILogger regularILogger = new RegularILogger();
            Assert.True(_initializedService.RegisterLogger(consoleLogger));
            Assert.True(_initializedService.RegisterLogger(regularILogger));
            Assert.NotNull(_initializedService.RegisteredLoggerTypeNames);

            // Should have 2 central loggers and 1 forwarding logger
            Assert.Equal(3, _initializedService.RegisteredLoggerTypeNames.Count);
            Assert.Contains("Microsoft.Build.BackEnd.Logging.CentralForwardingLogger", _initializedService.RegisteredLoggerTypeNames);
            Assert.Contains("Microsoft.Build.UnitTests.Logging.LoggingService_Tests+RegularILogger", _initializedService.RegisteredLoggerTypeNames);
            Assert.Contains("Microsoft.Build.Logging.ConsoleLogger", _initializedService.RegisteredLoggerTypeNames);

            // Should have 1 event sink
            Assert.NotNull(_initializedService.RegisteredSinkNames);
            Assert.Single(_initializedService.RegisteredSinkNames);
        }

        /// <summary>
        /// Try and register the same logger multiple times
        /// </summary>
        [Fact]
        public void RegisterDuplicateLogger()
        {
            ConsoleLogger consoleLogger = new ConsoleLogger();
            RegularILogger regularILogger = new RegularILogger();
            Assert.True(_initializedService.RegisterLogger(consoleLogger));
            Assert.False(_initializedService.RegisterLogger(consoleLogger));
            Assert.True(_initializedService.RegisterLogger(regularILogger));
            Assert.False(_initializedService.RegisterLogger(regularILogger));
            Assert.NotNull(_initializedService.RegisteredLoggerTypeNames);

            Assert.Equal(3, _initializedService.RegisteredLoggerTypeNames.Count);
            Assert.Contains("Microsoft.Build.BackEnd.Logging.CentralForwardingLogger", _initializedService.RegisteredLoggerTypeNames);
            Assert.Contains("Microsoft.Build.UnitTests.Logging.LoggingService_Tests+RegularILogger", _initializedService.RegisteredLoggerTypeNames);
            Assert.Contains("Microsoft.Build.Logging.ConsoleLogger", _initializedService.RegisteredLoggerTypeNames);

            // Should have 1 event sink
            Assert.NotNull(_initializedService.RegisteredSinkNames);
            Assert.Single(_initializedService.RegisteredSinkNames);
        }

        #endregion

        #region RegisterDistributedLogger
        /// <summary>
        /// Verify we get an exception when a null logger forwarding logger is passed in
        /// </summary>
        [Fact]
        public void NullForwardingLogger()
        {
            Assert.Throws<InternalErrorException>(() =>
            {
                _initializedService.RegisterDistributedLogger(null, null);
            }
           );
        }
        /// <summary>
        /// Verify we get an exception when we try and register a distributed logger
        /// and the system has already shutdown
        /// </summary>
        [Fact]
        public void RegisterDistributedLoggerServiceShutdown()
        {
            Assert.Throws<InternalErrorException>(() =>
            {
                _initializedService.ShutdownComponent();
                string className = "Microsoft.Build.Logging.ConfigurableForwardingLogger";
#if FEATURE_ASSEMBLY_LOCATION
                LoggerDescription description = CreateLoggerDescription(className, Assembly.GetAssembly(typeof(ProjectCollection)).FullName, true);
#else
                LoggerDescription description = CreateLoggerDescription(className, typeof(ProjectCollection).GetTypeInfo().Assembly.FullName, true);
#endif
                _initializedService.RegisterDistributedLogger(null, description);
            }
           );
        }
        /// <summary>
        /// Register both a good central logger and a good forwarding logger
        /// </summary>
        [Fact]
        public void RegisterGoodDistributedAndCentralLogger()
        {
            string configurableClassName = "Microsoft.Build.Logging.ConfigurableForwardingLogger";
            string distributedClassName = "Microsoft.Build.Logging.DistributedFileLogger";
#if FEATURE_ASSEMBLY_LOCATION
            LoggerDescription configurableDescription = CreateLoggerDescription(configurableClassName, Assembly.GetAssembly(typeof(ProjectCollection)).FullName, true);
            LoggerDescription distributedDescription = CreateLoggerDescription(distributedClassName, Assembly.GetAssembly(typeof(ProjectCollection)).FullName, true);
#else
            LoggerDescription configurableDescription = CreateLoggerDescription(configurableClassName, typeof(ProjectCollection).GetTypeInfo().Assembly.FullName, true);
            LoggerDescription distributedDescription = CreateLoggerDescription(distributedClassName, typeof(ProjectCollection).GetTypeInfo().Assembly.FullName, true);
#endif

            DistributedFileLogger fileLogger = new DistributedFileLogger();
            RegularILogger regularILogger = new RegularILogger();
            Assert.True(_initializedService.RegisterDistributedLogger(regularILogger, configurableDescription));
            Assert.True(_initializedService.RegisterDistributedLogger(null, distributedDescription));
            Assert.NotNull(_initializedService.RegisteredLoggerTypeNames);

            // Should have 2 central loggers and 2 forwarding logger
            Assert.Equal(4, _initializedService.RegisteredLoggerTypeNames.Count);
            Assert.Contains("Microsoft.Build.Logging.ConfigurableForwardingLogger", _initializedService.RegisteredLoggerTypeNames);
            Assert.Contains("Microsoft.Build.UnitTests.Logging.LoggingService_Tests+RegularILogger", _initializedService.RegisteredLoggerTypeNames);
            Assert.Contains("Microsoft.Build.Logging.DistributedFileLogger", _initializedService.RegisteredLoggerTypeNames);
            Assert.Contains("Microsoft.Build.BackEnd.Logging.NullCentralLogger", _initializedService.RegisteredLoggerTypeNames);

            // Should have 2 event sink
            Assert.NotNull(_initializedService.RegisteredSinkNames);
            Assert.Equal(2, _initializedService.RegisteredSinkNames.Count);
            Assert.Equal(2, _initializedService.LoggerDescriptions.Count);
        }

        /// <summary>
        /// Have a one forwarding logger which forwards build started and finished and have one which does not and a regular logger. Expect the central loggers to all get
        /// one build started and one build finished event only.
        /// </summary>
        [Fact]
        public void RegisterGoodDistributedAndCentralLoggerTestBuildStartedFinished()
        {
            string configurableClassNameA = "Microsoft.Build.Logging.ConfigurableForwardingLogger";
            string configurableClassNameB = "Microsoft.Build.Logging.ConfigurableForwardingLogger";

#if FEATURE_ASSEMBLY_LOCATION
            LoggerDescription configurableDescriptionA = CreateLoggerDescription(configurableClassNameA, Assembly.GetAssembly(typeof(ProjectCollection)).FullName, true);
            LoggerDescription configurableDescriptionB = CreateLoggerDescription(configurableClassNameB, Assembly.GetAssembly(typeof(ProjectCollection)).FullName, true);
#else
            LoggerDescription configurableDescriptionA = CreateLoggerDescription(configurableClassNameA, typeof(ProjectCollection).GetTypeInfo().Assembly.FullName, true);
            LoggerDescription configurableDescriptionB = CreateLoggerDescription(configurableClassNameB, typeof(ProjectCollection).GetTypeInfo().Assembly.FullName, true);
#endif

            RegularILogger regularILoggerA = new RegularILogger();
            RegularILogger regularILoggerB = new RegularILogger();
            RegularILogger regularILoggerC = new RegularILogger();
            Assert.True(_initializedService.RegisterDistributedLogger(regularILoggerA, configurableDescriptionA));
            Assert.True(_initializedService.RegisterDistributedLogger(regularILoggerB, configurableDescriptionB));
            Assert.True(_initializedService.RegisterLogger(regularILoggerC));
            Assert.NotNull(_initializedService.RegisteredLoggerTypeNames);

            _initializedService.LogBuildStarted();
            Assert.Equal(1, regularILoggerA.BuildStartedCount);
            Assert.Equal(1, regularILoggerB.BuildStartedCount);
            Assert.Equal(1, regularILoggerC.BuildStartedCount);

            _initializedService.LogBuildFinished(true);
            Assert.Equal(1, regularILoggerA.BuildFinishedCount);
            Assert.Equal(1, regularILoggerB.BuildFinishedCount);
            Assert.Equal(1, regularILoggerC.BuildFinishedCount);

            // Make sure if we call build started again we only get one other build started event.
            _initializedService.LogBuildStarted();
            Assert.Equal(2, regularILoggerA.BuildStartedCount);
            Assert.Equal(2, regularILoggerB.BuildStartedCount);
            Assert.Equal(2, regularILoggerC.BuildStartedCount);

            // Make sure if we call build started again we only get one other build started event.
            _initializedService.LogBuildFinished(true);
            Assert.Equal(2, regularILoggerA.BuildFinishedCount);
            Assert.Equal(2, regularILoggerB.BuildFinishedCount);
            Assert.Equal(2, regularILoggerC.BuildFinishedCount);
        }

        /// <summary>
        /// Try and register a duplicate central logger
        /// </summary>
        [Fact]
        public void RegisterDuplicateCentralLogger()
        {
            string className = "Microsoft.Build.Logging.ConfigurableForwardingLogger";
#if FEATURE_ASSEMBLY_LOCATION
            LoggerDescription description = CreateLoggerDescription(className, Assembly.GetAssembly(typeof(ProjectCollection)).FullName, true);
#else
            LoggerDescription description = CreateLoggerDescription(className, typeof(ProjectCollection).GetTypeInfo().Assembly.FullName, true);
#endif

            RegularILogger regularILogger = new RegularILogger();
            Assert.True(_initializedService.RegisterDistributedLogger(regularILogger, description));
            Assert.False(_initializedService.RegisterDistributedLogger(regularILogger, description));

            // Should have 2 central loggers and 1 forwarding logger
            Assert.Equal(2, _initializedService.RegisteredLoggerTypeNames.Count);
            Assert.Contains("Microsoft.Build.Logging.ConfigurableForwardingLogger", _initializedService.RegisteredLoggerTypeNames);
            Assert.Contains("Microsoft.Build.UnitTests.Logging.LoggingService_Tests+RegularILogger", _initializedService.RegisteredLoggerTypeNames);

            // Should have 1 sink
            Assert.NotNull(_initializedService.RegisteredSinkNames);
            Assert.Single(_initializedService.RegisteredSinkNames);
            Assert.Single(_initializedService.LoggerDescriptions);
        }

        /// <summary>
        /// Try and register a duplicate Forwarding logger
        /// </summary>
        [Fact]
        public void RegisterDuplicateForwardingLoggerLogger()
        {
            string className = "Microsoft.Build.Logging.ConfigurableForwardingLogger";
#if FEATURE_ASSEMBLY_LOCATION
            LoggerDescription description = CreateLoggerDescription(className, Assembly.GetAssembly(typeof(ProjectCollection)).FullName, true);
#else
            LoggerDescription description = CreateLoggerDescription(className, typeof(ProjectCollection).GetTypeInfo().Assembly.FullName, true);
#endif

            RegularILogger regularILogger = new RegularILogger();
            Assert.True(_initializedService.RegisterDistributedLogger(regularILogger, description));
            Assert.True(_initializedService.RegisterDistributedLogger(null, description));
            Assert.Equal(4, _initializedService.RegisteredLoggerTypeNames.Count);

            // Verify there are two versions in the type names, one for each description
            int countForwardingLogger = 0;
            foreach (string loggerName in _initializedService.RegisteredLoggerTypeNames)
            {
                if (String.Equals("Microsoft.Build.Logging.ConfigurableForwardingLogger", loggerName, StringComparison.OrdinalIgnoreCase))
                {
                    countForwardingLogger++;
                }
            }

            Assert.Equal(2, countForwardingLogger);
            Assert.Contains("Microsoft.Build.BackEnd.Logging.NullCentralLogger", _initializedService.RegisteredLoggerTypeNames);
            Assert.Contains("Microsoft.Build.UnitTests.Logging.LoggingService_Tests+RegularILogger", _initializedService.RegisteredLoggerTypeNames);

            // Should have 2 sink
            Assert.NotNull(_initializedService.RegisteredSinkNames);
            Assert.Equal(2, _initializedService.RegisteredSinkNames.Count);
            Assert.Equal(2, _initializedService.LoggerDescriptions.Count);
        }

        #endregion

        #region RegisterLoggerDescriptions
        /// <summary>
        /// Verify we get an exception when a null description collection is passed in
        /// </summary>
        [Fact]
        public void NullDescriptionCollection()
        {
            Assert.Throws<InternalErrorException>(() =>
            {
                _initializedService.InitializeNodeLoggers(null, new EventSourceSink(), 3);
            }
           );
        }
        /// <summary>
        /// Verify we get an exception when an empty description collection is passed in
        /// </summary>
        [Fact]
        public void EmptyDescriptionCollection()
        {
            Assert.Throws<InternalErrorException>(() =>
            {
                _initializedService.InitializeNodeLoggers(new List<LoggerDescription>(), new EventSourceSink(), 3);
            }
           );
        }
        /// <summary>
        /// Verify we get an exception when we try and register a description and the component has already shutdown
        /// </summary>
        [Fact]
        public void NullForwardingLoggerSink()
        {
            Assert.Throws<InternalErrorException>(() =>
            {
                string className = "Microsoft.Build.Logging.ConfigurableForwardingLogger";
#if FEATURE_ASSEMBLY_LOCATION
                LoggerDescription description = CreateLoggerDescription(className, Assembly.GetAssembly(typeof(ProjectCollection)).FullName, true);
#else
                LoggerDescription description = CreateLoggerDescription(className, typeof(ProjectCollection).GetTypeInfo().Assembly.FullName, true);
#endif
                _initializedService.ShutdownComponent();
                List<LoggerDescription> tempList = new List<LoggerDescription>();
                tempList.Add(description);
                _initializedService.InitializeNodeLoggers(tempList, new EventSourceSink(), 2);
            }
           );
        }
        /// <summary>
        /// Register both a good central logger and a good forwarding logger
        /// </summary>
        [Fact]
        public void RegisterGoodDiscriptions()
        {
            string configurableClassName = "Microsoft.Build.Logging.ConfigurableForwardingLogger";
            string distributedClassName = "Microsoft.Build.BackEnd.Logging.CentralForwardingLogger";
            EventSourceSink sink = new EventSourceSink();
            EventSourceSink sink2 = new EventSourceSink();
            List<LoggerDescription> loggerDescriptions = new List<LoggerDescription>();
#if FEATURE_ASSEMBLY_LOCATION
            loggerDescriptions.Add(CreateLoggerDescription(configurableClassName, Assembly.GetAssembly(typeof(ProjectCollection)).FullName, true));
            loggerDescriptions.Add(CreateLoggerDescription(distributedClassName, Assembly.GetAssembly(typeof(ProjectCollection)).FullName, true));
#else
            loggerDescriptions.Add(CreateLoggerDescription(configurableClassName, typeof(ProjectCollection).GetTypeInfo().Assembly.FullName, true));
            loggerDescriptions.Add(CreateLoggerDescription(distributedClassName, typeof(ProjectCollection).GetTypeInfo().Assembly.FullName, true));
#endif

            // Register some descriptions with a sink
            _initializedService.InitializeNodeLoggers(loggerDescriptions, sink, 1);

            // Register the same descriptions with another sink (so we can see that another sink was added)
            _initializedService.InitializeNodeLoggers(loggerDescriptions, sink2, 1);

            // Register the descriptions again with the same sink so we can verify that another sink was not created
            _initializedService.InitializeNodeLoggers(loggerDescriptions, sink, 1);

            Assert.NotNull(_initializedService.RegisteredLoggerTypeNames);

            // Should have 6 forwarding logger. three of each type
            Assert.Equal(6, _initializedService.RegisteredLoggerTypeNames.Count);
            Assert.Contains("Microsoft.Build.Logging.ConfigurableForwardingLogger", _initializedService.RegisteredLoggerTypeNames);
            Assert.Contains("Microsoft.Build.BackEnd.Logging.CentralForwardingLogger", _initializedService.RegisteredLoggerTypeNames);

            int countForwardingLogger = 0;
            foreach (string loggerName in _initializedService.RegisteredLoggerTypeNames)
            {
                if (String.Equals("Microsoft.Build.Logging.ConfigurableForwardingLogger", loggerName, StringComparison.OrdinalIgnoreCase))
                {
                    countForwardingLogger++;
                }
            }

            // Should be 3, one for each call to RegisterLoggerDescriptions
            Assert.Equal(3, countForwardingLogger);

            countForwardingLogger = 0;
            foreach (string loggerName in _initializedService.RegisteredLoggerTypeNames)
            {
                if (String.Equals("Microsoft.Build.BackEnd.Logging.CentralForwardingLogger", loggerName, StringComparison.OrdinalIgnoreCase))
                {
                    countForwardingLogger++;
                }
            }

            // Should be 3, one for each call to RegisterLoggerDescriptions
            Assert.Equal(3, countForwardingLogger);

            // Should have 2 event sink
            Assert.NotNull(_initializedService.RegisteredSinkNames);
            Assert.Equal(2, _initializedService.RegisteredSinkNames.Count);

            // There should not be any (this method is to be called on a child node)
            Assert.Empty(_initializedService.LoggerDescriptions);
        }

        /// <summary>
        /// Try and register a duplicate central logger
        /// </summary>
        [Fact]
        public void RegisterDuplicateDistributedCentralLogger()
        {
            string className = "Microsoft.Build.Logging.ConfigurableForwardingLogger";
#if FEATURE_ASSEMBLY_LOCATION
            LoggerDescription description = CreateLoggerDescription(className, Assembly.GetAssembly(typeof(ProjectCollection)).FullName, true);
#else
            LoggerDescription description = CreateLoggerDescription(className, typeof(ProjectCollection).GetTypeInfo().Assembly.FullName, true);
#endif

            RegularILogger regularILogger = new RegularILogger();
            Assert.True(_initializedService.RegisterDistributedLogger(regularILogger, description));
            Assert.False(_initializedService.RegisterDistributedLogger(regularILogger, description));

            // Should have 2 central loggers and 1 forwarding logger
            Assert.Equal(2, _initializedService.RegisteredLoggerTypeNames.Count);
            Assert.Contains("Microsoft.Build.Logging.ConfigurableForwardingLogger", _initializedService.RegisteredLoggerTypeNames);
            Assert.Contains("Microsoft.Build.UnitTests.Logging.LoggingService_Tests+RegularILogger", _initializedService.RegisteredLoggerTypeNames);

            // Should have 1 sink
            Assert.NotNull(_initializedService.RegisteredSinkNames);
            Assert.Single(_initializedService.RegisteredSinkNames);
            Assert.Single(_initializedService.LoggerDescriptions);
        }
        #endregion

        #region Test Properties
        /// <summary>
        /// Verify the getters and setters for the properties work.
        /// </summary>
        [Fact]
        public void Properties()
        {
            // Test OnlyLogCriticalEvents
            LoggingService loggingService = (LoggingService)LoggingService.CreateLoggingService(LoggerMode.Synchronous, 1);
            Assert.False(loggingService.OnlyLogCriticalEvents); // "Expected only log critical events to be false"
            loggingService.OnlyLogCriticalEvents = true;
            Assert.True(loggingService.OnlyLogCriticalEvents); // "Expected only log critical events to be true"

            // Test LoggingMode
            Assert.Equal(LoggerMode.Synchronous, loggingService.LoggingMode); // "Expected Logging mode to be Synchronous"

            // Test LoggerDescriptions
            Assert.Empty(loggingService.LoggerDescriptions); // "Expected LoggerDescriptions to be empty"

            // Test Number of InitialNodes
            Assert.Equal(1, loggingService.MaxCPUCount);
            loggingService.MaxCPUCount = 5;
            Assert.Equal(5, loggingService.MaxCPUCount);

            // Test MinimumRequiredMessageImportance
            Assert.Equal(MessageImportance.Low, loggingService.MinimumRequiredMessageImportance);
            loggingService.RegisterLogger(new ConsoleLogger(LoggerVerbosity.Normal));
            Assert.Equal(MessageImportance.Normal, loggingService.MinimumRequiredMessageImportance);
        }

        #endregion

        #region PacketHandling Tests
        /// <summary>
        /// Verify how a null packet is handled. There should be an InternalErrorException thrown
        /// </summary>
        [Fact]
        public void NullPacketReceived()
        {
            Assert.Throws<InternalErrorException>(() =>
            {
                LoggingService loggingService = (LoggingService)LoggingService.CreateLoggingService(LoggerMode.Synchronous, 1);
                loggingService.PacketReceived(1, null);
            }
           );
        }
        /// <summary>
        /// Verify when a non logging packet is received.
        /// An invalid operation should be thrown
        /// </summary>
        [Fact]
        public void NonLoggingPacketPacketReceived()
        {
            Assert.Throws<InternalErrorException>(() =>
            {
                LoggingService loggingService = (LoggingService)LoggingService.CreateLoggingService(LoggerMode.Synchronous, 1);
                NonLoggingPacket packet = new NonLoggingPacket();
                loggingService.PacketReceived(1, packet);
            }
           );
        }
        /// <summary>
        /// Verify when a logging packet is received the build event is
        /// properly passed to ProcessLoggingEvent
        /// An invalid operation should be thrown
        /// </summary>
        [Fact]
        public void LoggingPacketReceived()
        {
            LoggingServicesLogMethod_Tests.ProcessBuildEventHelper loggingService = (LoggingServicesLogMethod_Tests.ProcessBuildEventHelper)LoggingServicesLogMethod_Tests.ProcessBuildEventHelper.CreateLoggingService(LoggerMode.Synchronous, 1);
            BuildMessageEventArgs messageEvent = new BuildMessageEventArgs("MyMessage", "HelpKeyword", "Sender", MessageImportance.High);
            LogMessagePacket packet = new LogMessagePacket(new KeyValuePair<int, BuildEventArgs>(1, messageEvent));
            loggingService.PacketReceived(1, packet);

            BuildMessageEventArgs messageEventFromPacket = loggingService.ProcessedBuildEvent as BuildMessageEventArgs;
            Assert.NotNull(messageEventFromPacket);
            Assert.Equal(messageEventFromPacket, messageEvent); // "Expected messages to match"
        }

        #endregion

        #region WarningsAsErrors Tests

        private static readonly BuildWarningEventArgs BuildWarningEventForTreatAsErrorOrMessageTests = new BuildWarningEventArgs("subcategory", "C94A41A90FFB4EF592BF98BA59BEE8AF", "file", 1, 2, 3, 4, "message", "helpKeyword", "senderName");

        /// <summary>
        /// Verifies that a warning is logged as an error when it's warning code specified.
        /// </summary>
        [Theory]
        [InlineData(0, 1)]
        [InlineData(0, 2)]
        [InlineData(1, 1)]
        [InlineData(1, 2)]
        public void TreatWarningsAsErrorWhenSpecified(int loggerMode, int nodeId)
        {
            HashSet<string> warningsAsErrors = new HashSet<string>
            {
                "123",
                BuildWarningEventForTreatAsErrorOrMessageTests.Code,
                "ABC",
            };

            MockLogger logger = GetLoggedEventsWithWarningsAsErrorsOrMessages(BuildWarningEventForTreatAsErrorOrMessageTests, (LoggerMode)loggerMode, nodeId, warningsAsErrors);

            BuildErrorEventArgs actualBuildEvent = logger.Errors.ShouldHaveSingleItem();

            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.Code, actualBuildEvent.Code);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.File, actualBuildEvent.File);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.ProjectFile, actualBuildEvent.ProjectFile);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.Subcategory, actualBuildEvent.Subcategory);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.HelpKeyword, actualBuildEvent.HelpKeyword);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.Message, actualBuildEvent.Message);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.SenderName, actualBuildEvent.SenderName);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.ColumnNumber, actualBuildEvent.ColumnNumber);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.EndColumnNumber, actualBuildEvent.EndColumnNumber);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.EndLineNumber, actualBuildEvent.EndLineNumber);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.LineNumber, actualBuildEvent.LineNumber);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.BuildEventContext, actualBuildEvent.BuildEventContext);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.Timestamp, actualBuildEvent.Timestamp);
        }

        /// <summary>
        /// Verifies that a warning is not treated as an error when other warning codes are specified.
        /// </summary>
        [Fact]
        public void NotTreatWarningsAsErrorWhenNotSpecified()
        {
            HashSet<string> warningsAsErrors = new HashSet<string>
            {
                "123",
                "ABC",
            };

            MockLogger logger = GetLoggedEventsWithWarningsAsErrorsOrMessages(BuildWarningEventForTreatAsErrorOrMessageTests, warningsAsErrors: warningsAsErrors);

            var actualEvent = logger.Warnings.ShouldHaveSingleItem();

            actualEvent.ShouldBe(BuildWarningEventForTreatAsErrorOrMessageTests);
        }

        /// <summary>
        /// Verifies that a warning is not treated as an error when other warning codes are specified.
        /// </summary>
        [Theory]
        [InlineData(0, 1)]
        [InlineData(0, 2)]
        [InlineData(1, 1)]
        [InlineData(1, 2)]
        public void TreatWarningsAsErrorWhenAllSpecified(int loggerMode, int nodeId)
        {
            HashSet<string> warningsAsErrors = new HashSet<string>();

            MockLogger logger = GetLoggedEventsWithWarningsAsErrorsOrMessages(BuildWarningEventForTreatAsErrorOrMessageTests, (LoggerMode)loggerMode, nodeId, warningsAsErrors);

            logger.Errors.ShouldHaveSingleItem();
        }

        /// <summary>
        /// Verifies that a warning is logged as a low importance message when it's warning code is specified.
        /// </summary>
        [Theory]
        [InlineData(0, 1)]
        [InlineData(0, 2)]
        [InlineData(1, 1)]
        [InlineData(1, 2)]
        public void TreatWarningsAsMessagesWhenSpecified(int loggerMode, int nodeId)
        {
            HashSet<string> warningsAsMessages = new HashSet<string>
            {
                "FOO",
                BuildWarningEventForTreatAsErrorOrMessageTests.Code,
                "BAR",
            };

            MockLogger logger = GetLoggedEventsWithWarningsAsErrorsOrMessages(BuildWarningEventForTreatAsErrorOrMessageTests, (LoggerMode)loggerMode, nodeId, warningsAsMessages: warningsAsMessages);

            BuildMessageEventArgs actualBuildEvent = logger.BuildMessageEvents.ShouldHaveSingleItem();

            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.BuildEventContext, actualBuildEvent.BuildEventContext);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.Code, actualBuildEvent.Code);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.ColumnNumber, actualBuildEvent.ColumnNumber);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.EndColumnNumber, actualBuildEvent.EndColumnNumber);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.EndLineNumber, actualBuildEvent.EndLineNumber);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.File, actualBuildEvent.File);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.HelpKeyword, actualBuildEvent.HelpKeyword);
            Assert.Equal(MessageImportance.Low, actualBuildEvent.Importance);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.LineNumber, actualBuildEvent.LineNumber);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.Message, actualBuildEvent.Message);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.ProjectFile, actualBuildEvent.ProjectFile);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.SenderName, actualBuildEvent.SenderName);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.Subcategory, actualBuildEvent.Subcategory);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.Timestamp, actualBuildEvent.Timestamp);
        }

        /// <summary>
        /// Verifies that a warning is not treated as a low importance message when other warning codes are specified.
        /// </summary>
        [Fact]
        public void NotTreatWarningsAsMessagesWhenNotSpecified()
        {
            HashSet<string> warningsAsMessages = new HashSet<string>
            {
                "FOO",
                "BAR",
            };

            MockLogger logger = GetLoggedEventsWithWarningsAsErrorsOrMessages(BuildWarningEventForTreatAsErrorOrMessageTests, warningsAsMessages: warningsAsMessages);

            logger.Warnings.ShouldHaveSingleItem();
        }

        /// <summary>
        /// Verifies that warnings are treated as an error for a particular project when codes are specified.
        /// </summary>
        [Theory]
        [InlineData(0, 1)]
        [InlineData(0, 2)]
        [InlineData(1, 1)]
        [InlineData(1, 2)]
        public void TreatWarningsAsErrorByProjectWhenSpecified(int loggerMode, int nodeId)
        {
            HashSet<string> warningsAsErrorsForProject = new HashSet<string>
            {
                "123",
                BuildWarningEventForTreatAsErrorOrMessageTests.Code,
                "ABC"
            };

            MockLogger logger = GetLoggedEventsWithWarningsAsErrorsOrMessages(BuildWarningEventForTreatAsErrorOrMessageTests, (LoggerMode)loggerMode, nodeId, warningsAsErrorsForProject: warningsAsErrorsForProject);

            BuildErrorEventArgs actualBuildEvent = logger.Errors.ShouldHaveSingleItem();

            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.Code, actualBuildEvent.Code);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.File, actualBuildEvent.File);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.ProjectFile, actualBuildEvent.ProjectFile);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.Subcategory, actualBuildEvent.Subcategory);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.HelpKeyword, actualBuildEvent.HelpKeyword);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.Message, actualBuildEvent.Message);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.SenderName, actualBuildEvent.SenderName);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.ColumnNumber, actualBuildEvent.ColumnNumber);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.EndColumnNumber, actualBuildEvent.EndColumnNumber);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.EndLineNumber, actualBuildEvent.EndLineNumber);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.LineNumber, actualBuildEvent.LineNumber);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.BuildEventContext, actualBuildEvent.BuildEventContext);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.Timestamp, actualBuildEvent.Timestamp);
        }

        /// <summary>
        /// Verifies that all warnings are treated as errors for a particular project.
        /// </summary>
        [Theory]
        [InlineData(0, 1)]
        [InlineData(0, 2)]
        [InlineData(1, 1)]
        [InlineData(1, 2)]
        public void TreatWarningsAsErrorByProjectWhenAllSpecified(int loggerMode, int nodeId)
        {
            HashSet<string> warningsAsErrorsForProject = new HashSet<string>();

            MockLogger logger = GetLoggedEventsWithWarningsAsErrorsOrMessages(BuildWarningEventForTreatAsErrorOrMessageTests, (LoggerMode)loggerMode, nodeId, warningsAsErrorsForProject: warningsAsErrorsForProject);

            logger.Errors.ShouldHaveSingleItem();
        }

        /// <summary>
        /// Verifies that warnings are treated as messages for a particular project.
        /// </summary>
        [Theory]
        [InlineData(0, 1)]
        [InlineData(0, 2)]
        [InlineData(1, 1)]
        [InlineData(1, 2)]
        public void TreatWarningsAsMessagesByProjectWhenSpecified(int loggerMode, int nodeId)
        {
            HashSet<string> warningsAsMessagesForProject = new HashSet<string>
            {
                "123",
                BuildWarningEventForTreatAsErrorOrMessageTests.Code,
                "ABC"
            };

            MockLogger logger = GetLoggedEventsWithWarningsAsErrorsOrMessages(BuildWarningEventForTreatAsErrorOrMessageTests, (LoggerMode)loggerMode, nodeId, warningsAsMessagesForProject: warningsAsMessagesForProject);

            BuildMessageEventArgs actualBuildEvent = logger.BuildMessageEvents.ShouldHaveSingleItem();

            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.BuildEventContext, actualBuildEvent.BuildEventContext);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.Code, actualBuildEvent.Code);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.ColumnNumber, actualBuildEvent.ColumnNumber);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.EndColumnNumber, actualBuildEvent.EndColumnNumber);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.EndLineNumber, actualBuildEvent.EndLineNumber);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.File, actualBuildEvent.File);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.HelpKeyword, actualBuildEvent.HelpKeyword);
            Assert.Equal(MessageImportance.Low, actualBuildEvent.Importance);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.LineNumber, actualBuildEvent.LineNumber);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.Message, actualBuildEvent.Message);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.ProjectFile, actualBuildEvent.ProjectFile);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.SenderName, actualBuildEvent.SenderName);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.Subcategory, actualBuildEvent.Subcategory);
            Assert.Equal(BuildWarningEventForTreatAsErrorOrMessageTests.Timestamp, actualBuildEvent.Timestamp);
        }

        private MockLogger GetLoggedEventsWithWarningsAsErrorsOrMessages(
            BuildEventArgs buildEvent,
            LoggerMode loggerMode = LoggerMode.Synchronous,
            int nodeId = 1,
            ISet<string> warningsAsErrors = null,
            ISet<string> warningsAsMessages = null,
            ISet<string> warningsAsErrorsForProject = null,
            ISet<string> warningsAsMessagesForProject = null)
        {
            IBuildComponentHost host = new MockHost();

            BuildEventContext buildEventContext = new BuildEventContext(
                submissionId: 0,
                nodeId: 1,
                projectInstanceId: 2,
                projectContextId: -1,
                targetId: -1,
                taskId: -1);

            BuildRequestData buildRequestData = new BuildRequestData("projectFile", new Dictionary<string, string>(), "Current", new[] { "Build" }, null);

            ConfigCache configCache = host.GetComponent(BuildComponentType.ConfigCache) as ConfigCache;

            configCache.AddConfiguration(new BuildRequestConfiguration(buildEventContext.ProjectInstanceId, buildRequestData, buildRequestData.ExplicitlySpecifiedToolsVersion));

            MockLogger logger = new MockLogger();

            ILoggingService loggingService = LoggingService.CreateLoggingService(loggerMode, nodeId);

            ((IBuildComponent)loggingService).InitializeComponent(host);

            loggingService.RegisterLogger(logger);

            BuildEventContext projectStarted = loggingService.LogProjectStarted(buildEventContext, 0, buildEventContext.ProjectInstanceId, BuildEventContext.Invalid, "projectFile", "Build", Enumerable.Empty<DictionaryEntry>(), Enumerable.Empty<DictionaryEntry>());

            if (warningsAsErrorsForProject != null)
            {
                loggingService.AddWarningsAsErrors(projectStarted, warningsAsErrorsForProject);
            }

            if (warningsAsMessagesForProject != null)
            {
                loggingService.AddWarningsAsMessages(projectStarted, warningsAsMessagesForProject);
            }

            loggingService.WarningsAsErrors = warningsAsErrors;
            loggingService.WarningsAsMessages = warningsAsMessages;

            buildEvent.BuildEventContext = projectStarted;

            loggingService.LogBuildEvent(buildEvent);

            loggingService.LogProjectFinished(projectStarted, "projectFile", true);

            while (logger.ProjectFinishedEvents.Count == 0)
            {
                Thread.Sleep(100);
            }

            ((IBuildComponent)loggingService).ShutdownComponent();

            return logger;
        }

        #endregion

        #region MinimumRequiredMessageImportance Tests

        [Fact]
        public void ImportanceReflectsConsoleLoggerVerbosity()
        {
            _initializedService.RegisterLogger(new ConsoleLogger(LoggerVerbosity.Quiet));
            _initializedService.MinimumRequiredMessageImportance.ShouldBe(MessageImportance.High - 1);
            _initializedService.RegisterLogger(new ConsoleLogger(LoggerVerbosity.Minimal));
            _initializedService.MinimumRequiredMessageImportance.ShouldBe(MessageImportance.High);
            _initializedService.RegisterLogger(new ConsoleLogger(LoggerVerbosity.Normal));
            _initializedService.MinimumRequiredMessageImportance.ShouldBe(MessageImportance.Normal);
            _initializedService.RegisterLogger(new ConsoleLogger(LoggerVerbosity.Detailed));
            _initializedService.MinimumRequiredMessageImportance.ShouldBe(MessageImportance.Low);
            _initializedService.RegisterLogger(new ConsoleLogger(LoggerVerbosity.Diagnostic));
            _initializedService.MinimumRequiredMessageImportance.ShouldBe(MessageImportance.Low);
        }

        [Fact]
        public void ImportanceReflectsConfigurableForwardingLoggerVerbosity()
        {
            _initializedService.RegisterLogger(CreateConfigurableForwardingLogger(LoggerVerbosity.Quiet));
            _initializedService.MinimumRequiredMessageImportance.ShouldBe(MessageImportance.High - 1);
            _initializedService.RegisterLogger(CreateConfigurableForwardingLogger(LoggerVerbosity.Minimal));
            _initializedService.MinimumRequiredMessageImportance.ShouldBe(MessageImportance.High);
            _initializedService.RegisterLogger(CreateConfigurableForwardingLogger(LoggerVerbosity.Normal));
            _initializedService.MinimumRequiredMessageImportance.ShouldBe(MessageImportance.Normal);
            _initializedService.RegisterLogger(CreateConfigurableForwardingLogger(LoggerVerbosity.Detailed));
            _initializedService.MinimumRequiredMessageImportance.ShouldBe(MessageImportance.Low);
            _initializedService.RegisterLogger(CreateConfigurableForwardingLogger(LoggerVerbosity.Diagnostic));
            _initializedService.MinimumRequiredMessageImportance.ShouldBe(MessageImportance.Low);
        }

        [Fact]
        public void ImportanceReflectsCentralForwardingLoggerVerbosity()
        {
            MockHost mockHost = new MockHost();
            ILoggingService node1LoggingService = LoggingService.CreateLoggingService(LoggerMode.Synchronous, 1);
            ((IBuildComponent)node1LoggingService).InitializeComponent(mockHost);
            ILoggingService node2LoggingService = LoggingService.CreateLoggingService(LoggerMode.Synchronous, 2);
            ((IBuildComponent)node2LoggingService).InitializeComponent(mockHost);

            // CentralForwardingLogger is always registered in in-proc nodes and it does not affect minimum importance.
            node1LoggingService.RegisterLogger(CreateConfigurableForwardingLogger(LoggerVerbosity.Minimal));
            node1LoggingService.RegisterLogger(new CentralForwardingLogger());
            node1LoggingService.MinimumRequiredMessageImportance.ShouldBe(MessageImportance.High);

            // CentralForwardingLogger in out-of-proc nodes means that we are forwarding everything and the minimum importance
            // is Low regardless of what other loggers are registered.
            node2LoggingService.RegisterLogger(CreateConfigurableForwardingLogger(LoggerVerbosity.Minimal));
            node2LoggingService.RegisterLogger(new CentralForwardingLogger());
            node2LoggingService.MinimumRequiredMessageImportance.ShouldBe(MessageImportance.Low);
            // Register another ConsoleLogger and verify that minimum importance hasn't changed.
            node2LoggingService.RegisterLogger(CreateConfigurableForwardingLogger(LoggerVerbosity.Minimal));
            node2LoggingService.MinimumRequiredMessageImportance.ShouldBe(MessageImportance.Low);
        }

        [Fact]
        public void ImportanceReflectsUnknownLoggerVerbosity()
        {
            // Minimum message importance is Low (i.e. we're logging everything) even when all registered loggers have
            // Normal verbosity if at least of one them is not on our whitelist.
            _initializedService.RegisterLogger(new ConsoleLogger(LoggerVerbosity.Normal));
            _initializedService.RegisterLogger(new MockLogger() { Verbosity = LoggerVerbosity.Normal });
            _initializedService.RegisterLogger(CreateConfigurableForwardingLogger(LoggerVerbosity.Normal));
            _initializedService.MinimumRequiredMessageImportance.ShouldBe(MessageImportance.Low);
        }
        #endregion

        #region PrivateMethods

        /// <summary>
        /// Instantiate and Initialize a new loggingService.
        /// This is used by the test setup method to create
        /// a new logging service before each test.
        /// </summary>
        private void InitializeLoggingService()
        {
            BuildParameters parameters = new BuildParameters();
            parameters.MaxNodeCount = 2;
            MockHost mockHost = new MockHost(parameters);

            IBuildComponent logServiceComponent = (IBuildComponent)LoggingService.CreateLoggingService(LoggerMode.Synchronous, 1);
            logServiceComponent.InitializeComponent(mockHost);
            _initializedService = logServiceComponent as LoggingService;
        }

        /// <summary>
        /// Register the correct logger and then call the shutdownComponent method.
        /// This will call shutdown on the loggers, we should expect to see certain exceptions.
        /// </summary>
        /// <param name="logger">Logger to register, this will only be used if className is null</param>
        /// <param name="className">ClassName to instantiate a new distributed logger</param>
        /// <param name="expectedExceptionType">Exception type which is expected to be thrown</param>
        private void VerifyShutdownExceptions(ILogger logger, string className, Type expectedExceptionType)
        {
            InitializeLoggingService();
            if (className != null)
            {
#if FEATURE_ASSEMBLY_LOCATION
                Assembly thisAssembly = Assembly.GetAssembly(typeof(LoggingService_Tests));
#else
                Assembly thisAssembly = typeof(LoggingService_Tests).GetTypeInfo().Assembly;
#endif
                string loggerAssemblyName = thisAssembly.FullName;
                LoggerDescription centralLoggerDescrption = CreateLoggerDescription(className, loggerAssemblyName, true);
                _initializedService.RegisterDistributedLogger(null, centralLoggerDescrption);
            }
            else
            {
                _initializedService.RegisterLogger(logger);
            }

            try
            {
                _initializedService.ShutdownComponent();
                Assert.True(false, "No Exceptions Generated");
            }
            catch (Exception e)
            {
                if (e.GetType() != expectedExceptionType)
                {
                    Assert.True(false, "Expected a " + expectedExceptionType + " but got a " + e.GetType() + " Stack:" + e.ToString());
                }
            }
        }

        /// <summary>
        /// Create a logger description from the class name and logger assembly
        /// This is used in any test which needs to register a distributed logger.
        /// </summary>
        /// <param name="loggerClassName">Fully qualified class name (don't for get ParentClass+Nestedclass, if nested)</param>
        /// <param name="loggerAssemblyName">Assembly name which contains class</param>
        /// <returns>A logger description which can be registered</returns>
        private LoggerDescription CreateLoggerDescription(string loggerClassName, string loggerAssemblyName, bool forwardAllEvents)
        {
            string eventsToForward = "CustomEvent";

            if (forwardAllEvents)
            {
                eventsToForward = "BuildStartedEvent;BuildFinishedEvent;ProjectStartedEvent;ProjectFinishedEvent;TargetStartedEvent;TargetFinishedEvent;TaskStartedEvent;TaskFinishedEvent;ErrorEvent;WarningEvent;HighMessageEvent;NormalMessageEvent;LowMessageEvent;CustomEvent;CommandLine";
            }

            LoggerDescription centralLoggerDescrption = new LoggerDescription
                                                                             (
                                                                              loggerClassName,
                                                                              loggerAssemblyName,
                                                                              null /*Not needed as we are loading from current assembly*/,
                                                                              eventsToForward,
                                                                              LoggerVerbosity.Diagnostic /*Not used, but the spirit of the logger is to forward everything so this is the most appropriate verbosity */
                                                                             );
            return centralLoggerDescrption;
        }

        /// <summary>
        /// Creates a new <see cref="ConfigurableForwardingLogger"/> with the given verbosity.
        /// </summary>
        private ConfigurableForwardingLogger CreateConfigurableForwardingLogger(LoggerVerbosity verbosity)
        {
            return new ConfigurableForwardingLogger()
            {
                Verbosity = verbosity
            };
        }
        #endregion

        #region HelperClasses

        /// <summary>
        /// A forwarding logger which will throw an exception
        /// </summary>
        public class BaseFLThrowException : LoggerThrowException, IForwardingLogger
        {
            #region Constructor

            /// <summary>
            /// Create a forwarding logger which will throw an exception on initialize or shutdown
            /// </summary>
            /// <param name="throwOnShutdown">Throw exception on shutdown</param>
            /// <param name="throwOnInitialize">Throw exception on initialize</param>
            /// <param name="exception">Exception to throw</param>
            internal BaseFLThrowException(bool throwOnShutdown, bool throwOnInitialize, Exception exception)
                : base(throwOnShutdown, throwOnInitialize, exception)
            {
            }
            #endregion

            #region IForwardingLogger Members

            /// <summary>
            /// Not used, implemented due to interface
            /// </summary>
            /// <value>Notused</value>
            public IEventRedirector BuildEventRedirector
            {
                get;
                set;
            }

            /// <summary>
            /// Not used, implemented due to interface
            /// </summary>
            /// <value>Not used</value>
            public int NodeId
            {
                get;
                set;
            }

            #endregion
        }

        /// <summary>
        /// Forwarding logger which throws a logger exception in the shutdown method.
        /// This is to test the logging service exception handling.
        /// </summary>
        public class ShutdownLoggerExceptionFL : BaseFLThrowException
        {
            /// <summary>
            /// Create a logger which will throw a logger exception
            /// in the shutdown method
            /// </summary>
            public ShutdownLoggerExceptionFL()
                : base(true, false, new LoggerException("Hello"))
            {
            }
        }

        /// <summary>
        /// Forwarding logger which will throw a general exception in the shutdown method
        /// This is used to test the logging service shutdown handling method.
        /// </summary>
        public class ShutdownGeneralExceptionFL : BaseFLThrowException
        {
            /// <summary>
            /// Create a logger which logs a general exception in the shutdown method
            /// </summary>
            public ShutdownGeneralExceptionFL()
                : base(true, false, new Exception("Hello"))
            {
            }
        }

        /// <summary>
        /// Forwarding logger which will throw a StackOverflowException
        /// in the shutdown method. This is to test the shutdown exception handling
        /// </summary>
        public class ShutdownStackoverflowExceptionFL : BaseFLThrowException
        {
            /// <summary>
            /// Create a logger which will throw a StackOverflow exception
            /// in the shutdown method.
            /// </summary>
            public ShutdownStackoverflowExceptionFL()
                : base(true, false, new StackOverflowException())
            {
            }
        }

        /// <summary>
        /// Logger which can throw a defined exception in the initialize or shutdown methods
        /// </summary>
        public class LoggerThrowException : INodeLogger
        {
            #region Constructor

            /// <summary>
            /// Constructor to tell the logger when to throw an exception and what exception
            /// to throw
            /// </summary>
            /// <param name="throwOnShutdown">True, throw the exception when shutdown is called</param>
            /// <param name="throwOnInitialize">True, throw the exception when Initialize is called</param>
            /// <param name="exception">The exception to throw</param>
            internal LoggerThrowException(bool throwOnShutdown, bool throwOnInitialize, Exception exception)
            {
                ExceptionToThrow = exception;
                ThrowExceptionOnShutdown = throwOnShutdown;
                ThrowExceptionOnInitialize = throwOnInitialize;
            }
            #endregion

            #region Properties

            /// <summary>
            /// Not used, implemented due to ILoggerInterface
            /// </summary>
            /// <value>Not used</value>
            public LoggerVerbosity Verbosity
            {
                get;
                set;
            }

            /// <summary>
            /// Not used, implemented due to ILoggerInterface
            /// </summary>
            /// <value>Not used</value>
            public string Parameters
            {
                get;
                set;
            }

            /// <summary>
            /// Should the exception be thrown on the call to shutdown
            /// </summary>
            /// <value>Not used</value>
            protected bool ThrowExceptionOnShutdown
            {
                get;
                set;
            }

            /// <summary>
            /// Should the exception be thrown on the call to initialize
            /// </summary>
            /// <value>Not used</value>
            protected bool ThrowExceptionOnInitialize
            {
                get;
                set;
            }

            /// <summary>
            /// The exception which will be thrown in shutdown or initialize
            /// </summary>
            /// <value>Not used</value>
            protected Exception ExceptionToThrow
            {
                get;
                set;
            }
            #endregion

            #region ILogger Members

            /// <summary>
            /// Initialize the logger, throw an exception
            /// if ThrowExceptionOnInitialize is set
            /// </summary>
            /// <param name="eventSource">Not used</param>
            public void Initialize(IEventSource eventSource)
            {
                if (ThrowExceptionOnInitialize && ExceptionToThrow != null)
                {
                    throw ExceptionToThrow;
                }
            }

            /// <summary>
            /// Shutdown the logger, throw an exception if
            /// ThrowExceptionOnShutdown is set
            /// </summary>
            public void Shutdown()
            {
                if (ThrowExceptionOnShutdown && ExceptionToThrow != null)
                {
                    throw ExceptionToThrow;
                }
            }

            /// <summary>
            /// Initialize using the INodeLogger Interface
            /// </summary>
            /// <param name="eventSource">Not used</param>
            /// <param name="nodeCount">Not used</param>
            public void Initialize(IEventSource eventSource, int nodeCount)
            {
                Initialize(eventSource);
            }
            #endregion
        }

        /// <summary>
        /// Create a regular ILogger to test Registering ILoggers.
        /// </summary>
        public class RegularILogger : ILogger
        {
            #region Properties

            /// <summary>
            /// ParametersForTheLogger
            /// </summary>
            public string Parameters
            {
                get;
                set;
            }

            /// <summary>
            /// Verbosity
            /// </summary>
            public LoggerVerbosity Verbosity
            {
                get;
                set;
            }

            /// <summary>
            /// Number of times build started was logged
            /// </summary>
            internal int BuildStartedCount
            {
                get;
                set;
            }

            /// <summary>
            /// Number of times build finished was logged
            /// </summary>
            internal int BuildFinishedCount
            {
                get;
                set;
            }

            /// <summary>
            /// Initialize
            /// </summary>
            public void Initialize(IEventSource eventSource)
            {
                eventSource.AnyEventRaised +=
                         LoggerEventHandler;
            }

            /// <summary>
            /// DoNothing
            /// </summary>
            public void Shutdown()
            {
                // do nothing
            }

            /// <summary>
            /// Log the event
            /// </summary>
            internal void LoggerEventHandler(object sender, BuildEventArgs eventArgs)
            {
                if (eventArgs is BuildStartedEventArgs)
                {
                    ++BuildStartedCount;
                }

                if (eventArgs is BuildFinishedEventArgs)
                {
                    ++BuildFinishedCount;
                }
            }
        }

        /// <summary>
        /// Create a regular ILogger which keeps track of how many of each event were logged
        /// </summary>
        public class TestLogger : ILogger
        {
            /// <summary>
            /// Not Used
            /// </summary>
            /// <value>Not used</value>
            public LoggerVerbosity Verbosity
            {
                get;
                set;
            }

            /// <summary>
            /// Do Nothing
            /// </summary>
            /// <value>Not Used</value>
            public string Parameters
            {
                get;
                set;
            }

            /// <summary>
            /// Do Nothing
            /// </summary>
            /// <param name="eventSource">Not Used</param>
            public void Initialize(IEventSource eventSource)
            {
            }

            /// <summary>
            /// Do Nothing
            /// </summary>
            public void Shutdown()
            {
            }

            #endregion
        }

        /// <summary>
        ///  Create a non logging packet to test the packet handling code
        /// </summary>
        internal class NonLoggingPacket : INodePacket
        {
            #region Members

            /// <summary>
            /// Inform users of the class, this class is a BuildRequest packet
            /// </summary>
            public NodePacketType Type
            {
                get
                {
                    return NodePacketType.BuildRequest;
                }
            }

            /// <summary>
            /// Serialize the packet
            /// </summary>
            public void Translate(ITranslator translator)
            {
                throw new NotImplementedException();
            }

            #endregion
        }
        #endregion
    }
}
