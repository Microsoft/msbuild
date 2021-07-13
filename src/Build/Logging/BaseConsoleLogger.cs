// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Microsoft.Build.Evaluation;
using Microsoft.Build.Framework;
using Microsoft.Build.Internal;
using Microsoft.Build.Shared;

using ColorSetter = Microsoft.Build.Logging.ColorSetter;
using ColorResetter = Microsoft.Build.Logging.ColorResetter;
using WriteHandler = Microsoft.Build.Logging.WriteHandler;

namespace Microsoft.Build.BackEnd.Logging
{
    #region Delegates
    internal delegate void WriteLinePrettyFromResourceDelegate(int indentLevel, string resourceString, params object[] args);
    #endregion

    internal abstract class BaseConsoleLogger : INodeLogger
    {
        /// <summary>
        /// When set, we'll try reading background color.
        /// </summary>
        private static bool _supportReadingBackgroundColor = true;

        #region Properties

        /// <summary>
        /// Some platforms do not allow getting current background color. There
        /// is not way to check, but not-supported exception is thrown. Assume
        /// black, but don't crash.
        /// </summary>
        internal static ConsoleColor BackgroundColor
        {
            get
            {
                if (_supportReadingBackgroundColor)
                {
                    try
                    {
                        return Console.BackgroundColor;
                    }
                    catch (PlatformNotSupportedException)
                    {
                        _supportReadingBackgroundColor = false;
                    }
                }

                return ConsoleColor.Black;
            }
        }

        /// <summary>
        /// Gets or sets the level of detail to show in the event log.
        /// </summary>
        /// <value>Verbosity level.</value>
        public LoggerVerbosity Verbosity { get; set; } = LoggerVerbosity.Normal;

        /// <summary>
        /// Gets or sets the number of MSBuild processes participating in the build. If greater than 1,
        /// include the node ID
        /// </summary>
        public int NumberOfProcessors { get; set; } = 1;

        /// <summary>
        /// The console logger takes a single parameter to suppress the output of the errors
        /// and warnings summary at the end of a build.
        /// </summary>
        /// <value>null</value>
        public string Parameters { get; set; } = null;

        /// <summary>
        /// Suppresses the display of project headers. Project headers are
        /// displayed by default unless this property is set.
        /// </summary>
        /// <remarks>This is only needed by the IDE logger.</remarks>
        internal bool SkipProjectStartedText { get; set; } = false;

        /// <summary>
        /// Suppresses the display of error and warnings summary.
        /// If null, user has made no indication.
        /// </summary>
        internal bool? ShowSummary { get; set; }

        /// <summary>
        /// Provide access to the write hander delegate so that it can be redirected
        /// if necessary (e.g. to a file)
        /// </summary>
        protected internal WriteHandler WriteHandler { get; set; }

        #endregion

        /// <summary>
        /// Parses out the logger parameters from the Parameters string.
        /// </summary>
        public void ParseParameters()
        {
            if (Parameters == null) return;

            foreach (string parameter in Parameters.Split(parameterDelimiters))
            {
                if (string.IsNullOrWhiteSpace(parameter)) continue;

                string[] parameterAndValue = parameter.Split(s_parameterValueSplitCharacter);
                ApplyParameter(parameterAndValue[0], parameterAndValue.Length > 1 ? parameterAndValue[1] : null);
            }
        }

        /// <summary>
        /// An implementation of IComparer useful for comparing the keys
        /// on DictionaryEntry's
        /// </summary>
        /// <remarks>Uses CurrentCulture for display purposes</remarks>
        internal class DictionaryEntryKeyComparer : IComparer<DictionaryEntry>
        {
            public int Compare(DictionaryEntry a, DictionaryEntry b)
            {
                return string.Compare((string) a.Key, (string) b.Key, StringComparison.CurrentCultureIgnoreCase);
            }
        }

        /// <summary>
        /// An implementation of IComparer useful for comparing the ItemSpecs
        /// on ITaskItem's
        /// </summary>
        /// <remarks>Uses CurrentCulture for display purposes</remarks>
        internal class ITaskItemItemSpecComparer : IComparer
        {
            public int Compare(Object a, Object b)
            {
                return String.Compare(
                    ((ITaskItem)a).ItemSpec,
                    ((ITaskItem)b).ItemSpec,
                    StringComparison.CurrentCultureIgnoreCase);
            }
        }

        /// <summary>
        /// Indents the given string by the specified number of spaces.
        /// </summary>
        /// <param name="s">String to indent.</param>
        /// <param name="indent">Depth to indent.</param>
        internal string IndentString(string s, int indent)
        {
            // It's possible the event has a null message
            if (s == null) return string.Empty;

            // This will never return an empty array.  The returned array will always
            // have at least one non-null element, even if "s" is totally empty.
            String[] subStrings = SplitStringOnNewLines(s);

            StringBuilder result = new StringBuilder(
                (subStrings.Length * indent) +
                (subStrings.Length * Environment.NewLine.Length) +
                s.Length);

            for (int i = 0; i < subStrings.Length; i++)
            {
                result.Append(' ', indent).Append(subStrings[i]);
                result.AppendLine();
            }

            return result.ToString();
        }

        /// <summary>
        /// Splits strings on 'newLines' with tolerance for Everett and Dogfood builds.
        /// </summary>
        /// <param name="s">String to split.</param>
        internal static string[] SplitStringOnNewLines(string s)
        {
            string[] subStrings = s.Split(newLines, StringSplitOptions.None);
            return subStrings;
        }

        /// <summary>
        /// Writes a newline to the log.
        /// </summary>
        internal void WriteNewLine()
        {
            WriteHandler(Environment.NewLine);
        }

        /// <summary>
        /// Writes a line from a resource string to the log, using the default indentation.
        /// </summary>
        /// <param name="resourceString"></param>
        /// <param name="args"></param>
        internal void WriteLinePrettyFromResource(string resourceString, params object[] args)
        {
            int indentLevel = IsVerbosityAtLeast(LoggerVerbosity.Normal) ? this.currentIndentLevel : 0;
            WriteLinePrettyFromResource(indentLevel, resourceString, args);
        }

        /// <summary>
        /// Writes a line from a resource string to the log, using the specified indentation.
        /// </summary>
        internal void WriteLinePrettyFromResource(int indentLevel, string resourceString, params object[] args)
        {
            string formattedString = ResourceUtilities.FormatResourceStringStripCodeAndKeyword(resourceString, args);
            WriteLinePretty(indentLevel, formattedString);
        }

        /// <summary>
        /// Writes to the log, using the default indentation. Does not
        /// terminate with a newline.
        /// </summary>
        internal void WritePretty(string formattedString)
        {
            int indentLevel = IsVerbosityAtLeast(LoggerVerbosity.Normal) ? this.currentIndentLevel : 0;
            WritePretty(indentLevel, formattedString);
        }

        /// <summary>
        /// If requested, display a performance summary at the end of the build.  This
        /// shows how much time (and # hits) were spent inside of each project, target,
        /// and task.
        /// </summary>
        internal void ShowPerfSummary()
        {
            if (projectEvaluationPerformanceCounters != null)
            {
                setColor(ConsoleColor.Green);
                WriteNewLine();
                WriteLinePrettyFromResource("ProjectEvaluationPerformanceSummary");

                setColor(ConsoleColor.Gray);
                DisplayCounters(projectEvaluationPerformanceCounters);
            }

            // Show project performance summary.
            if (projectPerformanceCounters != null)
            {
                setColor(ConsoleColor.Green);
                WriteNewLine();
                WriteLinePrettyFromResource("ProjectPerformanceSummary");

                setColor(ConsoleColor.Gray);
                DisplayCounters(projectPerformanceCounters);
            }

            // Show target performance summary.
            if (targetPerformanceCounters != null)
            {
                setColor(ConsoleColor.Green);
                WriteNewLine();
                WriteLinePrettyFromResource("TargetPerformanceSummary");

                setColor(ConsoleColor.Gray);
                DisplayCounters(targetPerformanceCounters);
            }

            // Show task performance summary.
            if (taskPerformanceCounters != null)
            {
                setColor(ConsoleColor.Green);
                WriteNewLine();
                WriteLinePrettyFromResource("TaskPerformanceSummary");

                setColor(ConsoleColor.Gray);
                DisplayCounters(taskPerformanceCounters);
            }

            resetColor();
        }

        /// <summary>
        /// Writes to the log, using the specified indentation. Does not
        /// terminate with a newline.
        /// </summary>
        internal void WritePretty(int indentLevel, string formattedString)
        {
            StringBuilder result = new StringBuilder((indentLevel * tabWidth) + formattedString.Length);
            result.Append(' ', indentLevel * tabWidth).Append(formattedString);
            WriteHandler(result.ToString());
        }

        /// <summary>
        /// Writes a line to the log, using the default indentation.
        /// </summary>
        /// <param name="formattedString"></param>
        internal void WriteLinePretty(string formattedString)
        {
            int indentLevel = IsVerbosityAtLeast(LoggerVerbosity.Normal) ? currentIndentLevel : 0;
            WriteLinePretty(indentLevel, formattedString);
        }

        /// <summary>
        /// Writes a line to the log, using the specified indentation.
        /// </summary>
        internal void WriteLinePretty(int indentLevel, string formattedString)
        {
            indentLevel = indentLevel > 0 ? indentLevel : 0;
            WriteHandler(IndentString(formattedString, indentLevel * tabWidth));
        }

        /// <summary>
        /// Check to see what kind of device we are outputting the log to, is it a character device, a file, or something else
        /// this can be used by loggers to modify their outputs based on the device they are writing to
        /// </summary>
        internal void IsRunningWithCharacterFileType()
        {
            runningWithCharacterFileType = false;

            if (NativeMethodsShared.IsWindows)
            {
                // Get the std out handle
                IntPtr stdHandle = NativeMethodsShared.GetStdHandle(NativeMethodsShared.STD_OUTPUT_HANDLE);

                if (stdHandle != NativeMethods.InvalidHandle)
                {
                    uint fileType = NativeMethodsShared.GetFileType(stdHandle);

                    // The std out is a char type(LPT or Console)
                    runningWithCharacterFileType = (fileType == NativeMethodsShared.FILE_TYPE_CHAR);
                }
            }
        }

        /// <summary>
        /// Determines whether the current verbosity setting is at least the value
        /// passed in.
        /// </summary>
        internal bool IsVerbosityAtLeast(LoggerVerbosity checkVerbosity) => Verbosity >= checkVerbosity;

        /// <summary>
        /// Sets foreground color to color specified
        /// </summary>
        internal static void SetColor(ConsoleColor c)
        {
            try
            {
                Console.ForegroundColor = TransformColor(c, BackgroundColor);
            }
            catch (IOException)
            {
                // Does not matter if we cannot set the color
            }
        }

        /// <summary>
        /// Resets the color
        /// </summary>
        internal static void ResetColor()
        {
            try
            {
                Console.ResetColor();
            }
            catch (IOException)
            {
                // The color could not be reset, no reason to crash
            }
        }

        /// <summary>
        /// Sets foreground color to color specified using ANSI escape codes
        /// </summary>
        /// <param name="c">foreground color</param>
        internal static void SetColorAnsi(ConsoleColor c)
        {
            string colorString = "\x1b[";
            switch (c)
            {
                case ConsoleColor.Black: colorString += "30"; break;
                case ConsoleColor.DarkBlue: colorString += "34"; break;
                case ConsoleColor.DarkGreen: colorString += "32"; break;
                case ConsoleColor.DarkCyan: colorString += "36"; break;
                case ConsoleColor.DarkRed: colorString += "31"; break;
                case ConsoleColor.DarkMagenta: colorString += "35"; break;
                case ConsoleColor.DarkYellow: colorString += "33"; break;
                case ConsoleColor.Gray: colorString += "37"; break;
                case ConsoleColor.DarkGray: colorString += "30;1"; break;
                case ConsoleColor.Blue: colorString += "34;1"; break;
                case ConsoleColor.Green: colorString += "32;1"; break;
                case ConsoleColor.Cyan: colorString += "36;1"; break;
                case ConsoleColor.Red: colorString += "31;1"; break;
                case ConsoleColor.Magenta: colorString += "35;1"; break;
                case ConsoleColor.Yellow: colorString += "33;1"; break;
                case ConsoleColor.White: colorString += "37;1"; break;
                default: colorString = ""; break;
            }
            if ("" != colorString)
            {
                colorString += "m";
                Console.Out.Write(colorString);
            }
        }

        /// <summary>
        /// Resets the color using ANSI escape codes
        /// </summary>
        internal static void ResetColorAnsi()
        {
            Console.Out.Write("\x1b[m");
        }

        /// <summary>
        /// Changes the foreground color to black if the foreground is the
        /// same as the background. Changes the foreground to white if the
        /// background is black.
        /// </summary>
        /// <param name="foreground">foreground color for black</param>
        /// <param name="background">current background</param>
        internal static ConsoleColor TransformColor(ConsoleColor foreground, ConsoleColor background)
        {
            ConsoleColor result = foreground; //typically do nothing ...

            if (foreground == background)
            {
                result = background != ConsoleColor.Black ? ConsoleColor.Black : ConsoleColor.Gray;
            }

            return result;
        }

        /// <summary>
        /// Does nothing, meets the ColorSetter delegate type
        /// </summary>
        /// <param name="c">foreground color (is ignored)</param>
        internal static void DontSetColor(ConsoleColor c)
        {
            // do nothing...
        }

        /// <summary>
        /// Does nothing, meets the ColorResetter delegate type
        /// </summary>
        internal static void DontResetColor()
        {
            // do nothing...
        }

        internal void InitializeConsoleMethods(LoggerVerbosity logverbosity, WriteHandler logwriter, ColorSetter colorSet, ColorResetter colorReset)
        {
            Verbosity = logverbosity;
            WriteHandler = logwriter;
            IsRunningWithCharacterFileType();
            // This is a workaround, because the Console class provides no way to check that a color
            // can actually be set or not. Color cannot be set if the console has been redirected
            // in certain ways (e.g. how BUILD.EXE does it)
            bool canSetColor = true;

            try
            {
                ConsoleColor c = BackgroundColor;
            }
            catch (IOException)
            {
                // If the attempt to set a color fails with an IO exception then it is
                // likely that the console has been redirected in a way that cannot
                // cope with color (e.g. BUILD.EXE) so don't try to do color again.
                canSetColor = false;
            }

            if (colorSet != null && canSetColor)
            {
                this.setColor = colorSet;
            }
            else
            {
                this.setColor = DontSetColor;
            }

            if (colorReset != null && canSetColor)
            {
                this.resetColor = colorReset;
            }
            else
            {
                this.resetColor = DontResetColor;
            }
        }

        /// <summary>
        /// Writes out the list of property names and their values.
        /// This could be done at any time during the build to show the latest
        /// property values, using the cached reference to the list from the
        /// appropriate ProjectStarted event.
        /// </summary>
        /// <param name="properties">List of properties</param>
        internal void WriteProperties(List<DictionaryEntry> properties)
        {
            if (Verbosity == LoggerVerbosity.Diagnostic && showItemAndPropertyList)
            {
                if (properties.Count == 0)
                {
                    return;
                }

                OutputProperties(properties);
                // Add a blank line
                WriteNewLine();
            }
        }

        /// <summary>
        /// Writes out the environment as seen on build started.
        /// </summary>
        internal void WriteEnvironment(IDictionary<string, string> environment)
        {
            if (environment == null || environment.Count == 0)
            {
                return;
            }

            if (Verbosity == LoggerVerbosity.Diagnostic || showEnvironment)
            {
                OutputEnvironment(environment);

                // Add a blank line
                WriteNewLine();
            }
        }

        /// <summary>
        /// Generate a list which contains the properties referenced by the properties
        /// enumerable object
        /// </summary>
        internal List<DictionaryEntry> ExtractPropertyList(IEnumerable properties)
        {
            // Gather a sorted list of all the properties.
            var list = new List<DictionaryEntry>(properties.FastCountOrZero());

            Internal.Utilities.EnumerateProperties(properties, kvp => list.Add(new DictionaryEntry(kvp.Key, kvp.Value)));

            list.Sort(new DictionaryEntryKeyComparer());
            return list;
        }

        /// <summary>
        /// Write the environment of the build as was captured on the build started event.
        /// </summary>
        internal virtual void OutputEnvironment(IDictionary<string, string> environment)
        {
            // Write the banner
            setColor(ConsoleColor.Green);
            WriteLinePretty(currentIndentLevel, ResourceUtilities.GetResourceString("EnvironmentHeader"));

            if (environment != null)
            {
                // Write each environment value one per line
                foreach (KeyValuePair<string, string> entry in environment)
                {
                    setColor(ConsoleColor.Gray);
                    WritePretty(String.Format(CultureInfo.CurrentCulture, "{0,-30} = ", entry.Key));
                    setColor(ConsoleColor.DarkGray);
                    WriteLinePretty(entry.Value);
                }
            }

            resetColor();
        }

        internal virtual void OutputProperties(List<DictionaryEntry> list)
        {
            // Write the banner
            setColor(ConsoleColor.Green);
            WriteLinePretty(currentIndentLevel, ResourceUtilities.GetResourceString("PropertyListHeader"));
            // Write each property name and its value, one per line
            foreach (DictionaryEntry prop in list)
            {
                setColor(ConsoleColor.Gray);
                WritePretty(String.Format(CultureInfo.CurrentCulture, "{0,-30} = ", prop.Key));
                setColor(ConsoleColor.DarkGray);
                WriteLinePretty(EscapingUtilities.UnescapeAll((string)prop.Value));
            }
            resetColor();
        }

        /// <summary>
        /// Writes out the list of item specs and their metadata.
        /// This could be done at any time during the build to show the latest
        /// items, using the cached reference to the list from the
        /// appropriate ProjectStarted event.
        /// </summary>
        internal void WriteItems(SortedList itemTypes)
        {
            if (Verbosity != LoggerVerbosity.Diagnostic || !showItemAndPropertyList || itemTypes.Count == 0) return;

            // Write the banner
            setColor(ConsoleColor.Green);
            WriteLinePretty(currentIndentLevel, ResourceUtilities.GetResourceString("ItemListHeader"));

            // Write each item type and its itemspec, one per line
            foreach (DictionaryEntry entry in itemTypes)
            {
                string itemType = (string) entry.Key;
                ArrayList itemTypeList = (ArrayList) entry.Value;

                if (itemTypeList.Count == 0)
                {
                    continue;
                }

                // Sort the list by itemSpec
                itemTypeList.Sort(new ITaskItemItemSpecComparer());
                OutputItems(itemType, itemTypeList);
            }

            // Add a blank line
            WriteNewLine();
        }

        /// <summary>
        /// Extract the Items from the enumerable object and return a sorted list containing these items
        /// </summary>
        internal SortedList ExtractItemList(IEnumerable items)
        {
            // The "items" list is a flat list of itemtype-ITaskItem pairs.
            // We would like to organize the ITaskItems into groups by itemtype.

            // Use a SortedList instead of an ArrayList (because we need to lookup fast)
            // and instead of a Hashtable (because we need to sort it)
            SortedList itemTypes = new SortedList(CaseInsensitiveComparer.Default);

            Internal.Utilities.EnumerateItems(items, item =>
            {
                string key = (string)item.Key;
                var bucket = itemTypes[key] as ArrayList;
                if (bucket == null)
                {
                    bucket = new ArrayList();
                    itemTypes[key] = bucket;
                }

                bucket.Add(item.Value);
            });

            return itemTypes;
        }

        /// <summary>
        /// Dump the initial items provided.
        /// Overridden in ParallelConsoleLogger.
        /// </summary>
        internal virtual void OutputItems(string itemType, ArrayList itemTypeList)
        {
            WriteItemType(itemType);

            foreach (var item in itemTypeList)
            {
                string itemSpec = item switch
                {
                    ITaskItem taskItem => taskItem.ItemSpec,
                    IItem iitem => iitem.EvaluatedInclude,
                    { } misc => Convert.ToString(misc),
                    null => "null"
                };

                WriteItemSpec(itemSpec);

                var metadata = item switch
                {
                    IMetadataContainer metadataContainer => metadataContainer.EnumerateMetadata(),
                    IItem<ProjectMetadata> iitem => iitem.Metadata?.Select(m => new KeyValuePair<string, string>(m.Name, m.EvaluatedValue)),
                    _ => null
                };

                if (metadata != null)
                {
                    foreach (var metadatum in metadata)
                    {
                        WriteMetadata(metadatum.Key, metadatum.Value);
                    }
                }
            }

            resetColor();
        }

        protected virtual void WriteItemType(string itemType)
        {
            setColor(ConsoleColor.Gray);
            WriteLinePretty(itemType);
            setColor(ConsoleColor.DarkGray);
        }

        protected virtual void WriteItemSpec(string itemSpec)
        {
            WriteLinePretty("    " + itemSpec);
        }

        protected virtual void WriteMetadata(string name, string value)
        {
            WriteLinePretty("        " + name + " = " + value);
        }

        /// <summary>
        /// Returns a performance counter for a given scope (either task name or target name)
        /// from the given table.
        /// </summary>
        /// <param name="scopeName">Task name or target name.</param>
        /// <param name="table">Table that has tasks or targets.</param>
        /// <returns></returns>
        internal static PerformanceCounter GetPerformanceCounter(string scopeName, ref Dictionary<string, PerformanceCounter> table)
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
                counter = new PerformanceCounter(scopeName);
                table[scopeName] = counter;
            }

            return counter;
        }

        /// <summary>
        /// Display the timings for each counter in the dictionary.
        /// </summary>
        /// <param name="counters"></param>
        internal void DisplayCounters(Dictionary<string, PerformanceCounter> counters)
        {
            ArrayList perfCounters = new ArrayList(counters.Values.Count);
            perfCounters.AddRange(counters.Values);

            perfCounters.Sort(PerformanceCounter.DescendingByElapsedTimeComparer);

            bool reentrantCounterExists = false;

            WriteLinePrettyFromResourceDelegate lineWriter = WriteLinePrettyFromResource;

            foreach (PerformanceCounter counter in perfCounters)
            {
                if (counter.ReenteredScope)
                {
                    reentrantCounterExists = true;
                }

                counter.PrintCounterMessage(lineWriter, setColor, resetColor);
            }

            if (reentrantCounterExists)
            {
                // display an explanation of why there was no value displayed
                WriteLinePrettyFromResource(4, "PerformanceReentrancyNote");
            }
        }

        /// <summary>
        /// Records performance information consumed by a task or target.
        /// </summary>
        internal class PerformanceCounter
        {
            protected string scopeName;
            protected int calls;
            protected TimeSpan elapsedTime = new TimeSpan(0);
            protected bool inScope;
            protected DateTime scopeStartTime;
            protected bool reenteredScope;

            /// <summary>
            /// Construct.
            /// </summary>
            /// <param name="scopeName"></param>
            internal PerformanceCounter(string scopeName)
            {
                this.scopeName = scopeName;
            }

            /// <summary>
            /// Name of the scope.
            /// </summary>
            internal string ScopeName => scopeName;

            /// <summary>
            /// Total number of calls so far.
            /// </summary>
            internal int Calls => calls;

            /// <summary>
            /// Total accumulated time so far.
            /// </summary>
            internal TimeSpan ElapsedTime => elapsedTime;

            /// <summary>
            /// Whether or not this scope was reentered. Timing information is not recorded in these cases.
            /// </summary>
            internal bool ReenteredScope => reenteredScope;

            /// <summary>
            /// Whether or not this task or target is executing right now.
            /// </summary>
            internal bool InScope
            {
                get { return inScope; }
                set
                {
                    if (!reenteredScope)
                    {
                        if (InScope && !value)
                        {
                            // Edge meaning scope is finishing.
                            inScope = false;

                            elapsedTime += (DateTime.Now - scopeStartTime);
                        }
                        else if (!InScope && value)
                        {
                            // Edge meaning scope is starting.
                            inScope = true;

                            ++calls;
                            scopeStartTime = DateTime.Now;
                        }
                        else
                        {
                            // Should only happen when a scope is reentrant.
                            // We don't track these numbers.
                            reenteredScope = true;
                        }
                    }
                }
            }

            internal virtual void PrintCounterMessage(WriteLinePrettyFromResourceDelegate writeLinePrettyFromResource, ColorSetter setColor, ColorResetter resetColor)
            {
                string time;
                if (!reenteredScope)
                {
                    // round: sub-millisecond values are not meaningful
                    time = String.Format(CultureInfo.CurrentCulture,
                        "{0,5}", Math.Round(elapsedTime.TotalMilliseconds, 0));
                }
                else
                {
                    // no value available; instead display an asterisk
                    time = "    *";
                }

                writeLinePrettyFromResource(
                    2,
                    "PerformanceLine",
                    time,
                    String.Format(CultureInfo.CurrentCulture, "{0,-40}" /* pad to 40 align left */, scopeName),
                    String.Format(CultureInfo.CurrentCulture, "{0,3}", calls));
            }

            /// <summary>
            /// Returns an IComparer that will put performance counters
            /// in descending order by elapsed time.
            /// </summary>
            internal static IComparer DescendingByElapsedTimeComparer => new DescendingByElapsedTime();

            /// <summary>
            /// Private IComparer class for sorting performance counters
            /// in descending order by elapsed time.
            /// </summary>
            internal class DescendingByElapsedTime : IComparer
            {
                /// <summary>
                /// Compare two PerformanceCounters.
                /// </summary>
                /// <param name="o1"></param>
                /// <param name="o2"></param>
                /// <returns></returns>
                public int Compare(object o1, object o2)
                {
                    PerformanceCounter p1 = (PerformanceCounter)o1;
                    PerformanceCounter p2 = (PerformanceCounter)o2;

                    // don't compare reentrant counters, time is incorrect
                    // and we want to sort them first
                    if (!p1.reenteredScope && !p2.reenteredScope)
                    {
                        return TimeSpan.Compare(p1.ElapsedTime, p2.ElapsedTime);
                    }
                    else if (p1.Equals(p2))
                    {
                        return 0;
                    }
                    else if (p1.reenteredScope && !p2.reenteredScope)
                    {
                        // p1 was reentrant; sort first
                        return -1;
                    }
                    else if (!p1.reenteredScope && p2.reenteredScope)
                    {
                        // p2 was reentrant; sort first
                        return 1;
                    }
                    else
                    {
                        // both reentrant; sort stably by another field to avoid throwing
                        return string.Compare(p1.ScopeName, p2.ScopeName, StringComparison.Ordinal);
                    }
                }
            }
        }

        #region eventHandlers

        public virtual void Shutdown()
        {
            // do nothing
        }

        internal abstract void ResetConsoleLoggerState();

        public virtual void Initialize(IEventSource eventSource, int nodeCount)
        {
            NumberOfProcessors = nodeCount;
            Initialize(eventSource);
        }

        /// <summary>
        /// Signs up the console logger for all build events.
        /// </summary>
        /// <param name="eventSource">Available events.</param>
        public virtual void Initialize(IEventSource eventSource)
        {
            // Always show perf summary for diagnostic verbosity.
            if (IsVerbosityAtLeast(LoggerVerbosity.Diagnostic))
            {
                this.showPerfSummary = true;
            }

            ParseParameters();

            showTargetOutputs = !String.IsNullOrEmpty(Environment.GetEnvironmentVariable("MSBUILDTARGETOUTPUTLOGGING"));

            if (showOnlyWarnings || showOnlyErrors)
            {
                if (ShowSummary == null)
                {
                    // By default don't show the summary when the showOnlyWarnings / showOnlyErrors is specified.
                    // However, if the user explicitly specified summary or nosummary, use that.
                    ShowSummary = false;
                }

                this.showPerfSummary = false;
            }

            // If not specifically instructed otherwise, show a summary in normal
            // and higher verbosities.
            if (ShowSummary == null && IsVerbosityAtLeast(LoggerVerbosity.Normal))
            {
                ShowSummary = true;
            }

            // Put this after reading the parameters, since it may want to initialize something
            // specially based on some parameter value. For example, choose whether to have a summary, based
            // on the verbosity.
            ResetConsoleLoggerState();

            // Event source is allowed to be null; this allows the logger to be wrapped by a class that wishes
            // to call its event handlers directly. The VS HostLogger does this.
            if (eventSource != null)
            {
                eventSource.BuildStarted += BuildStartedHandler;
                eventSource.BuildFinished +=BuildFinishedHandler;
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
                eventSource.StatusEventRaised += StatusEventHandler;

                bool logPropertiesAndItemsAfterEvaluation = Utilities.Traits.Instance.EscapeHatches.LogPropertiesAndItemsAfterEvaluation ?? true;
                if (logPropertiesAndItemsAfterEvaluation && eventSource is IEventSource4 eventSource4)
                {
                    eventSource4.IncludeEvaluationPropertiesAndItems();
                }
            }
        }

        /// <summary>
        /// Apply a logger parameter.
        /// parameterValue may be null, if there is no parameter value.
        /// </summary>
        internal virtual bool ApplyParameter(string parameterName, string parameterValue)
        {
            ErrorUtilities.VerifyThrowArgumentNull(parameterName, nameof(parameterName));

            switch (parameterName.ToUpperInvariant())
            {
                case "PERFORMANCESUMMARY":
                    showPerfSummary = true;
                    return true;
                case "NOPERFORMANCESUMMARY":
                    showPerfSummary = false;
                    return true;
                case "NOSUMMARY":
                    ShowSummary = false;
                    return true;
                case "SUMMARY":
                    ShowSummary = true;
                    return true;
                case "NOITEMANDPROPERTYLIST":
                    showItemAndPropertyList = false;
                    return true;
                case "WARNINGSONLY":
                    showOnlyWarnings = true;
                    return true;
                case "ERRORSONLY":
                    showOnlyErrors = true;
                    return true;
                case "SHOWENVIRONMENT":
                    showEnvironment = true;
                    return true;
                case "SHOWPROJECTFILE":
                    if (parameterValue == null)
                    {
                        showProjectFile = true;
                    }
                    else
                    {
                        if (parameterValue.Length == 0)
                        {
                            showProjectFile = true;
                        }
                        else
                        {
                            showProjectFile = (parameterValue.ToUpperInvariant()) switch
                            {
                                "TRUE" => true,
                                _ => false,
                            };
                        }
                    }

                    return true;
                case "V":
                case "VERBOSITY":
                    return ApplyVerbosityParameter(parameterValue);
            }

            return false;
        }

        /// <summary>
        /// Apply the verbosity value
        /// </summary>
        private bool ApplyVerbosityParameter(string parameterValue)
        {
            switch (parameterValue.ToUpperInvariant())
            {
                case "Q":
                case "QUIET":
                    Verbosity = LoggerVerbosity.Quiet;
                    return true;
                case "M":
                case "MINIMAL":
                    Verbosity = LoggerVerbosity.Minimal;
                    return true;
                case "N":
                case "NORMAL":
                    Verbosity = LoggerVerbosity.Normal;
                    return true;
                case "D":
                case "DETAILED":
                    Verbosity = LoggerVerbosity.Detailed;
                    return true;
                case "DIAG":
                case "DIAGNOSTIC":
                    Verbosity = LoggerVerbosity.Diagnostic;
                    return true;
                default:
                    string errorCode;
                    string helpKeyword;
                    string message = ResourceUtilities.FormatResourceStringStripCodeAndKeyword(out errorCode, out helpKeyword, "InvalidVerbosity", parameterValue);
                    throw new LoggerException(message, null, errorCode, helpKeyword);
            }
        }

        public abstract void BuildStartedHandler(object sender, BuildStartedEventArgs e);

        public abstract void BuildFinishedHandler(object sender, BuildFinishedEventArgs e);

        public abstract void ProjectStartedHandler(object sender, ProjectStartedEventArgs e);

        public abstract void ProjectFinishedHandler(object sender, ProjectFinishedEventArgs e);

        public abstract void TargetStartedHandler(object sender, TargetStartedEventArgs e);

        public abstract void TargetFinishedHandler(object sender, TargetFinishedEventArgs e);

        public abstract void TaskStartedHandler(object sender, TaskStartedEventArgs e);

        public abstract void TaskFinishedHandler(object sender, TaskFinishedEventArgs e);

        public abstract void ErrorHandler(object sender, BuildErrorEventArgs e);

        public abstract void WarningHandler(object sender, BuildWarningEventArgs e);

        public abstract void MessageHandler(object sender, BuildMessageEventArgs e);

        public abstract void CustomEventHandler(object sender, CustomBuildEventArgs e);

        public abstract void StatusEventHandler(object sender, BuildStatusEventArgs e);

        #endregion

        #region Internal member data

        /// <summary>
        /// Time the build started
        /// </summary>
        internal DateTime buildStarted;

        /// <summary>
        /// Delegate used to change text color.
        /// </summary>
        internal ColorSetter setColor = null;

        /// <summary>
        /// Delegate used to reset text color
        /// </summary>
        internal ColorResetter resetColor = null;

        /// <summary>
        /// Number of spaces that each level of indentation is worth
        /// </summary>
        internal const int tabWidth = 2;

        /// <summary>
        /// Keeps track of the current indentation level.
        /// </summary>
        internal int currentIndentLevel = 0;

        /// <summary>
        /// The kinds of newline breaks we expect.
        /// </summary>
        /// <remarks>Currently we're not supporting "\r".</remarks>
        internal static readonly string[] newLines = { "\r\n", "\n" };

        /// <summary>
        /// Visual separator for projects. Line length was picked arbitrarily.
        /// </summary>
        internal const string projectSeparatorLine =
                 "__________________________________________________";

        /// <summary>
        /// Console logger parameters delimiters.
        /// </summary>
        internal static readonly char[] parameterDelimiters = MSBuildConstants.SemicolonChar;

        /// <summary>
        /// Console logger parameter value split character.
        /// </summary>
        private static readonly char[] s_parameterValueSplitCharacter = MSBuildConstants.EqualsChar;

        /// <summary>
        /// When true, accumulate performance numbers.
        /// </summary>
        internal bool showPerfSummary = false;

        /// <summary>
        /// When true, show the list of item and property values at the start of each project
        /// </summary>
        internal bool showItemAndPropertyList = true;

        /// <summary>
        /// Should the target output items be displayed
        /// </summary>
        internal bool showTargetOutputs = false;

        /// <summary>
        /// When true, suppresses all messages except for warnings. (And possibly errors, if showOnlyErrors is true.)
        /// </summary>
        protected bool showOnlyWarnings;

        /// <summary>
        /// When true, suppresses all messages except for errors. (And possibly warnings, if showOnlyWarnings is true.)
        /// </summary>
        protected bool showOnlyErrors;

        /// <summary>
        /// When true the environment block supplied by the build started event should be printed out at the start of the build
        /// </summary>
        protected bool showEnvironment;

        /// <summary>
        /// When true, indicates that the logger should tack the project file onto the end of errors and warnings.
        /// </summary>
        protected bool showProjectFile = false;

        internal bool ignoreLoggerErrors = true;

        internal bool runningWithCharacterFileType = false;

        #region Per-build Members

        /// <summary>
        /// Number of errors encountered in this build
        /// </summary>
        internal int errorCount = 0;

        /// <summary>
        /// Number of warnings encountered in this build
        /// </summary>
        internal int warningCount = 0;

        /// <summary>
        /// A list of the errors that have occurred during this build.
        /// </summary>
        internal List<BuildErrorEventArgs> errorList;

        /// <summary>
        /// A list of the warnings that have occurred during this build.
        /// </summary>
        internal List<BuildWarningEventArgs> warningList;

        /// <summary>
        /// Accumulated project performance information.
        /// </summary>
        internal Dictionary<string, PerformanceCounter> projectPerformanceCounters;

        /// <summary>
        /// Accumulated target performance information.
        /// </summary>
        internal Dictionary<string, PerformanceCounter> targetPerformanceCounters;

        /// <summary>
        /// Accumulated task performance information.
        /// </summary>
        internal Dictionary<string, PerformanceCounter> taskPerformanceCounters;

        /// <summary>
        ///
        /// </summary>
        internal Dictionary<string, PerformanceCounter> projectEvaluationPerformanceCounters;

        #endregion

        #endregion
    }
}
