// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Shared;
using Shouldly;
using Xunit;

namespace Microsoft.Build.Engine.UnitTests.Evaluation
{
    public class SimpleProjectRootElementCache_Tests : IDisposable
    {
        public SimpleProjectRootElementCache_Tests()
        {
            // Empty the cache
            ProjectCollection.GlobalProjectCollection.UnloadAllProjects();
        }

        public void Dispose()
        {
            // Empty the cache
            ProjectCollection.GlobalProjectCollection.UnloadAllProjects();
        }

        [Fact]
        public void Get_GivenCachedProjectFile_ReturnsRootElement()
        {
            string projectFile = NativeMethodsShared.IsUnixLike ? "/foo" : "c:\\foo";
            string projectFileToCache = NativeMethodsShared.IsUnixLike ? "/foo" : "c:\\foo";
            ProjectRootElement rootElementToCache = ProjectRootElement.Create(projectFileToCache);

            var cache = new SimpleProjectRootElementCache();
            cache.AddEntry(rootElementToCache);

            ProjectRootElement actualRootElement = cache.Get(projectFile, null, false, null);
            actualRootElement.ShouldBe(rootElementToCache);
        }

        [Fact]
        public void Get_GivenCachedProjectFileWithDifferentCasing_ReturnsRootElement()
        {
            string projectFile = NativeMethodsShared.IsUnixLike ? "/foo" : "c:\\foo";
            string projectFileToCache = NativeMethodsShared.IsUnixLike ? "/Foo" : "c:\\Foo";
            ProjectRootElement rootElementToCache = ProjectRootElement.Create(projectFileToCache);

            var cache = new SimpleProjectRootElementCache();
            cache.AddEntry(rootElementToCache);

            ProjectRootElement actualRootElement = cache.Get(projectFile, null, false, null);
            actualRootElement.ShouldBe(rootElementToCache);
        }

        [Fact]
        public void Get_GivenOpenFuncWhichAddsRootElement_ReturnsRootElement()
        {
            string projectFile = NativeMethodsShared.IsUnixLike ? "/foo" : "c:\\foo";
            string projectFileToCache = NativeMethodsShared.IsUnixLike ? "/foo" : "c:\\foo";
            ProjectRootElement rootElementToCache = ProjectRootElement.Create(projectFileToCache);
            ProjectRootElement OpenFunc(string pathArg, ProjectRootElementCacheBase cacheArg)
            {
                return rootElementToCache;
            }

            var cache = new SimpleProjectRootElementCache();
            cache.AddEntry(rootElementToCache);

            ProjectRootElement actualRootElement = cache.Get(projectFile, OpenFunc, false, null);
            actualRootElement.ShouldBe(rootElementToCache);
        }

        [Fact]
        public void Get_GivenOpenFuncWhichAddsRootElementWithDifferentCasing_ReturnsRootElement()
        {
            string projectFile = NativeMethodsShared.IsUnixLike ? "/foo" : "c:\\foo";
            string projectFileToCache = NativeMethodsShared.IsUnixLike ? "/Foo" : "c:\\Foo";
            ProjectRootElement rootElementToCache = ProjectRootElement.Create(projectFileToCache);
            ProjectRootElement OpenFunc(string pathArg, ProjectRootElementCacheBase cacheArg)
            {
                return rootElementToCache;
            }

            var cache = new SimpleProjectRootElementCache();
            cache.AddEntry(rootElementToCache);

            ProjectRootElement actualRootElement = cache.Get(projectFile, OpenFunc, false, null);
            actualRootElement.ShouldBe(rootElementToCache);
        }

        [Fact]
        public void Get_GivenOpenFuncWhichReturnsNull_ThrowsInternalErrorException()
        {
            string projectFile = NativeMethodsShared.IsUnixLike ? "/foo" : "c:\\foo";
            ProjectRootElement OpenFunc(string pathArg, ProjectRootElementCacheBase cacheArg) => null;

            var cache = new SimpleProjectRootElementCache();

            Should.Throw<InternalErrorException>(() =>
            {
                cache.Get(projectFile, OpenFunc, false, null);
            });
        }

        [Fact]
        public void Get_GivenOpenFuncWhichReturnsIncorrectProject_ThrowsInternalErrorException()
        {
            string projectFile = NativeMethodsShared.IsUnixLike ? "/foo" : "c:\\foo";
            string projectFileToCache = NativeMethodsShared.IsUnixLike ? "/bar" : "c:\\bar";
            ProjectRootElement rootElementToCache = ProjectRootElement.Create(projectFileToCache);
            ProjectRootElement OpenFunc(string pathArg, ProjectRootElementCacheBase cacheArg)
            {
                return rootElementToCache;
            }

            var cache = new SimpleProjectRootElementCache();

            Should.Throw<InternalErrorException>(() =>
            {
                cache.Get(projectFile, OpenFunc, false, null);
            });
        }
    }
}
