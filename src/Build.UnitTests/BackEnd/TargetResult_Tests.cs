// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using Microsoft.Build.BackEnd;
using Microsoft.Build.Unittest;
using Microsoft.Build.Exceptions;
using Microsoft.Build.Execution;
using TaskItem = Microsoft.Build.Execution.ProjectItemInstance.TaskItem;
using Xunit;
using Microsoft.Build.Engine.UnitTests.TestComparers;

namespace Microsoft.Build.UnitTests.BackEnd
{
    /// <summary>
    /// Tests for the target result test.
    /// </summary>
    public class TargetResult_Tests
    {
        /// <summary>
        /// Tests a constructor with no items.
        /// </summary>
        [Fact]
        public void TestConstructorNoItems()
        {
            TargetResult result = new TargetResult(new TaskItem[] { }, BuildResultUtilities.GetStopWithErrorResult());
            Assert.Empty(result.Items);
            Assert.Null(result.Exception);
            Assert.Equal(TargetResultCode.Failure, result.ResultCode);
        }

        /// <summary>
        /// Tests a constructor with items.
        /// </summary>
        [Fact]
        public void TestConstructorWithItems()
        {
            TaskItem item = new TaskItem("foo", "bar.proj");
            TargetResult result = new TargetResult(new TaskItem[] { item }, BuildResultUtilities.GetStopWithErrorResult());
            Assert.Single(result.Items);
            Assert.Equal(item.ItemSpec, result.Items[0].ItemSpec);
            Assert.Equal(TargetResultCode.Failure, result.ResultCode);
        }

        /// <summary>
        /// Tests a constructor with a null item array passed.
        /// </summary>
        [Fact]
        public void TestConstructorNullItems()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                TargetResult result = new TargetResult(null, BuildResultUtilities.GetStopWithErrorResult());
            }
           );
        }
        /// <summary>
        /// Tests a constructor with an exception passed.
        /// </summary>
        [Fact]
        public void TestConstructorWithException()
        {
            TaskItem item = new TaskItem("foo", "bar.proj");
            TargetResult result = new TargetResult(new TaskItem[] { item }, BuildResultUtilities.GetStopWithErrorResult(new ArgumentException()));
            Assert.Single(result.Items);
            Assert.NotNull(result.Exception);
            Assert.Equal(typeof(ArgumentException), result.Exception.GetType());
            Assert.Equal(TargetResultCode.Failure, result.ResultCode);
        }

        /// <summary>
        /// Tests a constructor with a null exception passed.
        /// </summary>
        [Fact]
        public void TestConstructorWithExceptionNull()
        {
            TaskItem item = new TaskItem("foo", "bar.proj");
            TargetResult result = new TargetResult(new TaskItem[] { item }, BuildResultUtilities.GetStopWithErrorResult());
            Assert.Single(result.Items);
            Assert.Null(result.Exception);
            Assert.Equal(TargetResultCode.Failure, result.ResultCode);
        }

        /// <summary>
        /// Tests serialization with no exception in the result.
        /// </summary>
        [Fact]
        public void TestTranslationNoException()
        {
            TaskItem item = new TaskItem("foo", "bar.proj");
            item.SetMetadata("a", "b");
            var buildEventContext = new Framework.BuildEventContext(1, 2, 3, 4, 5, 6, 7);

            TargetResult result = new TargetResult(
                new TaskItem[] { item },
                BuildResultUtilities.GetStopWithErrorResult(),
                buildEventContext);

            ((ITranslatable)result).Translate(TranslationHelpers.GetWriteTranslator());
            TargetResult deserializedResult = TargetResult.FactoryForDeserialization(TranslationHelpers.GetReadTranslator());

            Assert.Equal(result.ResultCode, deserializedResult.ResultCode);
            Assert.True(TranslationHelpers.CompareCollections(result.Items, deserializedResult.Items, TaskItemComparer.Instance));
            Assert.True(TranslationHelpers.CompareExceptions(result.Exception, deserializedResult.Exception));
            Assert.Equal(result.OriginalBuildEventContext, deserializedResult.OriginalBuildEventContext);
        }

        /// <summary>
        /// Tests serialization with an exception in the result.
        /// </summary>
        [Fact]
        public void TestTranslationWithException()
        {
            TaskItem item = new TaskItem("foo", "bar.proj");
            item.SetMetadata("a", "b");

            TargetResult result = new TargetResult(new TaskItem[] { item }, BuildResultUtilities.GetStopWithErrorResult(new BuildAbortedException()));

            ((ITranslatable)result).Translate(TranslationHelpers.GetWriteTranslator());
            TargetResult deserializedResult = TargetResult.FactoryForDeserialization(TranslationHelpers.GetReadTranslator());

            Assert.Equal(result.ResultCode, deserializedResult.ResultCode);
            Assert.True(TranslationHelpers.CompareCollections(result.Items, deserializedResult.Items, TaskItemComparer.Instance));
            Assert.True(TranslationHelpers.CompareExceptions(result.Exception, deserializedResult.Exception));
        }

        /// <summary>
        /// Test GetCacheDirectory is resilient to paths with strings that would normally make string.format to throw a FormatException
        /// </summary>
        [Fact]
        public void TestGetCacheDirectory()
        {
            string oldTmp = Environment.GetEnvironmentVariable("TMP");

            try
            {
                Environment.SetEnvironmentVariable("TMP", "C:\\}");
                string path1 = TargetResult.GetCacheDirectory(2, "Blah");

                Environment.SetEnvironmentVariable("TMP", "C:\\{");
                string path2 = TargetResult.GetCacheDirectory(2, "Blah");
            }
            finally
            {
                Environment.SetEnvironmentVariable("TMP", oldTmp);
            }
        }
    }
}
