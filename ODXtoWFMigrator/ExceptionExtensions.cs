// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;

namespace BizTalktoLogicApps.ODXtoWFMigrator
{
    /// <summary>
    /// Extension methods for exception handling.
    /// </summary>
    public static class ExceptionExtensions
    {
        /// <summary>
        /// Determines whether the exception is fatal and should not be caught.
        /// Fatal exceptions indicate unrecoverable conditions where the process should terminate.
        /// </summary>
        /// <param name="exception">The exception to check.</param>
        /// <returns>True if the exception is fatal; otherwise, false.</returns>
        public static bool IsFatal(this Exception exception)
        {
            if (exception == null)
            {
                return false;
            }

            return exception is OutOfMemoryException
                || exception is StackOverflowException
                || exception is ThreadAbortException
                || exception is AccessViolationException
                || exception is SEHException
                || exception is SecurityException
                || (exception.InnerException != null && exception.InnerException.IsFatal());
        }
    }
}
