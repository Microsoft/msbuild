// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Collections;

using Microsoft.Build.Framework;
using Microsoft.Build.Shared;

using ColorSetter = Microsoft.Build.Logging.ColorSetter;
using ColorResetter = Microsoft.Build.Logging.ColorResetter;
using WriteHandler = Microsoft.Build.Logging.WriteHandler;

namespace Microsoft.Build.BackEnd.Logging
{
    internal class SerialConsoleLogger : BaseConsoleLogger
    {
        #region Constructors

        /// <summary>
        /// Default constructor.
        /// </summary>
        public SerialConsoleLogger()
            : this(LoggerVerbosity.Normal)
        {
            // do nothing
        }

        /// <summary>
        /// Create a logger instance with a specific verbosity.  This logs to
        /// the default console.
        /// </summary>
        /// <param name="verbosity">Verbosity level.</param>
        public SerialConsoleLogger(LoggerVerbosity verbosity)
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
        /// <param name="verbosity"></param>
        /// <param name="write"></param>
        /// <param name="colorSet"></param>
        /// <param name="colorReset"></param>
        public SerialConsoleLogger
        (
            LoggerVerbosity verbosity,
            WriteHandler write,
            ColorSetter colorSet,
            ColorResetter colorReset
        )
        {
            InitializeConsoleMethods(verbosity, write, colorSet, colorReset);
        }

        #endregion

        #region Methods

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
        }

        /// <summary>
        /// Handler for build started events
        /// </summary>
        /// <param name="sender">sender (should be null)</param>
        /// <param name="e">event arguments</param>
        public override void BuildStartedHandler(object sender, BuildStartedEventArgs e)
        {
            buildStarted = e.Timestamp;

            if (ShowSummary == true)
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
            // Show the performance summary if the verbosity is diagnostic or the user specifically asked for it
            // with a logger parameter.
            if (this.showPerfSummary)
            {
                ShowPerfSummary();
            }

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
                ShowErrorWarningSummary();

                if (IsVerbosityAtLeast(LoggerVerbosity.Normal))
                {
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
            }

            if (ShowSummary == true)
            {
                string timeElapsed = LogFormatter.FormatTimeSpan(e.Timestamp - buildStarted);

                WriteNewLine();
                WriteLinePrettyFromResource("TimeElapsed", timeElapsed);
            }

            ResetConsoleLoggerState();
        }

        /// <summary>
        /// At the end of the build, repeats the errors and warnings that occurred 
        /// during the build, and displays the error count and warning count.
        /// </summary>
        private void ShowErrorWarningSummary()
        {
            if (warningCount == 0 && errorCount == 0) return;

            // Make some effort to distinguish the summary from the previous output
            WriteNewLine();

            if (warningCount > 0)
            {
                setColor(ConsoleColor.Yellow);
                foreach (BuildWarningEventArgs warningEventArgs in warningList)
                {
                    WriteLinePretty(EventArgsFormatting.FormatEventMessage(warningEventArgs, showProjectFile));
                }
            }

            if (errorCount > 0)
            {
                setColor(ConsoleColor.Red);
                foreach (BuildErrorEventArgs errorEventArgs in errorList)
                {
                    WriteLinePretty(EventArgsFormatting.FormatEventMessage(errorEventArgs, showProjectFile));
                }
            }

            resetColor();
        }

        /// <summary>
        /// Handler for project started events
        /// </summary>
        /// <param name="sender">sender (should be null)</param>
        /// <param name="e">event arguments</param>
        public override void ProjectStartedHandler(object sender, ProjectStartedEventArgs e)
        {
            if (!contextStack.IsEmpty())
            {
                this.VerifyStack(contextStack.Peek().type == FrameType.Target, "Bad stack -- Top is project {0}", contextStack.Peek().ID);
            }

            // if verbosity is normal, detailed or diagnostic
            if (IsVerbosityAtLeast(LoggerVerbosity.Normal) && ShowSummary != false)
            {
                ShowDeferredMessages();

                // check for stack corruption
                if (!contextStack.IsEmpty())
                {
                    this.VerifyStack(contextStack.Peek().type == FrameType.Target, "Bad stack -- Top is target {0}", contextStack.Peek().ID);
                }

                contextStack.Push(new Frame(FrameType.Project,
                                            false, // message not yet displayed
                                            this.currentIndentLevel,
                                            e.ProjectFile,
                                            e.TargetNames,
                                            null,
                                            GetCurrentlyBuildingProjectFile()));
                WriteProjectStarted();
            }
            else
            {
                contextStack.Push(new Frame(FrameType.Project,
                                            false, // message not yet displayed
                                            this.currentIndentLevel,
                                            e.ProjectFile,
                                            e.TargetNames,
                                            null,
                                            GetCurrentlyBuildingProjectFile()));
            }

            if (this.showPerfSummary)
            {
                PerformanceCounter counter = GetPerformanceCounter(e.ProjectFile, ref projectPerformanceCounters);

                // Place the counter "in scope" meaning the project is executing right now.
                counter.InScope = true;
            }

            if (Verbosity == LoggerVerbosity.Diagnostic && showItemAndPropertyList)
            {
                if (e.Properties != null)
                {
                    var propertyList = ExtractPropertyList(e.Properties);
                    WriteProperties(propertyList);
                }

                if (e.Items != null)
                {
                    SortedList itemList = ExtractItemList(e.Items);
                    WriteItems(itemList);
                }
            }
        }

        /// <summary>
        /// Handler for project finished events
        /// </summary>
        /// <param name="sender">sender (should be null)</param>
        /// <param name="e">event arguments</param>
        public override void ProjectFinishedHandler(object sender, ProjectFinishedEventArgs e)
        {
            if (this.showPerfSummary)
            {
                PerformanceCounter counter = GetPerformanceCounter(e.ProjectFile, ref projectPerformanceCounters);

                // Place the counter "in scope" meaning the project is done executing right now.
                counter.InScope = false;
            }

            // if verbosity is detailed or diagnostic, 
            // or there was an error or warning
            if (contextStack.Peek().hasErrorsOrWarnings
                || (IsVerbosityAtLeast(LoggerVerbosity.Detailed)))
            {
                setColor(ConsoleColor.Cyan);

                if (IsVerbosityAtLeast(LoggerVerbosity.Normal))
                {
                    WriteNewLine();
                }

                WriteLinePretty(e.Message);

                resetColor();
            }

            Frame top = contextStack.Pop();

            this.VerifyStack(top.type == FrameType.Project, "Unexpected project frame {0}", top.ID);
            this.VerifyStack(top.ID == e.ProjectFile, "Project frame {0} expected, but was {1}.", e.ProjectFile, top.ID);
        }

        /// <summary>
        /// Handler for target started events
        /// </summary>
        /// <param name="sender">sender (should be null)</param>
        /// <param name="e">event arguments</param>
        public override void TargetStartedHandler(object sender, TargetStartedEventArgs e)
        {
            contextStack.Push(new Frame(FrameType.Target,
                                        false,
                                        this.currentIndentLevel,
                                        e.TargetName,
                                        null,
                                        e.TargetFile,
                                        GetCurrentlyBuildingProjectFile()));

            // if verbosity is detailed or diagnostic
            if (IsVerbosityAtLeast(LoggerVerbosity.Detailed))
            {
                WriteTargetStarted();
            }

            if (this.showPerfSummary)
            {
                PerformanceCounter counter = GetPerformanceCounter(e.TargetName, ref targetPerformanceCounters);

                // Place the counter "in scope" meaning the target is executing right now.
                counter.InScope = true;
            }

            // Bump up the overall number of indents, so that anything within this target will show up
            // indented.
            this.currentIndentLevel++;
        }

        /// <summary>
        /// Handler for target finished events
        /// </summary>
        /// <param name="sender">sender (should be null)</param>
        /// <param name="e">event arguments</param>
        public override void TargetFinishedHandler(object sender, TargetFinishedEventArgs e)
        {
            // Done with the target, so shift everything left again.
            this.currentIndentLevel--;

            if (this.showPerfSummary)
            {
                PerformanceCounter counter = GetPerformanceCounter(e.TargetName, ref targetPerformanceCounters);

                // Place the counter "in scope" meaning the target is done executing right now.
                counter.InScope = false;
            }

            bool targetHasErrorsOrWarnings = contextStack.Peek().hasErrorsOrWarnings;

            // if verbosity is diagnostic, 
            // or there was an error or warning and verbosity is normal or detailed
            if ((targetHasErrorsOrWarnings && (IsVerbosityAtLeast(LoggerVerbosity.Normal)))
                  || Verbosity == LoggerVerbosity.Diagnostic)
            {
                setColor(ConsoleColor.Cyan);

                if (showTargetOutputs)
                {
                    IEnumerable targetOutputs = e.TargetOutputs;

                    if (targetOutputs != null)
                    {
                        WriteLinePretty(ResourceUtilities.GetResourceString("TargetOutputItemsHeader"));
                        foreach (ITaskItem item in targetOutputs)
                        {
                            WriteLinePretty(ResourceUtilities.FormatResourceStringIgnoreCodeAndKeyword("TargetOutputItem", item.ItemSpec));
                        }
                    }
                }

                WriteLinePretty(e.Message);
                resetColor();
            }

            Frame top = contextStack.Pop();
            this.VerifyStack(top.type == FrameType.Target, "bad stack frame type");
            this.VerifyStack(top.ID == e.TargetName, "bad stack frame id");

            // set the value on the Project frame, for the ProjectFinished handler
            if (targetHasErrorsOrWarnings)
            {
                SetErrorsOrWarningsOnCurrentFrame();
            }
        }

        /// <summary>
        /// Handler for task started events
        /// </summary>
        /// <param name="sender">sender (should be null)</param>
        /// <param name="e">event arguments</param>
        public override void TaskStartedHandler(object sender, TaskStartedEventArgs e)
        {
            // if verbosity is detailed or diagnostic
            if (IsVerbosityAtLeast(LoggerVerbosity.Detailed))
            {
                setColor(ConsoleColor.Cyan);
                WriteLinePretty(e.Message);
                resetColor();
            }

            if (this.showPerfSummary)
            {
                PerformanceCounter counter = GetPerformanceCounter(e.TaskName, ref taskPerformanceCounters);

                // Place the counter "in scope" meaning the task is executing right now.
                counter.InScope = true;
            }

            // Bump up the overall number of indents, so that anything within this task will show up
            // indented.
            this.currentIndentLevel++;
        }

        /// <summary>
        /// Handler for task finished events
        /// </summary>
        /// <param name="sender">sender (should be null)</param>
        /// <param name="e">event arguments</param>
        public override void TaskFinishedHandler(object sender, TaskFinishedEventArgs e)
        {
            // Done with the task, so shift everything left again.
            this.currentIndentLevel--;

            if (this.showPerfSummary)
            {
                PerformanceCounter counter = GetPerformanceCounter(e.TaskName, ref taskPerformanceCounters);

                // Place the counter "in scope" meaning the task is done executing.
                counter.InScope = false;
            }

            // if verbosity is detailed or diagnostic
            if (IsVerbosityAtLeast(LoggerVerbosity.Detailed))
            {
                setColor(ConsoleColor.Cyan);
                WriteLinePretty(e.Message);
                resetColor();
            }
        }

        /// <summary>
        /// Prints an error event
        /// </summary>
        public override void ErrorHandler(object sender, BuildErrorEventArgs e)
        {
            errorCount++;
            SetErrorsOrWarningsOnCurrentFrame();
            ShowDeferredMessages();
            setColor(ConsoleColor.Red);
            WriteLinePretty(EventArgsFormatting.FormatEventMessage(e, showProjectFile));
            if (ShowSummary == true)
            {
                errorList.Add(e);
            }
            resetColor();
        }

        /// <summary>
        /// Prints a warning event
        /// </summary>
        public override void WarningHandler(object sender, BuildWarningEventArgs e)
        {
            warningCount++;
            SetErrorsOrWarningsOnCurrentFrame();
            ShowDeferredMessages();
            setColor(ConsoleColor.Yellow);
            WriteLinePretty(EventArgsFormatting.FormatEventMessage(e, showProjectFile));
            if (ShowSummary == true)
            {
                warningList.Add(e);
            }
            resetColor();
        }

        /// <summary>
        /// Prints a message event
        /// </summary>
        public override void MessageHandler(object sender, BuildMessageEventArgs e)
        {
            LoggerVerbosity minimumVerbosity = ImportanceToMinimumVerbosity(e.Importance, out bool lightenText);
            bool print = IsVerbosityAtLeast(minimumVerbosity);

            if (print)
            {
                ShowDeferredMessages();

                if (lightenText)
                {
                    setColor(ConsoleColor.DarkGray);
                }

                string nonNullMessage;

                // Include file information if present.
                if (e.File != null)
                {
                    nonNullMessage = EventArgsFormatting.FormatEventMessage(e, showProjectFile);
                }
                else
                {
                    // null messages are ok -- treat as blank line
                    nonNullMessage = e.Message ?? String.Empty;
                }

                WriteLinePretty(nonNullMessage);

                if (lightenText)
                {
                    resetColor();
                }
            }
        }

        /// <summary>
        /// Prints a custom event
        /// </summary>
        public override void CustomEventHandler(object sender, CustomBuildEventArgs e)
        {
            // if verbosity is detailed or diagnostic
            if (IsVerbosityAtLeast(LoggerVerbosity.Detailed))
            {
                // ignore custom events with null messages -- some other
                // logger will handle them appropriately
                if (e.Message != null)
                {
                    ShowDeferredMessages();
                    WriteLinePretty(e.Message);
                }
            }
        }

        public override void StatusEventHandler(object sender, BuildStatusEventArgs e)
        {
            if (e is ProjectEvaluationStartedEventArgs projectEvaluationStarted)
            {
                if (showPerfSummary)
                {
                    PerformanceCounter counter = GetPerformanceCounter(projectEvaluationStarted.ProjectFile, ref projectEvaluationPerformanceCounters);
                    counter.InScope = true;
                }
            }
            else if (e is ProjectEvaluationFinishedEventArgs projectEvaluationFinished)
            {
                if (showPerfSummary)
                {
                    PerformanceCounter counter = GetPerformanceCounter(projectEvaluationFinished.ProjectFile, ref projectEvaluationPerformanceCounters);
                    counter.InScope = false;
                }

                if (Verbosity == LoggerVerbosity.Diagnostic && showItemAndPropertyList)
                {
                    if (projectEvaluationFinished.Properties != null)
                    {
                        var propertyList = ExtractPropertyList(projectEvaluationFinished.Properties);
                        WriteProperties(propertyList);
                    }

                    if (projectEvaluationFinished.Items != null)
                    {
                        SortedList itemList = ExtractItemList(projectEvaluationFinished.Items);
                        WriteItems(itemList);
                    }
                }
            }
        }

        /// <summary>
        /// Writes project started messages.
        /// </summary>
        internal void WriteProjectStarted()
        {
            this.VerifyStack(!contextStack.IsEmpty(), "Bad project stack");

            //Pop the current project
            Frame outerMost = contextStack.Pop();

            this.VerifyStack(!outerMost.displayed, "Bad project stack on {0}", outerMost.ID);
            this.VerifyStack(outerMost.type == FrameType.Project, "Bad project stack");

            outerMost.displayed = true;
            contextStack.Push(outerMost);

            WriteProjectStartedText(outerMost.ID, outerMost.targetNames, outerMost.parentProjectFile,
                this.IsVerbosityAtLeast(LoggerVerbosity.Normal) ? outerMost.indentLevel : 0);
        }

        /// <summary>
        /// Displays the text for a project started message.
        /// </summary>
        /// <param name ="current">current project file</param>
        /// <param name ="previous">previous project file</param>
        /// <param name="targetNames">targets that are being invoked</param>
        /// <param name="indentLevel">indentation level</param>
        private void WriteProjectStartedText(string current, string targetNames, string previous, int indentLevel)
        {
            if (!SkipProjectStartedText)
            {
                setColor(ConsoleColor.Cyan);

                this.VerifyStack(current != null, "Unexpected null project stack");

                WriteLinePretty(projectSeparatorLine);

                if (previous == null)
                {
                    if (string.IsNullOrEmpty(targetNames))
                    {
                        WriteLinePrettyFromResource(indentLevel, "ProjectStartedPrefixForTopLevelProjectWithDefaultTargets", current);
                    }
                    else
                    {
                        WriteLinePrettyFromResource(indentLevel, "ProjectStartedPrefixForTopLevelProjectWithTargetNames", current, targetNames);
                    }
                }
                else
                {
                    if (string.IsNullOrEmpty(targetNames))
                    {
                        WriteLinePrettyFromResource(indentLevel, "ProjectStartedPrefixForNestedProjectWithDefaultTargets", previous, current);
                    }
                    else
                    {
                        WriteLinePrettyFromResource(indentLevel, "ProjectStartedPrefixForNestedProjectWithTargetNames", previous, current, targetNames);
                    }
                }

                // add a little bit of extra space
                WriteNewLine();

                resetColor();
            }
        }

        /// <summary>
        /// Writes target started messages.
        /// </summary>
        private void WriteTargetStarted()
        {
            Frame f = contextStack.Pop();
            f.displayed = true;
            contextStack.Push(f);

            setColor(ConsoleColor.Cyan);

            if (this.Verbosity == LoggerVerbosity.Diagnostic)
            {
                WriteLinePrettyFromResource(f.indentLevel, "TargetStartedFromFile", f.ID, f.file);
            }
            else
            {
                WriteLinePrettyFromResource(this.IsVerbosityAtLeast(LoggerVerbosity.Normal) ? f.indentLevel : 0,
                    "TargetStartedPrefix", f.ID);
            }

            resetColor();
        }

        /// <summary>
        /// Determines the currently building project file.
        /// </summary>
        /// <returns>name of project file currently being built</returns>
        private string GetCurrentlyBuildingProjectFile()
        {
            if (contextStack.IsEmpty())
            {
                return null;
            }

            Frame topOfStack = contextStack.Peek();

            // If the top of the stack is a TargetStarted event, then its parent project
            // file is the one we want.
            if (topOfStack.type == FrameType.Target)
            {
                return topOfStack.parentProjectFile;
            }
            // If the top of the stack is a ProjectStarted event, then its ID is the project
            // file we want.
            else if (topOfStack.type == FrameType.Project)
            {
                return topOfStack.ID;
            }
            else
            {
                ErrorUtilities.VerifyThrow(false, "Unexpected frame type.");
                return null;
            }
        }

        /// <summary>
        /// Displays project started and target started messages that
        /// are shown only when the associated project or target produces
        /// output.
        /// </summary>
        private void ShowDeferredMessages()
        {
            if (contextStack.IsEmpty())
            {
                return;
            }

            if (!contextStack.Peek().displayed)
            {
                Frame f = contextStack.Pop();

                ShowDeferredMessages();

                //push now, so that the stack is in a good state
                //for WriteProjectStarted() and WriteLinePretty()
                //because we use the stack to control indenting
                contextStack.Push(f);

                switch (f.type)
                {
                    case FrameType.Project:
                        WriteProjectStarted();
                        break;

                    case FrameType.Target:
                        // Only do things if we're at normal verbosity.  If
                        // we're at a higher verbosity, we can assume that all
                        // targets have already be printed.  If we're at lower
                        // verbosity we don't need to print at all.
                        ErrorUtilities.VerifyThrow(this.Verbosity < LoggerVerbosity.Detailed,
                            "This target should have already been printed at a higher verbosity.");

                        if (IsVerbosityAtLeast(LoggerVerbosity.Normal))
                        {
                            WriteTargetStarted();
                        }

                        break;

                    default:
                        ErrorUtilities.VerifyThrow(false, "Unexpected frame type.");
                        break;
                }
            }
        }

        /// <summary>
        /// Marks the current frame to indicate that an error or warning
        /// occurred during it.
        /// </summary>
        private void SetErrorsOrWarningsOnCurrentFrame()
        {
            // under unit test, there may not be frames on the stack
            if (contextStack.Count == 0)
            {
                return;
            }

            Frame frame = contextStack.Pop();
            frame.hasErrorsOrWarnings = true;
            contextStack.Push(frame);
        }

        /// <summary>
        /// Checks the condition passed in.  If it's false, it emits an error message to the console
        /// indicating that there's a problem with the console logger.  These "problems" should
        /// never occur in the real world after we ship, unless there's a bug in the MSBuild
        /// engine such that events aren't getting paired up properly.  So the messages don't
        /// really need to be localized here, since they're only for our own benefit, and have
        /// zero value to a customer.
        /// </summary>
        /// <param name="condition"></param>
        /// <param name="unformattedMessage"></param>
        /// <param name="args"></param>
        private void VerifyStack
            (
            bool condition,
            string unformattedMessage,
            params object[] args
            )
        {
            if (!condition && !ignoreLoggerErrors)
            {
                string errorMessage = "INTERNAL CONSOLE LOGGER ERROR. " + ResourceUtilities.FormatString(unformattedMessage, args);
                ErrorUtilities.ThrowInternalError(errorMessage);
            }
        }
        #endregion

        #region Supporting classes

        /// <summary>
        /// This enumeration represents the kinds of context that can be
        /// stored in the context stack.
        /// </summary>
        internal enum FrameType
        {
            Project,
            Target
        }

        /// <summary>
        /// This struct represents context information about a single
        /// target or project.
        /// </summary>
        internal struct Frame
        {
            /// <summary>
            /// Creates a new instance of frame with all fields specified.
            /// </summary>
            /// <param name="t">the type of the this frame</param>
            /// <param name="d">display state. true indicates this frame has been displayed to the user</param>
            /// <param name="indent">indentation level for this frame</param>
            /// <param name="s">frame id</param>
            /// <param name="targets">targets to execute, in the case of a project frame</param>
            /// <param name="fileOfTarget">the file name where the target is defined</param>
            /// <param name="parent">parent project file</param>
            internal Frame
                (
                FrameType t,
                bool d,
                int indent,
                string s,
                string targets,
                string fileOfTarget,
                string parent
                )
            {
                type = t;
                displayed = d;
                indentLevel = indent;
                ID = s;
                targetNames = targets;
                file = fileOfTarget;
                hasErrorsOrWarnings = false;
                parentProjectFile = parent;
            }

            /// <summary>
            /// Indicates if project or target frame.
            /// </summary>
            internal FrameType type;

            /// <summary>
            /// Set to true to indicate the user has seen a message about this frame.
            /// </summary>
            internal bool displayed;

            /// <summary>
            /// The number of tabstops to indent this event when it is eventually displayed.
            /// </summary>
            internal int indentLevel;

            /// <summary>
            /// A string associated with this frame -- should be a target name
            /// or a project file.
            /// </summary>
            internal string ID;

            /// <summary>
            /// For a TargetStarted or a ProjectStarted event, this field tells us
            /// the name of the *parent* project file that was responsible.
            /// </summary>
            internal string parentProjectFile;

            /// <summary>
            /// Stores the TargetNames from the ProjectStarted event. Null for Target frames.
            /// </summary>
            internal string targetNames;

            /// <summary>
            /// For TargetStarted events, this stores the filename where the Target is defined
            /// (e.g., Microsoft.Common.targets).  This is different than the project that is 
            /// being built.  
            /// For ProjectStarted events, this is null.
            /// </summary>
            internal string file;

            /// <summary>
            /// True if there were errors/warnings during the project or target frame.
            /// </summary>
            internal bool hasErrorsOrWarnings;
        }

        /// <summary>
        /// The FrameStack class represents a (lifo) stack of Frames.
        /// </summary>
        internal class FrameStack
        {
            /// <summary>
            /// The frames member is contained by FrameStack and does
            /// all the heavy lifting for FrameStack.
            /// </summary>
            private System.Collections.Stack _frames;

            /// <summary>
            /// Create a new, empty, FrameStack.
            /// </summary>
            internal FrameStack()
            {
                _frames = new System.Collections.Stack();
            }

            /// <summary>
            /// Remove and return the top element in the stack.
            /// </summary>
            /// <exception cref="InvalidOperationException">Thrown when stack is empty.</exception>
            internal Frame Pop()
            {
                return (Frame)(_frames.Pop());
            }

            /// <summary>
            /// Returns, but does not remove, the top of the stack.
            /// </summary>
            internal Frame Peek()
            {
                return (Frame)(_frames.Peek());
            }

            /// <summary>
            /// Push(f) adds f to the top of the stack.
            /// </summary>
            /// <param name="f">a frame to push</param>
            internal void Push(Frame f)
            {
                _frames.Push(f);
            }

            /// <summary>
            /// Constant property that indicates the number of elements
            /// in the stack.
            /// </summary>
            internal int Count
            {
                get
                {
                    return _frames.Count;
                }
            }

            /// <summary>
            /// s.IsEmpty() is true iff s.Count == 0
            /// </summary>
            internal bool IsEmpty()
            {
                return _frames.Count == 0;
            }
        }
        #endregion

        #region Private member data

        /// <summary>
        /// contextStack is the only interesting state in the console
        /// logger.  The context stack contains a sequence of frames
        /// denoting current and previous containing projects and targets
        /// </summary>
        internal FrameStack contextStack = new FrameStack();
        #endregion
    }
}
