// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Text;
using System.Collections;
using System.Globalization;
using System.Collections.Generic;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;
using Microsoft.Build.Framework;
using Microsoft.Build.Shared;

using ColorSetter = Microsoft.Build.Logging.ColorSetter;
using ColorResetter = Microsoft.Build.Logging.ColorResetter;
using WriteHandler = Microsoft.Build.Logging.WriteHandler;

namespace Microsoft.Build.BackEnd.Logging
{
    /// <summary>
    /// This class implements the default logger that outputs event data
    /// to the console (stdout).
    /// </summary>
    /// <remarks>This class is not thread safe.</remarks>
    internal class ParallelConsoleLogger : BaseConsoleLogger
    {
        /// <summary>
        /// Associate a (nodeID and project_context_id) to a target framework.
        /// </summary>
        internal Dictionary<(int nodeId, int contextId), string> propertyOutputMap = new Dictionary<(int nodeId, int contextId), string>();

        #region Constructors
        /// <summary>
        /// Default constructor.
        /// </summary>
        public ParallelConsoleLogger()
            : this(LoggerVerbosity.Normal)
        {
            // do nothing
        }

        /// <summary>
        /// Create a logger instance with a specific verbosity.  This logs to
        /// the default console.
        /// </summary>
        public ParallelConsoleLogger(LoggerVerbosity verbosity)
            :
            this
            (
                verbosity,
                new WriteHandler(Console.Out.Write),
                new ColorSetter(SetColor),
                new ColorResetter(ResetColor)
            )
        {
            // do nothing
        }

        /// <summary>
        /// Initializes the logger, with alternate output handlers.
        /// </summary>
        public ParallelConsoleLogger
        (
            LoggerVerbosity verbosity,
            WriteHandler write,
            ColorSetter colorSet,
            ColorResetter colorReset
        )
        {
            InitializeConsoleMethods(verbosity, write, colorSet, colorReset);
            _deferredMessages = new Dictionary<BuildEventContext, List<BuildMessageEventArgs>>(s_compareContextNodeId);
            _buildEventManager = new BuildEventManager();
        }

        /// <summary>
        /// Check to see if the console is going to a char output such as a console,printer or com port, or if it going to a file
        /// </summary>
        private void CheckIfOutputSupportsAlignment()
        {
            _alignMessages = false;
            _bufferWidth = -1;

            // If forceNoAlign is set there is no point getting the console width as there will be no aligning of the text
            if (!_forceNoAlign)
            {
#if FEATURE_CONSOLE_BUFFERWIDTH
                if (runningWithCharacterFileType)
                {
                    // Get the size of the console buffer so messages can be formatted to the console width
                    try
                    {
                        _bufferWidth = Console.BufferWidth;
                        _alignMessages = true;
                    }
                    catch (Exception)
                    {
                        // on Win8 machines while in IDE Console.BufferWidth will throw (while it talks to native console it gets "operation aborted" native error)
                        // this is probably temporary workaround till we understand what is the reason for that exception
                        _alignMessages = false;
                    }
                }
                else
#endif
                {
                    _alignMessages = false;
                }
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Allows the logger to take action based on a parameter passed on when initializing the logger
        /// </summary>
        internal override bool ApplyParameter(string parameterName, string parameterValue)
        {
            if (base.ApplyParameter(parameterName, parameterValue))
            {
                return true;
            }

            if (String.Equals(parameterName, "SHOWCOMMANDLINE", StringComparison.OrdinalIgnoreCase))
            {
                if (String.IsNullOrEmpty(parameterValue))
                {
                    _showCommandLine = true;
                }
                else
                {
                    try
                    {
                        _showCommandLine = ConversionUtilities.ConvertStringToBool(parameterValue);
                    }
                    catch (ArgumentException)
                    {
                        // For compatibility, if the string is not a boolean, just ignore it
                        _showCommandLine = false;
                    }
                }

                return true;
            }
            else if (String.Equals(parameterName, "SHOWTIMESTAMP", StringComparison.OrdinalIgnoreCase))
            {
                _showTimeStamp = true;
                return true;
            }
            else if (String.Equals(parameterName, "SHOWEVENTID", StringComparison.OrdinalIgnoreCase))
            {
                _showEventId = true;
                return true;
            }
            else if (String.Equals(parameterName, "FORCENOALIGN", StringComparison.OrdinalIgnoreCase))
            {
                _forceNoAlign = true;
                _alignMessages = false;
                return true;
            }
            return false;
        }

        public override void Initialize(IEventSource eventSource)
        {
            // If the logger is being used in singleproc do not show EventId after each message unless it is set as part of a console parameter
            if (NumberOfProcessors == 1)
            {
                _showEventId = false;
            }

            // Parameters are parsed in Initialize
            base.Initialize(eventSource);
            CheckIfOutputSupportsAlignment();
        }

        /// <summary>
        /// Keep track of the last event displayed so target names can be displayed at the correct time
        /// </summary>
        private void ShownBuildEventContext(BuildEventContext e)
        {
            _lastDisplayedBuildEventContext = e;
        }

        /// <summary>
        /// Reset the states of per-build member variables
        /// VSW#516376 
        /// </summary>
        internal override void ResetConsoleLoggerState()
        {
            if (ShowSummary == true)
            {
                errorList = new List<BuildErrorEventArgs>();
                warningList = new List<BuildWarningEventArgs>();
            }
            else
            {
                errorList = null;
                warningList = null;
            }

            errorCount = 0;
            warningCount = 0;
            projectPerformanceCounters = null;
            targetPerformanceCounters = null;
            taskPerformanceCounters = null;
            _hasBuildStarted = false;

            // Reset the data structures created when the logger was created
            propertyOutputMap = new Dictionary<(int, int), string>();
            _buildEventManager = new BuildEventManager();
            _deferredMessages = new Dictionary<BuildEventContext, List<BuildMessageEventArgs>>(s_compareContextNodeId);
            _prefixWidth = 0;
            _lastDisplayedBuildEventContext = null;
        }

        /// <summary>
        /// Handler for build started events
        /// </summary>
        /// <param name="sender">sender (should be null)</param>
        /// <param name="e">event arguments</param>
        public override void BuildStartedHandler(object sender, BuildStartedEventArgs e)
        {
            buildStarted = e.Timestamp;
            _hasBuildStarted = true;

            if (showOnlyErrors || showOnlyWarnings) return;

            if (IsVerbosityAtLeast(LoggerVerbosity.Normal))
            {
                WriteLinePrettyFromResource("BuildStartedWithTime", e.Timestamp);
            }

            WriteEnvironment(e.BuildEnvironment);
        }

        /// <summary>
        /// Handler for build finished events
        /// </summary>
        /// <param name="sender">sender (should be null)</param>
        /// <param name="e">event arguments</param>
        public override void BuildFinishedHandler(object sender, BuildFinishedEventArgs e)
        {
            // If for some reason we have deferred messages at the end of the build they should be displayed
            // so that the reason why they are still buffered can be determined.
            if (!showOnlyErrors && !showOnlyWarnings && _deferredMessages.Count > 0)
            {
                if (IsVerbosityAtLeast(LoggerVerbosity.Normal))
                {
                    // Print out all of the deferred messages
                    WriteLinePrettyFromResource("DeferredMessages");
                    foreach (List<BuildMessageEventArgs> messageList in _deferredMessages.Values)
                    {
                        foreach (BuildMessageEventArgs message in messageList)
                        {
                            PrintMessage(message, false);
                        }
                    }
                }
            }

            // Show the performance summary if the verbosity is diagnostic or the user specifically asked for it
            // with a logger parameter.
            if (this.showPerfSummary)
            {
                ShowPerfSummary();
            }

            // Write the "Build Finished" event if verbosity is normal, detailed or diagnostic or the user
            // specified to show the summary.
            if (ShowSummary == true)
            {
                if (e.Succeeded)
                {
                    setColor(ConsoleColor.Green);
                }

                // Write the "Build Finished" event.
                WriteNewLine();
                WriteLinePretty(e.Message);
                resetColor();
            }

            // The decision whether or not to show a summary at this verbosity
            // was made during initialization. We just do what we're told.
            if (ShowSummary == true)
            {
                // We can't display a nice nested summary unless we're at Normal or above,
                // since we need to have gotten TargetStarted events, which aren't forwarded otherwise.
                if (IsVerbosityAtLeast(LoggerVerbosity.Normal))
                {
                    ShowNestedErrorWarningSummary();
                }
                else
                {
                    ShowFlatErrorWarningSummary();
                }

                // Emit text like:
                //     1 Warning(s)
                //     0 Error(s)
                // Don't color the line if it's zero. (Per Whidbey behavior.)
                if (warningCount > 0)
                {
                    setColor(ConsoleColor.Yellow);
                }
                WriteLinePrettyFromResource(2, "WarningCount", warningCount);
                resetColor();

                if (errorCount > 0)
                {
                    setColor(ConsoleColor.Red);
                }
                WriteLinePrettyFromResource(2, "ErrorCount", errorCount);
                resetColor();
            }

            // Show build time if verbosity is normal, detailed or diagnostic or the user specified to
            // show the summary.
            if (ShowSummary == true)
            {
                // The time elapsed is the difference between when the BuildStartedEventArg
                // was created and when the BuildFinishedEventArg was created
                string timeElapsed = LogFormatter.FormatTimeSpan(e.Timestamp - buildStarted);

                WriteNewLine();
                WriteLinePrettyFromResource("TimeElapsed", timeElapsed);
            }

            ResetConsoleLoggerState();
            CheckIfOutputSupportsAlignment();
        }

        /// <summary>
        /// At the end of the build, repeats the errors and warnings that occurred 
        /// during the build, and displays the error count and warning count.
        /// Does this in a "flat" style, without context.
        /// </summary>
        private void ShowFlatErrorWarningSummary()
        {
            if (warningList.Count == 0 && errorList.Count == 0) return;

            // If we're showing only warnings and/or errors, don't summarize.
            // This is the buildc.err case. There's no point summarizing since we'd just 
            // repeat the entire log content again.
            if (showOnlyErrors || showOnlyWarnings) return;

            // Make some effort to distinguish this summary from the output above, since otherwise
            // it's not clear in lower verbosities
            WriteNewLine();

            if (warningList.Count > 0)
            {
                setColor(ConsoleColor.Yellow);
                foreach (BuildWarningEventArgs warning in warningList)
                {
                    WriteMessageAligned(EventArgsFormatting.FormatEventMessage(warning, showProjectFile, FindLogOutputProperties(warning)), true);
                }
            }

            if (errorList.Count > 0)
            {
                setColor(ConsoleColor.Red);
                foreach (BuildErrorEventArgs error in errorList)
                {
                    WriteMessageAligned(EventArgsFormatting.FormatEventMessage(error, showProjectFile, FindLogOutputProperties(error)), true);
                }
            }

            resetColor();
        }

        /// <summary>
        /// At the end of the build, repeats the errors and warnings that occurred 
        /// during the build, and displays the error count and warning count.
        /// Does this in a "nested" style.
        /// </summary>
        private void ShowNestedErrorWarningSummary()
        {
            if (warningList.Count == 0 && errorList.Count == 0) return;

            // If we're showing only warnings and/or errors, don't summarize.
            // This is the buildc.err case. There's no point summarizing since we'd just 
            // repeat the entire log content again.
            if (showOnlyErrors || showOnlyWarnings) return;

            if (warningCount > 0)
            {
                setColor(ConsoleColor.Yellow);
                ShowErrorWarningSummary(warningList);
            }

            if (errorCount > 0)
            {
                setColor(ConsoleColor.Red);
                ShowErrorWarningSummary(errorList);
            }

            resetColor();
        }

        private void ShowErrorWarningSummary(IEnumerable<BuildEventArgs> listToProcess)
        {
            // Group the build warning event args based on the entry point and the target in which the warning occurred
            var groupByProjectEntryPoint = new Dictionary<ErrorWarningSummaryDictionaryKey, List<BuildEventArgs>>();

            // Loop through each of the warnings and put them into the correct buckets
            foreach (BuildEventArgs errorWarningEventArgs in listToProcess)
            {
                // Target event may be null for a couple of reasons:
                // 1) If the event was from a project load, or engine 
                // 2) If the flushing of the event queue for each request and result is turned off
                // as this could cause errors and warnings to be seen by the logger after the target finished event
                // which would cause the error or warning to have no matching target started event as they are removed
                // when a target finished event is logged.
                // 3) On NORMAL verbosity if the error or warning occurs in a project load then the error or warning and the target started event will be forwarded to 
                // different forwarding loggers which cannot communicate to each other, meaning there will be no matching target started event logged 
                // as the forwarding logger did not know to forward the target started event
                string targetName = null;
                TargetStartedEventMinimumFields targetEvent = _buildEventManager.GetTargetStartedEvent(errorWarningEventArgs.BuildEventContext);

                if (targetEvent != null)
                {
                    targetName = targetEvent.TargetName;
                }

                // Create a new key from the error event context and the target where the error happened
                ErrorWarningSummaryDictionaryKey key = new ErrorWarningSummaryDictionaryKey(errorWarningEventArgs.BuildEventContext, targetName);

                // Check to see if there is a bucket for the warning
                // If there is no bucket create a new one which contains a list of all the errors which
                // happened for a given buildEventContext / target
                if (!groupByProjectEntryPoint.TryGetValue(key, out var errorWarningEventListByTarget))
                {
                    // happened for a given buildEventContext / target
                    errorWarningEventListByTarget = new List<BuildEventArgs>();
                    groupByProjectEntryPoint.Add(key, errorWarningEventListByTarget);
                }
                // Add the error event to the correct bucket
                errorWarningEventListByTarget.Add(errorWarningEventArgs);
            }

            BuildEventContext previousEntryPoint = null;
            string previousTarget = null;
            // Loop through each of the bucket and print out the stack trace information for the errors
            foreach (KeyValuePair<ErrorWarningSummaryDictionaryKey, List<BuildEventArgs>> valuePair in groupByProjectEntryPoint)
            {
                // If the project entry point where the error occurred is the same as the previous message do not print the
                // stack trace again
                if (previousEntryPoint != valuePair.Key.EntryPointContext)
                {
                    WriteNewLine();
                    foreach (string s in _buildEventManager.ProjectCallStackFromProject(valuePair.Key.EntryPointContext))
                    {
                        WriteMessageAligned(s, false);
                    }
                    previousEntryPoint = valuePair.Key.EntryPointContext;
                }

                // If the target where the error occurred is the same as the previous message do not print the location
                // where the error occurred again
                if (!String.Equals(previousTarget, valuePair.Key.TargetName, StringComparison.OrdinalIgnoreCase))
                {
                    // If no targetName was specified then do not show the target where the error occurred
                    if (!string.IsNullOrEmpty(valuePair.Key.TargetName))
                    {
                        WriteMessageAligned(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("ErrorWarningInTarget", valuePair.Key.TargetName), false);
                    }
                    previousTarget = valuePair.Key.TargetName;
                }

                // Print out all of the errors under the ProjectEntryPoint / target
                foreach (BuildEventArgs errorWarningEvent in valuePair.Value)
                {
                    if (errorWarningEvent is BuildErrorEventArgs)
                    {
                        WriteMessageAligned("  " + EventArgsFormatting.FormatEventMessage(errorWarningEvent as BuildErrorEventArgs, showProjectFile, FindLogOutputProperties(errorWarningEvent)), false);
                    }
                    else if (errorWarningEvent is BuildWarningEventArgs)
                    {
                        WriteMessageAligned("  " + EventArgsFormatting.FormatEventMessage(errorWarningEvent as BuildWarningEventArgs, showProjectFile, FindLogOutputProperties(errorWarningEvent)), false);
                    }
                }
                WriteNewLine();
            }
        }

        /// <summary>
        /// Handler for project started events
        /// </summary>
        /// <param name="sender">sender (should be null)</param>
        /// <param name="e">event arguments</param>
        public override void ProjectStartedHandler(object sender, ProjectStartedEventArgs e)
        {
            ErrorUtilities.VerifyThrowArgumentNull(e.BuildEventContext, "BuildEventContext");
            ErrorUtilities.VerifyThrowArgumentNull(e.ParentProjectBuildEventContext, "ParentProjectBuildEventContext");

            // Add the project to the BuildManager so we can use the start information later in the build process
            _buildEventManager.AddProjectStartedEvent(e, _showTimeStamp || IsVerbosityAtLeast(LoggerVerbosity.Detailed));

            if (this.showPerfSummary)
            {
                // Create a new project performance counter for this project
                MPPerformanceCounter counter = GetPerformanceCounter(e.ProjectFile, ref projectPerformanceCounters);
                counter.AddEventStarted(e.TargetNames, e.BuildEventContext, e.Timestamp, s_compareContextNodeId);
            }

            // If there were deferred messages then we should show them now, this will cause the project started event to be shown properly
            if (_deferredMessages.TryGetValue(e.BuildEventContext, out var deferredMessages))
            {
                if (!showOnlyErrors && !showOnlyWarnings)
                {
                    foreach (BuildMessageEventArgs message in deferredMessages)
                    {
                        // This will display the project started event before the messages is shown
                        this.MessageHandler(sender, message);
                    }
                }
                _deferredMessages.Remove(e.BuildEventContext);
            }

            // If we are in diagnostic and are going to show items, show the project started event
            // along with the items. The project started event will only be shown if it has not been shown before
            if (Verbosity == LoggerVerbosity.Diagnostic && showItemAndPropertyList)
            {
                // Show the deferredProjectStartedEvent
                if (!showOnlyErrors && !showOnlyWarnings)
                {
                    DisplayDeferredProjectStartedEvent(e.BuildEventContext);
                }

                if (e.Properties != null)
                {
                    WriteProperties(e, e.Properties);
                }

                if (e.Items != null)
                {
                    WriteItems(e, e.Items);
                }
            }

            var projectKey = (e.BuildEventContext.NodeId, e.BuildEventContext.ProjectContextId);

            // If the value is available at all, it will be either in the items
            // from ProjectStarted (old behavior), or the items from ProjectEvaluationFinished (new behavior).
            // First try the old behavior, and fallback to the new behavior.
            var result = ReadProjectConfigurationDescription(e.Items);
            if (result != null)
            {
                // Found the items directly on ProjectStarted
                propertyOutputMap[projectKey] = result;
            }
            else
            {
                // Try to see if we saw the items on the corresponding ProjectEvaluationFinished
                var evaluationKey = GetEvaluationKey(e.BuildEventContext);

                // if the value was set from ProjectEvaluationFinished, copy it into the entry
                // for this project
                if (propertyOutputMap.TryGetValue(evaluationKey, out string value))
                {
                    propertyOutputMap[projectKey] = value;
                }
            }
        }

        private string ReadProjectConfigurationDescription(IEnumerable items)
        {
            if (items == null)
            {
                return null;
            }

            ReuseableStringBuilder projectConfigurationDescription = null;

            Internal.Utilities.EnumerateItems(items, item =>
            {
                // Determine if the LogOutputProperties item has been used.
                if (string.Equals((string)item.Key, ItemMetadataNames.ProjectConfigurationDescription, StringComparison.OrdinalIgnoreCase))
                {
                    if (projectConfigurationDescription == null)
                    {
                        projectConfigurationDescription = new ReuseableStringBuilder();
                    }

                    string itemSpec = item.Value switch
                    {
                        IItem iitem => iitem.EvaluatedInclude, // ProjectItem
                        ITaskItem taskItem => taskItem.ItemSpec, // ProjectItemInstance
                        _ => throw new NotSupportedException(Convert.ToString(item.Value))
                    };

                    // Add the item value to the string to be printed in error/warning messages.
                    projectConfigurationDescription.Append(" ").Append(itemSpec);
                }
            });

            if (projectConfigurationDescription != null)
            {
                var result = projectConfigurationDescription.ToString();
                (projectConfigurationDescription as IDisposable)?.Dispose();
                return result;
            }

            return null;
        }

        /// <summary>
        /// In case the items are stored on ProjectEvaluationFinishedEventArgs
        /// (new behavior), we first store the value per evaluation, and then
        /// in ProjectStarted, find the value from the project's evaluation
        /// and use that.
        /// </summary>
        private (int, int) GetEvaluationKey(BuildEventContext buildEventContext)
            // note that we use a negative number for evaluations so that we don't conflict
            // with project context ids.
            => (buildEventContext.NodeId, -buildEventContext.EvaluationId);

        /// <summary>
        /// Handler for project finished events
        /// </summary>
        /// <param name="sender">sender (should be null)</param>
        /// <param name="e">event arguments</param>
        public override void ProjectFinishedHandler(object sender, ProjectFinishedEventArgs e)
        {
            ErrorUtilities.VerifyThrowArgumentNull(e.BuildEventContext, "BuildEventContext");

            // Get the project started event so we can use its information to properly display a project finished event
            ProjectStartedEventMinimumFields startedEvent = _buildEventManager.GetProjectStartedEvent(e.BuildEventContext);
            ErrorUtilities.VerifyThrow(startedEvent != null, "Project finished event for {0} received without matching start event", e.ProjectFile);

            if (this.showPerfSummary)
            {
                // Stop the performance counter which was created in the project started event handler
                MPPerformanceCounter counter = GetPerformanceCounter(e.ProjectFile, ref projectPerformanceCounters);
                counter.AddEventFinished(startedEvent.TargetNames, e.BuildEventContext, e.Timestamp);
            }

            if (IsVerbosityAtLeast(LoggerVerbosity.Normal))
            {
                // Only want to show the project finished event if a project started event has been shown
                if (startedEvent.ShowProjectFinishedEvent)
                {
                    _lastProjectFullKey = GetFullProjectKey(e.BuildEventContext);

                    if (!showOnlyErrors && !showOnlyWarnings)
                    {
                        WriteLinePrefix(e.BuildEventContext, e.Timestamp, false);
                        setColor(ConsoleColor.Cyan);

                        // In the project finished message the targets which were built and the project which was built
                        // should be shown
                        string targets = startedEvent.TargetNames;
                        string projectName = startedEvent.ProjectFile ?? string.Empty;
                        
                        // Show which targets were built as part of this project
                        if (string.IsNullOrEmpty(targets))
                        {
                            if (e.Succeeded)
                            {
                                WriteMessageAligned(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("ProjectFinishedPrefixWithDefaultTargetsMultiProc", projectName), true);
                            }
                            else
                            {
                                WriteMessageAligned(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("ProjectFinishedPrefixWithDefaultTargetsMultiProcFailed", projectName), true);
                            }
                        }
                        else
                        {
                            if (e.Succeeded)
                            {
                                WriteMessageAligned(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("ProjectFinishedPrefixWithTargetNamesMultiProc", projectName, targets), true);
                            }
                            else
                            {
                                WriteMessageAligned(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("ProjectFinishedPrefixWithTargetNamesMultiProcFailed", projectName, targets), true);
                            }
                        }

                        // In single proc only make a space between the project done event and the next line, this 
                        // is to increase the readability on the single proc log when there are a number of done events
                        // or a mix of done events and project started events. Also only do this on the console and not any log file.
                        if (NumberOfProcessors == 1 && runningWithCharacterFileType)
                        {
                            WriteNewLine();
                        }
                    }

                    ShownBuildEventContext(e.BuildEventContext);
                    resetColor();
                }
            }
            // We are done with the project started event if the project has finished building, remove its reference
            _buildEventManager.RemoveProjectStartedEvent(e.BuildEventContext);
        }

        /// <summary>
        /// Writes out the list of property names and their values.
        /// This could be done at any time during the build to show the latest
        /// property values, using the cached reference to the list from the 
        /// appropriate ProjectStarted event.
        /// </summary>
        /// <param name="e">A <see cref="BuildEventArgs"/> object containing information about the build event.</param>
        /// <param name="properties">List of properties</param>
        internal void WriteProperties(BuildEventArgs e, IEnumerable properties)
        {
            if (showOnlyErrors || showOnlyWarnings) return;
            var propertyList = ExtractPropertyList(properties);

            // if there are no properties to display return out of the method and dont print out anything related to displaying
            // the properties, this includes the multiproc prefix information or the Initial properties header
            if (propertyList.Count == 0)
            {
                return;
            }

            WriteLinePrefix(e.BuildEventContext, e.Timestamp, false);
            WriteProperties(propertyList);
            ShownBuildEventContext(e.BuildEventContext);
        }

        internal override void OutputProperties(List<DictionaryEntry> list)
        {
            // Write the banner
            setColor(ConsoleColor.Green);
            WriteMessageAligned(ResourceUtilities.GetResourceString("PropertyListHeader"), true);
            // Write each property name and its value, one per line
            foreach (DictionaryEntry prop in list)
            {
                setColor(ConsoleColor.Gray);
                string propertyString = String.Format(CultureInfo.CurrentCulture, "{0} = {1}", prop.Key, EscapingUtilities.UnescapeAll((string)(prop.Value)));
                WriteMessageAligned(propertyString, false);
            }
            resetColor();
        }

        /// <summary>
        ///  Write the environment strings to the console.
        /// </summary>
        internal override void OutputEnvironment(IDictionary<string, string> environment)
        {
            // Write the banner
            setColor(ConsoleColor.Green);
            WriteMessageAligned(ResourceUtilities.GetResourceString("EnvironmentHeader"), true);

            if (environment != null)
            {
                // Write each property name and its value, one per line
                foreach (KeyValuePair<string, string> entry in environment)
                {
                    setColor(ConsoleColor.Gray);
                    string environmentMessage = String.Format(CultureInfo.CurrentCulture, "{0} = {1}", entry.Key, entry.Value);
                    WriteMessageAligned(environmentMessage, false);
                }
            }

            resetColor();
        }

        /// <summary>
        /// Writes out the list of item specs and their metadata.
        /// This could be done at any time during the build to show the latest
        /// items, using the cached reference to the list from the 
        /// appropriate ProjectStarted event.
        /// </summary>
        /// <param name="e">A <see cref="BuildEventArgs"/> object containing information about the build event.</param>
        /// <param name="items">List of items</param>
        internal void WriteItems(BuildEventArgs e, IEnumerable items)
        {
            if (showOnlyErrors || showOnlyWarnings) return;
            SortedList itemList = ExtractItemList(items);

            // If there are no Items to display return out of the method and don't print out anything related to displaying
            // the items, this includes the multiproc prefix information or the Initial items header
            if (itemList.Count == 0)
            {
                return;
            }
            WriteLinePrefix(e.BuildEventContext, e.Timestamp, false);
            WriteItems(itemList);
            ShownBuildEventContext(e.BuildEventContext);
        }

        protected override void WriteItemType(string itemType)
        {
            setColor(ConsoleColor.DarkGray);
            WriteMessageAligned(itemType, prefixAlreadyWritten: false);
            setColor(ConsoleColor.Gray);
        }

        protected override void WriteItemSpec(string itemSpec)
        {
            WriteMessageAligned(new string(' ', 2 * tabWidth) + itemSpec, prefixAlreadyWritten: false);
        }

        protected override void WriteMetadata(string name, string value)
        {
            WriteMessageAligned($"{new string(' ', 4 * tabWidth)}{name} = {value}", prefixAlreadyWritten: false);
        }

        /// <summary>
        /// Handler for target started events
        /// </summary>
        /// <param name="sender">sender (should be null)</param>
        /// <param name="e">event arguments</param>
        public override void TargetStartedHandler(object sender, TargetStartedEventArgs e)
        {
            ErrorUtilities.VerifyThrowArgumentNull(e.BuildEventContext, "BuildEventContext");

            // Add the target started information to the buildEventManager so its information can be used
            // later in the build
            _buildEventManager.AddTargetStartedEvent(e, _showTimeStamp || IsVerbosityAtLeast(LoggerVerbosity.Detailed));

            if (this.showPerfSummary)
            {
                // Create a new performance counter for this target
                MPPerformanceCounter counter = GetPerformanceCounter(e.TargetName, ref targetPerformanceCounters);
                counter.AddEventStarted(null, e.BuildEventContext, e.Timestamp, s_compareContextNodeIdTargetId);
            }
        }

        /// <summary>
        /// Handler for target finished events
        /// </summary>
        /// <param name="sender">sender (should be null)</param>
        /// <param name="e">event arguments</param>
        public override void TargetFinishedHandler(object sender, TargetFinishedEventArgs e)
        {
            ErrorUtilities.VerifyThrowArgumentNull(e.BuildEventContext, "BuildEventContext");

            if (this.showPerfSummary)
            {
                // Stop the performance counter started in the targetStartedEventHandler
                MPPerformanceCounter counter = GetPerformanceCounter(e.TargetName, ref targetPerformanceCounters);
                counter.AddEventFinished(null, e.BuildEventContext, e.Timestamp);
            }

            if (IsVerbosityAtLeast(LoggerVerbosity.Detailed))
            {
                // Display the target started event arg if we have got to the target finished but have not displayed it yet.
                DisplayDeferredTargetStartedEvent(e.BuildEventContext);

                // Get the target started event so we can determine whether or not to show the targetFinishedEvent
                // as we only want to show target finished events if a target started event has been shown
                TargetStartedEventMinimumFields startedEvent = _buildEventManager.GetTargetStartedEvent(e.BuildEventContext);
                ErrorUtilities.VerifyThrow(startedEvent != null, "Started event should not be null in the finished event handler");
                if (startedEvent.ShowTargetFinishedEvent)
                {
                    if (showTargetOutputs)
                    {
                        IEnumerable targetOutputs = e.TargetOutputs;

                        if (targetOutputs != null)
                        {
                            WriteMessageAligned(ResourceUtilities.GetResourceString("TargetOutputItemsHeader"), false);
                            foreach (ITaskItem item in targetOutputs)
                            {
                                WriteMessageAligned(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("TargetOutputItem", item.ItemSpec), false);

                                IDictionary metadata = item.CloneCustomMetadata();

                                foreach (DictionaryEntry metadatum in metadata)
                                {
                                    WriteMessageAligned(new String(' ', 4 * tabWidth) + metadatum.Key + " = " + item.GetMetadata(metadatum.Key as string), false);
                                }
                            }
                        }
                    }

                    if (!showOnlyErrors && !showOnlyWarnings)
                    {
                        _lastProjectFullKey = GetFullProjectKey(e.BuildEventContext);
                        WriteLinePrefix(e.BuildEventContext, e.Timestamp, false);
                        setColor(ConsoleColor.Cyan);
                        if (IsVerbosityAtLeast(LoggerVerbosity.Diagnostic) || _showEventId)
                        {
                            WriteMessageAligned(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("TargetMessageWithId", e.Message, e.BuildEventContext.TargetId), true);
                        }
                        else
                        {
                            WriteMessageAligned(e.Message, true);
                        }
                        resetColor();
                    }

                    ShownBuildEventContext(e.BuildEventContext);
                }
            }

            // We no longer need this target started event, it can be removed
            _buildEventManager.RemoveTargetStartedEvent(e.BuildEventContext);
        }

        /// <summary>
        /// Handler for task started events
        /// </summary>
        /// <param name="sender">sender (should be null)</param>
        /// <param name="e">event arguments</param>
        public override void TaskStartedHandler(object sender, TaskStartedEventArgs e)
        {
            ErrorUtilities.VerifyThrowArgumentNull(e.BuildEventContext, "BuildEventContext");

            // if verbosity is detailed or diagnostic

            if (IsVerbosityAtLeast(LoggerVerbosity.Detailed))
            {
                DisplayDeferredStartedEvents(e.BuildEventContext);

                if (!showOnlyErrors && !showOnlyWarnings)
                {
                    bool prefixAlreadyWritten = WriteTargetMessagePrefix(e, e.BuildEventContext, e.Timestamp);
                    setColor(ConsoleColor.DarkCyan);
                    if (IsVerbosityAtLeast(LoggerVerbosity.Diagnostic) || _showEventId)
                    {
                        WriteMessageAligned(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("TaskMessageWithId", e.Message, e.BuildEventContext.TaskId), prefixAlreadyWritten);
                    }
                    else
                    {
                        WriteMessageAligned(e.Message, prefixAlreadyWritten);
                    }
                    resetColor();
                }

                ShownBuildEventContext(e.BuildEventContext);
            }

            if (this.showPerfSummary)
            {
                // Create a new performance counter for this task
                MPPerformanceCounter counter = GetPerformanceCounter(e.TaskName, ref taskPerformanceCounters);
                counter.AddEventStarted(null, e.BuildEventContext, e.Timestamp, null);
            }
        }

        /// <summary>
        /// Handler for task finished events
        /// </summary>
        /// <param name="sender">sender (should be null)</param>
        /// <param name="e">event arguments</param>
        public override void TaskFinishedHandler(object sender, TaskFinishedEventArgs e)
        {
            ErrorUtilities.VerifyThrowArgumentNull(e.BuildEventContext, "BuildEventContext");
            if (this.showPerfSummary)
            {
                // Stop the task performance counter which was started in the task started event
                MPPerformanceCounter counter = GetPerformanceCounter(e.TaskName, ref taskPerformanceCounters);
                counter.AddEventFinished(null, e.BuildEventContext, e.Timestamp);
            }

            // if verbosity is detailed or diagnostic
            if (IsVerbosityAtLeast(LoggerVerbosity.Detailed))
            {
                if (!showOnlyErrors && !showOnlyWarnings)
                {
                    bool prefixAlreadyWritten = WriteTargetMessagePrefix(e, e.BuildEventContext, e.Timestamp);
                    setColor(ConsoleColor.DarkCyan);
                    if (IsVerbosityAtLeast(LoggerVerbosity.Diagnostic) || _showEventId)
                    {
                        WriteMessageAligned(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("TaskMessageWithId", e.Message, e.BuildEventContext.TaskId), prefixAlreadyWritten);
                    }
                    else
                    {
                        WriteMessageAligned(e.Message, prefixAlreadyWritten);
                    }
                    resetColor();
                }
                ShownBuildEventContext(e.BuildEventContext);
            }
        }

        /// <summary>
        /// Finds the LogOutProperty string to be printed in messages.
        /// </summary>
        /// <param name="e">Build event to extract context information from.</param>
        internal string FindLogOutputProperties(BuildEventArgs e)
        {
            string projectConfigurationDescription = String.Empty;
            if (e.BuildEventContext != null)
            {
                var key = (e.BuildEventContext.NodeId, e.BuildEventContext.ProjectContextId);
                propertyOutputMap.TryGetValue(key, out projectConfigurationDescription);
            }

            return projectConfigurationDescription;
        }

        /// <summary>
        /// Prints an error event
        /// </summary>
        public override void ErrorHandler(object sender, BuildErrorEventArgs e)
        {
            ErrorUtilities.VerifyThrowArgumentNull(e.BuildEventContext, "BuildEventContext");
            // Keep track of the number of error events raised 
            errorCount++;

            // If there is an error we need to walk up the call stack and make sure that 
            // the project started events back to the root project know an error has occurred
            // and are not removed when they finish
            _buildEventManager.SetErrorWarningFlagOnCallStack(e.BuildEventContext);

            TargetStartedEventMinimumFields targetStartedEvent = _buildEventManager.GetTargetStartedEvent(e.BuildEventContext);
            // Can be null if the error occurred outside of a target, or the error occurred before the targetStartedEvent
            if (targetStartedEvent != null)
            {
                targetStartedEvent.ErrorInTarget = true;
            }

            DisplayDeferredStartedEvents(e.BuildEventContext);

            // Display only if showOnlyWarnings is false;
            // unless showOnlyErrors is true, which trumps it.
            if (!showOnlyWarnings || showOnlyErrors)
            {
                if (IsVerbosityAtLeast(LoggerVerbosity.Normal))
                {
                    WriteLinePrefix(e.BuildEventContext, e.Timestamp, false);
                }

                setColor(ConsoleColor.Red);
                WriteMessageAligned(EventArgsFormatting.FormatEventMessage(e, showProjectFile, FindLogOutputProperties(e)), true);
                ShownBuildEventContext(e.BuildEventContext);
                if (ShowSummary == true)
                {
                    if (!errorList.Contains(e))
                    {
                        errorList.Add(e);
                    }
                }
                resetColor();
            }
        }

        /// <summary>
        /// Prints a warning event
        /// </summary>
        public override void WarningHandler(object sender, BuildWarningEventArgs e)
        {
            ErrorUtilities.VerifyThrowArgumentNull(e.BuildEventContext, "BuildEventContext");
            // Keep track of the number of warning events raised during the build
            warningCount++;

            // If there is a warning we need to walk up the call stack and make sure that 
            // the project started events back to the root project know a warning has occurred
            // and are not removed when they finish
            _buildEventManager.SetErrorWarningFlagOnCallStack(e.BuildEventContext);
            TargetStartedEventMinimumFields targetStartedEvent = _buildEventManager.GetTargetStartedEvent(e.BuildEventContext);

            // Can be null if the error occurred outside of a target, or the error occurs before the targetStartedEvent
            if (targetStartedEvent != null)
            {
                targetStartedEvent.ErrorInTarget = true;
            }

            DisplayDeferredStartedEvents(e.BuildEventContext);

            // Display only if showOnlyErrors is false;
            // unless showOnlyWarnings is true, which trumps it.
            if (!showOnlyErrors || showOnlyWarnings)
            {
                if (IsVerbosityAtLeast(LoggerVerbosity.Normal))
                {
                    WriteLinePrefix(e.BuildEventContext, e.Timestamp, false);
                }

                setColor(ConsoleColor.Yellow);
                WriteMessageAligned(EventArgsFormatting.FormatEventMessage(e, showProjectFile, FindLogOutputProperties(e)), true);
            }

            ShownBuildEventContext(e.BuildEventContext);

            if (ShowSummary == true)
            {
                if (!warningList.Contains(e))
                {
                    warningList.Add(e);
                }
            }
            resetColor();
        }

        /// <summary>
        /// Prints a message event
        /// </summary>
        public override void MessageHandler(object sender, BuildMessageEventArgs e)
        {
            if (showOnlyErrors || showOnlyWarnings) return;

            ErrorUtilities.VerifyThrowArgumentNull(e.BuildEventContext, "BuildEventContext");
            bool print = false;
            bool lightenText = false;

            if (e is TaskCommandLineEventArgs)
            {
                if ((_showCommandLine.HasValue && !_showCommandLine.Value) || (!_showCommandLine.HasValue && !IsVerbosityAtLeast(LoggerVerbosity.Normal)))
                {
                    return;
                }
                print = true;
            }
            else
            {
                LoggerVerbosity minimumVerbosity = ImportanceToMinimumVerbosity(e.Importance, out lightenText);
                print = IsVerbosityAtLeast(minimumVerbosity);
            }

            if (print)
            {
                // If the event has a valid Project contextId but the project started event has not been fired, the message needs to be 
                // buffered until the project started event is fired
                if (
                       _hasBuildStarted
                       && e.BuildEventContext.ProjectContextId != BuildEventContext.InvalidProjectContextId
                       && _buildEventManager.GetProjectStartedEvent(e.BuildEventContext) == null
                       && IsVerbosityAtLeast(LoggerVerbosity.Normal)
                    )
                {
                    if (!_deferredMessages.TryGetValue(e.BuildEventContext, out List<BuildMessageEventArgs> messageList))
                    {
                        messageList = new List<BuildMessageEventArgs>();
                        _deferredMessages.Add(e.BuildEventContext, messageList);
                    }
                    messageList.Add(e);
                    return;
                }

                DisplayDeferredStartedEvents(e.BuildEventContext);

                // Print the message event out to the console
                PrintMessage(e, lightenText);
                ShownBuildEventContext(e.BuildEventContext);
            }
        }

        public override void StatusEventHandler(object sender, BuildStatusEventArgs e)
        {
            if (e is ProjectEvaluationStartedEventArgs projectEvaluationStarted)
            {
                if (showPerfSummary)
                {
                    MPPerformanceCounter counter = GetPerformanceCounter(projectEvaluationStarted.ProjectFile, ref projectEvaluationPerformanceCounters);
                    counter.AddEventStarted(null, e.BuildEventContext, e.Timestamp, s_compareContextNodeId);
                }
            }
            else if (e is ProjectEvaluationFinishedEventArgs projectEvaluationFinished)
            {
                if (showPerfSummary)
                {
                    MPPerformanceCounter counter = GetPerformanceCounter(projectEvaluationFinished.ProjectFile, ref projectEvaluationPerformanceCounters);
                    counter.AddEventFinished(null, e.BuildEventContext, e.Timestamp);
                }

                if (Verbosity == LoggerVerbosity.Diagnostic && showItemAndPropertyList)
                {
                    if (projectEvaluationFinished.Properties != null)
                    {
                        WriteProperties(projectEvaluationFinished, projectEvaluationFinished.Properties);
                    }

                    if (projectEvaluationFinished.Items != null)
                    {
                        WriteItems(projectEvaluationFinished, projectEvaluationFinished.Items);
                    }
                }

                var value = ReadProjectConfigurationDescription(projectEvaluationFinished.Items);
                if (value != null)
                {
                    var evaluationKey = GetEvaluationKey(e.BuildEventContext);
                    propertyOutputMap[evaluationKey] = value;
                }
            }
        }

        private void DisplayDeferredStartedEvents(BuildEventContext e)
        {
            if (showOnlyErrors || showOnlyWarnings) return;

            // Display any project started events which were deferred until a visible 
            // message from their project is displayed
            if (IsVerbosityAtLeast(LoggerVerbosity.Normal))
            {
                DisplayDeferredProjectStartedEvent(e);
            }

            // Display any target started events which were deferred until a visible 
            // message from their target is displayed
            if (IsVerbosityAtLeast(LoggerVerbosity.Detailed))
            {
                DisplayDeferredTargetStartedEvent(e);
            }
        }

        /// <summary>
        /// Prints out a message event to the console
        /// </summary>
        private void PrintMessage(BuildMessageEventArgs e, bool lightenText)
        {
            string nonNullMessage;

            // Include file information if present.
            if (e.File != null)
            {
                nonNullMessage = EventArgsFormatting.FormatEventMessage(e, showProjectFile, FindLogOutputProperties(e));
            }
            else
            {
                nonNullMessage = e.Message ?? string.Empty;
            }

            int prefixAdjustment = 0;

            // Do not include prefixAdjustment if TaskId is invalid or file information is present.
            // We want messages with file information to appear aligned with warning and error messages.
            if (e.BuildEventContext.TaskId != BuildEventContext.InvalidTaskId && e.File == null)
            {
                prefixAdjustment = 2;
            }

            if (lightenText)
            {
                setColor(ConsoleColor.DarkGray);
            }

            PrintTargetNamePerMessage(e, lightenText);

            // On diagnostic or if showEventId is set the task message should also display the taskId to assist debugging
            if ((IsVerbosityAtLeast(LoggerVerbosity.Diagnostic) || _showEventId) && e.BuildEventContext.TaskId != BuildEventContext.InvalidTaskId)
            {
                bool prefixAlreadyWritten = WriteTargetMessagePrefix(e, e.BuildEventContext, e.Timestamp);
                WriteMessageAligned(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("TaskMessageWithId", nonNullMessage, e.BuildEventContext.TaskId), prefixAlreadyWritten, prefixAdjustment);
            }
            else
            {
                // A time stamp may be shown on verbosities lower than diagnostic
                if (_showTimeStamp || IsVerbosityAtLeast(LoggerVerbosity.Detailed))
                {
                    bool prefixAlreadyWritten = WriteTargetMessagePrefix(e, e.BuildEventContext, e.Timestamp);
                    WriteMessageAligned(nonNullMessage, prefixAlreadyWritten, prefixAdjustment);
                }
                else
                {
                    WriteMessageAligned(nonNullMessage, false, prefixAdjustment);
                }
            }

            if (lightenText)
            {
                resetColor();
            }
        }

        private void PrintTargetNamePerMessage(BuildMessageEventArgs e, bool lightenText)
        {
            if (IsVerbosityAtLeast(LoggerVerbosity.Normal))
            {
                // Event Context of the current message
                BuildEventContext currentBuildEventContext = e.BuildEventContext;

                // Should the target name be written before the message
                bool writeTargetName = false;
                string targetName = string.Empty;

                // Does the context (Project, Node, Context, Target, NOT task) of the previous event match the current message
                bool contextAreEqual = s_compareContextNodeIdTargetId.Equals(currentBuildEventContext, _lastDisplayedBuildEventContext);

                TargetStartedEventMinimumFields targetStartedEvent = null;
                // If the previous event does not have the same target context information, the target name needs to be printed to the console
                // to give the message some more contextual information
                if (!contextAreEqual)
                {
                    targetStartedEvent = _buildEventManager.GetTargetStartedEvent(currentBuildEventContext);
                    // Some messages such as engine messages will not have a target started event, in their case, dont print the targetName
                    if (targetStartedEvent != null)
                    {
                        targetName = targetStartedEvent.TargetName;
                        writeTargetName = true;
                    }
                }

                if (writeTargetName)
                {
                    bool prefixAlreadyWritten = WriteTargetMessagePrefix(e, targetStartedEvent.ProjectBuildEventContext, targetStartedEvent.TimeStamp);

                    setColor(ConsoleColor.Cyan);
                    if (IsVerbosityAtLeast(LoggerVerbosity.Diagnostic) || _showEventId)
                    {
                        WriteMessageAligned(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("TargetMessageWithId", targetName, e.BuildEventContext.TargetId), prefixAlreadyWritten);
                    }
                    else
                    {
                        WriteMessageAligned(targetName + ":", prefixAlreadyWritten);
                    }

                    if (lightenText)
                    {
                        setColor(ConsoleColor.DarkGray);
                    }
                    else
                    {
                        resetColor();
                    }
                }
            }
        }

        private bool WriteTargetMessagePrefix(BuildEventArgs e, BuildEventContext context, DateTime timeStamp)
        {
            bool prefixAlreadyWritten = true;
            ProjectFullKey currentProjectFullKey = GetFullProjectKey(e.BuildEventContext);
            if (!_lastProjectFullKey.Equals(currentProjectFullKey))
            {
                // Write the prefix information about the target for the message
                WriteLinePrefix(context, timeStamp, false);
                _lastProjectFullKey = currentProjectFullKey;
            }
            else
            {
                prefixAlreadyWritten = false;
            }
            return prefixAlreadyWritten;
        }

        /// <summary>
        /// Writes a message to the console, aligned and formatted to fit within the console width
        /// </summary>
        /// <param name="message">Message to be formatted to fit on the console</param>
        /// <param name="prefixAlreadyWritten">Has the prefix(timestamp or key been written)</param>
        private void WriteMessageAligned(string message, bool prefixAlreadyWritten)
        {
            WriteMessageAligned(message, prefixAlreadyWritten, 0);
        }

        /// <summary>
        /// Writes a message to the console, aligned and formatted to fit within the console width
        /// </summary>
        /// <param name="message">Message to be formatted to fit on the console</param>
        /// <param name="prefixAlreadyWritten">Has the prefix(timestamp or key been written)</param>
        /// <param name="prefixAdjustment">An amount to adjust the prefix by.</param>
        private void WriteMessageAligned(string message, bool prefixAlreadyWritten, int prefixAdjustment)
        {
            // This method may require the splitting of lines in order to format them to the console; this must be an atomic operation
            lock (_lockObject)
            {
                int adjustedPrefixWidth = _prefixWidth + prefixAdjustment;

                // The string may contain new lines, treat each new line as a different string to format and send to the console
                string[] nonNullMessages = SplitStringOnNewLines(message);
                for (int i = 0; i < nonNullMessages.Length; i++)
                {
                    string nonNullMessage = nonNullMessages[i];
                    // Take into account the new line char which will be added to the end or each reformatted string
                    int bufferWidthMinusNewLine = _bufferWidth - 1;

                    // If the buffer is larger then the prefix information (timestamp and key) then reformat the messages. 
                    // If there is not enough room just print the message out and let the console do the formatting
                    bool bufferIsLargerThanPrefix = bufferWidthMinusNewLine > adjustedPrefixWidth;
                    bool messageAndPrefixTooLargeForBuffer = (nonNullMessage.Length + adjustedPrefixWidth) > bufferWidthMinusNewLine;
                    if (bufferIsLargerThanPrefix && messageAndPrefixTooLargeForBuffer && _alignMessages)
                    {
                        // Our message may have embedded tab characters, so expand those to their space
                        // equivalent so that wrapping works as expected.
                        nonNullMessage = nonNullMessage.Replace("\t", consoleTab);

                        // If the message and the prefix are too large for one line in the console, split the string to fit
                        int index = 0;
                        int messageLength = nonNullMessage.Length;
                        // Loop until all the string has been sent to the console
                        while (index < messageLength)
                        {
                            // Calculate how many chars will fit on the console buffer
                            int amountToCopy = (messageLength - index) < (bufferWidthMinusNewLine - adjustedPrefixWidth) ? (messageLength - index) : (bufferWidthMinusNewLine - adjustedPrefixWidth);
                            WriteBasedOnPrefix(nonNullMessage.Substring(index, amountToCopy), prefixAlreadyWritten && index == 0 && i == 0, adjustedPrefixWidth);
                            index += amountToCopy;
                        }
                    }
                    else
                    {
                        //there is not enough room just print the message out and let the console do the formatting
                        WriteBasedOnPrefix(nonNullMessage, prefixAlreadyWritten, adjustedPrefixWidth);
                    }
                }
            }
        }

        /// <summary>
        /// Write message taking into account whether or not the prefix (timestamp and key) have already been written on the line
        /// </summary>
        private void WriteBasedOnPrefix(string nonNullMessage, bool prefixAlreadyWritten, int adjustedPrefixWidth)
        {
            if (prefixAlreadyWritten)
            {
                WriteHandler(nonNullMessage + Environment.NewLine);
            }
            else
            {
                // No prefix info has been written, indent the line to the proper location
                WriteHandler(IndentString(nonNullMessage, adjustedPrefixWidth));
            }
        }

        /// <summary>
        /// Will display the target started event which was deferred until the first visible message for the target is ready to be displayed
        /// </summary>
        private void DisplayDeferredTargetStartedEvent(BuildEventContext e)
        {
            if (showOnlyErrors || showOnlyWarnings) return;

            // Get the deferred target started event
            TargetStartedEventMinimumFields targetStartedEvent = _buildEventManager.GetTargetStartedEvent(e);

            // Make sure we have not shown the event before
            if (targetStartedEvent?.ShowTargetFinishedEvent == false)
            {
                // Since the target started event has been shows, the target finished event should also be shown
                targetStartedEvent.ShowTargetFinishedEvent = true;

                // If there are any other started events waiting and we are the first message, show them
                DisplayDeferredStartedEvents(targetStartedEvent.ProjectBuildEventContext);

                WriteLinePrefix(targetStartedEvent.ProjectBuildEventContext, targetStartedEvent.TimeStamp, false);

                setColor(ConsoleColor.Cyan);

                ProjectStartedEventMinimumFields startedEvent = _buildEventManager.GetProjectStartedEvent(e);
                ErrorUtilities.VerifyThrow(startedEvent != null, "Project Started should not be null in deferred target started");
                string currentProjectFile = startedEvent.ProjectFile ?? string.Empty;

                string targetName;
                if (IsVerbosityAtLeast(LoggerVerbosity.Diagnostic) || _showEventId)
                {
                    targetName = ResourceUtilities.FormatResourceStringStripCodeAndKeyword("TargetMessageWithId", targetStartedEvent.TargetName, targetStartedEvent.ProjectBuildEventContext.TargetId);
                }
                else
                {
                    targetName = targetStartedEvent.TargetName;
                }

                if (IsVerbosityAtLeast(LoggerVerbosity.Detailed))
                {
                    if (String.Equals(currentProjectFile, targetStartedEvent.TargetFile, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!String.IsNullOrEmpty(targetStartedEvent.ParentTarget))
                        {
                            WriteMessageAligned(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("TargetStartedProjectDepends", targetName, currentProjectFile, targetStartedEvent.ParentTarget), true);
                        }
                        else
                        {
                            WriteMessageAligned(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("TargetStartedProjectEntry", targetName, currentProjectFile), true);
                        }
                    }
                    else
                    {
                        if (!String.IsNullOrEmpty(targetStartedEvent.ParentTarget))
                        {
                            WriteMessageAligned(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("TargetStartedFileProjectDepends", targetName, targetStartedEvent.TargetFile, currentProjectFile, targetStartedEvent.ParentTarget), true);
                        }
                        else
                        {
                            WriteMessageAligned(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("TargetStartedFileProjectEntry", targetName, targetStartedEvent.TargetFile, currentProjectFile), true);
                        }
                    }
                }
                else
                {
                    WriteMessageAligned(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("TargetStartedFileProjectEntry", targetName, targetStartedEvent.TargetFile, currentProjectFile), true);
                }

                resetColor();
                ShownBuildEventContext(e);
            }
        }

        /// <summary>
        /// Will display the project started event which was deferred until the first visible message for the project is ready to be displayed
        /// </summary>
        private void DisplayDeferredProjectStartedEvent(BuildEventContext e)
        {
            if (showOnlyErrors || showOnlyWarnings) return;

            if (!SkipProjectStartedText)
            {
                // Get the project started event which matched the passed in event context
                ProjectStartedEventMinimumFields projectStartedEvent = _buildEventManager.GetProjectStartedEvent(e);

                // Make sure the project started event has not been show yet
                if (projectStartedEvent?.ShowProjectFinishedEvent == false)
                {
                    projectStartedEvent.ShowProjectFinishedEvent = true;

                    ProjectStartedEventMinimumFields parentStartedEvent = projectStartedEvent.ParentProjectStartedEvent;
                    if (parentStartedEvent != null)
                    {
                        // Make sure that if there are any events deferred on this event to show them first
                        DisplayDeferredStartedEvents(parentStartedEvent.ProjectBuildEventContext);
                    }

                    string current = projectStartedEvent.ProjectFile ?? string.Empty;
                    string previous = parentStartedEvent?.ProjectFile;
                    string targetNames = projectStartedEvent.TargetNames;

                    // Log 0-based node id's, where 0 is the parent. This is a little unnatural for the reader,
                    // but avoids confusion by being consistent with the Engine and any error messages it may produce.
                    int currentProjectNodeId = (projectStartedEvent.ProjectBuildEventContext.NodeId);
                    if (previous == null)
                    {
                        WriteLinePrefix(projectStartedEvent.FullProjectKey, projectStartedEvent.TimeStamp, false);
                        setColor(ConsoleColor.Cyan);
                        string message;
                        if (string.IsNullOrEmpty(targetNames))
                        {
                            message = ResourceUtilities.FormatResourceStringStripCodeAndKeyword("ProjectStartedTopLevelProjectWithDefaultTargets", current, currentProjectNodeId);
                        }
                        else
                        {
                            message = ResourceUtilities.FormatResourceStringStripCodeAndKeyword("ProjectStartedTopLevelProjectWithTargetNames", current, currentProjectNodeId, targetNames);
                        }

                        WriteMessageAligned(message, true);
                        resetColor();
                    }
                    else
                    {
                        WriteLinePrefix(parentStartedEvent.FullProjectKey, parentStartedEvent.TimeStamp, false);
                        setColor(ConsoleColor.Cyan);
                        if (string.IsNullOrEmpty(targetNames))
                        {
                            WriteMessageAligned(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("ProjectStartedWithDefaultTargetsMultiProc", previous, parentStartedEvent.FullProjectKey, current, projectStartedEvent.FullProjectKey, currentProjectNodeId), true);
                        }
                        else
                        {
                            WriteMessageAligned(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("ProjectStartedWithTargetsMultiProc", previous, parentStartedEvent.FullProjectKey, current, projectStartedEvent.FullProjectKey, currentProjectNodeId, targetNames), true);
                        }
                        resetColor();
                    }

                    // Make the last shown build event context to be null so that the next message will always print out the target name. If this is not null 
                    // then the first message after the project started event will not have the target name printed out which was causing some confusion.
                    ShownBuildEventContext(null);
                }
            }
        }

        /// <summary>
        /// Prints a custom event
        /// </summary>
        public override void CustomEventHandler(object sender, CustomBuildEventArgs e)
        {
            if (showOnlyErrors || showOnlyWarnings) return;

            ErrorUtilities.VerifyThrowArgumentNull(e.BuildEventContext, "BuildEventContext");
            if (IsVerbosityAtLeast(LoggerVerbosity.Detailed))
            {
                // ignore custom events with null messages -- some other
                // logger will handle them appropriately
                if (e.Message != null)
                {
                    DisplayDeferredStartedEvents(e.BuildEventContext);
                    WriteLinePrefix(e.BuildEventContext, e.Timestamp, false);
                    WriteMessageAligned(e.Message, true);
                    ShownBuildEventContext(e.BuildEventContext);
                }
            }
        }

        /// <summary>
        /// Writes message contextual information for each message displayed on the console
        /// </summary>
        private void WriteLinePrefix(BuildEventContext e, DateTime eventTimeStamp, bool isMessagePrefix)
        {
            WriteLinePrefix(GetFullProjectKey(e).ToString(Verbosity), eventTimeStamp, isMessagePrefix);
        }

        private void WriteLinePrefix(string key, DateTime eventTimeStamp, bool isMessagePrefix)
        {
            // Don't want any prefix for single proc
            if (NumberOfProcessors == 1)
            {
                return;
            }

            setColor(ConsoleColor.Cyan);

            string context = string.Empty;
            if (_showTimeStamp || IsVerbosityAtLeast(LoggerVerbosity.Diagnostic))
            {
                context = LogFormatter.FormatLogTimeStamp(eventTimeStamp);
            }

            string prefixString;

            if (!isMessagePrefix || IsVerbosityAtLeast(LoggerVerbosity.Detailed))
            {
                prefixString = ResourceUtilities.FormatResourceStringStripCodeAndKeyword("BuildEventContext", context, key) + ">";
            }
            else
            {
                prefixString = ResourceUtilities.FormatResourceStringStripCodeAndKeyword("BuildEventContext", context, string.Empty) + " ";
            }

            WritePretty(prefixString);
            resetColor();

            if (_prefixWidth == 0)
            {
                _prefixWidth = prefixString.Length;
            }
        }

        /// <summary>
        /// Extract the full project key from the BuildEventContext
        /// </summary>
        private ProjectFullKey GetFullProjectKey(BuildEventContext e)
        {
            ProjectStartedEventMinimumFields startedEvent = null;

            if (e != null)
            {
                startedEvent = _buildEventManager.GetProjectStartedEvent(e);
            }

            // Project started event can be null, if the message has come before the project started event
            // or the message is not part of a project such as if the message came from the engine
            return startedEvent == null
                ? new ProjectFullKey(0, 0)
                : new ProjectFullKey(startedEvent.ProjectKey, startedEvent.EntryPointKey);
        }

        /// <summary>
        /// Returns a performance counter for a given scope (either task name or target name)
        /// from the given table.
        /// </summary>
        /// <param name="scopeName">Task name or target name.</param>
        /// <param name="table">Table that has tasks or targets.</param>
        internal new static MPPerformanceCounter GetPerformanceCounter(string scopeName, ref Dictionary<string, PerformanceCounter> table)
        {
            // Lazily construct the performance counter table.
            if (table == null)
            {
                table = new Dictionary<string, PerformanceCounter>(StringComparer.OrdinalIgnoreCase);
            }

            // And lazily construct the performance counter itself.
            PerformanceCounter counter;
            if (!table.TryGetValue(scopeName, out counter))
            {
                counter = new MPPerformanceCounter(scopeName);
                table[scopeName] = counter;
            }

            return (MPPerformanceCounter)counter;
        }
        #endregion

        #region InternalClass
        /// <summary>
        /// Stores and calculates the performance numbers for the different events
        /// </summary>
        internal class MPPerformanceCounter : PerformanceCounter
        {
            // Set of performance counters for a project
            private Dictionary<string, PerformanceCounter> _internalPerformanceCounters;

            // Dictionary mapping event context to the start number of ticks, this will be used to calculate the amount
            // of time between the start of the performance counter and the end
            // An object is being used to box the start time long value to prevent jitting when this code path is executed.
            private Dictionary<BuildEventContext, object> _startedEvent;

            internal int MessageIndentLevel { get; set; } = 2;

            internal MPPerformanceCounter(string scopeName)
                : base(scopeName)
            {
                // Do Nothing
            }

            /// <summary>
            /// Add a started event to the performance counter, by adding the event this sets the start time of the performance counter
            /// </summary>
            internal void AddEventStarted(string projectTargetNames, BuildEventContext buildEventContext, DateTime eventTimeStamp, IEqualityComparer<BuildEventContext> comparer)
            {
                //If the projectTargetNames are set then we should be a project started event
                if (!string.IsNullOrEmpty(projectTargetNames))
                {
                    // Create a new performance counter for the project entry point to calculate how much time and how many calls
                    // were made to the entry point
                    MPPerformanceCounter entryPoint = GetPerformanceCounter(projectTargetNames, ref _internalPerformanceCounters);
                    entryPoint.AddEventStarted(null, buildEventContext, eventTimeStamp, s_compareContextNodeIdTargetId);
                    // Indent the output so it is indented with respect to its parent project
                    entryPoint.MessageIndentLevel = 7;
                }

                if (_startedEvent == null)
                {
                    _startedEvent = comparer == null
                        ? new Dictionary<BuildEventContext, object>()
                        : new Dictionary<BuildEventContext, object>(comparer);
                }

                if (!_startedEvent.ContainsKey(buildEventContext))
                {
                    _startedEvent.Add(buildEventContext, eventTimeStamp.Ticks);
                    ++calls;
                }
            }

            /// <summary>
            ///  Add a finished event to the performance counter, so perf numbers can be calculated
            /// </summary>
            internal void AddEventFinished(string projectTargetNames, BuildEventContext buildEventContext, DateTime eventTimeStamp)
            {
                if (!string.IsNullOrEmpty(projectTargetNames))
                {
                    MPPerformanceCounter entryPoint = GetPerformanceCounter(projectTargetNames, ref _internalPerformanceCounters);
                    entryPoint.AddEventFinished(null, buildEventContext, eventTimeStamp);
                }

                ErrorUtilities.VerifyThrow(_startedEvent != null, "Cannot have finished counter without started counter. ");

                if (_startedEvent.TryGetValue(buildEventContext, out object time))
                {
                    // Calculate the amount of time spent in the event based on the time stamp of when
                    // the started event was created and when the finished event was created
                    elapsedTime += (TimeSpan.FromTicks(eventTimeStamp.Ticks - (long)time));
                    _startedEvent.Remove(buildEventContext);
                }
            }

            /// <summary>
            /// Print out the performance counter message
            /// </summary>
            internal override void PrintCounterMessage(WriteLinePrettyFromResourceDelegate WriteLinePrettyFromResource, ColorSetter setColor, ColorResetter resetColor)
            {
                // round: sub-millisecond values are not meaningful
                string time = String.Format(CultureInfo.CurrentCulture,
                       "{0,5}", Math.Round(elapsedTime.TotalMilliseconds, 0));

                WriteLinePrettyFromResource(
                    MessageIndentLevel,
                    "PerformanceLine",
                    time,
                    String.Format(CultureInfo.CurrentCulture, "{0,-40}" /* pad to 40 align left */, scopeName),
                    String.Format(CultureInfo.CurrentCulture, "{0,3}", calls));

                if (_internalPerformanceCounters?.Count > 0)
                {
                    // For each of the entry points in the project print out the performance numbers for them
                    foreach (var counter in _internalPerformanceCounters.Values)
                    {
                        setColor(ConsoleColor.White);
                        counter.PrintCounterMessage(WriteLinePrettyFromResource, setColor, resetColor);
                        resetColor();
                    }
                }
            }
        }
        #endregion

        #region internal MemberData
        private static readonly ComparerContextNodeId<BuildEventContext> s_compareContextNodeId = new ComparerContextNodeId<BuildEventContext>();
        private static readonly ComparerContextNodeIdTargetId<BuildEventContext> s_compareContextNodeIdTargetId = new ComparerContextNodeIdTargetId<BuildEventContext>();
        private BuildEventContext _lastDisplayedBuildEventContext;
        private int _bufferWidth = -1;
        private readonly object _lockObject = new Object();
        private int _prefixWidth = 0;
        private ProjectFullKey _lastProjectFullKey = new ProjectFullKey(-1, -1);
        private bool _alignMessages;
        private bool _forceNoAlign;
        private bool _showEventId;
        // According to the documentation for ENABLE_PROCESSED_OUTPUT tab width for the console is 8 characters
        private const string consoleTab = "        ";
        #endregion

        #region Per-build Members
        //Holds messages that were going to be shown before the project started event, buffer them until the project started event is shown
        private Dictionary<BuildEventContext, List<BuildMessageEventArgs>> _deferredMessages;
        private BuildEventManager _buildEventManager;
        //  Has the build started
        private bool _hasBuildStarted;
        private bool? _showCommandLine;
        private bool _showTimeStamp;
        #endregion
    }
}
