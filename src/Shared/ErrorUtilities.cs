// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

#if BUILDINGAPPXTASKS
namespace Microsoft.Build.AppxPackage.Shared
#else
namespace Microsoft.Build.Shared
#endif
{
    /// <summary>
    /// This class contains methods that are useful for error checking and validation.
    /// </summary>
    internal static class ErrorUtilities
    {
        /// <summary>
        /// Emergency escape hatch. If a customer hits a bug in the shipped product causing an internal exception,
        /// and fortuitously it happens that ignoring the VerifyThrow allows execution to continue in a reasonable way,
        /// then we can give them this undocumented environment variable as an immediate workaround.
        /// </summary>
        private static readonly bool s_throwExceptions = String.IsNullOrEmpty(Environment.GetEnvironmentVariable("MSBUILDDONOTTHROWINTERNAL"));
        private static readonly bool s_enableMSBuildDebugTracing = !String.IsNullOrEmpty(Environment.GetEnvironmentVariable("MSBUILDENABLEDEBUGTRACING"));

        #region DebugTracing
        public static void DebugTraceMessage(string category, string formatstring, params object[] parameters)
        {
            if (s_enableMSBuildDebugTracing)
            {
                if (parameters != null)
                {
                    Trace.WriteLine(String.Format(CultureInfo.CurrentCulture, formatstring, parameters), category);
                }
                else
                {
                    Trace.WriteLine(formatstring, category);
                }
            }
        }
        #endregion

#if !BUILDINGAPPXTASKS
        #region VerifyThrow -- for internal errors

        internal static void VerifyThrowInternalError(bool condition, string message, params object[] args)
        {
            if (s_throwExceptions && !condition)
            {
                throw new InternalErrorException(ResourceUtilities.FormatString(message, args));
            }
        }

        /// <summary>
        /// Throws InternalErrorException. 
        /// This is only for situations that would mean that there is a bug in MSBuild itself.
        /// </summary>
        internal static void ThrowInternalError(string message, params object[] args)
        {
            if (s_throwExceptions)
            {
                throw new InternalErrorException(ResourceUtilities.FormatString(message, args));
            }
        }

        /// <summary>
        /// Throws InternalErrorException. 
        /// This is only for situations that would mean that there is a bug in MSBuild itself.
        /// </summary>
        internal static void ThrowInternalError(string message, Exception innerException, params object[] args)
        {
            if (s_throwExceptions)
            {
                throw new InternalErrorException(ResourceUtilities.FormatString(message, args), innerException);
            }
        }

        /// <summary>
        /// Throws InternalErrorException. 
        /// Indicates the code path followed should not have been possible.
        /// This is only for situations that would mean that there is a bug in MSBuild itself.
        /// </summary>
        internal static void ThrowInternalErrorUnreachable()
        {
            if (s_throwExceptions)
            {
                throw new InternalErrorException("Unreachable?");
            }
        }

        /// <summary>
        /// Throws InternalErrorException. 
        /// Indicates the code path followed should not have been possible.
        /// This is only for situations that would mean that there is a bug in MSBuild itself.
        /// </summary>
        internal static void VerifyThrowInternalErrorUnreachable(bool condition)
        {
            if (s_throwExceptions && !condition)
            {
                throw new InternalErrorException("Unreachable?");
            }
        }

        /// <summary>
        /// Throws InternalErrorException. 
        /// Indicates the code path followed should not have been possible.
        /// This is only for situations that would mean that there is a bug in MSBuild itself.
        /// </summary>
        internal static void ThrowIfTypeDoesNotImplementToString(object param)
        {
#if DEBUG
            // Check it has a real implementation of ToString()
            if (String.Equals(param.GetType().ToString(), param.ToString(), StringComparison.Ordinal))
            {
                ErrorUtilities.ThrowInternalError("This type does not implement ToString() properly {0}", param.GetType().FullName);
            }
#endif
        }

        /// <summary>
        /// Helper to throw an InternalErrorException when the specified parameter is null.
        /// This should be used ONLY if this would indicate a bug in MSBuild rather than
        /// anything caused by user action.
        /// </summary>
        /// <param name="parameter">The value of the argument.</param>
        /// <param name="parameterName">Parameter that should not be null</param>
        internal static void VerifyThrowInternalNull(object parameter, string parameterName)
        {
            if (parameter == null)
            {
                ThrowInternalError("{0} unexpectedly null", parameterName);
            }
        }

        /// <summary>
        /// Helper to throw an InternalErrorException when a lock on the specified object is not already held.
        /// This should be used ONLY if this would indicate a bug in MSBuild rather than
        /// anything caused by user action.
        /// </summary>
        /// <param name="locker">The object that should already have been used as a lock.</param>
        internal static void VerifyThrowInternalLockHeld(object locker)
        {
#if !CLR2COMPATIBILITY
            if (!Monitor.IsEntered(locker))
            {
                ThrowInternalError("Lock should already have been taken");
            }
#endif
        }

        /// <summary>
        /// Helper to throw an InternalErrorException when the specified parameter is null or zero length.
        /// This should be used ONLY if this would indicate a bug in MSBuild rather than
        /// anything caused by user action.
        /// </summary>
        /// <param name="parameterValue">The value of the argument.</param>
        /// <param name="parameterName">Parameter that should not be null or zero length</param>
        internal static void VerifyThrowInternalLength(string parameterValue, string parameterName)
        {
            VerifyThrowInternalNull(parameterValue, parameterName);

            if (parameterValue.Length == 0)
            {
                ThrowInternalError("{0} unexpectedly empty", parameterName);
            }
        }

        public static void VerifyThrowInternalLength<T>(T[] parameterValue, string parameterName)
        {
            VerifyThrowInternalNull(parameterValue, parameterName);

            if (parameterValue.Length == 0)
            {
                ThrowInternalError("{0} unexpectedly empty", parameterName);
            }
        }

        /// <summary>
        /// Helper to throw an InternalErrorException when the specified parameter is not a rooted path.
        /// This should be used ONLY if this would indicate a bug in MSBuild rather than
        /// anything caused by user action.
        /// </summary>
        /// <param name="value">Parameter that should be a rooted path</param>
        internal static void VerifyThrowInternalRooted(string value)
        {
            if (!Path.IsPathRooted(value))
            {
                ThrowInternalError("{0} unexpectedly not a rooted path", value);
            }
        }

        /// <summary>
        /// This method should be used in places where one would normally put
        /// an "assert". It should be used to validate that our assumptions are
        /// true, where false would indicate that there must be a bug in our
        /// code somewhere. This should not be used to throw errors based on bad
        /// user input or anything that the user did wrong.
        /// </summary>
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
                ThrowInternalError(unformattedMessage, null, null);
            }
        }

        /// <summary>
        /// Overload for one string format argument.
        /// </summary>
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
                ThrowInternalError(unformattedMessage, arg0);
            }
        }

        /// <summary>
        /// Overload for two string format arguments.
        /// </summary>
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
                ThrowInternalError(unformattedMessage, arg0, arg1);
            }
        }

        /// <summary>
        /// Overload for three string format arguments.
        /// </summary>
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
                ThrowInternalError(unformattedMessage, arg0, arg1, arg2);
            }
        }

        /// <summary>
        /// Overload for four string format arguments.
        /// </summary>
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
                ThrowInternalError(unformattedMessage, arg0, arg1, arg2, arg3);
            }
        }

        #endregion

        #region VerifyThrowInvalidOperation

        /// <summary>
        /// Throws an InvalidOperationException with the specified resource string
        /// </summary>
        /// <param name="resourceName">Resource to use in the exception</param>
        /// <param name="args">Formatting args.</param>
        internal static void ThrowInvalidOperation(string resourceName, params object[] args)
        {
#if DEBUG
            ResourceUtilities.VerifyResourceStringExists(resourceName);
#endif
            if (s_throwExceptions)
            {
                throw new InvalidOperationException(ResourceUtilities.FormatResourceStringStripCodeAndKeyword(resourceName, args));
            }
        }

        /// <summary>
        /// Throws an InvalidOperationException if the given condition is false.
        /// </summary>
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

        /// <summary>
        /// Overload for four string format arguments.
        /// </summary>
        /// <param name="condition"></param>
        /// <param name="resourceName"></param>
        /// <param name="arg0"></param>
        /// <param name="arg1"></param>
        /// <param name="arg2"></param>
        /// <param name="arg3"></param>
        internal static void VerifyThrowInvalidOperation
        (
            bool condition,
            string resourceName,
            object arg0,
            object arg1,
            object arg2,
            object arg3
        )
        {
            // PERF NOTE: check the condition here instead of pushing it into
            // the ThrowInvalidOperation() method, because that method always
            // allocates memory for its variable array of arguments
            if (!condition)
            {
                ThrowInvalidOperation(resourceName, arg0, arg1, arg2, arg3);
            }
        }

        #endregion

        #region VerifyThrowArgument

        /// <summary>
        /// Throws an ArgumentException that can include an inner exception.
        /// 
        /// PERF WARNING: calling a method that takes a variable number of arguments
        /// is expensive, because memory is allocated for the array of arguments -- do
        /// not call this method repeatedly in performance-critical scenarios
        /// </summary>
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
        /// 
        /// PERF WARNING: calling a method that takes a variable number of arguments
        /// is expensive, because memory is allocated for the array of arguments -- do
        /// not call this method repeatedly in performance-critical scenarios
        /// </summary>
        /// <remarks>
        /// This method is thread-safe.
        /// </remarks>
        /// <param name="innerException">Can be null.</param>
        /// <param name="resourceName"></param>
        /// <param name="args"></param>
        private static void ThrowArgument
        (
            Exception innerException,
            string resourceName,
            params object[] args
        )
        {
#if DEBUG
            ResourceUtilities.VerifyResourceStringExists(resourceName);
#endif
            if (s_throwExceptions)
            {
                throw new ArgumentException(ResourceUtilities.FormatResourceStringStripCodeAndKeyword(resourceName, args), innerException);
            }
        }

        /// <summary>
        /// Throws an ArgumentException if the given condition is false.
        /// </summary>
        /// <remarks>This method is thread-safe.</remarks>
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
        /// Overload for three string format arguments.
        /// </summary>
        /// <remarks>This method is thread-safe.</remarks>
        internal static void VerifyThrowArgument
        (
            bool condition,
            string resourceName,
            object arg0,
            object arg1,
            object arg2
        )
        {
            VerifyThrowArgument(condition, null, resourceName, arg0, arg1, arg2);
        }

        /// <summary>
        /// Overload for four string format arguments.
        /// </summary>
        /// <remarks>This method is thread-safe.</remarks>
        internal static void VerifyThrowArgument
        (
            bool condition,
            string resourceName,
            object arg0,
            object arg1,
            object arg2,
            object arg3
        )
        {
            VerifyThrowArgument(condition, null, resourceName, arg0, arg1, arg2, arg3);
        }

        /// <summary>
        /// Throws an ArgumentException that includes an inner exception, if
        /// the given condition is false.
        /// </summary>
        /// <remarks>This method is thread-safe.</remarks>
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

        /// <summary>
        /// Overload for three string format arguments.
        /// </summary>
        /// <remarks>This method is thread-safe.</remarks>
        internal static void VerifyThrowArgument
        (
            bool condition,
            Exception innerException,
            string resourceName,
            object arg0,
            object arg1,
            object arg2
        )
        {
            // PERF NOTE: check the condition here instead of pushing it into
            // the ThrowArgument() method, because that method always allocates
            // memory for its variable array of arguments
            if (!condition)
            {
                ThrowArgument(innerException, resourceName, arg0, arg1, arg2);
            }
        }

        /// <summary>
        /// Overload for four string format arguments.
        /// </summary>
        /// <remarks>This method is thread-safe.</remarks>
        internal static void VerifyThrowArgument
        (
            bool condition,
            Exception innerException,
            string resourceName,
            object arg0,
            object arg1,
            object arg2,
            object arg3
        )
        {
            // PERF NOTE: check the condition here instead of pushing it into
            // the ThrowArgument() method, because that method always allocates
            // memory for its variable array of arguments
            if (!condition)
            {
                ThrowArgument(innerException, resourceName, arg0, arg1, arg2, arg3);
            }
        }

        #endregion

        #region VerifyThrowArgumentXXX

        /// <summary>
        /// Throws an argument out of range exception.
        /// </summary>
        internal static void ThrowArgumentOutOfRange(string parameterName)
        {
            if (s_throwExceptions)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        /// <summary>
        /// Throws an ArgumentOutOfRangeException using the given parameter name
        /// if the condition is false.
        /// </summary>
        internal static void VerifyThrowArgumentOutOfRange(bool condition, string parameterName)
        {
            if (!condition)
            {
                ThrowArgumentOutOfRange(parameterName);
            }
        }

        /// <summary>
        /// Throws an ArgumentNullException if the given string parameter is null
        /// and ArgumentException if it has zero length.
        /// </summary>
        /// <param name="parameter"></param>
        /// <param name="parameterName"></param>
        internal static void VerifyThrowArgumentLength(string parameter, string parameterName)
        {
            VerifyThrowArgumentNull(parameter, parameterName);

            if (parameter.Length == 0 && s_throwExceptions)
            {
                throw new ArgumentException(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("Shared.ParameterCannotHaveZeroLength", parameterName));
            }
        }

#if !CLR2COMPATIBILITY
        /// <summary>
        /// Throws an ArgumentNullException if the given collection is null
        /// and ArgumentException if it has zero length.
        /// </summary>
        /// <param name="parameter"></param>
        /// <param name="parameterName"></param>
        internal static void VerifyThrowArgumentLength<T>(IReadOnlyCollection<T> parameter, string parameterName)
        {
            VerifyThrowArgumentNull(parameter, parameterName);

            if (parameter.Count == 0 && s_throwExceptions)
            {
                throw new ArgumentException(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("Shared.ParameterCannotHaveZeroLength", parameterName));
            }
        }

        /// <summary>
        /// Throws an ArgumentException if the given collection is not null but of zero length.
        /// </summary>
        /// <param name="parameter"></param>
        /// <param name="parameterName"></param>
        internal static void VerifyThrowArgumentLengthIfNotNull<T>(IReadOnlyCollection<T> parameter, string parameterName)
        {
            if (parameter?.Count == 0 && s_throwExceptions)
            {
                throw new ArgumentException(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("Shared.ParameterCannotHaveZeroLength", parameterName));
            }
        }
#endif
        
        /// <summary>
        /// Throws an ArgumentNullException if the given string parameter is null
        /// and ArgumentException if it has zero length.
        /// </summary>
        /// <param name="parameter"></param>
        /// <param name="parameterName"></param>
        internal static void VerifyThrowArgumentInvalidPath(string parameter, string parameterName)
        {
            VerifyThrowArgumentNull(parameter, parameterName);

            if (FileUtilities.PathIsInvalid(parameter) && s_throwExceptions)
            {
                throw new ArgumentException(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("Shared.ParameterCannotHaveInvalidPathChars", parameterName, parameter));
            }
        }

        /// <summary>
        /// Throws an ArgumentException if the string has zero length, unless it is 
        /// null, in which case no exception is thrown.
        /// </summary>
        internal static void VerifyThrowArgumentLengthIfNotNull(string parameter, string parameterName)
        {
            if (parameter?.Length == 0 && s_throwExceptions)
            {
                throw new ArgumentException(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("Shared.ParameterCannotHaveZeroLength", parameterName));
            }
        }

        /// <summary>
        /// Throws an ArgumentNullException if the given parameter is null.
        /// </summary>
        /// <remarks>This method is thread-safe.</remarks>
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
        internal static void VerifyThrowArgumentNull(object parameter, string parameterName, string resourceName)
        {
            if (parameter == null && s_throwExceptions)
            {
                // Most ArgumentNullException overloads append its own rather clunky multi-line message. 
                // So use the one overload that doesn't.
                throw new ArgumentNullException(
                    ResourceUtilities.FormatResourceStringStripCodeAndKeyword(resourceName, parameterName),
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

            if (parameter1.Length != parameter2.Length && s_throwExceptions)
            {
                throw new ArgumentException(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("Shared.ParametersMustHaveTheSameLength", parameter1Name, parameter2Name));
            }
        }

        #endregion

        #region VerifyThrowObjectDisposed

        internal static void VerifyThrowObjectDisposed(bool condition, string objectName)
        {
            {
                if (s_throwExceptions && !condition)
                {
                    throw new ObjectDisposedException(objectName);
                }
            }
        }

        #endregion
#endif
    }
}
