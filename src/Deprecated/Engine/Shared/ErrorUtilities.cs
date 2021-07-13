// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;

namespace Microsoft.Build.BuildEngine.Shared
{
    /// <summary>
    /// This class contains methods that are useful for error checking and validation.
    /// </summary>
    /// <owner>RGoel, SumedhK</owner>
    internal static class ErrorUtilities
    {
        #region LaunchMsBuildDebuggerOnFatalError
        /// <summary>
        /// Will launch the msbuild debugger when the environment variable "MSBuildLaunchDebuggerOnFatalError" is set
        /// </summary>
        internal static void LaunchMsBuildDebuggerOnFatalError()
        {
            string msBuildLaunchDebuggerOnFatalError = Environment.GetEnvironmentVariable("MSBuildLaunchDebuggerOnFatalError");
            if (!String.IsNullOrEmpty(msBuildLaunchDebuggerOnFatalError))
            {
                Debugger.Launch();
            }
        }
        #endregion

        #region VerifyThrow -- for internal errors

        /// <summary>
        /// Puts up an assertion dialog in debug builds, and throws an exception in
        /// both debug and release builds. Since this is not a no-op in release builds,
        /// it should not be called repeatedly in performance-critical scenarios.
        ///
        /// PERF WARNING: calling a method that takes a variable number of arguments
        /// is expensive, because memory is allocated for the array of arguments -- do
        /// not call this method repeatedly in performance-critical scenarios
        /// </summary>
        /// <owner>RGoel, SumedhK</owner>
        /// <param name="showAssert"></param>
        /// <param name="unformattedMessage"></param>
        /// <param name="args"></param>
        private static void ThrowInternalError
        (
            bool showAssert,
            string unformattedMessage,
            params object[] args
        )
        {
            // We ignore showAssert:  we don't want to show the assert dialog no matter what.
            throw new InternalErrorException(ResourceUtilities.FormatString(unformattedMessage, args));
        }

        /// <summary>
        /// Puts up an assertion dialog in debug builds, and throws an internal error exception in
        /// both debug and release builds. Since this is not a no-op in release builds,
        /// it should not be called repeatedly in performance-critical scenarios.
        /// This is only for situations that would mean that there is a bug in MSBuild itself.
        /// </summary>
        internal static void ThrowInternalError(string message)
        {
            throw new InternalErrorException(message);
        }

        /// <summary>
        /// Throws an InternalErrorException if the given condition is false,
        /// without showing an assert dialog. Use this method only for conditions
        /// that mean bugs in MSBuild code (that is, not expected user exceptions).
        /// </summary>
        /// <param name="condition"></param>
        /// <param name="unformattedMessage"></param>
        internal static void VerifyThrowNoAssert
        (
            bool condition,
            string unformattedMessage
        )
        {
            if (!condition)
            {
                // PERF NOTE: explicitly passing null for the arguments array
                // prevents memory allocation
                ThrowInternalError(false, unformattedMessage, null);
            }
        }

        /// <summary>
        /// This method should be used in places where one would normally put
        /// an "assert". It should be used to validate that our assumptions are
        /// true, where false would indicate that there must be a bug in our
        /// code somewhere. This should not be used to throw errors based on bad
        /// user input or anything that the user did wrong.
        /// </summary>
        /// <owner>RGoel, SumedhK</owner>
        /// <param name="condition"></param>
        /// <param name="unformattedMessage"></param>
        internal static void VerifyThrow
        (
            bool condition,
            string unformattedMessage
        )
        {
            if (!condition)
            {
                // PERF NOTE: explicitly passing null for the arguments array
                // prevents memory allocation
                ThrowInternalError(true, unformattedMessage, null);
            }
        }

        /// <summary>
        /// Overload for one string format argument.
        /// </summary>
        /// <owner>SumedhK</owner>
        /// <param name="condition"></param>
        /// <param name="unformattedMessage"></param>
        /// <param name="arg0"></param>
        internal static void VerifyThrow
        (
            bool condition,
            string unformattedMessage,
            object arg0
        )
        {
            // PERF NOTE: check the condition here instead of pushing it into
            // the ThrowInternalError() method, because that method always
            // allocates memory for its variable array of arguments
            if (!condition)
            {
                ThrowInternalError(true, unformattedMessage, arg0);
            }
        }

        /// <summary>
        /// Overload for two string format arguments.
        /// </summary>
        /// <owner>SumedhK</owner>
        /// <param name="condition"></param>
        /// <param name="unformattedMessage"></param>
        /// <param name="arg0"></param>
        /// <param name="arg1"></param>
        internal static void VerifyThrowNoAssert
        (
            bool condition,
            string unformattedMessage,
            object arg0,
            object arg1
        )
        {
            // PERF NOTE: check the condition here instead of pushing it into
            // the ThrowInternalError() method, because that method always
            // allocates memory for its variable array of arguments
            if (!condition)
            {
                ThrowInternalError(false, unformattedMessage, arg0, arg1);
            }
        }

        /// <summary>
        /// Overload for two string format arguments.
        /// </summary>
        /// <owner>SumedhK</owner>
        /// <param name="condition"></param>
        /// <param name="unformattedMessage"></param>
        /// <param name="arg0"></param>
        /// <param name="arg1"></param>
        internal static void VerifyThrow
        (
            bool condition,
            string unformattedMessage,
            object arg0,
            object arg1
        )
        {
            // PERF NOTE: check the condition here instead of pushing it into
            // the ThrowInternalError() method, because that method always
            // allocates memory for its variable array of arguments
            if (!condition)
            {
                ThrowInternalError(true, unformattedMessage, arg0, arg1);
            }
        }

        /// <summary>
        /// Overload for three string format arguments.
        /// </summary>
        /// <owner>SumedhK</owner>
        /// <param name="condition"></param>
        /// <param name="unformattedMessage"></param>
        /// <param name="arg0"></param>
        /// <param name="arg1"></param>
        /// <param name="arg2"></param>
        internal static void VerifyThrow
        (
            bool condition,
            string unformattedMessage,
            object arg0,
            object arg1,
            object arg2
        )
        {
            // PERF NOTE: check the condition here instead of pushing it into
            // the ThrowInternalError() method, because that method always
            // allocates memory for its variable array of arguments
            if (!condition)
            {
                ThrowInternalError(true, unformattedMessage, arg0, arg1, arg2);
            }
        }

        /// <summary>
        /// Overload for four string format arguments.
        /// </summary>
        /// <owner>SumedhK</owner>
        /// <param name="condition"></param>
        /// <param name="unformattedMessage"></param>
        /// <param name="arg0"></param>
        /// <param name="arg1"></param>
        /// <param name="arg2"></param>
        /// <param name="arg3"></param>
        internal static void VerifyThrow
        (
            bool condition,
            string unformattedMessage,
            object arg0,
            object arg1,
            object arg2,
            object arg3
        )
        {
            // PERF NOTE: check the condition here instead of pushing it into
            // the ThrowInternalError() method, because that method always
            // allocates memory for its variable array of arguments
            if (!condition)
            {
                ThrowInternalError(true, unformattedMessage, arg0, arg1, arg2, arg3);
            }
        }

        #endregion

        #region VerifyThrowInvalidOperation

        /// <summary>
        /// Throws an InvalidOperationException.
        ///
        /// PERF WARNING: calling a method that takes a variable number of arguments
        /// is expensive, because memory is allocated for the array of arguments -- do
        /// not call this method repeatedly in performance-critical scenarios
        /// </summary>
        /// <owner>RGoel, SumedhK</owner>
        /// <param name="resourceName"></param>
        /// <param name="args"></param>
        private static void ThrowInvalidOperation
        (
            string resourceName,
            params object[] args
        )
        {
#if DEBUG
            ResourceUtilities.VerifyResourceStringExists(resourceName);
#endif
            throw new InvalidOperationException(ResourceUtilities.FormatResourceString(resourceName, args));
        }

        /// <summary>
        /// Throws an InvalidOperationException if the given condition is false.
        /// </summary>
        /// <owner>RGoel, SumedhK</owner>
        /// <param name="condition"></param>
        /// <param name="resourceName"></param>
        internal static void VerifyThrowInvalidOperation
        (
            bool condition,
            string resourceName
        )
        {
            if (!condition)
            {
                // PERF NOTE: explicitly passing null for the arguments array
                // prevents memory allocation
                ThrowInvalidOperation(resourceName, null);
            }
        }

        /// <summary>
        /// Overload for one string format argument.
        /// </summary>
        /// <owner>SumedhK</owner>
        /// <param name="condition"></param>
        /// <param name="resourceName"></param>
        /// <param name="arg0"></param>
        internal static void VerifyThrowInvalidOperation
        (
            bool condition,
            string resourceName,
            object arg0
        )
        {
            // PERF NOTE: check the condition here instead of pushing it into
            // the ThrowInvalidOperation() method, because that method always
            // allocates memory for its variable array of arguments
            if (!condition)
            {
                ThrowInvalidOperation(resourceName, arg0);
            }
        }

        /// <summary>
        /// Overload for two string format arguments.
        /// </summary>
        /// <owner>SumedhK</owner>
        /// <param name="condition"></param>
        /// <param name="resourceName"></param>
        /// <param name="arg0"></param>
        /// <param name="arg1"></param>
        internal static void VerifyThrowInvalidOperation
        (
            bool condition,
            string resourceName,
            object arg0,
            object arg1
        )
        {
            // PERF NOTE: check the condition here instead of pushing it into
            // the ThrowInvalidOperation() method, because that method always
            // allocates memory for its variable array of arguments
            if (!condition)
            {
                ThrowInvalidOperation(resourceName, arg0, arg1);
            }
        }

        /// <summary>
        /// Overload for three string format arguments.
        /// </summary>
        /// <owner>SumedhK</owner>
        /// <param name="condition"></param>
        /// <param name="resourceName"></param>
        /// <param name="arg0"></param>
        /// <param name="arg1"></param>
        /// <param name="arg2"></param>
        internal static void VerifyThrowInvalidOperation
        (
            bool condition,
            string resourceName,
            object arg0,
            object arg1,
            object arg2
        )
        {
            // PERF NOTE: check the condition here instead of pushing it into
            // the ThrowInvalidOperation() method, because that method always
            // allocates memory for its variable array of arguments
            if (!condition)
            {
                ThrowInvalidOperation(resourceName, arg0, arg1, arg2);
            }
        }

        #endregion

        #region VerifyThrowArgument

        /// <summary>
        /// Throws an ArgumentException that can include an inner exception.
        /// </summary>
        /// <remarks>
        /// This method is thread-safe.
        /// </remarks>
        /// <owner>RGoel, SumedhK</owner>
        /// <param name="innerException">Can be null.</param>
        /// <param name="resourceName"></param>
        /// <param name="args"></param>
        internal static void ThrowArgument
        (
            string resourceName,
            params object[] args
        )
        {
            ThrowArgument(null, resourceName, args);
        }

        /// <summary>
        /// Throws an ArgumentException that can include an inner exception.
        /// </summary>
        /// <remarks>
        /// This method is thread-safe.
        /// </remarks>
        /// <owner>RGoel, SumedhK</owner>
        /// <param name="innerException">Can be null.</param>
        /// <param name="resourceName"></param>
        /// <param name="args"></param>
        internal static void ThrowArgument
        (
            Exception innerException,
            string resourceName,
            params object[] args
        )
        {
#if DEBUG
            ResourceUtilities.VerifyResourceStringExists(resourceName);
#endif
            throw new ArgumentException(
                ResourceUtilities.FormatResourceString(resourceName, args),
                innerException);
        }

        /// <summary>
        /// Throws an ArgumentException if the given condition is false.
        /// </summary>
        /// <remarks>This method is thread-safe.</remarks>
        /// <owner>RGoel, SumedhK</owner>
        /// <param name="condition"></param>
        /// <param name="resourceName"></param>
        internal static void VerifyThrowArgument
        (
            bool condition,
            string resourceName
        )
        {
            VerifyThrowArgument(condition, null, resourceName);
        }

        /// <summary>
        /// Overload for one string format argument.
        /// </summary>
        /// <remarks>This method is thread-safe.</remarks>
        /// <owner>SumedhK</owner>
        /// <param name="condition"></param>
        /// <param name="resourceName"></param>
        /// <param name="arg0"></param>
        internal static void VerifyThrowArgument
        (
            bool condition,
            string resourceName,
            object arg0
        )
        {
            VerifyThrowArgument(condition, null, resourceName, arg0);
        }

        /// <summary>
        /// Overload for two string format arguments.
        /// </summary>
        /// <remarks>This method is thread-safe.</remarks>
        /// <owner>SumedhK</owner>
        /// <param name="condition"></param>
        /// <param name="resourceName"></param>
        /// <param name="arg0"></param>
        /// <param name="arg1"></param>
        internal static void VerifyThrowArgument
        (
            bool condition,
            string resourceName,
            object arg0,
            object arg1
        )
        {
            VerifyThrowArgument(condition, null, resourceName, arg0, arg1);
        }

        /// <summary>
        /// Throws an ArgumentException that includes an inner exception, if
        /// the given condition is false.
        /// </summary>
        /// <remarks>This method is thread-safe.</remarks>
        /// <owner>SumedhK</owner>
        /// <param name="condition"></param>
        /// <param name="innerException">Can be null.</param>
        /// <param name="resourceName"></param>
        internal static void VerifyThrowArgument
        (
            bool condition,
            Exception innerException,
            string resourceName
        )
        {
            if (!condition)
            {
                // PERF NOTE: explicitly passing null for the arguments array
                // prevents memory allocation
                ThrowArgument(innerException, resourceName, null);
            }
        }

        /// <summary>
        /// Overload for one string format argument.
        /// </summary>
        /// <remarks>This method is thread-safe.</remarks>
        /// <owner>SumedhK</owner>
        /// <param name="condition"></param>
        /// <param name="innerException"></param>
        /// <param name="resourceName"></param>
        /// <param name="arg0"></param>
        internal static void VerifyThrowArgument
        (
            bool condition,
            Exception innerException,
            string resourceName,
            object arg0
        )
        {
            // PERF NOTE: check the condition here instead of pushing it into
            // the ThrowArgument() method, because that method always allocates
            // memory for its variable array of arguments
            if (!condition)
            {
                ThrowArgument(innerException, resourceName, arg0);
            }
        }

        /// <summary>
        /// Overload for two string format arguments.
        /// </summary>
        /// <remarks>This method is thread-safe.</remarks>
        /// <owner>SumedhK</owner>
        /// <param name="condition"></param>
        /// <param name="innerException"></param>
        /// <param name="resourceName"></param>
        /// <param name="arg0"></param>
        /// <param name="arg1"></param>
        internal static void VerifyThrowArgument
        (
            bool condition,
            Exception innerException,
            string resourceName,
            object arg0,
            object arg1
        )
        {
            // PERF NOTE: check the condition here instead of pushing it into
            // the ThrowArgument() method, because that method always allocates
            // memory for its variable array of arguments
            if (!condition)
            {
                ThrowArgument(innerException, resourceName, arg0, arg1);
            }
        }

        #endregion

        #region VerifyThrowArgumentXXX

        /// <summary>
        /// Throws an ArgumentOutOfRangeException using the given parameter name
        /// if the condition is false.
        /// </summary>
        /// <owner>SumedhK</owner>
        /// <param name="condition"></param>
        /// <param name="parameterName"></param>
        internal static void VerifyThrowArgumentOutOfRange(bool condition, string parameterName)
        {
            if (!condition)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        /// <summary>
        /// Throws an ArgumentNullException if the given string parameter is null
        /// and ArgumentException if it has zero length.
        /// </summary>
        /// <owner>SumedhK</owner>
        /// <param name="parameter"></param>
        /// <param name="parameterName"></param>
        internal static void VerifyThrowArgumentLength(string parameter, string parameterName)
        {
            VerifyThrowArgumentNull(parameter, parameterName);

            if (parameter.Length == 0)
            {
                throw new ArgumentException(ResourceUtilities.FormatResourceString("Shared.ParameterCannotHaveZeroLength", parameterName));
            }
        }

        /// <summary>
        /// Throws an ArgumentNullException if the given parameter is null.
        /// </summary>
        /// <remarks>This method is thread-safe.</remarks>
        /// <owner>SumedhK</owner>
        /// <param name="parameter"></param>
        /// <param name="parameterName"></param>
        internal static void VerifyThrowArgumentNull(object parameter, string parameterName)
        {
            VerifyThrowArgumentNull(parameter, parameterName, "Shared.ParameterCannotBeNull");
        }

        /// <summary>
        /// Throws an ArgumentNullException if the given parameter is null.
        /// </summary>
        /// <remarks>This method is thread-safe.</remarks>
        /// <owner>SumedhK</owner>
        /// <param name="parameter"></param>
        /// <param name="parameterName"></param>
        internal static void VerifyThrowArgumentNull(object parameter, string parameterName, string resourceName)
        {
            if (parameter == null)
            {
                // Most ArgumentNullException overloads append its own rather clunky multi-line message.
                // So use the one overload that doesn't.
                throw new ArgumentNullException(
                    ResourceUtilities.FormatResourceString(resourceName, parameterName),
                    (Exception)null);
            }
        }

        /// <summary>
        /// Verifies the given arrays are not null and have the same length
        /// </summary>
        /// <param name="parameter1"></param>
        /// <param name="parameter2"></param>
        /// <param name="parameter1Name"></param>
        /// <param name="parameter2Name"></param>
        internal static void VerifyThrowArgumentArraysSameLength(Array parameter1, Array parameter2, string parameter1Name, string parameter2Name)
        {
            VerifyThrowArgumentNull(parameter1, parameter1Name);
            VerifyThrowArgumentNull(parameter2, parameter2Name);

            if (parameter1.Length != parameter2.Length)
            {
                throw new ArgumentException(ResourceUtilities.FormatResourceString("Shared.ParametersMustHaveTheSameLength", parameter1Name, parameter2Name));
            }
        }

        #endregion
    }
}
