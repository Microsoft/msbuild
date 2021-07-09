// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;

using Microsoft.Build.Framework;
using Microsoft.Build.Shared;

namespace Microsoft.Build.Logging
{
    /// <summary>
    /// Logger that forwards events to a central logger (e.g ConsoleLogger)
    /// residing on the parent node.
    /// </summary>
    public class ConfigurableForwardingLogger : IForwardingLogger
    {
        #region Constructors
        /// <summary>
        /// Default constructor.
        /// </summary>
        public ConfigurableForwardingLogger()
        {
            InitializeForwardingTable();
        }
        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the level of detail to show in the event log.
        /// </summary>
        /// <value>Verbosity level.</value>
        public LoggerVerbosity Verbosity
        {
            get { return _verbosity; }
            set { _verbosity = value; }
        }

        /// <summary>
        /// The console logger takes a single parameter to suppress the output of the errors
        /// and warnings summary at the end of a build.
        /// </summary>
        /// <value>null</value>
        public string Parameters
        {
            get { return _loggerParameters; }
            set { _loggerParameters = value; }
        }

        /// <summary>
        /// This property is set by the build engine to allow a node loggers to forward messages to the
        /// central logger
        /// </summary>
        public IEventRedirector BuildEventRedirector
        {
            get { return _buildEventRedirector; }
            set { _buildEventRedirector = value; }
        }

        /// <summary>
        /// The identifier of the node.
        /// </summary>
        public int NodeId
        {
            get { return _nodeId; }
            set { _nodeId = value; }
        }
        #endregion

        #region Methods

        /// <summary>
        /// Initialize the Forwarding Table with the default values
        /// </summary>
        private void InitializeForwardingTable()
        {
            _forwardingTable = new Dictionary<string, int>(17, StringComparer.OrdinalIgnoreCase);
            _forwardingTable[BuildStartedEventDescription] = 0;
            _forwardingTable[BuildFinishedEventDescription] = 0;
            _forwardingTable[ProjectStartedEventDescription] = 0;
            _forwardingTable[ProjectFinishedEventDescription] = 0;
            _forwardingTable[ProjectEvaluationStartedEventDescription] = 0;
            _forwardingTable[ProjectEvaluationFinishedEventDescription] = 0;
            _forwardingTable[TargetStartedEventDescription] = 0;
            _forwardingTable[TargetFinishedEventDescription] = 0;
            _forwardingTable[TaskStartedEventDescription] = 0;
            _forwardingTable[TaskFinishedEventDescription] = 0;
            _forwardingTable[ErrorEventDescription] = 0;
            _forwardingTable[WarningEventDescription] = 0;
            _forwardingTable[HighMessageEventDescription] = 0;
            _forwardingTable[NormalMessageEventDescription] = 0;
            _forwardingTable[LowMessageEventDescription] = 0;
            _forwardingTable[CustomEventDescription] = 0;
            _forwardingTable[CommandLineDescription] = 0;
            _forwardingSetFromParameters = false;
        }

        /// <summary>
        /// Parses out the logger parameters from the Parameters string.
        /// </summary>
        private void ParseParameters()
        {
            if (_loggerParameters != null)
            {
                string[] parameterComponents = _loggerParameters.Split(s_parameterDelimiters);
                for (int param = 0; param < parameterComponents.Length; param++)
                {
                    if (parameterComponents[param].Length > 0)
                    {
                        ApplyParameter(parameterComponents[param]);
                    }
                }
                // Setting events to forward on the commandline will override the verbosity and other switches such as
                // showPerfSummand and ShowSummary
                if (_forwardingSetFromParameters)
                {
                    _showPerfSummary = false;
                    _showSummary = true;
                }
            }
        }

        /// <summary>
        /// Logger parameters can be used to enable and disable specific event types.
        /// Otherwise, the verbosity is used to choose which events to forward.
        /// </summary>
        private void ApplyParameter(string parameterName)
        {
            ErrorUtilities.VerifyThrowArgumentNull(parameterName, nameof(parameterName));

            if (_forwardingTable.ContainsKey(parameterName))
            {
                _forwardingSetFromParameters = true;
                _forwardingTable[parameterName] = 1;
            }

            // If any of the following parameters are set, we will make sure we forward the events
            // necessary for the central logger to emit the requested information
            if (String.Equals(parameterName, PerformanceSummaryDescription, StringComparison.OrdinalIgnoreCase))
            {
                _showPerfSummary = true;
            }
            else if (String.Equals(parameterName, NoSummaryDescription, StringComparison.OrdinalIgnoreCase))
            {
                _showSummary = false;
            }
            else if (String.Equals(parameterName, ShowCommandLineDescription, StringComparison.OrdinalIgnoreCase))
            {
                _showCommandLine = true;
            }
        }

        /// <summary>
        /// Signs up the console logger for all build events.
        /// </summary>
        public virtual void Initialize(IEventSource eventSource)
        {
            ErrorUtilities.VerifyThrowArgumentNull(eventSource, nameof(eventSource));

            ParseParameters();

            ResetLoggerState();

            if (!_forwardingSetFromParameters)
            {
                SetForwardingBasedOnVerbosity();
            }

            eventSource.BuildStarted += BuildStartedHandler;
            eventSource.BuildFinished += BuildFinishedHandler;
            eventSource.ProjectStarted += ProjectStartedHandler;
            eventSource.ProjectFinished += ProjectFinishedHandler;
            eventSource.TargetStarted += TargetStartedHandler;
            eventSource.TargetFinished += TargetFinishedHandler;
            eventSource.TaskStarted += TaskStartedHandler;
            eventSource.TaskFinished += TaskFinishedHandler;
            eventSource.ErrorRaised += ErrorHandler;
            eventSource.WarningRaised += WarningHandler;
            eventSource.MessageRaised += MessageHandler;
            eventSource.CustomEventRaised += CustomEventHandler;
            eventSource.StatusEventRaised += BuildStatusHandler;
        }

        /// <summary>
        /// Signs up the console logger for all build events.
        /// </summary>
        public void Initialize(IEventSource eventSource, int nodeCount)
        {
            Initialize(eventSource);
        }

        private void SetForwardingBasedOnVerbosity()
        {
            _forwardingTable[BuildStartedEventDescription] = 1;
            _forwardingTable[BuildFinishedEventDescription] = 1;

            if (IsVerbosityAtLeast(LoggerVerbosity.Quiet))
            {
                _forwardingTable[ErrorEventDescription] = 1;
                _forwardingTable[WarningEventDescription] = 1;
            }

            if (IsVerbosityAtLeast(LoggerVerbosity.Minimal))
            {
                _forwardingTable[HighMessageEventDescription] = 1;
            }

            if (IsVerbosityAtLeast(LoggerVerbosity.Normal))
            {
                _forwardingTable[NormalMessageEventDescription] = 1;
                _forwardingTable[ProjectStartedEventDescription] = 1;
                _forwardingTable[ProjectFinishedEventDescription] = 1;
                _forwardingTable[TargetStartedEventDescription] = 1;
                _forwardingTable[TargetFinishedEventDescription] = 1;
                _forwardingTable[CommandLineDescription] = 1;
            }

            if (IsVerbosityAtLeast(LoggerVerbosity.Detailed))
            {
                _forwardingTable[TargetStartedEventDescription] = 1;
                _forwardingTable[TargetFinishedEventDescription] = 1;
                _forwardingTable[TaskStartedEventDescription] = 1;
                _forwardingTable[TaskFinishedEventDescription] = 1;
                _forwardingTable[LowMessageEventDescription] = 1;
            }

            if (IsVerbosityAtLeast(LoggerVerbosity.Diagnostic))
            {
                _forwardingTable[CustomEventDescription] = 1;
                _forwardingTable[ProjectEvaluationStartedEventDescription] = 1;
                _forwardingTable[ProjectEvaluationFinishedEventDescription] = 1;
            }

            if (_showSummary)
            {
                _forwardingTable[ErrorEventDescription] = 1;
                _forwardingTable[WarningEventDescription] = 1;
            }

            if (_showPerfSummary)
            {
                _forwardingTable[TargetStartedEventDescription] = 1;
                _forwardingTable[TargetFinishedEventDescription] = 1;
                _forwardingTable[TaskStartedEventDescription] = 1;
                _forwardingTable[TaskFinishedEventDescription] = 1;
                _forwardingTable[TargetStartedEventDescription] = 1;
                _forwardingTable[TargetFinishedEventDescription] = 1;
                _forwardingTable[ProjectStartedEventDescription] = 1;
                _forwardingTable[ProjectFinishedEventDescription] = 1;
                _forwardingTable[ProjectEvaluationStartedEventDescription] = 1;
                _forwardingTable[ProjectEvaluationFinishedEventDescription] = 1;
            }

            if (_showCommandLine)
            {
                _forwardingTable[CommandLineDescription] = 1;
            }
        }

        /// <summary>
        /// Returns the minimum importance of messages logged by this logger.
        /// </summary>
        /// <returns>
        /// The minimum message importance corresponding to this logger's verbosity or (MessageImportance.High - 1)
        /// if this logger does not log messages of any importance.
        /// </returns>
        internal MessageImportance GetMinimumMessageImportance()
        {
            if (_forwardingTable[LowMessageEventDescription] == 1)
            {
                return MessageImportance.Low;
            }
            if (_forwardingTable[NormalMessageEventDescription] == 1)
            {
                return MessageImportance.Normal;
            }
            if (_forwardingTable[HighMessageEventDescription] == 1)
            {
                return MessageImportance.High;
            }
            return MessageImportance.High - 1;
        }

        /// <summary>
        /// Reset the states of per-build member variables.
        /// Used when a build is finished, but the logger might be needed for the next build.
        /// </summary>
        private void ResetLoggerState()
        {
            // No state needs resetting
        }

        /// <summary>
        /// Called when Engine is done with this logger
        /// </summary>
        public virtual void Shutdown()
        {
            // Nothing to do
        }

        /// <summary>
        /// Handler for build started events
        /// </summary>
        /// <param name="sender">sender (should be null)</param>
        /// <param name="e">event arguments</param>
        private void BuildStartedHandler(object sender, BuildStartedEventArgs e)
        {
            // This is false by default
            if (_forwardingTable[BuildStartedEventDescription] == 1)
            {
                ForwardToCentralLogger(e);
            }
        }

        /// <summary>
        /// Handler for build finished events
        /// </summary>
        /// <param name="sender">sender (should be null)</param>
        /// <param name="e">event arguments</param>
        private void BuildFinishedHandler(object sender, BuildFinishedEventArgs e)
        {
            // This is false by default
            if (_forwardingTable[BuildFinishedEventDescription] == 1)
            {
                ForwardToCentralLogger(e);
            }
            ResetLoggerState();
        }

        /// <summary>
        /// Handler for project started events
        /// </summary>
        /// <param name="sender">sender (should be null)</param>
        /// <param name="e">event arguments</param>
        private void ProjectStartedHandler(object sender, ProjectStartedEventArgs e)
        {
            if (_forwardingTable[ProjectStartedEventDescription] == 1)
            {
                ForwardToCentralLogger(e);
            }
        }

        /// <summary>
        /// Handler for project finished events
        /// </summary>
        /// <param name="sender">sender (should be null)</param>
        /// <param name="e">event arguments</param>
        private void ProjectFinishedHandler(object sender, ProjectFinishedEventArgs e)
        {
            if (_forwardingTable[ProjectFinishedEventDescription] == 1)
            {
                ForwardToCentralLogger(e);
            }
        }

        /// <summary>
        /// Handler for target started events
        /// </summary>
        /// <param name="sender">sender (should be null)</param>
        /// <param name="e">event arguments</param>
        private void TargetStartedHandler(object sender, TargetStartedEventArgs e)
        {
            if (_forwardingTable[TargetStartedEventDescription] == 1)
            {
                ForwardToCentralLogger(e);
            }
        }

        /// <summary>
        /// Handler for target finished events
        /// </summary>
        /// <param name="sender">sender (should be null)</param>
        /// <param name="e">event arguments</param>
        private void TargetFinishedHandler(object sender, TargetFinishedEventArgs e)
        {
            if (_forwardingTable[TargetFinishedEventDescription] == 1)
            {
                ForwardToCentralLogger(e);
            }
        }

        /// <summary>
        /// Handler for task started events
        /// </summary>
        /// <param name="sender">sender (should be null)</param>
        /// <param name="e">event arguments</param>
        private void TaskStartedHandler(object sender, TaskStartedEventArgs e)
        {
            if (_forwardingTable[TaskStartedEventDescription] == 1)
            {
                ForwardToCentralLogger(e);
            }
        }

        /// <summary>
        /// Handler for task finished events
        /// </summary>
        /// <param name="sender">sender (should be null)</param>
        /// <param name="e">event arguments</param>
        private void TaskFinishedHandler(object sender, TaskFinishedEventArgs e)
        {
            if (_forwardingTable[TaskFinishedEventDescription] == 1)
            {
                ForwardToCentralLogger(e);
            }
        }

        /// <summary>
        /// Prints an error event
        /// </summary>
        private void ErrorHandler(object sender, BuildErrorEventArgs e)
        {
            if (_forwardingTable[ErrorEventDescription] == 1)
            {
                ForwardToCentralLogger(e);
            }
        }

        /// <summary>
        /// Prints a warning event
        /// </summary>
        private void WarningHandler(object sender, BuildWarningEventArgs e)
        {
            if (_forwardingTable[WarningEventDescription] == 1)
            {
                ForwardToCentralLogger(e);
            }
        }

        /// <summary>
        /// Prints a message event
        /// </summary>
        private void MessageHandler(object sender, BuildMessageEventArgs e)
        {
            bool forwardEvent = false;

            if (_forwardingTable[LowMessageEventDescription] == 1 && e.Importance == MessageImportance.Low)
            {
                forwardEvent = true;
            }
            else if (_forwardingTable[NormalMessageEventDescription] == 1 && e.Importance == MessageImportance.Normal)
            {
                forwardEvent = true;
            }
            else if (_forwardingTable[HighMessageEventDescription] == 1 && e.Importance == MessageImportance.High)
            {
                forwardEvent = true;
            }
            else if (_forwardingTable[CommandLineDescription] == 1 && e is TaskCommandLineEventArgs)
            {
                forwardEvent = true;
            }

            if (forwardEvent)
            {
                ForwardToCentralLogger(e);
            }
        }

        /// <summary>
        /// Prints a custom event
        /// </summary>
        private void CustomEventHandler(object sender, CustomBuildEventArgs e)
        {
            if (_forwardingTable[CustomEventDescription] == 1)
            {
                ForwardToCentralLogger(e);
            }
        }

        private void BuildStatusHandler(object sender, BuildStatusEventArgs e)
        {
            if (_forwardingTable[ProjectEvaluationStartedEventDescription] == 1 && e is ProjectEvaluationStartedEventArgs)
            {
                ForwardToCentralLogger(e);
            }

            if (_forwardingTable[ProjectEvaluationFinishedEventDescription] == 1 && e is ProjectEvaluationFinishedEventArgs)
            {
                ForwardToCentralLogger(e);
            }
        }

        /// <summary>
        /// Forwards the specified event.
        /// </summary>
        /// <param name="e">The <see cref="BuildEventArgs"/> to forward.</param>
        protected virtual void ForwardToCentralLogger(BuildEventArgs e)
        {
            _buildEventRedirector.ForwardEvent(e);
        }

        /// <summary>
        /// Determines whether the current verbosity setting is at least the value
        /// passed in.
        /// </summary>
        private bool IsVerbosityAtLeast(LoggerVerbosity checkVerbosity)
        {
            return _verbosity >= checkVerbosity;
        }
        #endregion

        #region Private member data

        /// <summary>
        /// Controls the amount of text displayed by the logger
        /// </summary>
        private LoggerVerbosity _verbosity = LoggerVerbosity.Normal;

        /// <summary>
        /// Console logger parameters.
        /// </summary>
        private string _loggerParameters = null;

        /// <summary>
        /// Console logger parameters delimiters.
        /// </summary>
        private static readonly char[] s_parameterDelimiters = MSBuildConstants.SemicolonChar;

        /// <summary>
        /// Strings that users of this logger can pass in to enable specific events or logger output.
        /// Also used as keys into our dictionary.
        /// </summary>
        private const string BuildStartedEventDescription = "BUILDSTARTEDEVENT";
        private const string BuildFinishedEventDescription = "BUILDFINISHEDEVENT";
        private const string ProjectStartedEventDescription = "PROJECTSTARTEDEVENT";
        private const string ProjectFinishedEventDescription = "PROJECTFINISHEDEVENT";
        private const string ProjectEvaluationStartedEventDescription = "PROJECTEVALUATIONSTARTEDEVENT";
        private const string ProjectEvaluationFinishedEventDescription = "PROJECTEVALUATIONFINISHEDEVENT";
        private const string TargetStartedEventDescription = "TARGETSTARTEDEVENT";
        private const string TargetFinishedEventDescription = "TARGETFINISHEDEVENT";
        private const string TaskStartedEventDescription = "TASKSTARTEDEVENT";
        private const string TaskFinishedEventDescription = "TASKFINISHEDEVENT";
        private const string ErrorEventDescription = "ERROREVENT";
        private const string WarningEventDescription = "WARNINGEVENT";
        private const string HighMessageEventDescription = "HIGHMESSAGEEVENT";
        private const string NormalMessageEventDescription = "NORMALMESSAGEEVENT";
        private const string LowMessageEventDescription = "LOWMESSAGEEVENT";
        private const string CustomEventDescription = "CUSTOMEVENT";
        private const string CommandLineDescription = "COMMANDLINE";
        private const string PerformanceSummaryDescription = "PERFORMANCESUMMARY";
        private const string NoSummaryDescription = "NOSUMMARY";
        private const string ShowCommandLineDescription = "SHOWCOMMANDLINE";

        #region Per-build Members

        /// <summary>
        /// A table indicating if a particular event type should be forwarded
        /// The value is type int rather than bool to avoid the problem of JITting generics.
        /// <see cref="Dictionary{String, Int}" /> is already compiled into mscorlib.
        /// </summary>
        private Dictionary<string, int> _forwardingTable;

        /// <summary>
        /// A pointer to the central logger
        /// </summary>
        private IEventRedirector _buildEventRedirector;

        /// <summary>
        /// Indicates if the events to forward are being set by the parameters sent to the logger
        /// if this is false the events to forward are based on verbosity else verbosity settings will be ignored
        /// </summary>
        private bool _forwardingSetFromParameters;

        /// <summary>
        /// Console logger should show error and warning summary at the end of build?
        /// </summary>
        private bool _showSummary = true;

        /// <summary>
        /// When true, accumulate performance numbers.
        /// </summary>
        private bool _showPerfSummary = false;

        /// <summary>
        /// When true the commandline message is sent
        /// </summary>
        private bool _showCommandLine = false;

        /// <summary>
        /// Id of the node the logger is attached to
        /// </summary>
        private int _nodeId;

        #endregion
        #endregion
    }
}
