// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BizTalktoLogicApps.ODXtoWFMigrator;

namespace BizTalktoLogicApps.Tests.Unit
{
    [TestClass]
    public class ExceptionExtensionsTests
    {
        #region Fatal Exception Tests

        [TestMethod]
        [Owner("daviburg_microsoft")]
        public void IsFatal_OutOfMemoryException_ReturnsTrue()
        {
            // Arrange
            var exception = new OutOfMemoryException();

            // Act
            var result = exception.IsFatal();

            // Assert
            Assert.IsTrue(condition: result, message: "OutOfMemoryException should be fatal");
        }

        [TestMethod]
        public void IsFatal_StackOverflowException_ReturnsTrue()
        {
            // Arrange
            var exception = new StackOverflowException();

            // Act
            var result = exception.IsFatal();

            // Assert
            Assert.IsTrue(condition: result, message: "StackOverflowException should be fatal");
        }

        [TestMethod]
        public void IsFatal_ThreadAbortException_ReturnsTrue()
        {
            // Arrange
            // Note: ThreadAbortException cannot be instantiated directly in modern .NET,
            // but we can test the type check logic through a wrapper
            Exception exception = null;
            try
            {
                // Create using reflection since constructor is internal
                exception = (ThreadAbortException)System.Runtime.Serialization.FormatterServices
                    .GetUninitializedObject(typeof(ThreadAbortException));
            }
            catch (Exception ex) when (!ex.IsFatal())
            {
                // If we can't create it, skip this test
                Assert.Inconclusive(message: "Cannot create ThreadAbortException for testing");
                return;
            }

            // Act
            var result = exception.IsFatal();

            // Assert
            Assert.IsTrue(condition: result, message: "ThreadAbortException should be fatal");
        }

        [TestMethod]
        public void IsFatal_AccessViolationException_ReturnsTrue()
        {
            // Arrange
            var exception = new AccessViolationException();

            // Act
            var result = exception.IsFatal();

            // Assert
            Assert.IsTrue(condition: result, message: "AccessViolationException should be fatal");
        }

        [TestMethod]
        public void IsFatal_SEHException_ReturnsTrue()
        {
            // Arrange
            var exception = new SEHException();

            // Act
            var result = exception.IsFatal();

            // Assert
            Assert.IsTrue(condition: result, message: "SEHException should be fatal");
        }

        [TestMethod]
        public void IsFatal_SecurityException_ReturnsTrue()
        {
            // Arrange
            var exception = new SecurityException();

            // Act
            var result = exception.IsFatal();

            // Assert
            Assert.IsTrue(condition: result, message: "SecurityException should be fatal");
        }

        #endregion

        #region Non-Fatal Exception Tests

        [TestMethod]
        public void IsFatal_ArgumentException_ReturnsFalse()
        {
            // Arrange
            var exception = new ArgumentException("test");

            // Act
            var result = exception.IsFatal();

            // Assert
            Assert.IsFalse(condition: result, message: "ArgumentException should not be fatal");
        }

        [TestMethod]
        public void IsFatal_InvalidOperationException_ReturnsFalse()
        {
            // Arrange
            var exception = new InvalidOperationException("test");

            // Act
            var result = exception.IsFatal();

            // Assert
            Assert.IsFalse(condition: result, message: "InvalidOperationException should not be fatal");
        }

        [TestMethod]
        public void IsFatal_NullReferenceException_ReturnsFalse()
        {
            // Arrange
            var exception = new NullReferenceException("test");

            // Act
            var result = exception.IsFatal();

            // Assert
            Assert.IsFalse(condition: result, message: "NullReferenceException should not be fatal");
        }

        [TestMethod]
        public void IsFatal_IOException_ReturnsFalse()
        {
            // Arrange
            var exception = new System.IO.IOException("test");

            // Act
            var result = exception.IsFatal();

            // Assert
            Assert.IsFalse(condition: result, message: "IOException should not be fatal");
        }

        #endregion

        #region Null Exception Tests

        [TestMethod]
        public void IsFatal_NullException_ReturnsFalse()
        {
            // Arrange
            Exception exception = null;

            // Act
            var result = exception.IsFatal();

            // Assert
            Assert.IsFalse(condition: result, message: "Null exception should return false");
        }

        #endregion

        #region Inner Exception Tests

        [TestMethod]
        public void IsFatal_NonFatalWithFatalInnerException_ReturnsTrue()
        {
            // Arrange
            var innerException = new OutOfMemoryException();
            var outerException = new InvalidOperationException("wrapper", innerException);

            // Act
            var result = outerException.IsFatal();

            // Assert
            Assert.IsTrue(condition: result, message: "Exception with fatal inner exception should be fatal");
        }

        [TestMethod]
        public void IsFatal_NonFatalWithNonFatalInnerException_ReturnsFalse()
        {
            // Arrange
            var innerException = new ArgumentException("inner");
            var outerException = new InvalidOperationException("wrapper", innerException);

            // Act
            var result = outerException.IsFatal();

            // Assert
            Assert.IsFalse(condition: result, message: "Exception with non-fatal inner exception should not be fatal");
        }

        [TestMethod]
        public void IsFatal_NestedInnerExceptions_FindsFatalInChain()
        {
            // Arrange
            var fatalException = new AccessViolationException();
            var middleException = new ArgumentException("middle", fatalException);
            var outerException = new InvalidOperationException("outer", middleException);

            // Act
            var result = outerException.IsFatal();

            // Assert
            Assert.IsTrue(condition: result, message: "Should find fatal exception in nested inner exception chain");
        }

        [TestMethod]
        public void IsFatal_DeepNestedNonFatalExceptions_ReturnsFalse()
        {
            // Arrange
            var innermost = new ArgumentException("innermost");
            var middle = new InvalidOperationException("middle", innermost);
            var outer = new ApplicationException("outer", middle);

            // Act
            var result = outer.IsFatal();

            // Assert
            Assert.IsFalse(condition: result, message: "Deep nested non-fatal exceptions should return false");
        }

        #endregion
    }
}
