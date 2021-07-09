// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

using Microsoft.Build.Framework;
using Microsoft.Build.Shared;

using BaseConsoleLogger = Microsoft.Build.BackEnd.Logging.BaseConsoleLogger;
using SerialConsoleLogger = Microsoft.Build.BackEnd.Logging.SerialConsoleLogger;
using ParallelConsoleLogger = Microsoft.Build.BackEnd.Logging.ParallelConsoleLogger;

namespace Microsoft.Build.Logging
{
    #region Delegates

    /// <summary>
    /// Delegate to use for writing a string to some location like
    /// the console window or the IDE build window.
    /// </summary>
    /// <param name="message"></param>
    public delegate void WriteHandler(string message);

    /// <summary>
    /// Type of delegate used to set console color.
    /// </summary>
    /// <param name="color">Text color</param>
    public delegate void ColorSetter(ConsoleColor color);

    /// <summary>
    /// Type of delegate used to reset console color.
    /// </summary>
    public delegate void ColorResetter();

    #endregion

    /// <summary>
    /// This class implements the default logger that outputs event data
    /// to the console (stdout). 
    /// It is a facade: it creates, wraps and delegates to a kind of BaseConsoleLogger, 
    /// either SerialConsoleLogger or ParallelConsoleLogger.
    /// </summary>
    /// <remarks>This class is not thread safe.</remarks>
    public class ConsoleLogger : INodeLogger
    {
        private BaseConsoleLogger _consoleLogger;
        private int _numberOfProcessors = 1;
        private LoggerVerbosity _verbosity;
        private WriteHandler _write;
        private ColorSetter _colorSet;
        private ColorResetter _colorReset;
        private string _parameters;
        private bool _skipProjectStartedText = false;
        private bool? _showSummary;

        #region Constructors

        /// <summary>
        /// Default constructor.
        /// </summary>
        public ConsoleLogger()
            : this(LoggerVerbosity.Normal)
        {
            // do nothing
        }

        /// <summary>
        /// Create a logger instance with a specific verbosity.  This logs to
        /// the default console.
        /// </summary>
        /// <param name="verbosity">Verbosity level.</param>
        public ConsoleLogger(LoggerVerbosity verbosity) :
            this(verbosity, Console.Out.Write, BaseConsoleLogger.SetColor, BaseConsoleLogger.ResetColor)
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
        public ConsoleLogger
        (
            LoggerVerbosity verbosity,
            WriteHandler write,
            ColorSetter colorSet,
            ColorResetter colorReset
        )
        {
            _verbosity = verbosity;
            _write = write;
            _colorSet = colorSet;
            _colorReset = colorReset;
        }

        /// <summary>
        /// This is called by every event handler for compat reasons -- see DDB #136924
        /// However it will skip after the first call
        /// </summary>
        private void InitializeBaseConsoleLogger()
        {
            if (_consoleLogger != null) return;

            bool useMPLogger = false;
            bool disableConsoleColor = false;
            bool forceConsoleColor = false;
            if (!string.IsNullOrEmpty(_parameters))
            {
                string[] parameterComponents = _parameters.Split(BaseConsoleLogger.parameterDelimiters);
                foreach (string param in parameterComponents)
                {
                    if (param.Length <= 0) continue;

                    if (string.Equals(param, "ENABLEMPLOGGING", StringComparison.OrdinalIgnoreCase))
                    {
                        useMPLogger = true;
                    }
                    if (string.Equals(param, "DISABLEMPLOGGING", StringComparison.OrdinalIgnoreCase))
                    {
                        useMPLogger = false;
                    }
                    if (string.Equals(param, "DISABLECONSOLECOLOR", StringComparison.OrdinalIgnoreCase))
                    {
                        disableConsoleColor = true;
                    }
                    if (string.Equals(param, "FORCECONSOLECOLOR", StringComparison.OrdinalIgnoreCase))
                    {
                        forceConsoleColor = true;
                    }
                }
            }

            if (forceConsoleColor)
            {
                _colorSet = BaseConsoleLogger.SetColorAnsi;
                _colorReset = BaseConsoleLogger.ResetColorAnsi;
            }
            else if (disableConsoleColor)
            {
                _colorSet = BaseConsoleLogger.DontSetColor;
                _colorReset = BaseConsoleLogger.DontResetColor;
            }

            if (_numberOfProcessors == 1 && !useMPLogger)
            {
                _consoleLogger = new SerialConsoleLogger(_verbosity, _write, _colorSet, _colorReset);
            }
            else
            {
                _consoleLogger = new ParallelConsoleLogger(_verbosity, _write, _colorSet, _colorReset);
            }

            if (_showSummary != null)
            {
                _consoleLogger.ShowSummary = _showSummary;
            }

            if (!string.IsNullOrEmpty(_parameters))
            {
                _consoleLogger.Parameters = _parameters;
                _parameters = null;
            }

            _consoleLogger.SkipProjectStartedText = _skipProjectStartedText;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the level of detail to show in the event log.
        /// </summary>
        /// <value>Verbosity level.</value>
        public LoggerVerbosity Verbosity
        {
            get
            {
                return _consoleLogger?.Verbosity ?? _verbosity;
            }

            set
            {
                if (_consoleLogger == null)
                {
                    _verbosity = value;
                }
                else
                {
                    _consoleLogger.Verbosity = value;
                }
            }
        }

        /// <summary>
        /// A semi-colon delimited list of "key[=value]" parameter pairs.
        /// </summary>
        /// <value>null</value>
        public string Parameters
        {
            get
            {
                return _consoleLogger == null ? _parameters : _consoleLogger.Parameters;
            }

            set
            {
                if (_consoleLogger == null)
                {
                    _parameters = value;
                }
                else
                {
                    _consoleLogger.Parameters = value;
                }
            }
        }

        /// <summary>
        /// Suppresses the display of project headers. Project headers are
        /// displayed by default unless this property is set.
        /// </summary>
        /// <remarks>This is only needed by the IDE logger.</remarks>
        public bool SkipProjectStartedText
        {
            get
            {
                return _consoleLogger?.SkipProjectStartedText ?? _skipProjectStartedText;
            }

            set
            {
                if (_consoleLogger == null)
                {
                    _skipProjectStartedText = value;
                }
                else
                {
                    _consoleLogger.SkipProjectStartedText = value;
                }
            }
        }

        /// <summary>
        /// Suppresses the display of error and warnings summary.
        /// </summary>
        public bool ShowSummary
        {
            get
            {
                if (_consoleLogger == null)
                {
                    return _showSummary == true;
                }
                return _consoleLogger.ShowSummary == true;
            }

            set
            {
                if (_consoleLogger == null)
                {
                    _showSummary = value;
                }
                else
                {
                    _consoleLogger.ShowSummary = value;
                }
            }
        }

        /// <summary>
        /// Provide access to the write hander delegate so that it can be redirected
        /// if necessary (e.g. to a file)
        /// </summary>
        protected WriteHandler WriteHandler
        {
            get
            {
                return _consoleLogger == null ? _write : _consoleLogger.WriteHandler;
            }

            set
            {
                if (_consoleLogger == null)
                {
                    _write = value;
                }
                else
                {
                    _consoleLogger.WriteHandler = value;
                }
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Apply a parameter.
        /// NOTE: This method was public by accident in Whidbey, so it cannot be made internal now. It has 
        /// no good reason for being public.
        /// </summary>
        public void ApplyParameter(string parameterName, string parameterValue)
        {
            ErrorUtilities.VerifyThrowInvalidOperation(_consoleLogger != null, "MustCallInitializeBeforeApplyParameter");
            _consoleLogger.ApplyParameter(parameterName, parameterValue);
        }

        /// <summary>
        /// Signs up the console logger for all build events.
        /// </summary>
        /// <param name="eventSource">Available events.</param>
        public virtual void Initialize(IEventSource eventSource)
        {
            InitializeBaseConsoleLogger();
            _consoleLogger.Initialize(eventSource);
        }

        /// <summary>
        /// Initializes the logger.
        /// </summary>
        public virtual void Initialize(IEventSource eventSource, int nodeCount)
        {
            _numberOfProcessors = nodeCount;
            InitializeBaseConsoleLogger();
            _consoleLogger.Initialize(eventSource, nodeCount);
        }

        /// <summary>
        /// The console logger does not need to release any resources.
        /// This method does nothing.
        /// </summary>
        public virtual void Shutdown()
        {
            _consoleLogger?.Shutdown();
        }

        /// <summary>
        /// Handler for build started events
        /// </summary>
        /// <param name="sender">sender (should be null)</param>
        /// <param name="e">event arguments</param>
        public void BuildStartedHandler(object sender, BuildStartedEventArgs e)
        {
            InitializeBaseConsoleLogger(); // for compat: see DDB#136924

            _consoleLogger.BuildStartedHandler(sender, e);
        }

        /// <summary>
        /// Handler for build finished events
        /// </summary>
        /// <param name="sender">sender (should be null)</param>
        /// <param name="e">event arguments</param>
        public void BuildFinishedHandler(object sender, BuildFinishedEventArgs e)
        {
            InitializeBaseConsoleLogger(); // for compat: see DDB#136924

            _consoleLogger.BuildFinishedHandler(sender, e);
        }

        /// <summary>
        /// Handler for project started events
        /// </summary>
        /// <param name="sender">sender (should be null)</param>
        /// <param name="e">event arguments</param>
        public void ProjectStartedHandler(object sender, ProjectStartedEventArgs e)
        {
            InitializeBaseConsoleLogger(); // for compat: see DDB#136924

            _consoleLogger.ProjectStartedHandler(sender, e);
        }

        /// <summary>
        /// Handler for project finished events
        /// </summary>
        /// <param name="sender">sender (should be null)</param>
        /// <param name="e">event arguments</param>
        public void ProjectFinishedHandler(object sender, ProjectFinishedEventArgs e)
        {
            InitializeBaseConsoleLogger(); // for compat: see DDB#136924

            _consoleLogger.ProjectFinishedHandler(sender, e);
        }

        /// <summary>
        /// Handler for target started events
        /// </summary>
        /// <param name="sender">sender (should be null)</param>
        /// <param name="e">event arguments</param>
        public void TargetStartedHandler(object sender, TargetStartedEventArgs e)
        {
            InitializeBaseConsoleLogger(); // for compat: see DDB#136924

            _consoleLogger.TargetStartedHandler(sender, e);
        }

        /// <summary>
        /// Handler for target finished events
        /// </summary>
        /// <param name="sender">sender (should be null)</param>
        /// <param name="e">event arguments</param>
        public void TargetFinishedHandler(object sender, TargetFinishedEventArgs e)
        {
            InitializeBaseConsoleLogger(); // for compat: see DDB#136924

            _consoleLogger.TargetFinishedHandler(sender, e);
        }

        /// <summary>
        /// Handler for task started events
        /// </summary>
        /// <param name="sender">sender (should be null)</param>
        /// <param name="e">event arguments</param>
        public void TaskStartedHandler(object sender, TaskStartedEventArgs e)
        {
            InitializeBaseConsoleLogger(); // for compat: see DDB#136924

            _consoleLogger.TaskStartedHandler(sender, e);
        }

        /// <summary>
        /// Handler for task finished events
        /// </summary>
        /// <param name="sender">sender (should be null)</param>
        /// <param name="e">event arguments</param>
        public void TaskFinishedHandler(object sender, TaskFinishedEventArgs e)
        {
            InitializeBaseConsoleLogger(); // for compat: see DDB#136924

            _consoleLogger.TaskFinishedHandler(sender, e);
        }

        /// <summary>
        /// Prints an error event
        /// </summary>
        public void ErrorHandler(object sender, BuildErrorEventArgs e)
        {
            InitializeBaseConsoleLogger(); // for compat: see DDB#136924

            _consoleLogger.ErrorHandler(sender, e);
        }

        /// <summary>
        /// Prints a warning event
        /// </summary>
        public void WarningHandler(object sender, BuildWarningEventArgs e)
        {
            InitializeBaseConsoleLogger(); // for compat: see DDB#136924

            _consoleLogger.WarningHandler(sender, e);
        }

        /// <summary>
        /// Prints a message event
        /// </summary>
        public void MessageHandler(object sender, BuildMessageEventArgs e)
        {
            InitializeBaseConsoleLogger(); // for compat: see DDB#136924

            _consoleLogger.MessageHandler(sender, e);
        }

        /// <summary>
        /// Prints a custom event
        /// </summary>
        public void CustomEventHandler(object sender, CustomBuildEventArgs e)
        {
            InitializeBaseConsoleLogger(); // for compat: see DDB#136924

            _consoleLogger.CustomEventHandler(sender, e);
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
            if (Verbosity >= BaseConsoleLogger.ImportanceToMinimumVerbosity(MessageImportance.Low, out _))
            {
                return MessageImportance.Low;
            }
            else if (Verbosity >= BaseConsoleLogger.ImportanceToMinimumVerbosity(MessageImportance.Normal, out _))
            {
                return MessageImportance.Normal;
            }
            else if (Verbosity >= BaseConsoleLogger.ImportanceToMinimumVerbosity(MessageImportance.High, out _))
            {
                return MessageImportance.High;
            }
            return MessageImportance.High - 1;
        }

        #endregion
    }
}
