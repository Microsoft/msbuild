// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if FEATURE_APPDOMAIN

using System;
using System.Collections.Generic;

// TYPELIBATTR clashes with the one in InteropServices.
using TYPELIBATTR = System.Runtime.InteropServices.ComTypes.TYPELIBATTR;

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.Build.Tasks;
using Xunit;
using Microsoft.Build.Shared;
using System.IO;
using Microsoft.Build.BackEnd;
using Shouldly;

namespace Microsoft.Build.UnitTests
{
    sealed public class ResolveComReference_Tests
    {
        /// <summary>
        /// Creates a valid task item that's modified later
        /// </summary>
        private TaskItem SetupTaskItem()
        {
            var item = new TaskItem();

            item.SetMetadata(ComReferenceItemMetadataNames.guid, "{5C6D0C4D-D530-4B08-B22F-307CA6BFCB65}");
            item.SetMetadata(ComReferenceItemMetadataNames.versionMajor, "1");
            item.SetMetadata(ComReferenceItemMetadataNames.versionMinor, "0");
            item.SetMetadata(ComReferenceItemMetadataNames.lcid, "0");
            item.SetMetadata(ComReferenceItemMetadataNames.wrapperTool, "tlbimp");

            return item;
        }

        private void AssertReference(ITaskItem item, bool valid, string attribute)
        {
            string missingOrInvalidAttribute;
            Assert.Equal(ResolveComReference.VerifyReferenceMetadataForNameItem(item, out missingOrInvalidAttribute), valid);
            Assert.Equal(missingOrInvalidAttribute, attribute);
        }

        private void AssertMetadataInitialized(ITaskItem item, string metadataName, string metadataValue)
        {
            Assert.Equal(item.GetMetadata(metadataName), metadataValue);
        }

        /// <summary>
        /// Issue in this bug was an ArgumentNullException when ResolvedAssemblyReferences was null
        /// </summary>
        [Fact]
        public void GetResolvedASsemblyReferenceSpecNotNull()
        {
            var task = new ResolveComReference();
            Assert.NotNull(task.GetResolvedAssemblyReferenceItemSpecs());
        }

        [Fact]
        public void TestSerializationAndDeserialization()
        {
            ResolveComReferenceCache cache = new("path1", "path2");
            cache.componentTimestamps = new()
            {
                { "first", DateTime.Now },
                { "second", DateTime.FromBinary(10000) },
            };
            ResolveComReferenceCache cache2 = null;
            using (TestEnvironment env = TestEnvironment.Create())
            {
                TransientTestFile file = env.CreateFile();
                cache.SerializeCache(file.Path, null);
                cache2 = StateFileBase.DeserializeCache(file.Path, null, typeof(ResolveComReferenceCache)) as ResolveComReferenceCache;
            }

            cache2.tlbImpLocation.ShouldBe(cache.tlbImpLocation);
            cache2.axImpLocation.ShouldBe(cache.axImpLocation);
            cache2.componentTimestamps.Count.ShouldBe(cache.componentTimestamps.Count);
            cache2.componentTimestamps["second"].ShouldBe(cache.componentTimestamps["second"]);
        }

        /*
         * Method:  CheckComReferenceAttributeVerificationForNameItems
         * 
         * Checks if verification of Com reference item metadata works properly
         */
        [Fact]
        public void CheckComReferenceMetadataVerificationForNameItems()
        {
            // valid item
            TaskItem item = SetupTaskItem();
            AssertReference(item, true, "");

            // invalid guid
            item = SetupTaskItem();
            item.SetMetadata(ComReferenceItemMetadataNames.guid, "{I'm pretty sure this is not a valid guid}");
            AssertReference(item, false, ComReferenceItemMetadataNames.guid);

            // missing guid
            item = SetupTaskItem();
            item.RemoveMetadata(ComReferenceItemMetadataNames.guid);
            AssertReference(item, false, ComReferenceItemMetadataNames.guid);

            // invalid verMajor
            item = SetupTaskItem();
            item.SetMetadata(ComReferenceItemMetadataNames.versionMajor, "eleventy one");
            AssertReference(item, false, ComReferenceItemMetadataNames.versionMajor);

            // missing verMajor
            item = SetupTaskItem();
            item.RemoveMetadata(ComReferenceItemMetadataNames.versionMajor);
            AssertReference(item, false, ComReferenceItemMetadataNames.versionMajor);

            // invalid verMinor
            item = SetupTaskItem();
            item.SetMetadata(ComReferenceItemMetadataNames.versionMinor, "eleventy one");
            AssertReference(item, false, ComReferenceItemMetadataNames.versionMinor);

            // missing verMinor
            item = SetupTaskItem();
            item.RemoveMetadata(ComReferenceItemMetadataNames.versionMinor);
            AssertReference(item, false, ComReferenceItemMetadataNames.versionMinor);

            // invalid lcid
            item = SetupTaskItem();
            item.SetMetadata(ComReferenceItemMetadataNames.lcid, "Mars-us");
            AssertReference(item, false, ComReferenceItemMetadataNames.lcid);

            // missing lcid - it's optional, so this should work ok
            item = SetupTaskItem();
            item.RemoveMetadata(ComReferenceItemMetadataNames.lcid);
            AssertReference(item, true, String.Empty);

            // invalid tool
            item = SetupTaskItem();
            item.SetMetadata(ComReferenceItemMetadataNames.wrapperTool, "crowbar");
            AssertReference(item, false, ComReferenceItemMetadataNames.wrapperTool);

            // missing tool - it's optional, so this should work ok
            item = SetupTaskItem();
            item.RemoveMetadata(ComReferenceItemMetadataNames.wrapperTool);
            AssertReference(item, true, String.Empty);
        }

        /*
         * Method:  CheckComReferenceAttributeInitializationForNameItems
         * 
         * Checks if missing optional attributes for COM name references get initialized correctly
         */
        [Fact]
        public void CheckComReferenceMetadataInitializationForNameItems()
        {
            // missing lcid - should get initialized to 0
            TaskItem item = SetupTaskItem();
            item.RemoveMetadata(ComReferenceItemMetadataNames.lcid);
            ResolveComReference.InitializeDefaultMetadataForNameItem(item);
            AssertMetadataInitialized(item, ComReferenceItemMetadataNames.lcid, "0");

            // existing lcid - should not get modified
            item = SetupTaskItem();
            item.SetMetadata(ComReferenceItemMetadataNames.lcid, "1033");
            ResolveComReference.InitializeDefaultMetadataForNameItem(item);
            AssertMetadataInitialized(item, ComReferenceItemMetadataNames.lcid, "1033");

            // missing wrapperTool - should get initialized to tlbimp
            item = SetupTaskItem();
            item.RemoveMetadata(ComReferenceItemMetadataNames.wrapperTool);
            ResolveComReference.InitializeDefaultMetadataForNameItem(item);
            AssertMetadataInitialized(item, ComReferenceItemMetadataNames.wrapperTool, ComReferenceTypes.tlbimp);

            // existing wrapperTool - should not get modified
            item = SetupTaskItem();
            item.SetMetadata(ComReferenceItemMetadataNames.wrapperTool, ComReferenceTypes.aximp);
            ResolveComReference.InitializeDefaultMetadataForNameItem(item);
            AssertMetadataInitialized(item, ComReferenceItemMetadataNames.wrapperTool, ComReferenceTypes.aximp);
        }

        /*
         * Method:  CheckComReferenceAttributeInitializationForFileItems
         * 
         * Checks if missing optional attributes for COM file references get initialized correctly
         */
        [Fact]
        public void CheckComReferenceMetadataInitializationForFileItems()
        {
            // missing wrapperTool - should get initialized to tlbimp
            TaskItem item = SetupTaskItem();
            item.RemoveMetadata(ComReferenceItemMetadataNames.wrapperTool);
            ResolveComReference.InitializeDefaultMetadataForFileItem(item);
            AssertMetadataInitialized(item, ComReferenceItemMetadataNames.wrapperTool, ComReferenceTypes.tlbimp);

            // existing wrapperTool - should not get modified
            item = SetupTaskItem();
            item.SetMetadata(ComReferenceItemMetadataNames.wrapperTool, ComReferenceTypes.aximp);
            ResolveComReference.InitializeDefaultMetadataForFileItem(item);
            AssertMetadataInitialized(item, ComReferenceItemMetadataNames.wrapperTool, ComReferenceTypes.aximp);
        }

        /// <summary>
        /// Helper function for creating a COM reference task item instance
        /// </summary>
        private TaskItem CreateComReferenceTaskItem(string itemSpec, string guid, string vMajor, string vMinor, string lcid, string wrapperType, string embedInteropTypes)
        {
            var item = new TaskItem(itemSpec);

            item.SetMetadata(ComReferenceItemMetadataNames.guid, guid);
            item.SetMetadata(ComReferenceItemMetadataNames.versionMajor, vMajor);
            item.SetMetadata(ComReferenceItemMetadataNames.versionMinor, vMinor);
            item.SetMetadata(ComReferenceItemMetadataNames.lcid, lcid);
            item.SetMetadata(ComReferenceItemMetadataNames.wrapperTool, wrapperType);
            item.SetMetadata(ItemMetadataNames.embedInteropTypes, embedInteropTypes);

            return item;
        }

        /// <summary>
        /// Helper function for creating a COM reference task item instance
        /// </summary>
        private TaskItem CreateComReferenceTaskItem(string itemSpec, string guid, string vMajor, string vMinor, string lcid, string wrapperType)
        {
            return CreateComReferenceTaskItem(itemSpec, guid, vMajor, vMinor, lcid, wrapperType, String.Empty);
        }

        /// <summary>
        /// Test the ResolveComReference.TaskItemToTypeLibAttr method
        /// </summary>
        [Fact]
        public void CheckTaskItemToTypeLibAttr()
        {
            Guid refGuid = Guid.NewGuid();

            TaskItem reference = CreateComReferenceTaskItem("ref", refGuid.ToString(), "11", "0", "1033", ComReferenceTypes.tlbimp);
            TYPELIBATTR refAttr = ResolveComReference.TaskItemToTypeLibAttr(reference);

            Assert.Equal(refGuid, refAttr.guid); // "incorrect guid"
            Assert.Equal(11, refAttr.wMajorVerNum); // "incorrect version major"
            Assert.Equal(0, refAttr.wMinorVerNum); // "incorrect version minor"
            Assert.Equal(1033, refAttr.lcid); // "incorrect lcid"
        }

        /// <summary>
        /// Helper function for creating a ComReferenceInfo object using an existing TaskInfo object and
        /// typelib name/path. The type lib pointer will obviously not be initialized, so this object cannot
        /// be used in any code that uses it.
        /// </summary>
        private ComReferenceInfo CreateComReferenceInfo(ITaskItem taskItem, string typeLibName, string typeLibPath)
        {
            var referenceInfo = new ComReferenceInfo();

            referenceInfo.taskItem = taskItem;
            referenceInfo.attr = ResolveComReference.TaskItemToTypeLibAttr(taskItem);
            referenceInfo.typeLibName = typeLibName;
            referenceInfo.fullTypeLibPath = typeLibPath;
            referenceInfo.strippedTypeLibPath = typeLibPath;
            referenceInfo.typeLibPointer = null;

            return referenceInfo;
        }

        /// <summary>
        /// Create a few test references for unit tests
        /// </summary>
        private void CreateTestReferences(
            out ComReferenceInfo axRefInfo, out ComReferenceInfo tlbRefInfo, out ComReferenceInfo piaRefInfo,
            out TYPELIBATTR axAttr, out TYPELIBATTR tlbAttr, out TYPELIBATTR piaAttr, out TYPELIBATTR notInProjectAttr)
        {
            // doing my part to deplete the worldwide guid reserves...
            Guid axGuid = Guid.NewGuid();
            Guid tlbGuid = Guid.NewGuid();
            Guid piaGuid = Guid.NewGuid();

            // create reference task items
            TaskItem axTaskItem = CreateComReferenceTaskItem("axref", axGuid.ToString(), "1", "0", "1033", ComReferenceTypes.aximp);
            TaskItem tlbTaskItem = CreateComReferenceTaskItem("tlbref", tlbGuid.ToString(), "5", "1", "0", ComReferenceTypes.tlbimp);
            TaskItem piaTaskItem = CreateComReferenceTaskItem("piaref", piaGuid.ToString(), "999", "444", "123", ComReferenceTypes.primary);

            // create reference infos
            axRefInfo = CreateComReferenceInfo(axTaskItem, "AxRefLibName", "AxRefLibPath");
            tlbRefInfo = CreateComReferenceInfo(tlbTaskItem, "TlbRefLibName", "TlbRefLibPath");
            piaRefInfo = CreateComReferenceInfo(piaTaskItem, "PiaRefLibName", "PiaRefLibPath");

            // get the references' typelib attributes
            axAttr = ResolveComReference.TaskItemToTypeLibAttr(axTaskItem);
            tlbAttr = ResolveComReference.TaskItemToTypeLibAttr(tlbTaskItem);
            piaAttr = ResolveComReference.TaskItemToTypeLibAttr(piaTaskItem);

            // create typelib attributes not matching any of the project refs
            notInProjectAttr = new TYPELIBATTR();
            notInProjectAttr.guid = tlbGuid;
            notInProjectAttr.wMajorVerNum = 5;
            notInProjectAttr.wMinorVerNum = 1;
            notInProjectAttr.lcid = 1033;
        }

        /// <summary>
        /// Unit test for the ResolveComReference.IsExistingProjectReference() method
        /// </summary>
        [Fact]
        public void CheckIsExistingProjectReference()
        {
            TYPELIBATTR axAttr, tlbAttr, piaAttr, notInProjectAttr;
            ComReferenceInfo axRefInfo, tlbRefInfo, piaRefInfo;

            CreateTestReferences(out axRefInfo, out tlbRefInfo, out piaRefInfo,
                out axAttr, out tlbAttr, out piaAttr, out notInProjectAttr);

            var rcr = new ResolveComReference();

            // populate the ResolveComReference's list of project references
            rcr.allProjectRefs = new List<ComReferenceInfo>();
            rcr.allProjectRefs.Add(axRefInfo);
            rcr.allProjectRefs.Add(tlbRefInfo);
            rcr.allProjectRefs.Add(piaRefInfo);

            // find the Ax ref, matching with any type of reference - should NOT find it
            bool retValue = rcr.IsExistingProjectReference(axAttr, null, out ComReferenceInfo referenceInfo);
            Assert.True(!retValue && referenceInfo == null); // "ActiveX ref should NOT be found for any type of ref"

            // find the Ax ref, matching with aximp types - should find it
            retValue = rcr.IsExistingProjectReference(axAttr, ComReferenceTypes.aximp, out referenceInfo);
            Assert.True(retValue && referenceInfo == axRefInfo); // "ActiveX ref should be found for aximp ref types"

            // find the Ax ref, matching with tlbimp types - should NOT find it
            retValue = rcr.IsExistingProjectReference(axAttr, ComReferenceTypes.tlbimp, out referenceInfo);
            Assert.True(!retValue && referenceInfo == null); // "ActiveX ref should NOT be found for tlbimp ref types"


            // find the Tlb ref, matching with any type of reference - should find it
            retValue = rcr.IsExistingProjectReference(tlbAttr, null, out referenceInfo);
            Assert.True(retValue && referenceInfo == tlbRefInfo); // "Tlb ref should be found for any type of ref"

            // find the Tlb ref, matching with tlbimp types - should find it
            retValue = rcr.IsExistingProjectReference(tlbAttr, ComReferenceTypes.tlbimp, out referenceInfo);
            Assert.True(retValue && referenceInfo == tlbRefInfo); // "Tlb ref should be found for tlbimp ref types"

            // find the Tlb ref, matching with pia types - should NOT find it
            retValue = rcr.IsExistingProjectReference(tlbAttr, ComReferenceTypes.primary, out referenceInfo);
            Assert.True(!retValue && referenceInfo == null); // "Tlb ref should NOT be found for primary ref types"


            // find the Pia ref, matching with any type of reference - should find it
            retValue = rcr.IsExistingProjectReference(piaAttr, null, out referenceInfo);
            Assert.True(retValue && referenceInfo == piaRefInfo); // "Pia ref should be found for any type of ref"

            // find the Pia ref, matching with pia types - should find it
            retValue = rcr.IsExistingProjectReference(piaAttr, ComReferenceTypes.primary, out referenceInfo);
            Assert.True(retValue && referenceInfo == piaRefInfo); // "Pia ref should be found for pia ref types"

            // find the Pia ref, matching with pia types - should NOT find it
            retValue = rcr.IsExistingProjectReference(piaAttr, ComReferenceTypes.aximp, out referenceInfo);
            Assert.True(!retValue && referenceInfo == null); // "Pia ref should NOT be found for aximp ref types"

            // try to find a non existing reference
            retValue = rcr.IsExistingProjectReference(notInProjectAttr, null, out referenceInfo);
            Assert.True(!retValue && referenceInfo == null); // "not in project ref should not be found"
        }

        /// <summary>
        /// Unit test for the ResolveComReference.IsExistingDependencyReference() method
        /// </summary>
        [Fact]
        public void CheckIsExistingDependencyReference()
        {
            TYPELIBATTR axAttr, tlbAttr, piaAttr, notInProjectAttr;
            ComReferenceInfo axRefInfo, tlbRefInfo, piaRefInfo;

            CreateTestReferences(out axRefInfo, out tlbRefInfo, out piaRefInfo,
                out axAttr, out tlbAttr, out piaAttr, out notInProjectAttr);

            var rcr = new ResolveComReference();

            // populate the ResolveComReference's list of project references
            rcr.allDependencyRefs = new List<ComReferenceInfo>();
            rcr.allDependencyRefs.Add(axRefInfo);
            rcr.allDependencyRefs.Add(tlbRefInfo);
            rcr.allDependencyRefs.Add(piaRefInfo);

            // find the Ax ref - should find it
            bool retValue = rcr.IsExistingDependencyReference(axAttr, out ComReferenceInfo referenceInfo);
            Assert.True(retValue && referenceInfo == axRefInfo); // "ActiveX ref should be found"

            // find the Tlb ref - should find it
            retValue = rcr.IsExistingDependencyReference(tlbAttr, out referenceInfo);
            Assert.True(retValue && referenceInfo == tlbRefInfo); // "Tlb ref should be found"

            // find the Pia ref - should find it
            retValue = rcr.IsExistingDependencyReference(piaAttr, out referenceInfo);
            Assert.True(retValue && referenceInfo == piaRefInfo); // "Pia ref should be found"

            // try to find a non existing reference - should not find it
            retValue = rcr.IsExistingDependencyReference(notInProjectAttr, out referenceInfo);
            Assert.True(!retValue && referenceInfo == null); // "not in project ref should not be found"

            // Now, try to resolve a non-existent ComAssemblyReference. 
            string path;
            IComReferenceResolver resolver = (IComReferenceResolver)rcr;
            Assert.False(resolver.ResolveComAssemblyReference("MyAssembly", out path));
            Assert.Null(path);
        }

        /// <summary>
        /// ResolveComReference automatically adds missing tlbimp references for aximp references.
        /// This test verifies we actually create the missing references.
        /// </summary>
        [Fact]
        public void CheckAddMissingTlbReference()
        {
            TYPELIBATTR axAttr, tlbAttr, piaAttr, notInProjectAttr;
            ComReferenceInfo axRefInfo, tlbRefInfo, piaRefInfo;

            CreateTestReferences(out axRefInfo, out tlbRefInfo, out piaRefInfo,
                out axAttr, out tlbAttr, out piaAttr, out notInProjectAttr);

            var rcr = new ResolveComReference();
            rcr.BuildEngine = new MockEngine();

            // populate the ResolveComReference's list of project references
            rcr.allProjectRefs = new List<ComReferenceInfo>();
            rcr.allProjectRefs.Add(axRefInfo);
            rcr.allProjectRefs.Add(tlbRefInfo);
            rcr.allProjectRefs.Add(piaRefInfo);

            rcr.AddMissingTlbReferences();

            Assert.Equal(4, rcr.allProjectRefs.Count); // "There should be four references now"

            ComReferenceInfo newTlbInfo = (ComReferenceInfo)rcr.allProjectRefs[3];
            Assert.Equal(axRefInfo.primaryOfAxImpRef, newTlbInfo); // "axRefInfo should hold back reference to tlbRefInfo"
            Assert.True(ComReference.AreTypeLibAttrEqual(newTlbInfo.attr, axRefInfo.attr)); // "The added reference should have the same attributes as the Ax reference"
            Assert.Equal(newTlbInfo.typeLibName, axRefInfo.typeLibName); // "The added reference should have the same type lib name as the Ax reference"
            Assert.Equal(newTlbInfo.strippedTypeLibPath, axRefInfo.strippedTypeLibPath); // "The added reference should have the same type lib path as the Ax reference"

            Assert.Equal(newTlbInfo.taskItem.ItemSpec, axRefInfo.taskItem.ItemSpec); // "The added reference should have the same task item spec as the Ax reference"
            Assert.Equal(newTlbInfo.taskItem.GetMetadata(ComReferenceItemMetadataNames.wrapperTool), ComReferenceTypes.primaryortlbimp); // "The added reference should have the tlbimp/primary wrapper tool"

            rcr.AddMissingTlbReferences();
            Assert.Equal(4, rcr.allProjectRefs.Count); // "There should still be four references"
        }

        [Fact]
        public void BothKeyFileAndKeyContainer()
        {
            var rcr = new ResolveComReference();
            var e = new MockEngine();
            rcr.BuildEngine = e;

            rcr.KeyFile = "foo";
            rcr.KeyContainer = "bar";

            Assert.False(rcr.Execute());

            e.AssertLogContains("MSB3300");
        }

        [Fact]
        public void DelaySignWithoutEitherKeyFileOrKeyContainer()
        {
            var rcr = new ResolveComReference();
            var e = new MockEngine();
            rcr.BuildEngine = e;

            rcr.DelaySign = true;
            Assert.False(rcr.Execute());

            e.AssertLogContains("MSB3301");
        }

        /// <summary>
        /// Test if assemblies located in the gac get their CopyLocal attribute set to False
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void CheckSetCopyLocalToFalseOnEmbedInteropTypesAssemblies()
        {
            string gacPath = @"C:\windows\gac";

            ResolveComReference rcr = new ResolveComReference();
            rcr.BuildEngine = new MockEngine();

            // the matrix of TargetFrameworkVersion values we are testing
            string[] fxVersions =
            {
                "v2.0",
                "v3.0",
                "v3.5",
                "v4.0"
            };

            for (int i = 0; i < fxVersions.Length; i++)
            {
                string fxVersion = fxVersions[i];

                var taskItems = new List<ITaskItem>();

                var nonGacNoPrivate = new TaskItem(@"C:\windows\gar\test1.dll");
                nonGacNoPrivate.SetMetadata(ItemMetadataNames.embedInteropTypes, "true");

                var gacNoPrivate = new TaskItem(@"C:\windows\gac\assembly1.dll");
                gacNoPrivate.SetMetadata(ItemMetadataNames.embedInteropTypes, "true");

                var nonGacPrivateFalse = new TaskItem(@"C:\windows\gar\test1.dll");
                nonGacPrivateFalse.SetMetadata(ItemMetadataNames.privateMetadata, "false");
                nonGacPrivateFalse.SetMetadata(ItemMetadataNames.embedInteropTypes, "true");

                var gacPrivateFalse = new TaskItem(@"C:\windows\gac\assembly1.dll");
                gacPrivateFalse.SetMetadata(ItemMetadataNames.privateMetadata, "false");
                gacPrivateFalse.SetMetadata(ItemMetadataNames.embedInteropTypes, "true");

                var nonGacPrivateTrue = new TaskItem(@"C:\windows\gar\test1.dll");
                nonGacPrivateTrue.SetMetadata(ItemMetadataNames.privateMetadata, "true");
                nonGacPrivateTrue.SetMetadata(ItemMetadataNames.embedInteropTypes, "true");

                var gacPrivateTrue = new TaskItem(@"C:\windows\gac\assembly1.dll");
                gacPrivateTrue.SetMetadata(ItemMetadataNames.privateMetadata, "true");
                gacPrivateTrue.SetMetadata(ItemMetadataNames.embedInteropTypes, "true");

                taskItems.Add(nonGacNoPrivate);
                taskItems.Add(gacNoPrivate);

                taskItems.Add(nonGacPrivateFalse);
                taskItems.Add(gacPrivateFalse);

                taskItems.Add(nonGacPrivateTrue);
                taskItems.Add(gacPrivateTrue);

                rcr.TargetFrameworkVersion = fxVersion;
                rcr.SetFrameworkVersionFromString(rcr.TargetFrameworkVersion);

                rcr.SetCopyLocalToFalseOnGacOrNoPIAAssemblies(taskItems, gacPath);

                bool enabledNoPIA = false;
                switch (fxVersion)
                {
                    case "v4.0":
                        enabledNoPIA = true;
                        break;
                    default:
                        break;
                }

                // if Private is missing, by default GAC items are CopyLocal=false, non GAC CopyLocal=true
                Assert.Equal(nonGacNoPrivate.GetMetadata(ItemMetadataNames.copyLocal), enabledNoPIA ? "false" : "true");

                Assert.Equal(gacNoPrivate.GetMetadata(ItemMetadataNames.copyLocal), enabledNoPIA ? "false" : "false");

                // if Private is set, it takes precedence
                Assert.Equal(nonGacPrivateFalse.GetMetadata(ItemMetadataNames.copyLocal), enabledNoPIA ? "false" : "false");

                Assert.Equal(gacPrivateFalse.GetMetadata(ItemMetadataNames.copyLocal), enabledNoPIA ? "false" : "false");

                Assert.Equal(nonGacPrivateTrue.GetMetadata(ItemMetadataNames.copyLocal), enabledNoPIA ? "false" : "true");

                Assert.Equal(gacPrivateTrue.GetMetadata(ItemMetadataNames.copyLocal), enabledNoPIA ? "false" : "true");
            }
        }

        /// <summary>
        /// Test if assemblies located in the gac get their CopyLocal attribute set to False
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void CheckSetCopyLocalToFalseOnGacAssemblies()
        {
            string gacPath = @"C:\windows\gac";

            var rcr = new ResolveComReference();
            rcr.BuildEngine = new MockEngine();

            var taskItems = new List<ITaskItem>();
            TaskItem nonGacNoPrivate = new TaskItem(@"C:\windows\gar\test1.dll");
            TaskItem gacNoPrivate = new TaskItem(@"C:\windows\gac\assembly1.dll");

            TaskItem nonGacPrivateFalse = new TaskItem(@"C:\windows\gar\test1.dll");
            nonGacPrivateFalse.SetMetadata(ItemMetadataNames.privateMetadata, "false");
            TaskItem gacPrivateFalse = new TaskItem(@"C:\windows\gac\assembly1.dll");
            gacPrivateFalse.SetMetadata(ItemMetadataNames.privateMetadata, "false");

            TaskItem nonGacPrivateTrue = new TaskItem(@"C:\windows\gar\test1.dll");
            nonGacPrivateTrue.SetMetadata(ItemMetadataNames.privateMetadata, "true");
            TaskItem gacPrivateTrue = new TaskItem(@"C:\windows\gac\assembly1.dll");
            gacPrivateTrue.SetMetadata(ItemMetadataNames.privateMetadata, "true");

            taskItems.Add(nonGacNoPrivate);
            taskItems.Add(gacNoPrivate);

            taskItems.Add(nonGacPrivateFalse);
            taskItems.Add(gacPrivateFalse);

            taskItems.Add(nonGacPrivateTrue);
            taskItems.Add(gacPrivateTrue);

            rcr.SetCopyLocalToFalseOnGacOrNoPIAAssemblies(taskItems, gacPath);

            // if Private is missing, by default GAC items are CopyLocal=false, non GAC CopyLocal=true
            Assert.Equal("true", nonGacNoPrivate.GetMetadata(ItemMetadataNames.copyLocal)); // "Non Gac assembly, missing Private, should be TRUE"

            Assert.Equal("false", gacNoPrivate.GetMetadata(ItemMetadataNames.copyLocal)); // "Gac assembly, missing Private, should be FALSE"

            // if Private is set, it takes precedence
            Assert.Equal("false", nonGacPrivateFalse.GetMetadata(ItemMetadataNames.copyLocal)); // "Non Gac assembly, Private false, should be FALSE"

            Assert.Equal("false", gacPrivateFalse.GetMetadata(ItemMetadataNames.copyLocal)); // "Gac assembly, Private false, should be FALSE"

            Assert.Equal("true", nonGacPrivateTrue.GetMetadata(ItemMetadataNames.copyLocal)); // "Non Gac assembly, Private true, should be TRUE"

            Assert.Equal("true", gacPrivateTrue.GetMetadata(ItemMetadataNames.copyLocal)); // "Gac assembly, Private true, should be TRUE"
        }

        /// <summary>
        /// Make sure the conflicting references are detected correctly
        /// </summary>
        [Fact]
        public void TestCheckForConflictingReferences()
        {
            TYPELIBATTR axAttr, tlbAttr, piaAttr, notInProjectAttr;
            ComReferenceInfo axRefInfo, tlbRefInfo, piaRefInfo;

            CreateTestReferences(out axRefInfo, out tlbRefInfo, out piaRefInfo,
                out axAttr, out tlbAttr, out piaAttr, out notInProjectAttr);

            var rcr = new ResolveComReference();
            rcr.BuildEngine = new MockEngine();

            // populate the ResolveComReference's list of project references
            rcr.allProjectRefs = new List<ComReferenceInfo>();
            rcr.allProjectRefs.Add(axRefInfo);
            rcr.allProjectRefs.Add(tlbRefInfo);
            rcr.allProjectRefs.Add(piaRefInfo);

            // no conflicts should be found with just the three initial refs
            Assert.True(rcr.CheckForConflictingReferences());
            Assert.Equal(3, rcr.allProjectRefs.Count);

            
            // duplicate refs should not be treated as conflicts
            ComReferenceInfo referenceInfo = new ComReferenceInfo(tlbRefInfo);
            rcr.allProjectRefs.Add(referenceInfo);
            referenceInfo = new ComReferenceInfo(axRefInfo);
            rcr.allProjectRefs.Add(referenceInfo);
            referenceInfo = new ComReferenceInfo(piaRefInfo);
            rcr.allProjectRefs.Add(referenceInfo);

            Assert.True(rcr.CheckForConflictingReferences());
            Assert.Equal(6, rcr.allProjectRefs.Count);

            // tlb and ax refs with same lib name but different attributes should be considered conflicting
            // We don't care about typelib name conflicts for PIA refs, because we don't have to create wrappers for them
            var conflictTlb = new ComReferenceInfo(tlbRefInfo);
            conflictTlb.attr = notInProjectAttr;
            rcr.allProjectRefs.Add(conflictTlb);
            var conflictAx = new ComReferenceInfo(axRefInfo);
            conflictAx.attr = notInProjectAttr;
            rcr.allProjectRefs.Add(conflictAx);
            var piaRef = new ComReferenceInfo(piaRefInfo);
            piaRef.attr = notInProjectAttr;
            rcr.allProjectRefs.Add(piaRef);

            Assert.False(rcr.CheckForConflictingReferences());

            // ... and conflicting references should have been removed
            Assert.Equal(7, rcr.allProjectRefs.Count);
            Assert.DoesNotContain(conflictTlb, rcr.allProjectRefs);
            Assert.DoesNotContain(conflictAx, rcr.allProjectRefs);
            Assert.Contains(piaRef, rcr.allProjectRefs);
        }

        /// <summary>
        /// In order to make ResolveComReferences multitargetable, two properties, ExecuteAsTool
        /// and SdkToolsPath were added.  In order to have correct behavior when using pre-4.0
        /// toolsversions, ExecuteAsTool must default to true, and the paths to the tools will be the
        /// v3.5 path.  It is difficult to verify the tool paths in a unit test, however, so
        /// this was done by ad hoc testing and will be maintained by the dev suites.
        /// </summary>
        [Fact]
        public void MultiTargetingDefaultSetCorrectly()
        {
            ResolveComReference t = new ResolveComReference();

            Assert.True(t.ExecuteAsTool); // "ExecuteAsTool should default to true"
        }

        /// <summary>
        /// When calling AxImp.exe directly, the runtime-callable wrapper needs to be
        /// passed via the /rcw switch, so RCR needs to make sure that the ax reference knows about
        /// its corresponding TLB wrapper.
        /// </summary>
        [Fact]
        public void AxReferenceKnowsItsRCWCreateTlb()
        {
            CheckAxReferenceRCWTlbExists(RcwStyle.GenerateTlb /* have RCR create the TLB reference */, false /* don't include TLB version in the interop name */);
        }

        /// <summary>
        /// When calling AxImp.exe directly, the runtime-callable wrapper needs to be
        /// passed via the /rcw switch, so RCR needs to make sure that the ax reference knows about
        /// its corresponding TLB wrapper.
        /// </summary>
        [Fact]
        public void AxReferenceKnowsItsRCWCreateTlb_IncludeVersion()
        {
            CheckAxReferenceRCWTlbExists(RcwStyle.GenerateTlb /* have RCR create the TLB reference */, true /* include TLB version in the interop name */);
        }

        /// <summary>
        /// When calling AxImp.exe directly, the runtime-callable wrapper needs to be
        /// passed via the /rcw switch, so RCR needs to make sure that the ax reference knows about
        /// its corresponding TLB wrapper.
        /// </summary>
        [Fact]
        public void AxReferenceKnowsItsRCWTlbExists()
        {
            CheckAxReferenceRCWTlbExists(RcwStyle.PreexistingTlb /* pass in the TLB reference */, false /* don't include TLB version in the interop name */);
        }

        /// <summary>
        /// When calling AxImp.exe directly, the runtime-callable wrapper needs to be
        /// passed via the /rcw switch, so RCR needs to make sure that the ax reference knows about
        /// its corresponding TLB wrapper.
        ///
        /// Tests that still works when IncludeVersionInInteropName = true
        /// </summary>
        [Fact]
        public void AxReferenceKnowsItsRCWTlbExists_IncludeVersion()
        {
            CheckAxReferenceRCWTlbExists(RcwStyle.PreexistingTlb /* pass in the TLB reference */, true /* include TLB version in the interop name */);
        }

        /// <summary>
        /// When calling AxImp.exe directly, the runtime-callable wrapper needs to be
        /// passed via the /rcw switch, so RCR needs to make sure that the ax reference knows about
        /// its corresponding TLB wrapper.
        /// </summary>
        [Fact]
        public void AxReferenceKnowsItsRCWPiaExists()
        {
            CheckAxReferenceRCWTlbExists(RcwStyle.PreexistingPia /* pass in the TLB reference */, false /* don't include version in the interop name */);
        }

        /// <summary>
        /// When calling AxImp.exe directly, the runtime-callable wrapper needs to be
        /// passed via the /rcw switch, so RCR needs to make sure that the ax reference knows about
        /// its corresponding TLB wrapper.
        ///
        /// Tests that still works when IncludeVersionInInteropName = true
        /// </summary>
        [Fact]
        public void AxReferenceKnowsItsRCWPiaExists_IncludeVersion()
        {
            CheckAxReferenceRCWTlbExists(RcwStyle.PreexistingPia /* pass in the PIA reference */, true /* include version in the interop name */);
        }

        private enum RcwStyle { GenerateTlb, PreexistingTlb, PreexistingPia };

        /// <summary>
        /// Helper method that will new up an AX and matching TLB reference, and verify that the AX reference
        /// sets its RCW appropriately.
        /// </summary>
        private void CheckAxReferenceRCWTlbExists(RcwStyle rcwStyle, bool includeVersionInInteropName)
        {
            Guid axGuid = Guid.NewGuid();
            ComReferenceInfo tlbRefInfo;

            var rcr = new ResolveComReference();
            rcr.BuildEngine = new MockEngine();
            rcr.IncludeVersionInInteropName = includeVersionInInteropName;
            rcr.allProjectRefs = new List<ComReferenceInfo>();

            TaskItem axTaskItem = CreateComReferenceTaskItem("ref", axGuid.ToString(), "1", "2", "1033", ComReferenceTypes.aximp);
            ComReferenceInfo axRefInfo = CreateComReferenceInfo(axTaskItem, "RefLibName", "RefLibPath");
            rcr.allProjectRefs.Add(axRefInfo);

            switch (rcwStyle)
            {
                case RcwStyle.GenerateTlb: break;
                case RcwStyle.PreexistingTlb:
                    {
                        TaskItem tlbTaskItem = CreateComReferenceTaskItem("ref", axGuid.ToString(), "1", "2", "1033", ComReferenceTypes.tlbimp, "true");
                        tlbRefInfo = CreateComReferenceInfo(tlbTaskItem, "RefLibName", "RefLibPath");
                        rcr.allProjectRefs.Add(tlbRefInfo);
                        break;
                    }
                case RcwStyle.PreexistingPia:
                    {
                        TaskItem tlbTaskItem = CreateComReferenceTaskItem("ref", axGuid.ToString(), "1", "2", "1033", ComReferenceTypes.primary, "true");
                        tlbRefInfo = CreateComReferenceInfo(tlbTaskItem, "RefLibName", "RefLibPath");
                        rcr.allProjectRefs.Add(tlbRefInfo);
                        break;
                    }
            }

            rcr.AddMissingTlbReferences();

            Assert.Equal(2, rcr.allProjectRefs.Count); // "Should be two references"

            tlbRefInfo = rcr.allProjectRefs[1];
            var embedInteropTypes = tlbRefInfo.taskItem.GetMetadata(ItemMetadataNames.embedInteropTypes);
            Assert.Equal("false", embedInteropTypes); // "The tlb wrapper for the activex control should have EmbedInteropTypes=false not " + embedInteropTypes);
            Assert.True(ComReference.AreTypeLibAttrEqual(tlbRefInfo.attr, axRefInfo.attr)); // "reference information should be the same"
            Assert.Equal(TlbReference.GetWrapperFileName
                        (
                        axRefInfo.taskItem.GetMetadata(ComReferenceItemMetadataNames.tlbReferenceName),
                        includeVersionInInteropName,
                        axRefInfo.attr.wMajorVerNum,
                        axRefInfo.attr.wMinorVerNum
                        ),
                    TlbReference.GetWrapperFileName
                        (
                        tlbRefInfo.typeLibName,
                        includeVersionInInteropName,
                        tlbRefInfo.attr.wMajorVerNum,
                        tlbRefInfo.attr.wMinorVerNum
                        )); //                     "Expected Ax reference's RCW name to match the new TLB"
        }
    }
}

#endif
