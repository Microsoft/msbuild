// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Resources;
using System.Reflection;
using System.Collections;
using System.Xml;
using System.Text;
using System.Globalization;

using NUnit.Framework;

using Microsoft.Build.Framework;
using Microsoft.Build.BuildEngine;
using Microsoft.Build.BuildEngine.Shared;
using Microsoft.Win32;
using System.Configuration;
using System.Diagnostics;

namespace Microsoft.Build.UnitTests.Project_Tests
{
    [TestFixture]
    public class AddItem
    {
        /// <summary>
        /// This loads an existing project, and uses the MSBuild object model to
        /// add a new item (Type="Compile" Include="c.cs") to the project.  Then
        /// it compares the final project XML to make sure the item was added in
        /// the correct place.
        /// </summary>
        /// <param name="originalProjectContents"></param>
        /// <param name="newExpectedProjectContents"></param>
        /// <param name="newItemType"></param>
        /// <param name="newItemInclude"></param>
        /// <returns></returns>
        /// <owner>RGoel</owner>
        internal static BuildItem AddNewItemHelper
            (
            string originalProjectContents,
            string newExpectedProjectContents,
            string newItemType,
            string newItemInclude
            )
        {
            Project project = ObjectModelHelpers.CreateInMemoryProject(originalProjectContents);

            // The project shouldn't be marked dirty yet.
            Assertion.Assert("Project shouldn't be dirty", !project.IsDirtyNeedToReevaluate);

            // Add a new item (Type="Compile", Include="c.cs") to the project using
            // the object model.
            BuildItem newItem = project.AddNewItem(newItemType, newItemInclude);

            // The project should be marked dirty now.
            Assertion.Assert("Project should be dirty", project.IsDirtyNeedToReevaluate);

            ObjectModelHelpers.CompareProjectContents(project, newExpectedProjectContents);

            return newItem;
        }

        /// <summary>
        /// This loads an existing project that contains items within <ItemGroup>s.
        /// It then uses the MSBuild object model to
        /// add a new item to the project.  Then it compares the final project
        /// XML to make sure the item was added in the correct place.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void AddNewItemToExistingItemGroup()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <ItemGroup>
                        <Compile Include=`b.cs` />
                        <Compile Include=`c.cs` />
                    </ItemGroup>

                    <ItemGroup>
                        <Reference Include=`System` />
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <ItemGroup>
                        <Compile Include=`a.cs` />
                        <Compile Include=`b.cs` />
                        <Compile Include=`c.cs` />
                    </ItemGroup>

                    <ItemGroup>
                        <Reference Include=`System` />
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";

            AddNewItemHelper(projectOriginalContents, projectNewExpectedContents,
                "Compile", "a.cs");
        }

        /// <summary>
        /// This tests that the project is correctly marked as dirty when we
        /// add a new item to the project.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void AddNewItemToNewItemGroup()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <WarningLevel>4</WarningLevel>
                    </PropertyGroup>

                    <ItemGroup>
                        <Compile Include=`a.cs`>
                            <HintPath>hint</HintPath>
                        </Compile>
                        <Compile Include=`b.cs` />
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <WarningLevel>4</WarningLevel>
                    </PropertyGroup>

                    <ItemGroup>
                        <Compile Include=`a.cs`>
                            <HintPath>hint</HintPath>
                        </Compile>
                        <Compile Include=`b.cs` />
                    </ItemGroup>

                    <ItemGroup>
                        <Resource Include=`strings.resx` />
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";

            AddNewItemHelper(projectOriginalContents, projectNewExpectedContents,
                "Resource", "strings.resx");
        }

        /// <summary>
        /// This loads an existing project that did not contain any items previously.
        /// It then uses the MSBuild object model to
        /// add a new item to the project.  Then it compares the final project
        /// XML to make sure the item was added in the correct place.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void AddNewItemWithNoPreviousItemGroup()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <Foo>bar</Foo>
                    </PropertyGroup>

                    <Target Name=`Build` />

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <Foo>bar</Foo>
                    </PropertyGroup>

                    <ItemGroup>
                        <Compile Include=`c.cs` />
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";

            AddNewItemHelper(projectOriginalContents, projectNewExpectedContents,
                "Compile", "c.cs");
        }

        /// <summary>
        /// This adds a new item into the project, and then immediately queries
        /// the item for an evaluated attribute, which of course doesn't exist yet.
        /// We're testing to make sure we don't throw an exception in this case.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void AddNewItemAndQueryForNonExistentMetadata()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <ItemGroup>
                        <Compile Include=`foo.cs` />
                    </ItemGroup>

                </Project>
                ";

            BuildItem newItem = AddNewItemHelper(projectOriginalContents,
                projectNewExpectedContents, "Compile", "foo.cs");

            string hintPath = newItem.GetEvaluatedMetadata("HintPath");

            Assertion.AssertEquals(String.Empty, hintPath);
        }

        /// <summary>
        /// Add a new item of the same name and include path of an item that already
        /// exists in the project.  Current behavior is that we add the duplicated item,
        /// although there's no great reason for this.  If we wanted, we could have
        /// made it so that adding a dup results in a no-op to the project file.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void AddNewDuplicate()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <ItemGroup>
                        <MyWildCard Include=`foo.weirdo`/>
                    </ItemGroup>
                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <ItemGroup>
                        <MyWildCard Include=`foo.weirdo`/>
                        <MyWildCard Include=`foo.weirdo`/>
                    </ItemGroup>
                </Project>
                ";

            BuildItem newItem = AddNewItemHelper(projectOriginalContents,
                projectNewExpectedContents, "MyWildCard", "foo.weirdo");
        }

        /// <summary>
        /// If user tries to add a new item that has the same item name as an existing
        /// wildcarded item, but the wildcard won't pick up the new file, then we
        /// of course have to add the new item.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void AddNewItemThatDoesntMatchWildcard()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <ItemGroup>
                        <MyWildCard Include=`*.weirdo`/>
                    </ItemGroup>
                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <ItemGroup>
                        <MyWildCard Include=`*.weirdo`/>
                        <MyWildCard Include=`foo.txt`/>
                    </ItemGroup>
                </Project>
                ";

            BuildItem newItem = AddNewItemHelper(projectOriginalContents,
                projectNewExpectedContents, "MyWildCard", "foo.txt");
        }

        /// <summary>
        /// In order to match a new item with a wildcard already in the project,
        /// they of course have to have the same name.  If the item names differ,
        /// then we just add the new item.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void AddNewItemThatMatchesWildcardWithDifferentItemName()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <ItemGroup>
                        <MyWildCard Include=`*.weirdo`/>
                    </ItemGroup>
                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <ItemGroup>
                        <MyWildCard Include=`*.weirdo`/>
                    </ItemGroup>
                    <ItemGroup>
                        <MyNewItemName Include=`foo.weirdo`/>
                    </ItemGroup>
                </Project>
                ";

            BuildItem newItem = AddNewItemHelper(projectOriginalContents,
                projectNewExpectedContents, "MyNewItemName", "foo.weirdo");
        }

        /// <summary>
        /// When the wildcarded item already in the project file has a Condition
        /// on it, we don't try to match with it when a user tries to add a new
        /// item to the project.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void AddNewItemThatMatchesWildcardWithCondition()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <ItemGroup>
                        <MyWildCard Include=`*.weirdo` Condition=`'1'=='1'`/>
                    </ItemGroup>
                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <ItemGroup>
                        <MyWildCard Include=`*.weirdo` Condition=`'1'=='1'`/>
                        <MyWildCard Include=`foo.weirdo`/>
                    </ItemGroup>
                </Project>
                ";

            BuildItem newItem = AddNewItemHelper(projectOriginalContents,
                projectNewExpectedContents, "MyWildCard", "foo.weirdo");
        }

        /// <summary>
        /// When the wildcarded item already in the project file has a Exclude
        /// on it, we don't try to match with it when a user tries to add a new
        /// item to the project.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void AddNewItemThatMatchesWildcardWithExclude()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <ItemGroup>
                        <MyWildCard Include=`*.weirdo` Exclude=`bar.weirdo`/>
                    </ItemGroup>
                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <ItemGroup>
                        <MyWildCard Include=`*.weirdo` Exclude=`bar.weirdo`/>
                        <MyWildCard Include=`foo.weirdo`/>
                    </ItemGroup>
                </Project>
                ";

            BuildItem newItem = AddNewItemHelper(projectOriginalContents,
                projectNewExpectedContents, "MyWildCard", "foo.weirdo");
        }

        /// <summary>
        /// There's a wildcard in the project already, and the user tries to add an item
        /// that matches that wildcard.  In this case, we don't touch the project at all.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void AddNewItemThatMatchesWildcard()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <ItemGroup>
                        <MyWildCard Include=`*.weirdo`/>
                    </ItemGroup>
                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <ItemGroup>
                        <MyWildCard Include=`*.weirdo`/>
                    </ItemGroup>
                </Project>
                ";

            BuildItem newItem = AddNewItemHelper(projectOriginalContents,
                projectNewExpectedContents, "MyWildCard", "foo.weirdo");

            Assertion.AssertEquals("Newly added item should have correct ItemName", "MyWildCard", newItem.Name);
            Assertion.AssertEquals("Newly added item should have correct Include", "*.weirdo", newItem.Include);
            Assertion.AssertEquals("Newly added item should have correct FinalItemSpec", "foo.weirdo", newItem.FinalItemSpecEscaped);
        }

        /// <summary>
        /// There's a complicated recursive wildcard in the project already, and the user tries to add an item
        /// that matches that wildcard.  In this case, we don't touch the project at all.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void AddNewItemThatMatchesComplicatedWildcard()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <ItemGroup>
                        <MyWildCard Include=`c:\subdir1\**\subdir2\**\*.we?rdo`/>
                    </ItemGroup>
                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <ItemGroup>
                        <MyWildCard Include=`c:\subdir1\**\subdir2\**\*.we?rdo`/>
                    </ItemGroup>
                </Project>
                ";

            BuildItem newItem = AddNewItemHelper(projectOriginalContents,
                projectNewExpectedContents, "MyWildCard", @"c:\subdir1\xmake\engine\subdir2\items\foo.weirdo");

            Assertion.AssertEquals("Newly added item should have correct ItemName", "MyWildCard", newItem.Name);
            Assertion.AssertEquals("Newly added item should have correct Include", @"c:\subdir1\**\subdir2\**\*.we?rdo", newItem.Include);
            Assertion.AssertEquals("Newly added item should have correct FinalItemSpec", @"c:\subdir1\xmake\engine\subdir2\items\foo.weirdo", newItem.FinalItemSpecEscaped);
        }

        /// <summary>
        /// There's a complicated recursive wildcard in the project already, and the user tries to add an item
        /// that matches that wildcard.  In this case, we don't touch the project at all.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void AddNewItemThatDoesntMatchComplicatedWildcard()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <ItemGroup>
                        <MyWildCard Include=`c:\subdir1\**\subdir2\**\*.we?rdo`/>
                    </ItemGroup>
                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <ItemGroup>
                        <MyWildCard Include=`c:\subdir1\**\subdir2\**\*.we?rdo`/>
                        <MyWildCard Include=`c:\subdir1\xmake\engine\items\foo.weirdo`/>
                    </ItemGroup>
                </Project>
                ";

            BuildItem newItem = AddNewItemHelper(projectOriginalContents,
                projectNewExpectedContents, "MyWildCard", @"c:\subdir1\xmake\engine\items\foo.weirdo");

            Assertion.AssertEquals("Newly added item should have correct ItemName", "MyWildCard", newItem.Name);
            Assertion.AssertEquals("Newly added item should have correct Include", @"c:\subdir1\xmake\engine\items\foo.weirdo", newItem.Include);
            Assertion.AssertEquals("Newly added item should have correct FinalItemSpec", @"c:\subdir1\xmake\engine\items\foo.weirdo", newItem.FinalItemSpecEscaped);
        }

        /// <summary>
        /// There's a wildcard in the project already, and the user tries to add an item
        /// that matches that wildcard.  In this case, we don't touch the project at all.
        /// Then take the item that you got back from Project.AddNewItem and try and modify
        /// its metadata.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void AddNewItemThatMatchesWildcardAndThenModifyIt()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <ItemGroup>
                        <MyWildCard Include=`a.cs; *.weirdo; c.cs`>
                            <Culture>fr</Culture>
                        </MyWildCard>
                    </ItemGroup>
                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <ItemGroup>
                        <MyWildCard Include=`a.cs; *.weirdo; c.cs`>
                            <Culture>fr</Culture>
                        </MyWildCard>
                    </ItemGroup>
                </Project>
                ";

            BuildItem newItem = AddNewItemHelper(projectOriginalContents,
                projectNewExpectedContents, "MyWildCard", "foo.weirdo");

            Assertion.AssertEquals("Newly added item should have correct ItemName", "MyWildCard", newItem.Name);
            Assertion.AssertEquals("Newly added item should have correct Include", "a.cs; *.weirdo; c.cs", newItem.Include);
            Assertion.AssertEquals("Newly added item should have correct FinalItemSpec", "foo.weirdo", newItem.FinalItemSpecEscaped);

            newItem.SetMetadata("Culture", "en");

            // ************************************
            //               AFTER MODIFICATION
            // ************************************
            string projectFinalExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <ItemGroup>
                        <MyWildCard Include=`a.cs`>
                            <Culture>fr</Culture>
                        </MyWildCard>
                        <MyWildCard Include=`c.cs`>
                            <Culture>fr</Culture>
                        </MyWildCard>
                        <MyWildCard Include=`foo.weirdo`>
                            <Culture>en</Culture>
                        </MyWildCard>
                    </ItemGroup>
                </Project>
                ";

            ObjectModelHelpers.CompareProjectContents(newItem.ParentPersistedItem.ParentPersistedItemGroup.ParentProject,
                projectFinalExpectedContents);
        }

        /// <summary>
        /// There's a wildcard in the project already, and the user tries to add an item
        /// that matches that wildcard.  In this case, we don't touch the project at all,
        /// even though the existing item had metadata on it.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void AddNewItemThatMatchesWildcardWithMetadata()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <MyCulture>fr</MyCulture>
                    </PropertyGroup>

                    <ItemGroup>
                        <MyWildCard Include=`*.weirdo`>
                            <Culture>$(MyCulture)</Culture>
                        </MyWildCard>
                    </ItemGroup>

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <MyCulture>fr</MyCulture>
                    </PropertyGroup>

                    <ItemGroup>
                        <MyWildCard Include=`*.weirdo`>
                            <Culture>$(MyCulture)</Culture>
                        </MyWildCard>
                    </ItemGroup>

                </Project>
                ";

            BuildItem newItem = AddNewItemHelper(projectOriginalContents,
                projectNewExpectedContents, "MyWildCard", "foo.weirdo");

            Assertion.AssertEquals("Newly added item should have correct ItemName", "MyWildCard", newItem.Name);
            Assertion.AssertEquals("Newly added item should have correct Include", "*.weirdo", newItem.Include);
            Assertion.AssertEquals("Newly added item should have correct FinalItemSpec", "foo.weirdo", newItem.FinalItemSpecEscaped);
            Assertion.AssertEquals("Newly added item should have correct metadata Culture", "$(MyCulture)", newItem.GetMetadata("Culture"));
            Assertion.AssertEquals("Newly added item should have correct evaluated metadata Culture", "fr", newItem.GetEvaluatedMetadata("Culture"));
        }

        /// <summary>
        /// There's a wildcard in the project already, but it's part of a semicolon-separated
        /// list of items.  Now the user tries to add an item that matches that wildcard.
        /// In this case, we don't touch the project at all.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void AddNewItemThatMatchesWildcardInSemicolonList()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <ItemGroup>
                        <MyWildCard Include=`a.cs; *.weirdo; c.cs`/>
                    </ItemGroup>
                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <ItemGroup>
                        <MyWildCard Include=`a.cs; *.weirdo; c.cs`/>
                    </ItemGroup>
                </Project>
                ";

            BuildItem newItem = AddNewItemHelper(projectOriginalContents,
                projectNewExpectedContents, "MyWildCard", "foo.weirdo");

            Assertion.AssertEquals("Newly added item should have correct ItemName", "MyWildCard", newItem.Name);
            Assertion.AssertEquals("Newly added item should have correct Include", "a.cs; *.weirdo; c.cs", newItem.Include);
            Assertion.AssertEquals("Newly added item should have correct FinalItemSpec", "foo.weirdo", newItem.FinalItemSpecEscaped);
        }

        /// <summary>
        /// There's a wildcard in the project already, but it's part of a semicolon-separated
        /// list of items, and it uses a property reference.  Now the user tries to add a new
        /// item that matches that wildcard.  In this case, we don't touch the project at all.
        /// We're so smart.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void AddNewItemThatMatchesWildcardWithPropertyReference()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <MySpecialFileExtension>weirdo</MySpecialFileExtension>
                    </PropertyGroup>

                    <ItemGroup>
                        <MyWildCard Include=`a.cs; *.$(MySpecialFileExtension); c.cs`/>
                    </ItemGroup>

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <MySpecialFileExtension>weirdo</MySpecialFileExtension>
                    </PropertyGroup>

                    <ItemGroup>
                        <MyWildCard Include=`a.cs; *.$(MySpecialFileExtension); c.cs`/>
                    </ItemGroup>

                </Project>
                ";

            BuildItem newItem = AddNewItemHelper(projectOriginalContents,
                projectNewExpectedContents, "MyWildCard", "foo.weirdo");

            Assertion.AssertEquals("Newly added item should have correct ItemName", "MyWildCard", newItem.Name);
            Assertion.AssertEquals("Newly added item should have correct Include", "a.cs; *.$(MySpecialFileExtension); c.cs", newItem.Include);
            Assertion.AssertEquals("Newly added item should have correct FinalItemSpec", "foo.weirdo", newItem.FinalItemSpecEscaped);
        }

        /// <summary>
        /// This tests that the project is correctly marked as dirty when we
        /// add a new ItemGroup to the project.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void AddNewItemGroup()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <WarningLevel>4</WarningLevel>
                    </PropertyGroup>

                    <ItemGroup>
                        <Compile Include=`a.cs`>
                            <HintPath>hint</HintPath>
                        </Compile>
                        <Compile Include=`b.cs` />
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <WarningLevel>4</WarningLevel>
                    </PropertyGroup>

                    <ItemGroup>
                        <Compile Include=`a.cs`>
                            <HintPath>hint</HintPath>
                        </Compile>
                        <Compile Include=`b.cs` />
                    </ItemGroup>

                    <ItemGroup />

                    <Target Name=`Build` />

                </Project>
                ";

            Project project = ObjectModelHelpers.CreateInMemoryProject(projectOriginalContents);

            Assertion.Assert("Project shouldn't be dirty", !project.IsDirtyNeedToReevaluate);

            project.AddNewItemGroup();

            Assertion.Assert("Project should be dirty", project.IsDirtyNeedToReevaluate);

            ObjectModelHelpers.CompareProjectContents(project, projectNewExpectedContents);
        }
    }

    [TestFixture]
    public class RemoveItem
    {
        /// <summary>
        /// This loads an existing project, and uses the MSBuild object model to
        /// remove an item of a particular item spec (e.g., "b.cs").  It then
        /// compares the final project XML to make sure the item was added in
        /// the correct place.
        /// </summary>
        /// <param name="originalProjectContents"></param>
        /// <param name="newExpectedProjectContents"></param>
        /// <param name="itemSpecToRemove"></param>
        /// <owner>RGoel</owner>
        private void RemoveItemHelper
            (
            string originalProjectContents,
            string newExpectedProjectContents,
            string itemSpecToRemove
            )
        {
            Project project = ObjectModelHelpers.CreateInMemoryProject(originalProjectContents);

            // The project shouldn't be marked dirty yet.
            Assertion.Assert("Project shouldn't be dirty", !project.IsDirtyNeedToReevaluate);

            // Get the set of evaluated items.
            BuildItemGroup evaluatedItems = project.EvaluatedItemsIgnoringCondition;

            // The VS IDE does a few re-evaluations with different sets of global properties
            // (i.e., Configuration=Debug, Configuration=Release, etc.).  This is to simulate
            // that.  If there's a bug in the Project object, then re-evaluation can
            // potentially mess up the number of items hanging around.
            project.MarkProjectAsDirty ();
            BuildItemGroup evaluatedItems2 = project.EvaluatedItemsIgnoringCondition;

            // The project should be marked dirty now.
            Assertion.Assert("Project should be dirty", project.IsDirtyNeedToReevaluate);

            // Search all the evaluated items for the one with the item spec we want
            // to remove.
            foreach (BuildItem evaluatedItem in evaluatedItems)
            {
                if (evaluatedItem.FinalItemSpecEscaped == itemSpecToRemove)
                {
                    project.RemoveItem (evaluatedItem);
                }
            }

            // The project should still be marked dirty now.
            Assertion.Assert("Project should be dirty", project.IsDirtyNeedToReevaluate);

            ObjectModelHelpers.CompareProjectContents(project, newExpectedProjectContents);
        }

        /// <summary>
        /// This loads an existing project that contained a few items, and tries
        /// to remove one of them through the MSBuild object model.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void RemoveItemBySpec()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <ItemGroup>
                        <Compile Include=`a.cs` />
                        <Compile Include=`b.cs` />
                        <Compile Include=`c.cs` />
                    </ItemGroup>

                    <ItemGroup>
                        <Reference Include=`System` />
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <ItemGroup>
                        <Compile Include=`a.cs` />
                        <Compile Include=`c.cs` />
                    </ItemGroup>

                    <ItemGroup>
                        <Reference Include=`System` />
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";

            this.RemoveItemHelper (projectOriginalContents, projectNewExpectedContents, "b.cs");
        }

        /// <summary>
        /// This loads an existing project that contained an item tag that
        /// declares several items with a single tag.  Then it tries
        /// to remove one of those items through the MSBuild object model.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void RemoveItemBySpecFromMultiItemSpec()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <File>a</File>
                    </PropertyGroup>

                    <ItemGroup>
                        <Compile Include=`$(File).cs; b.cs; c.cs` />
                        <Compile Include=`d.cs` />
                    </ItemGroup>

                    <ItemGroup>
                        <Reference Include=`System` />
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <File>a</File>
                    </PropertyGroup>

                    <ItemGroup>
                        <Compile Include=`a.cs` />
                        <Compile Include=`c.cs` />
                        <Compile Include=`d.cs` />
                    </ItemGroup>

                    <ItemGroup>
                        <Reference Include=`System` />
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";

            this.RemoveItemHelper (projectOriginalContents, projectNewExpectedContents, "b.cs");
        }

        /// <summary>
        /// This loads an existing project that contained an item tag that
        /// declares several items with a single tag.  Then it tries
        /// to remove one of those items through the MSBuild object model.
        /// The trick here is to test that we correctly preserve the metadata
        /// on the items when we do this.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void RemoveItemBySpecFromMultiItemSpecWithMetadata()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <ItemGroup>
                        <Compile Include=`a.cs; b.cs; c.cs`>
                            <HintPath>$(mypath)</HintPath>
                        </Compile>
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <ItemGroup>
                        <Compile Include=`a.cs`>
                            <HintPath>$(mypath)</HintPath>
                        </Compile>
                        <Compile Include=`c.cs`>
                            <HintPath>$(mypath)</HintPath>
                        </Compile>
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";

            this.RemoveItemHelper (projectOriginalContents, projectNewExpectedContents, "b.cs");
        }

        /// <summary>
        /// Another simple test of removing a single item.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void RemoveItemBySpecWhenMultiItemSpecExists()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <ItemGroup>
                        <Compile Include=`a.cs; b.cs; c.cs`>
                            <HintPath>$(mypath)</HintPath>
                        </Compile>
                        <Compile Include=`d.cs` />
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <ItemGroup>
                        <Compile Include=`a.cs; b.cs; c.cs`>
                            <HintPath>$(mypath)</HintPath>
                        </Compile>
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";

            this.RemoveItemHelper (projectOriginalContents, projectNewExpectedContents, "d.cs");
        }

        /// <summary>
        /// This tests that the project is correctly marked as dirty when we
        /// remove an item.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void RemoveSpecificItem()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <WarningLevel>4</WarningLevel>
                    </PropertyGroup>

                    <ItemGroup>
                        <Compile Include=`a.cs`>
                            <HintPath>hint</HintPath>
                        </Compile>
                        <Compile Include=`b.cs` />
                        <Resource Include=`strings.resx` />
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <WarningLevel>4</WarningLevel>
                    </PropertyGroup>

                    <ItemGroup>
                        <Compile Include=`b.cs` />
                        <Resource Include=`strings.resx` />
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";

            Project project = ObjectModelHelpers.CreateInMemoryProject(projectOriginalContents);

            Assertion.Assert("Project shouldn't be dirty", !project.IsDirtyNeedToReevaluate);

            BuildItemGroup evaluatedItems = project.EvaluatedItemsIgnoringCondition;
            BuildItem itemToRemove = null;
            foreach (BuildItem item in evaluatedItems)
            {
                if (item.Include == "a.cs")
                {
                    itemToRemove = item;
                }
            }
            Assertion.Assert(itemToRemove != null);
            project.RemoveItem(itemToRemove);

            Assertion.Assert("Project should be dirty", project.IsDirtyNeedToReevaluate);

            ObjectModelHelpers.CompareProjectContents(project, projectNewExpectedContents);
        }

        /// <summary>
        /// This tests that the project is correctly marked as dirty when we
        /// remove a whole class of items from the project.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void RemoveItemsByName()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <WarningLevel>4</WarningLevel>
                    </PropertyGroup>

                    <ItemGroup>
                        <Compile Include=`a.cs`>
                            <HintPath>hint</HintPath>
                        </Compile>
                        <Compile Include=`b.cs` />
                        <Resource Include=`strings.resx` />
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <WarningLevel>4</WarningLevel>
                    </PropertyGroup>

                    <ItemGroup>
                        <Resource Include=`strings.resx` />
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";

            Project project = ObjectModelHelpers.CreateInMemoryProject(projectOriginalContents);

            Assertion.Assert("Project shouldn't be dirty", !project.IsDirtyNeedToReevaluate);

            project.RemoveItemsByName("Compile");

            Assertion.Assert("Project should be dirty", project.IsDirtyNeedToReevaluate);

            ObjectModelHelpers.CompareProjectContents(project, projectNewExpectedContents);
        }

        /// <summary>
        /// This tests that the project is correctly marked as dirty when we
        /// remove an ItemGroup from the project.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void RemoveItemGroup()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <WarningLevel>4</WarningLevel>
                    </PropertyGroup>

                    <ItemGroup>
                        <Compile Include=`a.cs`>
                            <HintPath>hint</HintPath>
                        </Compile>
                        <Compile Include=`b.cs` />
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <WarningLevel>4</WarningLevel>
                    </PropertyGroup>

                    <Target Name=`Build` />

                </Project>
                ";

            Project project = ObjectModelHelpers.CreateInMemoryProject(projectOriginalContents);

            Assertion.Assert("Project shouldn't be dirty", !project.IsDirtyNeedToReevaluate);

            project.RemoveItemGroup(project.ItemGroups.LastLocalItemGroup);

            Assertion.Assert("Project should be dirty", project.IsDirtyNeedToReevaluate);

            ObjectModelHelpers.CompareProjectContents(project, projectNewExpectedContents);
        }

        /// <summary>
        /// Test the RemoveAllItemGroups method.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void RemoveAllItemGroups()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string original = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <ItemGroup Condition=`'$(x)'=='y'`>
                        <ReferencePath Include='c:\foobar'/>
                    </ItemGroup>

                    <ItemGroup Condition=`'$(x)'=='z'`>
                        <ReferencePath Include='c:\foobar'/>
                    </ItemGroup>

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string expected = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                </Project>
                ";

            Project project = ObjectModelHelpers.CreateInMemoryProject(original);
            Assertion.AssertEquals(2, project.ItemGroups.Count);

            project.RemoveAllItemGroups();

            Assertion.AssertEquals(0, project.ItemGroups.Count);
            ObjectModelHelpers.CompareProjectContents(project, expected);
        }

        /// <summary>
        /// Test the Unload method
        /// </summary>
        [Test]
        public void TestUnload()
        {
            string original = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <ItemGroup Condition=`'$(x)'=='y'`>
                        <ReferencePath Include=`c:\foobar` />
                    </ItemGroup>
                </Project>
                ";

            Project project = ObjectModelHelpers.CreateInMemoryProject(original);
            bool exceptionThrown = false;

            // Unload to cover the method
            project.ParentEngine.UnloadProject(project);
            try
            {
                Engine engine = project.ParentEngine;
            }
            catch (InvalidOperationException e)
            {
                exceptionThrown = true;
                Assertion.AssertEquals(AssemblyResources.GetString("ProjectInvalidUnloaded"), e.Message);
            }

            Assertion.AssertEquals(true, exceptionThrown);
        }

        /// <summary>
        /// Test the RemoveAllItemGroups method.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void RemoveAllItemGroupsWithChoose()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string original = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <ItemGroup Condition=`'$(x)'=='y'`>
                        <ReferencePath Include='c:\foobar'/>
                    </ItemGroup>

                    <Choose>
                        <When Condition = `true`>
                            <ItemGroup Condition=`'$(x)'=='z'`>
                                <ReferencePath Include='c:\foobar'/>
                            </ItemGroup>
                        </When>
                        <Otherwise>
                            <ItemGroup Condition=`'$(x)'=='v'`>
                                <ReferencePath Include='c:\foobar'/>
                            </ItemGroup>
                        </Otherwise>
                    </Choose>

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string expected = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <Choose>
                        <When Condition=`true`>
                        </When>
                        <Otherwise>
                        </Otherwise>
                    </Choose>

                </Project>
                ";

            Project project = ObjectModelHelpers.CreateInMemoryProject(original);
            Assertion.AssertEquals(3, project.ItemGroups.Count);

            project.RemoveAllItemGroups();

            Assertion.AssertEquals(0, project.ItemGroups.Count);
            ObjectModelHelpers.CompareProjectContents(project, expected);
        }

        /// <summary>
        /// Test the RemoveAllItemGroupsByCondition method.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void RemoveAllItemGroupsByCondition()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string original = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <ItemGroup Condition=`'$(x)'=='y'`>
                        <ReferencePath Include=`c:\foobar` />
                    </ItemGroup>

                    <ItemGroup Condition=`'$(x)'=='z'`>
                        <ReferencePath Include=`c:\foobar` />
                    </ItemGroup>

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string expected = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <ItemGroup Condition=`'$(x)'=='z'`>
                        <ReferencePath Include=`c:\foobar` />
                    </ItemGroup>

                </Project>
                ";

            Project project = ObjectModelHelpers.CreateInMemoryProject(original);
            Assertion.AssertEquals(2, project.ItemGroups.Count);

            project.RemoveItemGroupsWithMatchingCondition("'$(x)'=='y'");

            Assertion.AssertEquals(1, project.ItemGroups.Count);
            ObjectModelHelpers.CompareProjectContents(project, expected);
        }


        /// <summary>
        /// Test the RemoveAllItemGroupsByCondition method.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void RemoveAllItemGroupsByConditionWithChoose()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string original = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <ItemGroup Condition=`'$(x)'=='y'`>
                        <ReferencePath Include=`c:\foobar` />
                    </ItemGroup>

                    <ItemGroup Condition=`'$(x)'=='z'`>
                        <ReferencePath Include=`c:\foobar` />
                    </ItemGroup>

                    <Choose>
                        <When Condition = `true`>
                            <ItemGroup Condition=`'$(x)'=='y'`>
                                <ReferencePath Include=`c:\foobar` />
                            </ItemGroup>

                            <ItemGroup Condition=`'$(x)'=='z'`>
                                <ReferencePath Include=`c:\foobar` />
                            </ItemGroup>
                        </When>
                        <Otherwise>
                            <ItemGroup Condition=`'$(x)'=='y'`>
                                <ReferencePath Include=`c:\foobar` />
                            </ItemGroup>

                            <ItemGroup Condition=`'$(x)'=='z'`>
                                <ReferencePath Include=`c:\foobar` />
                            </ItemGroup>
                        </Otherwise>
                    </Choose>

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string expected = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <ItemGroup Condition=`'$(x)'=='z'`>
                        <ReferencePath Include=`c:\foobar` />
                    </ItemGroup>

                    <Choose>
                        <When Condition=`true`>
                            <ItemGroup Condition=`'$(x)'=='z'`>
                                <ReferencePath Include=`c:\foobar` />
                            </ItemGroup>
                        </When>
                        <Otherwise>
                            <ItemGroup Condition=`'$(x)'=='z'`>
                                <ReferencePath Include=`c:\foobar` />
                            </ItemGroup>
                        </Otherwise>
                    </Choose>

                </Project>
                ";

            Project project = ObjectModelHelpers.CreateInMemoryProject(original);
            Assertion.AssertEquals(6, project.ItemGroups.Count);

            project.RemoveItemGroupsWithMatchingCondition("'$(x)'=='y'");

            Assertion.AssertEquals(3, project.ItemGroups.Count);
            ObjectModelHelpers.CompareProjectContents(project, expected);
        }

        /// <summary>
        /// Test the RemoveAllItemGroupsByCondition method.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void RemoveItemsByNameWithChoose()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string original = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <ItemGroup Condition=`'$(x)'=='y'`>
                        <ReferencePath Include=`c:\foobar` />
                    </ItemGroup>

                    <ItemGroup Condition=`'$(x)'=='z'`>
                        <IncludePath Include=`c:\foobaz` />
                    </ItemGroup>

                    <Choose>
                        <When Condition = `true`>
                            <ItemGroup Condition=`'$(x)'=='y'`>
                                <IncludePath Include=`c:\foobaz` />
                            </ItemGroup>

                            <ItemGroup Condition=`'$(x)'=='z'`>
                                <ReferencePath Include=`c:\foobar` />
                            </ItemGroup>
                        </When>
                        <Otherwise>
                            <ItemGroup Condition=`'$(x)'=='y'`>
                                <IncludePath Include=`c:\foobaz` />
                            </ItemGroup>

                            <ItemGroup Condition=`'$(x)'=='z'`>
                                <ReferencePath Include=`c:\foobar` />
                            </ItemGroup>
                        </Otherwise>
                    </Choose>

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string expected = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <ItemGroup Condition=`'$(x)'=='y'`>
                        <ReferencePath Include=`c:\foobar` />
                    </ItemGroup>

                    <Choose>
                        <When Condition=`true`>
                            <ItemGroup Condition=`'$(x)'=='z'`>
                                <ReferencePath Include=`c:\foobar` />
                            </ItemGroup>
                        </When>
                        <Otherwise>
                            <ItemGroup Condition=`'$(x)'=='z'`>
                                <ReferencePath Include=`c:\foobar` />
                            </ItemGroup>
                        </Otherwise>
                    </Choose>

                </Project>
                ";

            Project project = ObjectModelHelpers.CreateInMemoryProject(original);
            Assertion.AssertEquals(6, project.ItemGroups.Count);

            project.RemoveItemsByName("IncludePath");

            Assertion.AssertEquals(3, project.ItemGroups.Count);
            ObjectModelHelpers.CompareProjectContents(project, expected);
        }
    }

    [TestFixture]
    public class ModifyItem
    {
        /// <summary>
        /// This loads an existing project, and uses the MSBuild object model to
        /// modify the "Include" attribute of an item of a particular item spec (e.g.,
        /// "b.cs").  It then compares the final project XML to make sure the item was
        /// modified correctly.
        /// </summary>
        /// <param name="originalProjectContents"></param>
        /// <param name="newExpectedProjectContents"></param>
        /// <param name="oldItemSpec"></param>
        /// <param name="newIncludePath"></param>
        /// <owner>RGoel</owner>
        internal static void ModifyItemIncludeHelper
            (
            string originalProjectContents,
            string newExpectedProjectContents,
            string oldItemSpec,
            string newIncludePath
            )
        {
            Project project = ObjectModelHelpers.CreateInMemoryProject(originalProjectContents);

            // The project shouldn't be marked dirty yet.
            Assertion.Assert("Project shouldn't be dirty", !project.IsDirtyNeedToReevaluate);

            // Get the set of evaluated items.
            BuildItemGroup evaluatedItems = project.EvaluatedItemsIgnoringCondition;

            // The VS IDE does a few re-evaluations with different sets of global properties
            // (i.e., Configuration=Debug, Configuration=Release, etc.).  This is to simulate
            // that.  If there's a bug in the Project object, then re-evaluation can
            // potentially mess up the number of items hanging around.
            project.MarkProjectAsDirty ();
            BuildItemGroup evaluatedItems2 = project.EvaluatedItemsIgnoringCondition;

            // The project should be marked dirty now.
            Assertion.Assert("Project should be dirty", project.IsDirtyNeedToReevaluate);

            // Search all the evaluated items for the one with the item spec we want
            // to remove.
            foreach (BuildItem evaluatedItem in evaluatedItems)
            {
                if (evaluatedItem.FinalItemSpecEscaped == oldItemSpec)
                {
                    evaluatedItem.Include = newIncludePath;
                }
            }

            // The project should still be marked dirty now.
            Assertion.Assert("Project should be dirty", project.IsDirtyNeedToReevaluate);

            ObjectModelHelpers.CompareProjectContents(project, newExpectedProjectContents);
        }

        /// <summary>
        /// Tests the ability to change an item's "Include" path through the object
        /// model.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void ModifyItemIncludeWithEmbeddedProperty()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <fname>b</fname>
                    </PropertyGroup>

                    <ItemGroup>
                        <Compile Include=`a.cs`>
                            <HintPath>$(mypath1)</HintPath>
                        </Compile>
                        <Compile Include=`$(fname).cs` Condition=`'1'=='1'`>
                            <HintPath>$(mypath2)</HintPath>
                        </Compile>
                        <Compile Include=`c.cs`>
                            <HintPath>$(mypath3)</HintPath>
                        </Compile>
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <fname>b</fname>
                    </PropertyGroup>

                    <ItemGroup>
                        <Compile Include=`a.cs`>
                            <HintPath>$(mypath1)</HintPath>
                        </Compile>
                        <Compile Include=`d.cs` Condition=`'1'=='1'`>
                            <HintPath>$(mypath2)</HintPath>
                        </Compile>
                        <Compile Include=`c.cs`>
                            <HintPath>$(mypath3)</HintPath>
                        </Compile>
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";

            ModifyItemIncludeHelper (projectOriginalContents, projectNewExpectedContents,
                "b.cs", "d.cs");
        }

        /// <summary>
        /// Tests the ability to change an item's "Include" path that was originally
        /// declared using a multi-item item tag.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void ModifyItemIncludeWithinMultiItemSpec()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <ItemGroup>
                        <Compile Include=`a.cs;b.cs;c.cs` Condition=`'0'=='1'`>
                            <HintPath>$(mypath1)</HintPath>
                        </Compile>
                        <Compile Include=`d.cs`>
                            <HintPath>$(mypath3)</HintPath>
                        </Compile>
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <ItemGroup>
                        <Compile Include=`a.cs` Condition=`'0'=='1'`>
                            <HintPath>$(mypath1)</HintPath>
                        </Compile>
                        <Compile Include=`foo.cs` Condition=`'0'=='1'`>
                            <HintPath>$(mypath1)</HintPath>
                        </Compile>
                        <Compile Include=`c.cs` Condition=`'0'=='1'`>
                            <HintPath>$(mypath1)</HintPath>
                        </Compile>
                        <Compile Include=`d.cs`>
                            <HintPath>$(mypath3)</HintPath>
                        </Compile>
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";

            ModifyItemIncludeHelper (projectOriginalContents, projectNewExpectedContents,
                "b.cs", "foo.cs");
        }

        /// <summary>
        /// Deletes all *.weirdo files from the temp path, and dumps 3 files there --
        /// a.weirdo, b.weirdo, c.weirdo.  This is so that we can exercise our wildcard
        /// matching a little bit without having to plumb mock objects all the way through
        /// the engine.
        /// </summary>
        /// <owner>RGoel</owner>
        internal static void CreateThreeWeirdoFilesHelper()
        {
            CleanupWeirdoFilesHelper();

            string tempPath = Path.GetTempPath();

            // Create 3 files in the temp path -- a.weirdo, b.weirdo, and c.weirdo.
            File.WriteAllText(Path.Combine(tempPath, "a.weirdo"), String.Empty);
            File.WriteAllText(Path.Combine(tempPath, "b.weirdo"), String.Empty);
            File.WriteAllText(Path.Combine(tempPath, "c.weirdo"), String.Empty);
        }

        /// <summary>
        /// Delete all *.weirdo files from the temp directory.
        /// </summary>
        /// <owner>RGoel</owner>
        internal static void CleanupWeirdoFilesHelper()
        {
            // Delete all *.weirdo files from the temp path.
            string[] filesEndingWithWeirdo = Directory.GetFiles(Path.GetTempPath(), "*.weirdo");
            foreach (string fileEndingWithWeirdo in filesEndingWithWeirdo)
            {
                File.Delete(fileEndingWithWeirdo);
            }
        }

        /// <summary>
        /// Tests the ability to change an item's "Include" path that was originally
        /// part of a wildcard.  The new item spec does not match the original wildcard,
        /// so the wildcard has to be exploded.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void ModifyItemIncludeWithinNonMatchingWildcard()
        {
            // Populate the project directory with three physical files on disk -- a.weirdo, b.weirdo, c.weirdo.
            CreateThreeWeirdoFilesHelper();

            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <ItemGroup>
                        <MyWildcard Include=`*.weirdo` />
                    </ItemGroup>

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <ItemGroup>
                        <MyWildcard Include=`a.weirdo` />
                        <MyWildcard Include=`banana.cs` />
                        <MyWildcard Include=`c.weirdo` />
                    </ItemGroup>

                </Project>
                ";

            // Change b.weirdo to banana.cs.
            ModifyItemIncludeHelper(projectOriginalContents, projectNewExpectedContents,
                "b.weirdo", "banana.cs");

            CleanupWeirdoFilesHelper();
        }

        /// <summary>
        /// Tests the ability to change an item's "Include" path that was originally
        /// part of a wildcard.  The new item spec matches the original wildcard,
        /// so the project file doesn't have to be touched at all.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void ModifyItemIncludeWithinMatchingWildcard()
        {
            // Populate the project directory with three physical files on disk -- a.weirdo, b.weirdo, c.weirdo.
            CreateThreeWeirdoFilesHelper();

            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <ItemGroup>
                        <MyWildcard Include=`*.weirdo` />
                    </ItemGroup>

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <ItemGroup>
                        <MyWildcard Include=`*.weirdo` />
                    </ItemGroup>

                </Project>
                ";

            // Change b.weirdo to banana.weirdo.
            ModifyItemIncludeHelper(projectOriginalContents, projectNewExpectedContents,
                "b.weirdo", "banana.weirdo");

            CleanupWeirdoFilesHelper();
        }

        /// <summary>
        /// Tests the ability to change an item's "Include" path that was originally
        /// part of a wildcard.  In this test, we grab a reference to the *raw* item
        /// instead of the evaluated item.  When changing the Include of the raw item,
        /// we never try to be smart with respect to wildcards.  We just literally replace
        /// the "Include" attribute with the new string given to us by the user.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void ModifyRawItemIncludeWithinMatchingWildcard()
        {
            // Populate the project directory with three physical files on disk -- a.weirdo, b.weirdo, c.weirdo.
            CreateThreeWeirdoFilesHelper();

            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <ItemGroup>
                        <MyWildcard Include=`*.weirdo` />
                    </ItemGroup>

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <ItemGroup>
                        <MyWildcard Include=`banana.weirdo` />
                    </ItemGroup>

                </Project>
                ";

            Project project = ObjectModelHelpers.CreateInMemoryProject(projectOriginalContents);

            // Get a reference to the one and only raw item in the original project file.
            BuildItem rawItemForStarDotWeirdo = null;
            foreach (BuildItemGroup rawItemGroup in project.ItemGroups)
            {
                foreach (BuildItem rawItem in rawItemGroup)
                {
                    rawItemForStarDotWeirdo = rawItem;
                }
            }

            Assertion.AssertNotNull("Original raw item not found?", rawItemForStarDotWeirdo);
            Assertion.AssertEquals("Original raw item should have Include *.weirdo", "*.weirdo", rawItemForStarDotWeirdo.Include);

            // Change the Include attribute to "banana.weirdo".
            rawItemForStarDotWeirdo.Include = "banana.weirdo";

            // The project should still be marked dirty now.
            Assertion.Assert("Project should be dirty", project.IsDirtyNeedToReevaluate);

            ObjectModelHelpers.CompareProjectContents(project, projectNewExpectedContents);

            CleanupWeirdoFilesHelper();
        }

        /// <summary>
        /// This helper method checks the project to determine whether a particular item of a particular
        /// name exists in the project, such that the build process (the tasks) would see it.
        /// </summary>
        /// <param name="project"></param>
        /// <param name="itemType"></param>
        /// <param name="itemSpec"></param>
        /// <returns></returns>
        /// <owner>RGoel</owner>
        private bool ItemExistsInBuildProcessHelper
            (
            Project project,
            string itemType,
            string itemSpec
            )
        {
            BuildItemGroup evaluatedItemsOfParticularType = project.GetEvaluatedItemsByName(itemType);

            Assertion.AssertNotNull(evaluatedItemsOfParticularType);

            // Search all the evaluated items for the one with the item spec we want
            // to remove.
            foreach (BuildItem evaluatedItem in evaluatedItemsOfParticularType)
            {
                if (evaluatedItem.FinalItemSpecEscaped == itemSpec)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// This loads an existing project, and uses the MSBuild object model to
        /// modify the Name of an item of a particular item spec (e.g.,
        /// "b.cs").  It then compares the final project XML to make sure the item was
        /// modified correctly.
        /// </summary>
        /// <param name="originalProjectContents"></param>
        /// <param name="newExpectedProjectContents"></param>
        /// <param name="itemSpecToModify"></param>
        /// <param name="newItemType"></param>
        /// <owner>RGoel</owner>
        private void ModifyItemNameHelper
            (
            string originalProjectContents,
            string newExpectedProjectContents,
            string itemSpecToModify,
            string newItemType
            )
        {
            Project project = ObjectModelHelpers.CreateInMemoryProject(originalProjectContents);

            // The project shouldn't be marked dirty yet.
            Assertion.Assert("Project shouldn't be dirty", !project.IsDirtyNeedToReevaluate);

            // Get the set of evaluated items.
            BuildItemGroup evaluatedItems = project.EvaluatedItemsIgnoringCondition;

            // The VS IDE does a few re-evaluations with different sets of global properties
            // (i.e., Configuration=Debug, Configuration=Release, etc.).  This is to simulate
            // that.  If there's a bug in the Project object, then re-evaluation can
            // potentially mess up the number of items hanging around.
            project.MarkProjectAsDirty();
            BuildItemGroup evaluatedItems2 = project.EvaluatedItemsIgnoringCondition;

            // The project should be marked dirty now.
            Assertion.Assert("Project should be dirty", project.IsDirtyNeedToReevaluate);

            // If we were to do a build right now, we certainly hope the tasks would not
            // see the new item type.
            Assertion.Assert("New item already exists?", !ItemExistsInBuildProcessHelper(project, newItemType, itemSpecToModify));

            // The above assertion caused a re-evaluation, so we're no longer dirty.
            Assertion.Assert("Project should not be dirty", !project.IsDirtyNeedToReevaluate);

            // Search all the evaluated items for the one with the item spec we want
            // to remove.
            foreach (BuildItem evaluatedItem in evaluatedItems)
            {
                if (evaluatedItem.FinalItemSpecEscaped == itemSpecToModify)
                {
                    evaluatedItem.Name = newItemType;
                }
            }

            // The project should be dirty again, because we modified an item.
            Assertion.Assert("Project should be dirty", project.IsDirtyNeedToReevaluate);

            // If we were to do a build right now, we hope the tasks would see the new item type.
            Assertion.Assert("New item type did not get set properly.", ItemExistsInBuildProcessHelper(project, newItemType, itemSpecToModify));

            ObjectModelHelpers.CompareProjectContents(project, newExpectedProjectContents);
        }

        /// <summary>
        /// Tests the ability to change an item's Name through the object model.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void ModifyItemNameWithEmbeddedProperty()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <fname>b</fname>
                    </PropertyGroup>

                    <ItemGroup>
                        <Compile Include=`a.cs`>
                            <HintPath>$(mypath1)</HintPath>
                        </Compile>
                        <Compile Include=`$(fname).cs` Condition=`'1'=='1'`>
                            <HintPath>$(mypath2)</HintPath>
                        </Compile>
                        <Compile Include=`c.cs`>
                            <HintPath>$(mypath3)</HintPath>
                        </Compile>
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <fname>b</fname>
                    </PropertyGroup>

                    <ItemGroup>
                        <Compile Include=`a.cs`>
                            <HintPath>$(mypath1)</HintPath>
                        </Compile>
                        <Resource Include=`$(fname).cs` Condition=`'1'=='1'`>
                            <HintPath>$(mypath2)</HintPath>
                        </Resource>
                        <Compile Include=`c.cs`>
                            <HintPath>$(mypath3)</HintPath>
                        </Compile>
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";

            this.ModifyItemNameHelper(projectOriginalContents, projectNewExpectedContents,
                "b.cs", "Resource");
        }

        /// <summary>
        /// Tests the ability to change an item's "Type" that was originally
        /// declared using a multi-item item tag.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void ModifyItemNameWithinMultiItemSpec()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <ItemGroup>
                        <Compile Include=`a.cs;b.cs;c.cs` Condition=`'1'=='1'`>
                            <HintPath>$(mypath1)</HintPath>
                        </Compile>
                        <Compile Include=`d.cs`>
                            <HintPath>$(mypath3)</HintPath>
                        </Compile>
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <ItemGroup>
                        <Compile Include=`a.cs` Condition=`'1'=='1'`>
                            <HintPath>$(mypath1)</HintPath>
                        </Compile>
                        <Resource Include=`b.cs` Condition=`'1'=='1'`>
                            <HintPath>$(mypath1)</HintPath>
                        </Resource>
                        <Compile Include=`c.cs` Condition=`'1'=='1'`>
                            <HintPath>$(mypath1)</HintPath>
                        </Compile>
                        <Compile Include=`d.cs`>
                            <HintPath>$(mypath3)</HintPath>
                        </Compile>
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";

            this.ModifyItemNameHelper(projectOriginalContents, projectNewExpectedContents,
                "b.cs", "Resource");
        }

        /// <summary>
        /// This tests that the project is correctly marked as dirty when we
        /// modify an item metadata.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void ModifyItemMetadata()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <WarningLevel>4</WarningLevel>
                    </PropertyGroup>

                    <ItemGroup>
                        <Compile Include=`a.cs;b.cs`>
                            <HintPath>hint</HintPath>
                        </Compile>
                        <Resource Include=`strings.resx` />
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <WarningLevel>4</WarningLevel>
                    </PropertyGroup>

                    <ItemGroup>
                        <Compile Include=`a.cs`>
                            <HintPath>flint</HintPath>
                        </Compile>
                        <Compile Include=`b.cs`>
                            <HintPath>hint</HintPath>
                        </Compile>
                        <Resource Include=`strings.resx` />
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";

            Project project = ObjectModelHelpers.CreateInMemoryProject(projectOriginalContents);

            Assertion.Assert("Project shouldn't be dirty", !project.IsDirtyNeedToReevaluate);

            BuildItemGroup evaluatedItems = project.EvaluatedItemsIgnoringCondition;
            foreach (BuildItem item in evaluatedItems)
            {
                if (item.FinalItemSpecEscaped == "a.cs")
                {
                    item.SetMetadata("HintPath", "flint");
                }
            }

            Assertion.Assert("Project should be dirty", project.IsDirtyNeedToReevaluate);

            ObjectModelHelpers.CompareProjectContents(project, projectNewExpectedContents);
        }
    }

    [TestFixture]
    public class AddProperty
    {
        /// <summary>
        /// Tests that the object model correctly adds a new property to the correct
        /// existing PropertyGroup.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void SetPropertyOnNewPropertyInExistingPropertyGroup()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup Condition=` '$(A)' == 'B' `>
                        <OutputPath>c:\blah</OutputPath>
                    </PropertyGroup>

                    <PropertyGroup>
                        <WarningLevel>1</WarningLevel>
                    </PropertyGroup>

                    <PropertyGroup>
                        <Optimize>true</Optimize>
                    </PropertyGroup>

                    <Target Name=`Build` />

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup Condition=` '$(A)' == 'B' `>
                        <OutputPath>c:\blah</OutputPath>
                    </PropertyGroup>

                    <PropertyGroup>
                        <WarningLevel>1</WarningLevel>
                        <MyNewProperty>woohoo</MyNewProperty>
                    </PropertyGroup>

                    <PropertyGroup>
                        <Optimize>true</Optimize>
                    </PropertyGroup>

                    <Target Name=`Build` />

                </Project>
                ";

            Project project = ObjectModelHelpers.CreateInMemoryProject(projectOriginalContents);

            // The project shouldn't be marked dirty yet.
            Assertion.Assert("Project shouldn't be dirty", !project.IsDirtyNeedToReevaluate);

            // Set the given new property in the project file using
            // the object model.
            project.SetProperty("MyNewProperty", "woohoo", "");

            // The project should be marked dirty now.
            Assertion.Assert("Project should be dirty", project.IsDirtyNeedToReevaluate);

            ObjectModelHelpers.CompareProjectContents(project, projectNewExpectedContents);
        }

        /// <summary>
        /// This tests that the project is correctly marked as dirty when we
        /// add a new property to the project.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void AddNewPropertyThroughPropertyGroup()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <WarningLevel>4</WarningLevel>
                    </PropertyGroup>

                    <ItemGroup>
                        <Compile Include=`a.cs`>
                            <HintPath>hint</HintPath>
                        </Compile>
                        <Compile Include=`b.cs` />
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <WarningLevel>4</WarningLevel>
                        <Optimize>true</Optimize>
                    </PropertyGroup>

                    <ItemGroup>
                        <Compile Include=`a.cs`>
                            <HintPath>hint</HintPath>
                        </Compile>
                        <Compile Include=`b.cs` />
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";

            Project project = ObjectModelHelpers.CreateInMemoryProject(projectOriginalContents);

            Assertion.Assert("Project shouldn't be dirty", !project.IsDirtyNeedToReevaluate);

            project.PropertyGroups.LastLocalPropertyGroup.AddNewProperty("Optimize", "true");

            Assertion.Assert("Project should be dirty", project.IsDirtyNeedToReevaluate);

            ObjectModelHelpers.CompareProjectContents(project, projectNewExpectedContents);
        }

        /// <summary>
        /// This tests that the project is correctly marked as dirty when we
        /// add a new PropertyGroup to the project.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void AddNewPropertyGroup()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <WarningLevel>4</WarningLevel>
                    </PropertyGroup>

                    <ItemGroup>
                        <Compile Include=`a.cs`>
                            <HintPath>hint</HintPath>
                        </Compile>
                        <Compile Include=`b.cs` />
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <WarningLevel>4</WarningLevel>
                    </PropertyGroup>

                    <PropertyGroup />

                    <ItemGroup>
                        <Compile Include=`a.cs`>
                            <HintPath>hint</HintPath>
                        </Compile>
                        <Compile Include=`b.cs` />
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";

            Project project = ObjectModelHelpers.CreateInMemoryProject(projectOriginalContents);

            Assertion.Assert("Project shouldn't be dirty", !project.IsDirtyNeedToReevaluate);

            project.AddNewPropertyGroup(false);

            Assertion.Assert("Project should be dirty", project.IsDirtyNeedToReevaluate);

            ObjectModelHelpers.CompareProjectContents(project, projectNewExpectedContents);
        }

        [Test]
        public void AddPropertyToImportedProjectFile()
        {
            // Create temp files on disk for the main project file and the imported project file.
            string importedProjFilename = ObjectModelHelpers.CreateTempFileOnDisk(@"

                    <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    </Project>

                ");

            string mainProjFilename = ObjectModelHelpers.CreateTempFileOnDisk(@"

                    <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                        <PropertyGroup>
                            <NonConsumingRefPath>$(ReferencePath)$(NewImportedProperty)</NonConsumingRefPath>
                        </PropertyGroup>

                        <Import Project=`{0}`/>

                        <PropertyGroup>
                            <ConsumingRefPath>$(ReferencePath)$(NewImportedProperty)</ConsumingRefPath>
                        </PropertyGroup>

                    </Project>

                ", importedProjFilename);

            try
            {
                // Load the two projects using the MSBuild object model.
                Project mainProj = new Project(new Engine(@"c:\"));
                Project importedProj = new Project(mainProj.ParentEngine);

                mainProj.Load(mainProjFilename);
                importedProj.Load(importedProjFilename);

                // This adds a new property
                mainProj.SetImportedProperty("ReferencePath", @"c:\foobar", "'true' == 'true'", importedProj);

                // Initially, the main project gets the correct value of the property from
                // the imported project.  So ConsumingRefPath == "$(ReferencePath)" == "c:\foobar".
                Assertion.AssertEquals(@"c:\foobar", mainProj.EvaluatedProperties["ConsumingRefPath"].FinalValueEscaped);
                Assertion.AssertEquals(string.Empty, mainProj.EvaluatedProperties["NonConsumingRefPath"].FinalValueEscaped);

                mainProj.SetImportedProperty("ReferencePath", @"c:\boobah", "'true' == 'true'", importedProj);

                // Now if we query for the property, we should get back the new value.
                Assertion.AssertEquals(@"c:\boobah", mainProj.EvaluatedProperties["ConsumingRefPath"].FinalValueEscaped);
                Assertion.AssertEquals(@"c:\boobah", importedProj.EvaluatedProperties["ReferencePath"].FinalValueEscaped);
                Assertion.AssertEquals(string.Empty, mainProj.EvaluatedProperties["NonConsumingRefPath"].FinalValueEscaped);

                // Now we add a new imported property to the main file, into an existing imported
                // property group.
                mainProj.SetImportedProperty("NewImportedProperty", @"newpropertyvalue", "'true' == 'true'", importedProj);

                // Now if we query for the property, we should get back the new value.
                Assertion.AssertEquals(@"newpropertyvalue", mainProj.EvaluatedProperties["NewImportedProperty"].FinalValueEscaped);
                Assertion.AssertEquals(@"newpropertyvalue", importedProj.EvaluatedProperties["NewImportedProperty"].FinalValueEscaped);
                Assertion.AssertEquals(@"c:\boobahnewpropertyvalue", mainProj.EvaluatedProperties["ConsumingRefPath"].FinalValueEscaped);
                Assertion.AssertEquals(string.Empty, mainProj.EvaluatedProperties["NonConsumingRefPath"].FinalValueEscaped);
            }
            finally
            {
                File.Delete(mainProjFilename);
                File.Delete(importedProjFilename);
            }
        }
    }

    [TestFixture]
    public class RemoveProperty
    {
        /// <summary>
        /// This tests that the project is correctly marked as dirty when we
        /// remove a property.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void RemovePropertyByName()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <Optimize>false</Optimize>
                        <WarningLevel>4</WarningLevel>
                    </PropertyGroup>

                    <ItemGroup>
                        <Compile Include=`a.cs`>
                            <HintPath>hint</HintPath>
                        </Compile>
                        <Compile Include=`b.cs` />
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <Optimize>false</Optimize>
                    </PropertyGroup>

                    <ItemGroup>
                        <Compile Include=`a.cs`>
                            <HintPath>hint</HintPath>
                        </Compile>
                        <Compile Include=`b.cs` />
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";

            Project project = ObjectModelHelpers.CreateInMemoryProject(projectOriginalContents);

            Assertion.Assert("Project shouldn't be dirty", !project.IsDirtyNeedToReevaluate);

            project.PropertyGroups.LastLocalPropertyGroup.RemoveProperty("WarningLevel");

            Assertion.Assert("Project should be dirty", project.IsDirtyNeedToReevaluate);

            ObjectModelHelpers.CompareProjectContents(project, projectNewExpectedContents);
        }

        /// <summary>
        /// This tests that the project is correctly marked as dirty when we
        /// remove a BuildPropertyGroup from the project.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void RemovePropertyGroup()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <WarningLevel>4</WarningLevel>
                    </PropertyGroup>

                    <ItemGroup>
                        <Compile Include=`a.cs`>
                            <HintPath>hint</HintPath>
                        </Compile>
                        <Compile Include=`b.cs` />
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <ItemGroup>
                        <Compile Include=`a.cs`>
                            <HintPath>hint</HintPath>
                        </Compile>
                        <Compile Include=`b.cs` />
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";

            Project project = ObjectModelHelpers.CreateInMemoryProject(projectOriginalContents);

            Assertion.AssertEquals(1, project.PropertyGroups.Count);
            Assertion.Assert("Project shouldn't be dirty", !project.IsDirtyNeedToReevaluate);

            project.RemovePropertyGroup(project.PropertyGroups.LastLocalPropertyGroup);

            Assertion.AssertEquals(0, project.PropertyGroups.Count);
            Assertion.Assert("Project should be dirty", project.IsDirtyNeedToReevaluate);

            ObjectModelHelpers.CompareProjectContents(project, projectNewExpectedContents);
        }

        /// <summary>
        /// Test the RemoveAllPropertyGroups method.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void RemoveAllPropertyGroups()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string original = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup Condition=`'$(x)'=='y'`>
                        <ReferencePath>c:\foobar</ReferencePath>
                    </PropertyGroup>

                    <PropertyGroup Condition=`'$(x)'=='z'`>
                        <ReferencePath>c:\foobar</ReferencePath>
                    </PropertyGroup>

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string expected = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                </Project>
                ";

            Project project = ObjectModelHelpers.CreateInMemoryProject(original);
            Assertion.AssertEquals(2, project.PropertyGroups.Count);

            project.RemoveAllPropertyGroups();

            Assertion.AssertEquals(0, project.PropertyGroups.Count);
            ObjectModelHelpers.CompareProjectContents(project, expected);
        }

        /// <summary>
        /// Test the RemoveAllPropertyGroups method.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void RemoveAllPropertyGroupsWithChoose()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string original = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup Condition=`'$(x)'=='y'`>
                        <ReferencePath>c:\foobar</ReferencePath>
                    </PropertyGroup>

                    <Choose>
                        <When Condition = `true`>
                            <PropertyGroup Condition=`'$(x)'=='z'`>
                                <ReferencePath>c:\foobar</ReferencePath>
                            </PropertyGroup>
                        </When>
                        <Otherwise>
                            <PropertyGroup Condition=`'$(x)'=='v'`>
                                <ReferencePath>c:\foobar</ReferencePath>
                            </PropertyGroup>
                        </Otherwise>
                    </Choose>

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string expected = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <Choose>
                        <When Condition=`true`>
                        </When>
                        <Otherwise>
                        </Otherwise>
                    </Choose>

                </Project>
                ";

            Project project = ObjectModelHelpers.CreateInMemoryProject(original);
            Assertion.AssertEquals(3, project.PropertyGroups.Count);

            project.RemoveAllPropertyGroups();

            Assertion.AssertEquals(0, project.PropertyGroups.Count);
            ObjectModelHelpers.CompareProjectContents(project, expected);
        }

        /// <summary>
        /// Test the RemoveAllPropertyGroupsByCondition method.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void RemoveAllPropertyGroupsByCondition()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string original = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup Condition=`'$(x)'=='y'`>
                        <ReferencePath>c:\foobar</ReferencePath>
                    </PropertyGroup>

                    <PropertyGroup Condition=`'$(x)'=='z'`>
                        <ReferencePath>c:\foobar</ReferencePath>
                    </PropertyGroup>

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string expected = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup Condition=`'$(x)'=='z'`>
                        <ReferencePath>c:\foobar</ReferencePath>
                    </PropertyGroup>

                </Project>
                ";

            Project project = ObjectModelHelpers.CreateInMemoryProject(original);
            Assertion.AssertEquals(2, project.PropertyGroups.Count);

            project.RemovePropertyGroupsWithMatchingCondition("'$(x)'=='y'");

            Assertion.AssertEquals(1, project.PropertyGroups.Count);
            ObjectModelHelpers.CompareProjectContents(project, expected);
        }

        /// <summary>
        /// Test the RemoveAllPropertyGroupsByCondition method.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void RemoveAllPropertyGroupsByConditionWithChoose()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string original = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup Condition=`'$(x)'=='y'`>
                        <ReferencePath>c:\foobar</ReferencePath>
                    </PropertyGroup>

                    <PropertyGroup Condition=`'$(x)'=='z'`>
                        <ReferencePath>c:\foobar</ReferencePath>
                    </PropertyGroup>

                    <Choose>
                        <When Condition = `true`>
                            <PropertyGroup Condition=`'$(x)'=='y'`>
                                  <ReferencePath>c:\foobar</ReferencePath>
                            </PropertyGroup>

                            <PropertyGroup Condition=`'$(x)'=='z'`>
                                  <ReferencePath>c:\foobar</ReferencePath>
                            </PropertyGroup>
                        </When>
                        <Otherwise>
                            <PropertyGroup Condition=`'$(x)'=='y'`>
                                  <ReferencePath>c:\foobar</ReferencePath>
                            </PropertyGroup>

                            <PropertyGroup Condition=`'$(x)'=='z'`>
                                  <ReferencePath>c:\foobar</ReferencePath>
                            </PropertyGroup>
                        </Otherwise>
                    </Choose>

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string expected = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup Condition=`'$(x)'=='z'`>
                        <ReferencePath>c:\foobar</ReferencePath>
                    </PropertyGroup>

                    <Choose>
                        <When Condition=`true`>
                            <PropertyGroup Condition=`'$(x)'=='z'`>
                                <ReferencePath>c:\foobar</ReferencePath>
                            </PropertyGroup>
                        </When>
                        <Otherwise>
                            <PropertyGroup Condition=`'$(x)'=='z'`>
                                <ReferencePath>c:\foobar</ReferencePath>
                            </PropertyGroup>
                        </Otherwise>
                    </Choose>

                </Project>
                ";

            Project project = ObjectModelHelpers.CreateInMemoryProject(original);
            Assertion.AssertEquals(6, project.PropertyGroups.Count);

            project.RemovePropertyGroupsWithMatchingCondition("'$(x)'=='y'");

            Assertion.AssertEquals(3, project.PropertyGroups.Count);
            ObjectModelHelpers.CompareProjectContents(project, expected);
        }

        [Test]
        public void RemoveImportedPropertyGroupMatchingCondition()
        {
            // Create temp files on disk for the main project file and the imported project file.
            string importedProjFilename = ObjectModelHelpers.CreateTempFileOnDisk(@"

                    <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    </Project>

                ");

            string mainProjFilename = ObjectModelHelpers.CreateTempFileOnDisk(@"

                    <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                        <Import Project=`{0}`/>
                    </Project>

                ", importedProjFilename);

            try
            {
                // Load the two projects using the MSBuild object model.
                Project mainProj = new Project(new Engine(@"c:\"));
                Project importedProj = new Project(mainProj.ParentEngine);

                mainProj.Load(mainProjFilename);
                importedProj.Load(importedProjFilename);

                string configCondition1 = "'$(Configuration)' == 'Config1'";
                string configCondition2 = "'$(Configuration)' == 'Config2'";
                string configTestCondition = "'$(Configuration)' == 'Test'";

                // Add same property to imported user file
                mainProj.SetImportedProperty("Prop", "FromUserFile",
                    configCondition1, importedProj, PropertyPosition.UseExistingOrCreateAfterLastImport);

                mainProj.SetImportedProperty("Prop", "FromUserFile",
                    configCondition2, importedProj, PropertyPosition.UseExistingOrCreateAfterLastImport);

                mainProj.SetImportedProperty("Prop", "FromUserFile",
                    configTestCondition, importedProj, PropertyPosition.UseExistingOrCreateAfterLastImport);

                mainProj.RemovePropertyGroupsWithMatchingCondition(configTestCondition, true /* include imported */);
                importedProj.RemovePropertyGroupsWithMatchingCondition(configTestCondition, true /* include imported */);

                string[] configs = mainProj.GetConditionedPropertyValues("Configuration");

                Assertion.AssertEquals(2, configs.Length);
                Assertion.Assert(configs[0] == "Config1" || configs[1] == "Config1");
                Assertion.Assert(configs[0] == "Config2" || configs[1] == "Config2");
            }
            finally
            {
                File.Delete(mainProjFilename);
                File.Delete(importedProjFilename);
            }
        }

        [Test]
        public void RemovePersistedImportedPropertyGroupMatchingCondition()
        {
            // Create temp files on disk for the main project file and the imported project file.
            string importedProjFilename = ObjectModelHelpers.CreateTempFileOnDisk(@"

                    <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                        <PropertyGroup Condition=`'$(Configuration)' == 'Config1'`>
                            <Prop>FromUserFile1</Prop>
                        </PropertyGroup>
                        <PropertyGroup Condition=`'$(Configuration)' == 'Config2'`>
                            <Prop>FromUserFile2</Prop>
                        </PropertyGroup>
                        <PropertyGroup Condition=`'$(Configuration)' == 'Test'`>
                            <Prop>FromUserFileTest</Prop>
                        </PropertyGroup>

                    </Project>

                ");

            string mainProjFilename = ObjectModelHelpers.CreateTempFileOnDisk(@"

                    <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                        <Import Project=`{0}`/>
                    </Project>

                ", importedProjFilename);

            try
            {
                // Load the two projects using the MSBuild object model.
                Project mainProj = new Project(new Engine(@"c:\"));
                Project importedProj = new Project(mainProj.ParentEngine);

                mainProj.Load(mainProjFilename);
                importedProj.Load(importedProjFilename);

                mainProj.RemovePropertyGroupsWithMatchingCondition("'$(Configuration)' == 'Test'", true /* include imported */);
                importedProj.RemovePropertyGroupsWithMatchingCondition("'$(Configuration)' == 'Test'", true /* include imported */);

                string[] configs = mainProj.GetConditionedPropertyValues("Configuration");

                Assertion.AssertEquals(2, configs.Length);
                Assertion.Assert(configs[0] == "Config1" || configs[1] == "Config1");
                Assertion.Assert(configs[0] == "Config2" || configs[1] == "Config2");
            }
            finally
            {
                File.Delete(mainProjFilename);
                File.Delete(importedProjFilename);
            }
        }
    }

    [TestFixture]
    public class ModifyProperty
    {
        /// <summary>
        /// This tests that the project is correctly marked as dirty when we
        /// set a property value for a property that already existed on the project.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void SetPropertyOnExistingProperty()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <WarningLevel>4</WarningLevel>
                    </PropertyGroup>

                    <ItemGroup>
                        <Compile Include=`a.cs`>
                            <HintPath>hint</HintPath>
                        </Compile>
                        <Compile Include=`b.cs` />
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <WarningLevel>3</WarningLevel>
                    </PropertyGroup>

                    <ItemGroup>
                        <Compile Include=`a.cs`>
                            <HintPath>hint</HintPath>
                        </Compile>
                        <Compile Include=`b.cs` />
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";

            Project project = ObjectModelHelpers.CreateInMemoryProject(projectOriginalContents);

            Assertion.Assert("Project shouldn't be dirty", !project.IsDirtyNeedToReevaluate);

            project.SetProperty("WarningLevel", "3", null);

            Assertion.Assert("Project should be dirty", project.IsDirtyNeedToReevaluate);

            ObjectModelHelpers.CompareProjectContents(project, projectNewExpectedContents);
        }

        /// <summary>
        /// This tests that the project is correctly marked as dirty when we
        /// directly change the value of a property.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void ModifyPropertyValue()
        {
            // ************************************
            //               BEFORE
            // ************************************
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <Optimize>false</Optimize>
                        <WarningLevel>4</WarningLevel>
                    </PropertyGroup>

                    <ItemGroup>
                        <Compile Include=`a.cs`>
                            <HintPath>hint</HintPath>
                        </Compile>
                        <Compile Include=`b.cs` />
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";


            // ************************************
            //               AFTER
            // ************************************
            string projectNewExpectedContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <Optimize>false</Optimize>
                        <WarningLevel>3</WarningLevel>
                    </PropertyGroup>

                    <ItemGroup>
                        <Compile Include=`a.cs`>
                            <HintPath>hint</HintPath>
                        </Compile>
                        <Compile Include=`b.cs` />
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";

            Project project = ObjectModelHelpers.CreateInMemoryProject(projectOriginalContents);

            // Force an evaluation of the project.  If there were a bug in the project code, this might
            // cause the back pointers from properties to the parent BuildPropertyGroup to get messed up.
            BuildPropertyGroup evaluatedProperties = project.EvaluatedProperties;

            Assertion.Assert("Project shouldn't be dirty", !project.IsDirtyNeedToReevaluate);

            BuildPropertyGroup propertyGroup = project.PropertyGroups.LastLocalPropertyGroup;
            foreach (BuildProperty property in propertyGroup)
            {
                if (property.Name == "WarningLevel")
                {
                    property.Value = "3";
                }
            }

            Assertion.Assert("Project should be dirty", project.IsDirtyNeedToReevaluate);

            ObjectModelHelpers.CompareProjectContents(project, projectNewExpectedContents);
        }

        /// <summary>
        /// Tests to see if the main project's evaluated properties will pick up changes made
        /// to the imported project file.  This is necessary for IDE scenarios, particularly for
        /// things like the "ReferencePath" property which is stored in the .USER file and is
        /// used during the build process for the main project.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void ModifyPropertyInImportedProjectFile()
        {
            // Create temp files on disk for the main project file and the imported project file.
            string importedProjFilename = ObjectModelHelpers.CreateTempFileOnDisk(@"

                    <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                        <PropertyGroup>
                            <ReferencePath>c:\foobar</ReferencePath>
                        </PropertyGroup>

                    </Project>

                ");

            string mainProjFilename = ObjectModelHelpers.CreateTempFileOnDisk(@"

                    <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                        <Import Project=`{0}`/>

                        <PropertyGroup>
                            <ConsumingRefPath>$(ReferencePath)</ConsumingRefPath>
                        </PropertyGroup>

                    </Project>

                ", importedProjFilename);

            try
            {
                // Load the two projects using the MSBuild object model.
                Project mainProj = new Project(new Engine(@"c:\"));
                Project importedProj = new Project(mainProj.ParentEngine);

                mainProj.Load(mainProjFilename);
                importedProj.Load(importedProjFilename);

                // Initially, the main project gets the correct value of the property from
                // the imported project.  So ConsumingRefPath == "$(ReferencePath)" == "c:\foobar".
                Assertion.AssertEquals(@"c:\foobar", mainProj.EvaluatedProperties["ConsumingRefPath"].FinalValueEscaped);

                Assertion.Assert("Main project should not be dirty for Save before setting imported property", !mainProj.IsDirty);
                Assertion.Assert("Main project should not be dirty for Evaluation before setting imported property", !mainProj.IsDirtyNeedToReevaluate);
                Assertion.Assert("Imported project should not be dirty for Save before setting imported property", !importedProj.IsDirty);
                Assertion.Assert("Imported project should not be dirty for Evaluation before setting imported property", !importedProj.IsDirtyNeedToReevaluate);

                mainProj.SetImportedProperty("ReferencePath", @"c:\boobah", null, importedProj);

                Assertion.Assert("Main project should not be dirty for Save after setting first imported property", !mainProj.IsDirty);
                Assertion.Assert("Main project should be dirty for Evaluation after setting first imported property", mainProj.IsDirtyNeedToReevaluate);
                Assertion.Assert("Imported project should be dirty for Save after setting first imported property", importedProj.IsDirty);
                Assertion.Assert("Imported project should be dirty for Evaluation after setting first imported property", importedProj.IsDirtyNeedToReevaluate);

                // Now if we query for the property, we should get back the new value.
                Assertion.AssertEquals(@"c:\boobah", mainProj.EvaluatedProperties["ConsumingRefPath"].FinalValueEscaped);
                Assertion.AssertEquals(@"c:\boobah", importedProj.EvaluatedProperties["ReferencePath"].FinalValueEscaped);

                // Now we add a new imported property to the main file, into an existing imported
                // property group.
                mainProj.SetImportedProperty("NewImportedProperty", @"newpropertyvalue", null, importedProj);

                // Now if we query for the property, we should get back the new value.
                Assertion.AssertEquals(@"newpropertyvalue", mainProj.EvaluatedProperties["NewImportedProperty"].FinalValueEscaped);
                Assertion.AssertEquals(@"newpropertyvalue", importedProj.EvaluatedProperties["NewImportedProperty"].FinalValueEscaped);

                // Now we add a new imported property to the main file, into a new imported
                // property group (by setting a unique condition).
                mainProj.SetImportedProperty("NewImportedPropertyWithCondition", @"anotherpropertyvalue", "'$(foo)' == '$(bar)'", importedProj);

                // Now if we query for the property, we should get back the new value.
                Assertion.AssertEquals(@"anotherpropertyvalue", mainProj.EvaluatedProperties["NewImportedPropertyWithCondition"].FinalValueEscaped);
                Assertion.AssertEquals(@"anotherpropertyvalue", importedProj.EvaluatedProperties["NewImportedPropertyWithCondition"].FinalValueEscaped);

                Assertion.Assert("Main project should not be dirty for Save after setting last imported property", !mainProj.IsDirty);
                Assertion.Assert("Imported project should be dirty for Save after setting last imported property", importedProj.IsDirty);
            }
            finally
            {
                File.Delete(mainProjFilename);
                File.Delete(importedProjFilename);
            }
        }

        [Test]
        public void ModifyImportedPropertyGroupCondition()
        {
            // Create temp files on disk for the main project file and the imported project file.
            string importedProjFilename = ObjectModelHelpers.CreateTempFileOnDisk(@"

                    <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    </Project>

                ");

            string mainProjFilename = ObjectModelHelpers.CreateTempFileOnDisk(@"

                    <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                        <Import Project=`{0}`/>
                    </Project>

                ", importedProjFilename);

            try
            {
                // Load the two projects using the MSBuild object model.
                Project mainProj = new Project(new Engine(@"c:\"));
                Project importedProj = new Project(mainProj.ParentEngine);

                mainProj.Load(mainProjFilename);
                importedProj.Load(importedProjFilename);

                string configCondition1 = "'$(Configuration)' == 'Config1'";
                string configCondition2 = "'$(Configuration)' == 'Config2'";

                // Add same property to imported user file
                mainProj.SetImportedProperty("Prop", "FromUserFile",
                    configCondition1, importedProj, PropertyPosition.UseExistingOrCreateAfterLastImport);

                mainProj.SetImportedProperty("Prop", "FromUserFile",
                    configCondition2, importedProj, PropertyPosition.UseExistingOrCreateAfterLastImport);

                foreach (BuildPropertyGroup bpg in mainProj.PropertyGroups)
                {
                    if (bpg.Condition == configCondition2)
                    {
                        bpg.SetImportedPropertyGroupCondition("'$(Configuration)' == 'NewConfig'");
                    }
                }

                string[] configs = mainProj.GetConditionedPropertyValues("Configuration");

                Assertion.AssertEquals(2, configs.Length);
                Assertion.Assert(configs[0] == "Config1" || configs[1] == "Config1");
                Assertion.Assert(configs[0] == "NewConfig" || configs[1] == "NewConfig");
            }
            finally
            {
                File.Delete(mainProjFilename);
                File.Delete(importedProjFilename);
            }
        }

        [Test]
        public void ModifyPersistedImportedPropertyGroupCondition()
        {
            // Create temp files on disk for the main project file and the imported project file.
            string importedProjFilename = ObjectModelHelpers.CreateTempFileOnDisk(@"

                    <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                        <PropertyGroup Condition=`'$(Configuration)' == 'Config1'`>
                            <Prop>FromUserFile1</Prop>
                        </PropertyGroup>
                        <PropertyGroup Condition=`'$(Configuration)' == 'Config2'`>
                            <Prop>FromUserFile2</Prop>
                        </PropertyGroup>

                    </Project>

                ");

            string mainProjFilename = ObjectModelHelpers.CreateTempFileOnDisk(@"

                    <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                        <Import Project=`{0}`/>
                    </Project>

                ", importedProjFilename);

            try
            {
                // Load the two projects using the MSBuild object model.
                Project mainProj = new Project(new Engine(@"c:\"));
                Project importedProj = new Project(mainProj.ParentEngine);

                mainProj.Load(mainProjFilename);
                importedProj.Load(importedProjFilename);

                foreach (BuildPropertyGroup bpg in mainProj.PropertyGroups)
                {
                    if (bpg.Condition == "'$(Configuration)' == 'Config2'")
                    {
                        bpg.SetImportedPropertyGroupCondition("'$(Configuration)' == 'NewConfig'");
                    }
                }

                string[] configs = mainProj.GetConditionedPropertyValues("Configuration");

                Assertion.AssertEquals(2, configs.Length);
                Assertion.Assert(configs[0] == "Config1" || configs[1] == "Config1");
                Assertion.Assert(configs[0] == "NewConfig" || configs[1] == "NewConfig");
            }
            finally
            {
                File.Delete(mainProjFilename);
                File.Delete(importedProjFilename);
            }
        }

        /// <summary>
        /// Verify that the programfiles32 property points to the correct location
        /// </summary>
        [Test]
        public void VerifyMsbuildProgramFiles32ReservedProperty()
        {
            MockLogger ml = ObjectModelHelpers.BuildProjectExpectSuccess(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
                                <PropertyGroup>
                                    <abcdef>$(MsBuildProgramFiles32)</abcdef>
                                </PropertyGroup>

                                <Target Name='t'>
                                    <Message Text='[$(abcdef)]' />
                                </Target>
                              </Project>");

            // Make sure the log contains the correct strings.
            ml .AssertLogContains(String.Format("[{0}]", FrameworkLocationHelper.programFiles32));
        }

        /// <summary>
        /// Tests to see if the main project's evaluated properties will pick up changes made
        /// to the imported project file.  This is necessary for IDE scenarios, particularly for
        /// things like the "ReferencePath" property which is stored in the .USER file and is
        /// used during the build process for the main project.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void ModifyPropertyInImportedProjectFileAfterRename()
        {
            ObjectModelHelpers.DeleteTempProjectDirectory();

            // Create temp files on disk for the main project file and the imported project file.
            string importedProjFilename = ObjectModelHelpers.CreateFileInTempProjectDirectory("imported.proj", @"

                    <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                        <PropertyGroup>
                            <ReferencePath>c:\foobar</ReferencePath>
                        </PropertyGroup>

                    </Project>

                ");

            string mainProjFilename = ObjectModelHelpers.CreateFileInTempProjectDirectory("main.proj", string.Format(@"

                    <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                        <Import Project=`{0}`/>

                        <PropertyGroup>
                            <ConsumingRefPath>$(ReferencePath)</ConsumingRefPath>
                        </PropertyGroup>

                    </Project>

                ", importedProjFilename));

            // Load the two projects using the MSBuild object model.
            Project mainProj = new Project(new Engine(@"c:\"));
            Project importedProj = new Project(mainProj.ParentEngine);

            mainProj.Load(mainProjFilename);
            importedProj.Load(importedProjFilename);

            // Initially, the main project gets the correct value of the property from
            // the imported project.  So ConsumingRefPath == "$(ReferencePath)" == "c:\foobar".
            Assertion.AssertEquals(@"c:\foobar", mainProj.EvaluatedProperties["ConsumingRefPath"].FinalValueEscaped);

            mainProj.SetImportedProperty("ReferencePath", @"c:\boobah", null, importedProj);

            // Now if we query for the property, we should get back the new value.
            Assertion.AssertEquals(@"c:\boobah", mainProj.EvaluatedProperties["ConsumingRefPath"].FinalValueEscaped);
            Assertion.AssertEquals(@"c:\boobah", importedProj.EvaluatedProperties["ReferencePath"].FinalValueEscaped);

            importedProj.Save(Path.Combine(ObjectModelHelpers.TempProjectDir, "newimported.proj"));

            // Now we add a new imported property to the main file, into an existing imported
            // property group.
            mainProj.SetImportedProperty("ReferencePath", @"c:\hoohah", null, importedProj);

            // Now if we query for the property, we should get back the new value.
            Assertion.AssertEquals(@"c:\hoohah", importedProj.EvaluatedProperties["ReferencePath"].FinalValueEscaped);
            Assertion.AssertEquals(@"c:\hoohah", mainProj.EvaluatedProperties["ReferencePath"].FinalValueEscaped);
        }
    }

    /// <summary>
    /// A very simple task that logs a constant message.
    /// </summary>
    /// <owner>RGoel</owner>
    public class WashCar : Microsoft.Build.Utilities.Task
    {
        public override bool Execute()
        {
            this.Log.LogMessage("Done washing car.");
            return true;
        }
    }

    /// <summary>
    /// A very simple Message task.  We are intentionally giving it the same name as one of our shipping tasks.
    /// </summary>
    /// <owner>RGoel</owner>
    public class Message : Microsoft.Build.Utilities.Task
    {
        private string text;
        public string Text
        {
            set {text = value;}
        }

        public override bool Execute()
        {
            this.Log.LogMessage("Custom Message task: " + text);
            return true;
        }
    }

    [TestFixture]
    public class UsingTask
    {
        /// <summary>
        /// Register a custom task using the fully qualified name, and invoke it using the fully qualified name.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void CustomTaskRegisteredFullInvokedFull()
        {
            // Create a project file that calls our custom WashCar task (implemented just a few lines above).
            MockLogger ml = ObjectModelHelpers.BuildProjectExpectSuccess(String.Format(@"

                    <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                        <UsingTask TaskName=`Microsoft.Build.UnitTests.Project_Tests.WashCar` AssemblyFile=`{0}`/>

                        <Target Name=`Build`>
                            <Microsoft.Build.UnitTests.Project_Tests.WashCar/>
                        </Target>

                    </Project>

                ", new Uri(Assembly.GetExecutingAssembly().EscapedCodeBase).LocalPath));

            // Make sure the log contains the correct strings.
            Assertion.Assert("Custom WashCar task should have been called.", ml.FullLog.Contains("Done washing car."));
        }

        /// <summary>
        /// Register a custom task using the fully qualified name, and invoke it using the simple name.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void CustomTaskRegisteredFullInvokedSimple()
        {
            // Create a project file that calls our custom WashCar task (implemented just a few lines above).
            MockLogger ml = ObjectModelHelpers.BuildProjectExpectSuccess(String.Format(@"

                    <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                        <UsingTask TaskName=`Microsoft.Build.UnitTests.Project_Tests.WashCar` AssemblyFile=`{0}`/>

                        <Target Name=`Build`>
                            <WashCar/>
                        </Target>

                    </Project>

                ", new Uri(Assembly.GetExecutingAssembly().EscapedCodeBase).LocalPath));

            // Make sure the log contains the correct strings.
            Assertion.Assert("Custom WashCar task should have been called.", ml.FullLog.Contains("Done washing car."));
        }

        /// <summary>
        /// Register a custom task using the simple name, and invoke it using the fully qualified name.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void CustomTaskRegisteredSimpleInvokedFull()
        {
            // Create a project file that calls our custom WashCar task (implemented just a few lines above).
            MockLogger ml = ObjectModelHelpers.BuildProjectExpectSuccess(String.Format(@"

                    <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                        <UsingTask TaskName=`WashCar` AssemblyFile=`{0}`/>

                        <Target Name=`Build`>
                            <Microsoft.Build.UnitTests.Project_Tests.WashCar/>
                        </Target>

                    </Project>

                ", new Uri(Assembly.GetExecutingAssembly().EscapedCodeBase).LocalPath));

            // Make sure the log contains the correct strings.
            Assertion.Assert("Custom WashCar task should have been called.", ml.FullLog.Contains("Done washing car."));
        }

        /// <summary>
        /// Register a custom task using the simple name, and invoke it using the simple name.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void CustomTaskRegisteredSimpleInvokedSimple()
        {
            // Create a project file that calls our custom WashCar task (implemented just a few lines above).
            MockLogger ml = ObjectModelHelpers.BuildProjectExpectSuccess(String.Format(@"

                    <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                        <UsingTask TaskName=`WashCar` AssemblyFile=`{0}`/>

                        <Target Name=`Build`>
                            <WashCar/>
                        </Target>

                    </Project>

                ", new Uri(Assembly.GetExecutingAssembly().EscapedCodeBase).LocalPath));

            // Make sure the log contains the correct strings.
            Assertion.Assert("Custom WashCar task should have been called.", ml.FullLog.Contains("Done washing car."));
        }

        /// <summary>
        /// Register a custom task that has the same class name as one of our shipping tasks, but in a different
        /// namespace.  Register it using the fully qualified namespace.  Then invoke the task with:
        /// 1.) fully qualified namespace of the custom task.  This should invoke the custom task.
        /// 2.) fully qualified namespace of the shipping task.  This should invoke the shipping task.
        /// 3.) unqualified task name.  This should invoke the shipping task.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void CustomMessageTaskRegisteredFullyQualifed()
        {
            // Create a project file that calls our custom Message task and the shipping Message task.
            // (The custom Message task is implemented just a few lines above.)
            MockLogger ml = ObjectModelHelpers.BuildProjectExpectSuccess(String.Format(ObjectModelHelpers.CleanupFileContents(@"

                    <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                        <UsingTask TaskName=`Microsoft.Build.UnitTests.Project_Tests.Message` AssemblyFile=`{0}`/>

                        <!-- In our .tasks file Message is used fully qualified. In order to perform this test, we need to make sure it's defined partially qualified-->
                        <UsingTask TaskName='Message' AssemblyName='Microsoft.Build.Tasks.Core, Version=msbuildassemblyversion, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a'/>

                        <Target Name=`Build`>

                            <!-- This should run the custom Message task. -->
                            <Microsoft.Build.UnitTests.Project_Tests.Message Text=`Being`/>

                            <!-- This should run the shipping Message task. -->
                            <Microsoft.Build.Tasks.Message Text=`John`/>

                            <!-- This should run the shipping Message task. -->
                            <Message Text=`Malkovich`/>

                        </Target>

                    </Project>

                "), new Uri(Assembly.GetExecutingAssembly().EscapedCodeBase).LocalPath));

            // Make sure the log contains the correct strings.
            Assertion.Assert("Custom Message task should have been called the first time.",     ml.FullLog.Contains("Custom Message task: Being"));

            Assertion.Assert("Some Message task should have been called the second time.",      ml.FullLog.Contains("John"));
            Assertion.Assert("Shipping Message task should have been called the second time.", !ml.FullLog.Contains("Custom Message task: John"));

            Assertion.Assert("Some Message task should have been called the third time.",       ml.FullLog.Contains("Malkovich"));
            Assertion.Assert("Shipping Message task should have been called the third time.",  !ml.FullLog.Contains("Custom Message task: Malkovich"));
        }

        /// <summary>
        /// Register a custom task that has the same class name as one of our shipping tasks, but in a different
        /// namespace.  Register it using only the simple name.  Then invoke the task with:
        /// 1.) fully qualified namespace of the custom task.  This should invoke the custom task.
        /// 2.) fully qualified namespace of the shipping task.  This should invoke the shipping task.
        /// 3.) unqualified task name.  This should invoke the custom task.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void CustomMessageTaskRegisteredSimple()
        {
            // Create a project file that calls our custom Message task and the shipping Message task.
            // (The custom Message task is implemented just a few lines above.)
            MockLogger ml = ObjectModelHelpers.BuildProjectExpectSuccess(String.Format(@"

                    <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                        <UsingTask TaskName=`Message` AssemblyFile=`{0}`/>

                        <Target Name=`Build`>

                            <!-- This should run the custom Message task. -->
                            <Microsoft.Build.UnitTests.Project_Tests.Message Text=`Being`/>

                            <!-- This should run the shipping Message task. -->
                            <Microsoft.Build.Tasks.Message Text=`John`/>

                            <!-- This should run the custom Message task. -->
                            <Message Text=`Malkovich`/>

                        </Target>

                    </Project>

                ", new Uri(Assembly.GetExecutingAssembly().EscapedCodeBase).LocalPath));

            // Make sure the log contains the correct strings.
            Assertion.Assert("Custom Message task should have been called the first time.",     ml.FullLog.Contains("Custom Message task: Being"));

            Assertion.Assert("Some Message task should have been called the second time.",      ml.FullLog.Contains("John"));
            Assertion.Assert("Shipping Message task should have been called the second time.", !ml.FullLog.Contains("Custom Message task: John"));

            Assertion.Assert("Custom Message task should have been called the third time.",     ml.FullLog.Contains("Custom Message task: Malkovich"));
        }
    }

    [TestFixture]
    public class Properties
    {
        [TearDown]
        public void TearDown()
        {
            if (Registry.CurrentUser.OpenSubKey("msbuildUnitTests") != null)
            {
                Registry.CurrentUser.DeleteSubKeyTree("msbuildUnitTests");
            }
        }

        private const string testRegistryPath = @"msbuildUnitTests";

        /// <summary>
        /// Basic test.
        /// </summary>
        [Test]
        public void RegistryProperties()
        {
            RegistryKey testRegistryKey = null;

            testRegistryKey = Registry.CurrentUser.CreateSubKey(testRegistryPath);
            testRegistryKey.SetValue("Foo", "FooValue");

            Project p = ObjectModelHelpers.CreateInMemoryProject(@"

                   <Project DefaultTargets=`Build` ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                      <PropertyGroup>
                        <P>$(Registry:HKEY_CURRENT_USER\" + testRegistryPath + @"@Foo)</P>
                        <Q Condition=""'$(Registry:HKEY_CURRENT_USER\" + testRegistryPath + @"@Foo)' == 'FooValue'"">QValue</Q>
                      </PropertyGroup>
                    </Project>
                ");

            Assertion.AssertEquals("FooValue", p.EvaluatedProperties["P"].FinalValue);
            Assertion.AssertEquals("QValue", p.EvaluatedProperties["Q"].FinalValue);
        }

        /// <summary>
        /// Basic test.
        /// </summary>
        [Test]
        public void RegistryPropertiesWithEscapedCharactersInValue()
        {
            RegistryKey testRegistryKey = null;
            testRegistryKey = Registry.CurrentUser.CreateSubKey(testRegistryPath);
            testRegistryKey.SetValue("Foo", "FooValue");
            testRegistryKey.SetValue("Bar", "%24(Foo)");
            testRegistryKey.SetValue("Property3", "%24(NUMBER_OF_PROCESSORS)");
            testRegistryKey.SetValue("@Property4", "Value4");

            Project p = ObjectModelHelpers.CreateInMemoryProject(@"

                   <Project DefaultTargets=`Build` ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                      <PropertyGroup>
                        <P>$(Registry:HKEY_CURRENT_USER\" + testRegistryPath + @"@Foo)</P>
                        <Q Condition=""'$(Registry:HKEY_CURRENT_USER\" + testRegistryPath + @"@Bar)' == '%24(Foo)'"">QValue</Q>
                        <R>$(Registry:HKEY_CURRENT_USER\" + testRegistryPath + @"@Property3)</R>
                        <S>$(Registry:HKEY_CURRENT_USER\" + testRegistryPath + @"@%40Property4)</S>
                      </PropertyGroup>
                    </Project>
                ");

            Assertion.AssertEquals("FooValue", p.EvaluatedProperties["P"].FinalValue);
            Assertion.AssertEquals("QValue", p.EvaluatedProperties["Q"].FinalValue);
            Assertion.AssertEquals("$(NUMBER_OF_PROCESSORS)", p.EvaluatedProperties["R"].FinalValue);
            Assertion.AssertEquals("Value4", p.EvaluatedProperties["S"].FinalValue);
        }
    }

    [TestFixture]
    public class QueryProjectState
    {
        /// <summary>
        /// This tests the Project.EvaluatedItemsIgnoringCondition property.  This
        /// property should return the list of evaluated items in the project,
        /// pretending that all "Condition"s evaluated to true.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void GetEvaluatedItemsIgnoringCondition()
        {
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <Ext>.cs</Ext>
                    </PropertyGroup>

                    <ItemGroup>
                        <Compile Include=`a$(Ext); b.cs` Condition=`'1'=='0'`>
                            <HintPath>hint</HintPath>
                        </Compile>
                        <Compile Include=`c$(Ext)` />
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";

            Project project = ObjectModelHelpers.CreateInMemoryProject(projectOriginalContents);

            // Get the set of evaluated items.
            BuildItem[] evaluatedItemsIgnoringCondition1 = project.EvaluatedItemsIgnoringCondition.ToArray();
            BuildItem[] evaluatedItems1 = project.EvaluatedItems.ToArray();

            // The VS IDE does a few re-evaluations with different sets of global properties
            // (i.e., Configuration=Debug, Configuration=Release, etc.).  This is to simulate
            // that.  The point is that re-evaluating a project should NOT change the items
            // that are returned from Project.EvaluatedItemsIgnoringCondition.  Those items
            // need to be semi-permanent so that the IDE can hang on to them across multiple
            // builds.
            project.MarkProjectAsDirty ();
            BuildItem[] evaluatedItemsIgnoringCondition2 = project.EvaluatedItemsIgnoringCondition.ToArray();
            BuildItem[] evaluatedItems2 = project.EvaluatedItems.ToArray();

            // Confirm the "IgnoreCondition" lists:
            {
                EngineHelpers.AssertItemsMatch(@"
                    a.cs : HintPath=hint
                    b.cs : HintPath=hint
                    c.cs : HintPath=
                    ", evaluatedItemsIgnoringCondition1);

                EngineHelpers.AssertItemsMatch(@"
                    a.cs : HintPath=hint
                    b.cs : HintPath=hint
                    c.cs : HintPath=
                    ", evaluatedItemsIgnoringCondition2);

                // Confirm that both lists contain the exact same 3 items.
                Assertion.AssertEquals(evaluatedItemsIgnoringCondition1[0], evaluatedItemsIgnoringCondition2[0]);
                Assertion.AssertEquals(evaluatedItemsIgnoringCondition1[1], evaluatedItemsIgnoringCondition2[1]);
                Assertion.AssertEquals(evaluatedItemsIgnoringCondition1[2], evaluatedItemsIgnoringCondition2[2]);
            }

            // Confirm the other "normal" lists:
            {
                EngineHelpers.AssertItemsMatch("c.cs", evaluatedItems1);
                EngineHelpers.AssertItemsMatch("c.cs", evaluatedItems2);

                // Confirm that both lists do *not* contain the exact same item.
                Assertion.Assert(evaluatedItems1[0] != evaluatedItems2[0]);
            }
        }

        /// <summary>
        /// Ensures that if we define a new item list based on another previously defined
        /// item list that the new item list inherits the metadata from the previous item list.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void GetItemMetadataInheritedFromOtherItems()
        {
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <ItemGroup>
                        <Compile Include=`a;b`>
                            <Culture>Klingon</Culture>
                            <Magazine>Time</Magazine>
                        </Compile>
                        <Compile Include=`c`>
                            <Culture>Smurf</Culture>
                        </Compile>
                        <Compile2 Include=`@(Compile)` />
                        <Compile3 Include=`@(Compile->'%(Identity)3')`>
                            <Magazine>Newsweek</Magazine>
                        </Compile3>
                    </ItemGroup>

                </Project>
                ";

            Project project = ObjectModelHelpers.CreateInMemoryProject(projectOriginalContents);

            // ==========================================
            // VALIDATE @(COMPILE2) IS DEFINED CORRECTLY.
            // ==========================================
            BuildItemGroup compile2 = project.GetEvaluatedItemsByName("Compile2");

            EngineHelpers.AssertItemsMatch(@"
                a : Culture=Klingon ; Magazine=Time
                b : Culture=Klingon ; Magazine=Time
                c : Culture=Smurf   ; Magazine=
                ", compile2);

            // ==========================================
            // VALIDATE @(COMPILE3) IS DEFINED CORRECTLY.
            // ==========================================
            BuildItemGroup compile3 = project.GetEvaluatedItemsByName("Compile3");

            EngineHelpers.AssertItemsMatch(@"
                a3 : Culture=Klingon ; Magazine=Newsweek
                b3 : Culture=Klingon ; Magazine=Newsweek
                c3 : Culture=Smurf   ; Magazine=Newsweek
                ", compile3);
        }
    }

    [TestFixture]
    public class Imports
    {
        /// <summary>
        /// Tests that the engine does care about an invalid import if the condition
        /// is false - #484931
        /// </summary>
        /// <owner>danmose</owner>
        [Test]
        public void InvalidImportFalseCondition()
        {
            // Very important to put the Project attribute before the Condition attribute,
            // to repro the crash in #484931
            Project p = ObjectModelHelpers.CreateInMemoryProject(@"

                    <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                        <Import Project=`||oops*invalid*project||` Condition=`'$(nonexistent)'!=''`  />
                        <Target Name=`t`/>
                    </Project>

                ");

            // Should build successfully
            Assertion.Assert(p.Build(new string[] { "t" }, null));

            // No exception
        }

        /// <summary>
        /// Tests that the engine handles invalid Import project path nicely - #347276
        /// </summary>
        /// <owner>danmose</owner>
        [Test]
        [ExpectedException(typeof(InvalidProjectFileException))]
        public void InvalidImportProject()
        {
            Project p = ObjectModelHelpers.CreateInMemoryProject(@"

                    <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                        <Import Project=`||||` Condition=`true` />
                        <Target Name=`t`/>
                    </Project>

                ");

            p.Build(new string[] { "t" }, null); // Should throw
        }

        /// <summary>
        /// Tests that the engine handles invalid Import project path nicely - #347276
        /// </summary>
        /// <owner>danmose</owner>
        [Test]
        [ExpectedException(typeof(InvalidProjectFileException))]
        public void InvalidImportProject2()
        {
            Project p = ObjectModelHelpers.CreateInMemoryProject(@"

                    <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                        <Import Project=`     `  />
                        <Target Name=`t`/>
                    </Project>

                ");

            p.Build(new string[] { "t" }, null); // Should throw
        }

        /// <summary>
        /// Tests that the engine handles invalid Import project path nicely - #347276
        /// </summary>
        /// <owner>danmose</owner>
        [Test]
        [ExpectedException(typeof(InvalidProjectFileException))]
        public void InvalidImportProject3()
        {
            Project p = ObjectModelHelpers.CreateInMemoryProject(@"

                    <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                        <Import Project=`***`  />
                        <Target Name=`t`/>
                    </Project>

                ");

            p.Build(new string[] { "t" }, null); // Should throw
        }

        /// <summary>
        /// Test replacing the project import path with something else
        /// </summary>
        [Test]
        public void ReplaceImport()
        {
            XmlDocument xmldoc = new XmlDocument();
            xmldoc.LoadXml(ObjectModelHelpers.CleanupFileContents(@"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` DefaultTargets=`Build` InitialTargets=`Clean` xmlns=`msbuildnamespace`>
                    <Import Project=`Microsoft.Uncommon.targets` />
                </Project>
                "));

            Engine engine = new Engine(@"c:\");
            Project project = engine.CreateNewProject();
            project.LoadFromXmlDocument(xmldoc, null, ProjectLoadSettings.IgnoreMissingImports);

            IEnumerator enumerator = project.Imports.GetEnumerator();
            enumerator.MoveNext();
            Import import = (Import)enumerator.Current;

            import.ProjectPath = "Microsoft.Rare.targets";
            Assert.AreEqual(null, import.Condition);
            import.Condition = "'true' == 'true'";
            Assert.AreEqual("'true' == 'true'", import.Condition);

            StringWriter writer = new StringWriter();
            project.Save(writer);

            // Load the modified project into a new project object
            xmldoc = new XmlDocument();
            xmldoc.LoadXml(writer.ToString());

            Project projectWithChangedImport = engine.CreateNewProject();
            project.LoadFromXmlDocument(xmldoc, null, ProjectLoadSettings.IgnoreMissingImports);
            Assert.AreEqual(1, project.Imports.Count);

            enumerator = project.Imports.GetEnumerator();
            enumerator.MoveNext();
            import = (Import)enumerator.Current;

            Assert.AreEqual("Microsoft.Rare.targets", import.ProjectPath);
            Assert.AreEqual("'true' == 'true'", import.Condition);
        }
    }

    [TestFixture]
    public class Evaluation
    {
        /// <summary>
        /// Relative paths in 'exists' on conditions should be evalauted relative to the
        /// project directory.
        /// </summary>
        [Test]
        public void ImportConditionsEvaluatedUsingProjectsDirectory()
        {
            string importFile = ObjectModelHelpers.CreateTempFileOnDisk(@"
                    <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                        <Target Name=`t`>
                           <Message Text=`[in import]`/>
                        </Target>
                    </Project>
                ");

            // Import the file (and express the import condition) using a *relative* path
            string importFileRelative = Path.GetFileName(importFile);

            string projectFile = ObjectModelHelpers.CreateTempFileOnDisk(@"
                    <Project xmlns=`msbuildnamespace`>
                        <Import Project=`{0}` Condition=`exists('{0}')` />
                    </Project>
                ", importFileRelative);

            string currentDirectory = Environment.CurrentDirectory;
            try
            {
                MockLogger logger = new MockLogger();
                Project project = LoadAndBuildInDifferentCurrentDirectory(projectFile, logger);

                logger.AssertLogContains("[in import]");
                logger.ClearLog();

                DirtyAndBuildInDifferentCurrentDirectory(project);
                logger.AssertLogContains("[in import]");
            }
            finally
            {
                File.Delete(projectFile);
                File.Delete(importFile);
                Environment.CurrentDirectory = currentDirectory;
            }
        }

        /// <summary>
        /// Relative paths in 'exists' on conditions should be evalauted relative to the
        /// project directory.
        /// </summary>
        [Test]
        public void PropertyConditionsEvaluatedUsingProjectsDirectory()
        {
            string tempFile = Path.GetTempFileName();
            string relativeTempFile = Path.GetFileName(tempFile);

            string projectFile = ObjectModelHelpers.CreateTempFileOnDisk(@"
                    <Project xmlns=`msbuildnamespace`>
                        <PropertyGroup>
                            <p Condition=`exists('{0}')`>v1</p>
                        </PropertyGroup>
                        <Target Name=`t`>
                           <Message Text=`[$(p)]`/>
                        </Target>
                    </Project>
                ", relativeTempFile);

            try
            {
                MockLogger logger = new MockLogger();
                Project project = LoadAndBuildInDifferentCurrentDirectory(projectFile, logger);

                logger.AssertLogContains("[v1]");
                logger.ClearLog();

                DirtyAndBuildInDifferentCurrentDirectory(project);
                logger.AssertLogContains("[v1]");
            }
            finally
            {
                File.Delete(projectFile);
                File.Delete(tempFile);
            }
        }

        /// <summary>
        /// Relative paths in 'exists' on conditions should be evalauted relative to the
        /// project directory.
        /// </summary>
        [Test]
        public void ItemAndTargetConditionsEvaluatedUsingProjectsDirectory()
        {
            string tempFile = Path.GetTempFileName();
            string relativeTempFile = Path.GetFileName(tempFile);

            string projectFile = ObjectModelHelpers.CreateTempFileOnDisk(@"
                    <Project xmlns=`msbuildnamespace`>
                        <ItemGroup>
                            <i Include=`i1` Condition=`exists('{0}')`/>
                        </ItemGroup>
                        <Target Name=`t` Condition=`exists('{0}')`>
                           <Message Text=`[@(i)]`/>
                        </Target>
                    </Project>
                ", relativeTempFile);

            try
            {
                MockLogger logger = new MockLogger();
                Project project = LoadAndBuildInDifferentCurrentDirectory(projectFile, logger);

                logger.AssertLogContains("[i1]");
                logger.ClearLog();

                DirtyAndBuildInDifferentCurrentDirectory(project);
                logger.AssertLogContains("[i1]");
            }
            finally
            {
                File.Delete(projectFile);
                File.Delete(tempFile);
            }
        }

        /// <summary>
        /// Changes the current directory, loads the project, changes the current directory again, and builds it.
        /// </summary>
        private static Project LoadAndBuildInDifferentCurrentDirectory(string projectFile, MockLogger logger)
        {
            string currentDirectory = Environment.CurrentDirectory;
            Project project;

            try
            {
                project = new Project(new Engine());
                project.ParentEngine.RegisterLogger(logger);

                // Make sure that the current directory isn't the project directory somehow
                Environment.CurrentDirectory = Environment.SystemDirectory;
                project.Load(projectFile);

                // Make sure that the current directory isn't the project directory somehow
                Environment.CurrentDirectory = Environment.SystemDirectory;
                project.Build();
            }
            finally
            {
                Environment.CurrentDirectory = currentDirectory;
            }

            return project;
        }

        /// <summary>
        /// Dirties the project, changes the current directory, and builds it.
        /// </summary>
        private void DirtyAndBuildInDifferentCurrentDirectory(Project project)
        {
            string currentDirectory = Environment.CurrentDirectory;

            try
            {
                project.MarkProjectAsDirty();

                // Make sure that the current directory isn't the project directory somehow
                Environment.CurrentDirectory = Environment.SystemDirectory;
                project.Build();
            }
            finally
            {
                Environment.CurrentDirectory = currentDirectory;
            }
        }
    }

    [TestFixture]
    public class EscapingAndRecursiveDir
    {
        /// <summary>
        /// Regress DDB# 114268. %28 in an item's include in the XML should
        /// match '(' in a file path. This was not happening in the code path
        /// that produces %(RecursiveDir).
        /// </summary>
        [Test]
        public void RecursiveDirPathWithParentheses()
        {
            string directory = null, subdirectory = null, file1 = null;

            try
            {
                directory = Path.Combine(Path.GetTempPath(), "a(b)c");
                subdirectory = Path.Combine(directory, "d");
                file1 = Path.Combine(subdirectory, "e");
                Directory.CreateDirectory(subdirectory);

                // Create a file "%temp%\a(b)c\d\e"
                File.WriteAllText(file1, String.Empty);


                string xml = @"
                    <Project xmlns=`http://schemas.microsoft.com/developer/msbuild/2003`>
                        <ItemGroup>
                            <i Include=`" + directory + @"\**\*" + @"`/>
                        </ItemGroup>
                        <Target Name=`t`>
                           <Message Text=`[%(i.identity)-->c:\foo\%(i.recursivedir)%(i.filename)%(i.extension)]`/>
                        </Target>
                    </Project>
                ";

                Console.WriteLine(xml);

                Project p = new Project();
                p.FullFileName = Path.Combine(subdirectory, "x.proj");
                p.LoadXml(xml.Replace('`', '"'));

                MockLogger logger = new MockLogger();
                p.ParentEngine.RegisterLogger(logger);

                p.Build();

                logger.AssertLogContains("[" + directory + @"\d\e-->c:\foo\d\e]");
            }
            finally
            {
                File.Delete(file1);
                Directory.Delete(subdirectory);
                Directory.Delete(directory);
            }
        }
    }

    [TestFixture]
    public class ErrorCases
    {
        /// <summary>
        /// Tests that the engine correctly reports a failure when somebody tries to
        /// reference an item list inside the Condition for an Import.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        [ExpectedException(typeof(InvalidProjectFileException))]
        public void ItemGroupInAnImportCondition()
        {
            Project p = ObjectModelHelpers.CreateInMemoryProject(@"

                    <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                        <Import Condition=`@(foo) == 'a.cs'` Project=`foo.proj` />

                        <Target Name=`t`>
                            <Message Text=`[$(a)]`/>
                        </Target>

                    </Project>

                ");

            p.Build(new string[] { "t" }, null);
        }

        /// <summary>
        /// Tests to make sure we correctly fail on a target name that contains an embedded property.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        [ExpectedException(typeof(InvalidProjectFileException))]
        public void InvalidTargetName()
        {
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <Target Name=`Build$(Configuration)` />
                </Project>
                ";

            Project project = ObjectModelHelpers.CreateInMemoryProject(projectOriginalContents);
        }

        /// <summary>
        /// Tests to make sure we correctly fail on a meta-data name containing a period.
        /// </summary>
        /// <owner>danmose</owner>
        [Test]
        [ExpectedException(typeof(InvalidProjectFileException))]
        public void InvalidMetadataName()
        {
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <ItemGroup>
                        <a Include=`x`>
                            <meta.data>foo</meta.data>
                        </a>
                    </ItemGroup>
                    <Target Name=`t` />
                </Project>
                ";

            Project project = ObjectModelHelpers.CreateInMemoryProject(projectOriginalContents);
        }

        /// <summary>
        /// Regression test for bug VSWhidbey 243657
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        [ExpectedException(typeof(InvalidProjectFileException))]
        public void IllegalCharactersInItemName()
        {
            string original = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <ItemGroup>
                        <A.B Include=`blah` />
                    </ItemGroup>

                </Project>
                ";

            Project project = ObjectModelHelpers.CreateInMemoryProject(original);
        }

        /// <summary>
        /// Regression test for bug VSWhidbey 412627
        /// </summary>
        /// <owner>danmose</owner>
        [Test]
        [ExpectedException(typeof(InvalidProjectFileException))]
        public void IllegalCharactersInUsingTaskAssemblyFile()
        {
            string original = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <UsingTask TaskName=`x` AssemblyFile=`||invalid||`/>
                </Project>
                ";

            Project project = ObjectModelHelpers.CreateInMemoryProject(original);
        }

        /// <summary>
        /// Unknown attribute on UsingTask should throw
        /// </summary>
        /// <owner>danmose</owner>
        [Test]
        [ExpectedException(typeof(InvalidProjectFileException))]
        public void UnknownAttributeInUsingTask()
        {
            string original = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <UsingTask TaskName=`x` AssemblyFile=`x` BogusAttribute=`v3.5`/>
                </Project>
                ";

            // Should throw
            Project project = ObjectModelHelpers.CreateInMemoryProject(original);
        }

        /// <summary>
        /// RequiredRuntime attribute on UsingTask should be ignored
        /// (we'll make it actually do something in V3.5+1
        /// </summary>
        /// <owner>danmose</owner>
        [Test]
        public void RequiredRuntimeAttributeInUsingTask()
        {
            string original = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <UsingTask TaskName=`x` AssemblyFile=`x` RequiredRuntime=`v3.5`/>
                </Project>
                ";

            // Should not throw
            Project project = ObjectModelHelpers.CreateInMemoryProject(original);
        }

        /// <summary>
        /// Tests that putting invalid characters in the <Import> path results in a
        /// InvalidProjectFileException.
        /// </summary>
        /// <owner>RGoel</owner>
        [ExpectedException(typeof(InvalidProjectFileException))]
        [Test]
        public void InvalidCharactersInImportedPath()
        {
            string original = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <Import Project=`|||.proj` />
                </Project>
                ";

            Project project = ObjectModelHelpers.CreateInMemoryProject(original);
        }

        /// <summary>
        /// Regress Whidbey 531457. Make sure that we restore the current directory after a child project
        /// throws an exception
        /// </summary>
        /// <owner>LukaszG</owner>
        [Test]
        public void CurrentDirectoryRestoredAfterException()
        {
            ObjectModelHelpers.DeleteTempProjectDirectory();

            // ---------------------
            // dirs.proj
            // ---------------------
            ObjectModelHelpers.CreateFileInTempProjectDirectory("dirs.proj", @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <ItemGroup>
                        <Project Include=`a\a.proj` />
                        <Project Include=`b\b.proj` />
                    </ItemGroup>
                    <Target Name=`dirs`>
                        <MSBuild Projects=`@(Project)` />
                    </Target>
                </Project>
            ");

            // ---------------------
            // a.proj
            // An invalid project file that will cause an InvalidProjectException
            // ---------------------
            ObjectModelHelpers.CreateFileInTempProjectDirectory(@"a\a.proj", @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <Message Text=`a` />
                    <Target Name=`a`>
                        <Message Text=`Greetings from an invalid project!`/>
                    </Target>
                </Project>
            ");

            // ---------------------
            // b.proj
            // A control project file that should build correctly after a.proj throws
            // ---------------------
            ObjectModelHelpers.CreateFileInTempProjectDirectory(@"b\b.proj", @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <Target Name=`b`>
                        <Message Text=`Greetings from a valid project!`/>
                    </Target>
                </Project>
            ");

            // Create a logger.
            MockLogger logger = new MockLogger();
            Project project = ObjectModelHelpers.LoadProjectFileInTempProjectDirectory("dirs.proj", logger);

            bool success = project.Build(null, null);
            Assertion.Assert("Build should have failed.  See Standard Out tab for details", !success);

            logger.AssertLogDoesntContain("Greetings from an invalid project!");
            logger.AssertLogContains("Greetings from a valid project!");
        }
    }

    [TestFixture]
    public class GlobalProperties
    {
        /// <summary>
        /// This tests that the project is correctly marked as dirty when we
        /// modify a global property.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void SetNewGlobalProperty()
        {
            string projectOriginalContents = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <PropertyGroup>
                        <WarningLevel>4</WarningLevel>
                    </PropertyGroup>

                    <ItemGroup>
                        <Compile Include=`a.cs;b.cs`>
                            <HintPath>hint</HintPath>
                        </Compile>
                        <Resource Include=`strings.resx` />
                    </ItemGroup>

                    <Target Name=`Build` />

                </Project>
                ";

            Project project = ObjectModelHelpers.CreateInMemoryProject(projectOriginalContents);

            Assertion.Assert("Project shouldn't be dirty", !project.IsDirtyNeedToReevaluate);

            project.GlobalProperties.SetProperty("Configuration", "Debug");

            Assertion.Assert("Project should be dirty", project.IsDirtyNeedToReevaluate);
        }

        /// <summary>
        /// This tests that the project is NOT marked as dirty when we set a
        /// global property to the exact same value it had before.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void SetGlobalPropertyToSameValue()
        {
            Project project = ObjectModelHelpers.CreateInMemoryProject(@"<Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`/>");

            Assertion.Assert("Project shouldn't be dirty to begin with.", !project.IsDirtyNeedToReevaluate);

            project.GlobalProperties.SetProperty("Configuration", "Debug");

            Assertion.Assert("Project should be dirty after setting a global property for the first time.", project.IsDirtyNeedToReevaluate);

            // This forces a re-evaluation.
            BuildPropertyGroup evaluatedProps = project.EvaluatedProperties;

            Assertion.Assert("Project should not be dirty after re-evaluation.", !project.IsDirtyNeedToReevaluate);

            // Set the global property to the exact same value it had before.
            project.GlobalProperties.SetProperty("Configuration", "Debug");

            Assertion.Assert("Project should not be dirty after setting global property to same value.", !project.IsDirtyNeedToReevaluate);
        }

        /// <summary>
        /// Test that the default value for $(MSBuildExtensionsPath) points to "c:\program files\msbuild" on a 32 bit machine
        /// or points to c:\program files(x86)\msbuild on 64 bit machine.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void MSBuildExtensionsPathDefault()
        {
            string specialPropertyName = ReservedPropertyNames.extensionsPath;  // "MSBuildExtensionsPath"

            // Save the old copy of the MSBuildExtensionsPath, so we can restore it when the unit test is done.
            string backupMSBuildExtensionsPath = Environment.GetEnvironmentVariable(specialPropertyName);

            // Set an environment variable called MSBuildExtensionsPath to some value, for the purpose
            // of seeing whether our value wins.
            Environment.SetEnvironmentVariable(specialPropertyName, null);

            // Need to create a new engine object in order to pick up the new environment variables.
            Engine myEngine = new Engine();
            myEngine.BinPath = @"c:\";

            // Create a new project, and see what MSBuild gives us for the value of MSBuildExtensionsPath.
            // we should get the default value which is "c:\program files\msbuild".
            Project myProject = new Project(myEngine);

            string expectedValue = null;
            if(Environment.Is64BitOperatingSystem)
            {
                expectedValue = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
            }
            else
            {
                expectedValue = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            }

            Assertion.AssertEquals(expectedValue + @"\MSBuild",
                (string)myProject.EvaluatedProperties[specialPropertyName]);

            // Restore the original value of the MSBuildExtensionsPath environment variable.
            Environment.SetEnvironmentVariable(specialPropertyName, backupMSBuildExtensionsPath);
        }

        /// <summary>
        /// When MSBUILDLEGACYEXTENSIONSPATH is set test tha $(MSBuildExtensionsPath) points to "c:\program files\msbuild". This should be valid for both 32 and 64 bit.
        /// </summary>
        [Test]
        public void MSBuildExtensionsPathDefault_Legacy()
        {
            string specialPropertyName = "MSBuildExtensionsPath";

            // Save the old copy of the MSBuildExtensionsPath, so we can restore it when the unit test is done.
            string backupMSBuildExtensionsPath = Environment.GetEnvironmentVariable(specialPropertyName);
            string backupMagicSwitch = Environment.GetEnvironmentVariable("MSBUILDLEGACYEXTENSIONSPATH");
            string targetVar = Environment.GetEnvironmentVariable("Target");
            string numberVar = Environment.GetEnvironmentVariable("0env");
            string msbuildVar = Environment.GetEnvironmentVariable("msbuildtoolsversion");

            try
            {
                // Set an environment variable called MSBuildExtensionsPath to some value, for the purpose
                // of seeing whether our value wins.
                Environment.SetEnvironmentVariable(specialPropertyName, null);
                Environment.SetEnvironmentVariable("MSBUILDLEGACYEXTENSIONSPATH", "1");

                // Need to create a new engine object in order to pick up the new environment variables.
                Engine myEngine = new Engine();
                myEngine.BinPath = @"c:\";

                // Create a new project, and see what MSBuild gives us for the value of MSBuildExtensionsPath.
                // we should get the default value which is "c:\program files\msbuild".
                Project myProject = new Project(myEngine);
                Assertion.AssertEquals(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + @"\MSBuild",
                                      (string)myProject.EvaluatedProperties[specialPropertyName]);
            }
            finally
            {
                // Restore the original value of the MSBuildExtensionsPath environment variable.
                Environment.SetEnvironmentVariable(specialPropertyName, backupMSBuildExtensionsPath);
                Environment.SetEnvironmentVariable("MSBUILDLEGACYEXTENSIONSPATH", backupMagicSwitch);
                Environment.SetEnvironmentVariable("Target", targetVar);
                Environment.SetEnvironmentVariable("0env", numberVar);
                Environment.SetEnvironmentVariable("msbuildtoolsversion", msbuildVar);
            }
        }

        /// <summary>
        /// Test that if I set an environment variable called "MSBuildExtensionPath", that my env var
        /// should win over whatever MSBuild thinks the default is.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void MSBuildExtensionsPathWithEnvironmentOverride()
        {
            string specialPropertyName = ReservedPropertyNames.extensionsPath;  // "MSBuildExtensionsPath"

            // Save the old copy of the MSBuildExtensionsPath, so we can restore it when the unit test is done.
            string backupMSBuildExtensionsPath = Environment.GetEnvironmentVariable(specialPropertyName);

            // Set an environment variable called MSBuildExtensionsPath to some value, for the purpose
            // of seeing whether our value wins.
            Environment.SetEnvironmentVariable(specialPropertyName, @"c:\devdiv\vscore\msbuild");

            // Need to create a new engine object in order to pick up the new environment variables.
            Engine myEngine = new Engine();
            myEngine.BinPath = @"c:\";

            // Create a new project, and see what MSBuild gives us for the value of MSBuildExtensionsPath.
            Project myProject = new Project(myEngine);
            Assertion.AssertEquals(@"c:\devdiv\vscore\msbuild",
                (string)myProject.EvaluatedProperties[specialPropertyName]);

            // Restore the original value of the MSBuildExtensionsPath environment variable.
            Environment.SetEnvironmentVariable(specialPropertyName, backupMSBuildExtensionsPath);
        }

        /// <summary>
        /// Test that if I set a global property called "MSBuildExtensionPath", that my global property
        /// should win over whatever MSBuild thinks the default is.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void MSBuildExtensionsPathWithGlobalOverride()
        {
            string specialPropertyName = ReservedPropertyNames.extensionsPath;  // "MSBuildExtensionsPath"

            // Create a new project, and see what MSBuild gives us for the value of MSBuildExtensionsPath.
            Engine engine = new Engine(@"c:\");
            Project myProject = new Project(engine);

            // Set a global property called MSBuildExtensionsPath to some value, for the purpose
            // of seeing whether our value wins.
            myProject.GlobalProperties.SetProperty(specialPropertyName, @"c:\devdiv\vscore\msbuild");

            Assertion.AssertEquals(@"c:\devdiv\vscore\msbuild",
                (string)myProject.EvaluatedProperties[specialPropertyName]);
        }

        /// <summary>
        /// The default value for $(MSBuildExtensionsPath32) should point to "c:\program files (x86)\msbuild" on a 64 bit machine.
        /// We can't test that directly since tests generally don't run on 64 bit boxes. However we can set the "ProgramFiles(x86)"
        /// environment variable and make sure that that's the value used.
        /// </summary>
        [Test]
        public void MSBuildExtensionsPath32Default()
        {
            string programFiles32EnvVar = "ProgramFiles(x86)";
            string originalProgramFiles32Value = Environment.GetEnvironmentVariable(programFiles32EnvVar);
            string extensionsPath32EnvValue = Environment.GetEnvironmentVariable("MSBuildExtensionsPath32");

            try
            {
                Environment.SetEnvironmentVariable("MSBuildExtensionsPath32", null);

                if (String.IsNullOrEmpty(originalProgramFiles32Value))
                {
                    // 32 bit box
                    originalProgramFiles32Value = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                }

                // First try it with whatever value it currently has (it will probably be blank since it's a 32 bit box)
                Project p = new Project(new Engine());
                string msbuildExtensionsPath32Value = (string)p.EvaluatedProperties[ReservedPropertyNames.extensionsPath32];
                Assertion.AssertEquals(originalProgramFiles32Value + @"\MSBuild", msbuildExtensionsPath32Value);

                // Now try setting it temporarily to some value -- as if we were on a 64 bit machine -- and getting MSBuildExtensionsPath32 again.
                try
                {
                    Environment.SetEnvironmentVariable(programFiles32EnvVar, @"c:\Program Files (x86)");
                    Project p2 = new Project(new Engine());
                    msbuildExtensionsPath32Value = (string)p2.EvaluatedProperties[ReservedPropertyNames.extensionsPath32];
                    Assertion.AssertEquals(@"c:\Program Files (x86)\MSBuild", msbuildExtensionsPath32Value);
                }
                finally
                {
                    // And restore the old value
                    Environment.SetEnvironmentVariable(programFiles32EnvVar, originalProgramFiles32Value);
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("MSBuildExtensionsPath32", extensionsPath32EnvValue);
            }
        }

        [Test]
        public void MSBuildExtensionsPath32WithEnvironmentOverride()
        {
            string originalMSBuildExtensionsPath32Value = Environment.GetEnvironmentVariable(ReservedPropertyNames.extensionsPath32);

            try
            {
                // Set an env var called MSBuildExtensionsPath32 to some value, for the purpose
                // of seeing whether our value wins.
                Environment.SetEnvironmentVariable(ReservedPropertyNames.extensionsPath32, @"c:\devdiv\vscore\msbuild");
                Project p = new Project(new Engine());
                string msbuildExtensionsPath32Value = (string)p.EvaluatedProperties[ReservedPropertyNames.extensionsPath32];
                Assertion.AssertEquals(@"c:\devdiv\vscore\msbuild", msbuildExtensionsPath32Value);
            }
            finally
            {
                // And restore the old value
                Environment.SetEnvironmentVariable(ReservedPropertyNames.extensionsPath32, originalMSBuildExtensionsPath32Value);
            }
        }

        [Test]
        public void MSBuildExtensionsPath32WithGlobalOverride()
        {
            Project p = new Project(new Engine());

            // Set a global property called MSBuildExtensionsPath32 to some value, for the purpose
            // of seeing whether our value wins.
            p.GlobalProperties.SetProperty(ReservedPropertyNames.extensionsPath32, @"c:\devdiv\vscore\msbuild");
            string msbuildExtensionsPath32Value = (string)p.EvaluatedProperties[ReservedPropertyNames.extensionsPath32];
            Assertion.AssertEquals(@"c:\devdiv\vscore\msbuild", msbuildExtensionsPath32Value);
        }


        /// <summary>
        /// Test standard reserved properties
        /// </summary>
        [Test]
        public void ReservedProjectProperties()
        {
            string file = ObjectModelHelpers.CreateTempFileOnDisk(
                  @"<Project InitialTargets=`CheckForErrors` DefaultTargets=`Build` xmlns=`msbuildnamespace`/>
                ");
            Project project = new Project(new Engine());
            project.Load(file);

            Assertion.AssertEquals(Path.GetDirectoryName(file), (string)project.EvaluatedProperties["MSBuildProjectDirectory"]);
            Assertion.AssertEquals(Path.GetFileName(file), (string)project.EvaluatedProperties["MSBuildProjectFile"]);
            Assertion.AssertEquals(Path.GetExtension(file), (string)project.EvaluatedProperties["MSBuildProjectExtension"]);
            Assertion.AssertEquals(file, (string)project.EvaluatedProperties["MSBuildProjectFullPath"]);
            Assertion.AssertEquals(Path.GetFileNameWithoutExtension(file), (string)project.EvaluatedProperties["MSBuildProjectName"]);
            Assertion.AssertEquals("Build", (string)project.EvaluatedProperties["MSBuildProjectDefaultTargets"]);

            int rootLength = Path.GetPathRoot(file).Length;
            int fileLength = Path.GetFileName(file).Length;
            string projectDirectoryNoRoot = file.Substring(rootLength, file.Length - fileLength - rootLength - 1 /* no slash */);

            Console.WriteLine("project is: " + file);
            Console.WriteLine("expect MSBuildProjectDirectoryNoRoot:" + projectDirectoryNoRoot);
            Console.WriteLine("actual MSBuildProjectDirectoryNoRoot:" + (string)project.EvaluatedProperties["MSBuildProjectDirectoryNoRoot"]);
            Assertion.AssertEquals(projectDirectoryNoRoot, (string)project.EvaluatedProperties["MSBuildProjectDirectoryNoRoot"]);
        }

        /// <summary>
        /// Test standard reserved properties
        /// </summary>
        [Test]
        public void ReservedProjectPropertiesAtRoot()
        {
            string file = Path.Combine(@"c:\", "MSBuildUnitTests_ReservedProjectPropertiesAtRoot.proj");
            try
            {
                File.WriteAllText(file, @"<Project xmlns='http://schemas.microsoft.com/developer/msbuild/2003'/>");
                Project project = new Project(new Engine());
                project.Load(file);

                Assertion.AssertEquals(Path.GetDirectoryName(file), (string)project.EvaluatedProperties["MSBuildProjectDirectory"]);
                Assertion.AssertEquals(Path.GetFileName(file), (string)project.EvaluatedProperties["MSBuildProjectFile"]);
                Assertion.AssertEquals(Path.GetExtension(file), (string)project.EvaluatedProperties["MSBuildProjectExtension"]);
                Assertion.AssertEquals(file, (string)project.EvaluatedProperties["MSBuildProjectFullPath"]);
                Assertion.AssertEquals(Path.GetFileNameWithoutExtension(file), (string)project.EvaluatedProperties["MSBuildProjectName"]);

                // Should be empty as there's no directory
                Console.WriteLine("project is: " + file);
                Console.WriteLine("expect MSBuildProjectDirectoryNoRoot: (empty)");
                Console.WriteLine("actual MSBuildProjectDirectoryNoRoot:" + (string)project.EvaluatedProperties["MSBuildProjectDirectoryNoRoot"]);
                Assertion.AssertEquals(String.Empty, (string)project.EvaluatedProperties["MSBuildProjectDirectoryNoRoot"]);
            }
            finally
            {
                if (file != null) File.Delete(file);
            }
        }

        /// <summary>
        /// Test standard reserved properties on UNC
        /// </summary>
        [Test]
        public void ReservedProjectPropertiesOnUNC()
        {
            string file = ObjectModelHelpers.CreateTempFileOnDisk(
                  @"<Project xmlns=`msbuildnamespace`/>
                ");
            Project project = new Project(new Engine());

            // Hacky way to get UNC path to file
            string uncFile = @"\\" + Environment.MachineName + @"\" + file[0] + "$" + file.Substring(2);

            project.Load(uncFile);

            Assertion.AssertEquals(Path.GetDirectoryName(uncFile), (string)project.EvaluatedProperties["MSBuildProjectDirectory"]);
            Assertion.AssertEquals(Path.GetFileName(uncFile), (string)project.EvaluatedProperties["MSBuildProjectFile"]);
            Assertion.AssertEquals(Path.GetExtension(uncFile), (string)project.EvaluatedProperties["MSBuildProjectExtension"]);
            Assertion.AssertEquals(uncFile, (string)project.EvaluatedProperties["MSBuildProjectFullPath"]);
            Assertion.AssertEquals(Path.GetFileNameWithoutExtension(uncFile), (string)project.EvaluatedProperties["MSBuildProjectName"]);

            int fileLength = Path.GetFileName(uncFile).Length;
            int rootLength = Path.GetPathRoot(uncFile).Length;
            string projectDirectoryNoRoot = uncFile.Substring(rootLength, uncFile.Length - rootLength - fileLength);
            projectDirectoryNoRoot = EngineHelpers.EnsureNoLeadingSlash(projectDirectoryNoRoot);
            projectDirectoryNoRoot = EngineHelpers.EnsureNoTrailingSlash(projectDirectoryNoRoot);

            Console.WriteLine("project is: " + uncFile);
            Console.WriteLine("expect MSBuildProjectDirectoryNoRoot:" + projectDirectoryNoRoot);
            Console.WriteLine("actual MSBuildProjectDirectoryNoRoot:" + (string)project.EvaluatedProperties["MSBuildProjectDirectoryNoRoot"]);
            Assertion.AssertEquals(projectDirectoryNoRoot, (string)project.EvaluatedProperties["MSBuildProjectDirectoryNoRoot"]);
        }

        /// <summary>
        /// MSBuildStartupDirectory should point to the directory in which the process hosting the Engine
        /// was first started.
        /// </summary>
        [Test]
        public void MSBuildStartupDirectory()
        {
            // This test crashes if Engine is not GAC'd, as it typically isn't during development
            if (Environment.GetEnvironmentVariable("DO_NOT_RUN_TESTS_REQUIRING_ENGINE_GACD") == "1")
            {
                return;
            }

            string projectFile = null;
            string resultFile = null;
            string startDirectory = null;

            string oldCurrentDirectory = Environment.CurrentDirectory;

            try
            {
                projectFile = Path.GetTempFileName();
                resultFile = Path.GetTempFileName();
                Random rand = new Random();
                startDirectory = Path.Combine(Path.GetTempPath(), Convert.ToString(rand.NextDouble()));
                Directory.CreateDirectory(startDirectory);

                Environment.CurrentDirectory = startDirectory;
                Engine e = new Engine();

                Project p = ObjectModelHelpers.CreateInMemoryProject(e, @"
                    <Project ToolsVersion='msbuilddefaulttoolsversion' xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
                        <Target Name='Build'>
                            <WriteLinesToFile File='" + resultFile + @"' Lines='[$(MSBuildStartupDirectory)]'/>
                        </Target>
                    </Project>", null);

                bool result = p.Build();

                Assertion.Assert("ERROR: Check Engine is in the GAC?", File.Exists(resultFile));

                FileInfo fileInfo = new FileInfo(resultFile);
                Assertion.Assert("ERROR: Check Engine is in the GAC?", fileInfo.Length > 0);

                string resultContent = File.ReadAllLines(resultFile)[0];
                Console.WriteLine("[$(MSBuildStartupDirectory)] was: " + resultContent);
                Assertion.AssertEquals("[" + startDirectory + "]", resultContent);
            }
            catch (Exception e)
            {
                string stack = e.StackTrace.Replace('\n', ' ').Replace('\t', ' ');
                Assertion.Fail(stack);
                throw;
            }
            finally
            {
                // Sometimes this fails with "being used by another process" - heaven knows why;
                // use a Sleep and a catch to make it more robust.
                System.Threading.Thread.Sleep(3);

                try
                {
                    Environment.CurrentDirectory = oldCurrentDirectory;
                    File.Delete(projectFile);
                    File.Delete(resultFile);
                    Directory.Delete(startDirectory);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }
    }

    [TestFixture]
    public class LoadAndSave
    {
        /// <summary>
        /// Just load an MSBuild project by passing in a TextReader, and get back the contents to
        /// make sure the project was read in correctly.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void LoadFromTextReader()
        {
            StringReader stringReader = new StringReader(ObjectModelHelpers.CleanupFileContents(@"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <PropertyGroup>
                        <blah>true</blah>
                    </PropertyGroup>
                </Project>
                "));

            Engine engine = new Engine(@"c:\");
            Project project = engine.CreateNewProject();
            project.Load(stringReader);

            ObjectModelHelpers.CompareProjectContents(project, @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <PropertyGroup>
                        <blah>true</blah>
                    </PropertyGroup>
                </Project>
                ");
        }

        /// <summary>
        /// Just load an MSBuild project with invalid XML by passing in a TextReader, and make sure
        /// it throws an InvalidProjectFileException.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        [ExpectedException(typeof(InvalidProjectFileException))]
        public void LoadFromTextReaderInvalidXml()
        {
            StringReader stringReader = new StringReader(ObjectModelHelpers.CleanupFileContents(@"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                </PROJECT>
                "));

            Engine engine = new Engine(@"c:\");
            Project project = engine.CreateNewProject();
            project.Load(stringReader);
        }

        /// <summary>
        /// Just load an MSBuild project with invalid XML by passing in a TextReader, and make sure
        /// it throws an InvalidProjectFileException.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        [ExpectedException(typeof(InvalidProjectFileException))]
        public void LoadFromTextReaderInvalidProject()
        {
            StringReader stringReader = new StringReader(ObjectModelHelpers.CleanupFileContents(@"
                <Project xmlns=`foobar`>
                </Project>
                "));

            Engine engine = new Engine(@"c:\");
            Project project = engine.CreateNewProject();
            project.Load(stringReader);
        }

        /// <summary>
        /// Exercises the internal-only feature of being able to load a project from a XML document.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void LoadFromXmlDocument()
        {
            XmlDocument xmldoc = new XmlDocument();
            xmldoc.LoadXml(ObjectModelHelpers.CleanupFileContents(@"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <PropertyGroup>
                        <blah>true</blah>
                    </PropertyGroup>
                </Project>
                "));

            Engine engine = new Engine(@"c:\");
            Project project = engine.CreateNewProject();
            project.LoadFromXmlDocument(xmldoc, null, ProjectLoadSettings.None);

            ObjectModelHelpers.CompareProjectContents(project, @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <PropertyGroup>
                        <blah>true</blah>
                    </PropertyGroup>
                </Project>
                ");
        }

        /// <summary>
        /// Exercises the internal-only feature of being able to load a project from a XML document,
        /// when the project file is invalid.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        [ExpectedException(typeof(InvalidProjectFileException))]
        public void LoadFromXmlDocumentInvalidProject()
        {
            XmlDocument xmldoc = new XmlDocument();
            xmldoc.LoadXml(ObjectModelHelpers.CleanupFileContents(@"
                <Project xmlns=`foobar`>
                </Project>
                "));

            Engine engine = new Engine(@"c:\");
            Project project = engine.CreateNewProject();
            project.LoadFromXmlDocument(xmldoc, null, ProjectLoadSettings.None);
        }

        /// <summary>
        /// Exercises the internal-only feature of being able to load a project from a XML document,
        /// when the project file is invalid.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        [ExpectedException(typeof(InvalidProjectFileException))]
        public void LoadFromStringInvalidXml()
        {
            string projectContents = ObjectModelHelpers.CleanupFileContents(@"
                <Project xmlns=`foobar`>
                </PROJECT>
                ");

            Engine engine = new Engine(@"c:\");
            Project project = engine.CreateNewProject();
            project.LoadXml(projectContents);
        }

        /// <summary>
        /// Tests the 'load project ignoring imports' flag
        /// </summary>
        [Test]
        public void LoadFromXmlDocumentIgnoringImports()
        {
            XmlDocument xmldoc = new XmlDocument();
            xmldoc.LoadXml(ObjectModelHelpers.CleanupFileContents(@"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` DefaultTargets=`Build` InitialTargets=`Clean` xmlns=`msbuildnamespace`>
                    <Import Project=`Microsoft.Uncommon.targets` />
                </Project>
                "));

            Engine engine = new Engine(@"c:\");
            Project project = engine.CreateNewProject();
            project.LoadFromXmlDocument(xmldoc, null, ProjectLoadSettings.IgnoreMissingImports);

            Assert.AreEqual(1, project.Imports.Count);
        }

        /// <summary>
        /// Make sure missing imports throw for the standard load
        /// </summary>
        [Test]
        [ExpectedException(typeof(InvalidProjectFileException))]
        public void LoadFromXmlDocumentMissingImport()
        {
            XmlDocument xmldoc = new XmlDocument();
            xmldoc.LoadXml(ObjectModelHelpers.CleanupFileContents(@"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <Import Project=`Microsoft.Uncommon.targets` />
                </Project>
                "));

            Engine engine = new Engine(@"c:\");
            Project project = engine.CreateNewProject();
            project.LoadFromXmlDocument(xmldoc, null, ProjectLoadSettings.None);
        }

        [Test]
        public void RemoveMissingImportAndLoadNormally()
        {
            XmlDocument xmldoc = new XmlDocument();
            xmldoc.LoadXml(ObjectModelHelpers.CleanupFileContents(@"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` DefaultTargets=`Build` InitialTargets=`Clean` xmlns=`msbuildnamespace`>
                    <Import Project=`Microsoft.Uncommon.targets` />
                </Project>
                "));

            // Load the project ignoring missing imports
            Engine engine = new Engine(@"c:\");
            Project project = engine.CreateNewProject();
            project.LoadFromXmlDocument(xmldoc, null, ProjectLoadSettings.IgnoreMissingImports);
            Assert.AreEqual(1, project.Imports.Count);

            IEnumerator enumerator = project.Imports.GetEnumerator();
            enumerator.MoveNext();
            Import import = (Import) enumerator.Current;

            // Remove the import
            project.Imports.RemoveImport(import);
            Assert.AreEqual(0, project.Imports.Count);

            // Save the modified project
            StringWriter writer = new StringWriter();
            project.Save(writer);

            // Load the modified project into a new project object
            xmldoc = new XmlDocument();
            xmldoc.LoadXml(writer.ToString());

            Project projectWithNoImport = engine.CreateNewProject();
            project.LoadFromXmlDocument(xmldoc, null, ProjectLoadSettings.None);
            Assert.AreEqual(0, project.Imports.Count);
        }
    }

    [TestFixture]
    public class Build
    {
        /// <summary>
        /// Targets that fail should not produce target outputs (the list that comes back from project.build())
        /// (Note that they may change the items and properties that are visible in the project after a build is done, though.)
        /// All this is Whidbey behavior.
        /// </summary>
        [Test]
        public void FailingTargetsDoNotHaveOutputs()
        {
            Project p = ObjectModelHelpers.CreateInMemoryProject(@"

                    <Project xmlns=`msbuildnamespace`>
                        <Target Name=`Build` Outputs=`$(p);@(i);$(q)`>
                            <CreateProperty Value=`v`>
                                <Output TaskParameter=`Value` PropertyName=`p` />
                            </CreateProperty>
                            <CreateItem Value=`a`>
                                <Output TaskParameter=`Value` ItemName=`i` />
                            </CreateItem>
                            <ItemGroup>
                                <i Include='b'/>
                            </ItemGroup>
                            <PropertyGroup>
                                <q>u</q>
                            </PropertyGroup>
                            <Error Text='error occurred'/>
                        </Target>
                    </Project>

                ");

            Hashtable outputs = new Hashtable();
            bool result = p.Build(new string[] { "Build" }, outputs);

            Assertion.AssertEquals(false, result);
            Assertion.AssertEquals(0, outputs.Count);
        }

        /// <summary>
        /// Checks to make sure that passing in the DoNotResetPreviouslyBuiltTargets flag
        /// works as expected.
        /// </summary>
        /// <owner>JomoF</owner>
        [Test]
        public void CheckDoNotResetPreviouslyBuiltTargets()
        {
            // Create a temporary file.
            string tempFile = Path.GetTempFileName();

            // This project sets $(FileExists) to true if the file exists
            Project p = ObjectModelHelpers.CreateInMemoryProject(String.Format(@"

                    <Project DefaultTargets=`Build` ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                        <Target Name=`Build` Condition=`Exists('{0}')`>
                            <CreateProperty Value=`true`>
                                <Output TaskParameter=`Value` PropertyName=`FileExists` />
                            </CreateProperty>
                        </Target>
                    </Project>

                ", tempFile));

            // Build first time with 'DoNotResetPreviouslyBuiltTargets' passed in.
            p.Build(new string[]{"Build"}, null, BuildSettings.DoNotResetPreviouslyBuiltTargets);

            // At this point, the property $(FileExists) should be 'true'
            Assertion.AssertEquals("true", p.GetEvaluatedProperty("FileExists"));

            // Delete the file
            File.Delete(tempFile);

            // Build again. The result should still be 'true' because the target won't be reevaluated.
            p.Build(new string[]{"Build"}, null, BuildSettings.DoNotResetPreviouslyBuiltTargets);
            Assertion.AssertEquals("true", p.GetEvaluatedProperty("FileExists"));

            // Build a third time, but now don't pass the DoNotResetPreviouslyBuiltTargets flag. The target should
            // be reevaluated and the result should be empty.
            p.Build(new string[]{"Build"}, null, BuildSettings.None);
            Assertion.AssertEquals(null, p.GetEvaluatedProperty("FileExists"));
        }

        /// <summary>
        /// Makes sure that after somebody requests a build of some targets in a project, the project
        /// should always be in the "not reset" state.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void AllTargetsGetRebuiltAfterModificationToProject()
        {
            Project p = ObjectModelHelpers.CreateInMemoryProject(@"

                    <Project DefaultTargets=`Build` ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                        <Target Name=`Build`/>
                    </Project>

                ");

            // Set some random property in the project to force it to be dirty.
            p.SetProperty("Foo", "Bar", null);

            Assertion.Assert("Project should be dirty.", p.IsDirty);

            // Build the target.
            p.Build("Build");

            Assertion.Assert("Project should not be in the 'reset' state after a build", !p.IsReset);
        }
    }

    [TestFixture]
    public class InitialTargets
    {
        /// <summary>
        /// Simple case.  Just make sure that the target specified in InitialTargets gets run.
        /// </summary>
        [Test]
        public void RunInitialTargetsInMainProject()
        {
            MockLogger myLogger = new MockLogger();
            Project p = ObjectModelHelpers.CreateInMemoryProject(@"

                    <Project InitialTargets=`CheckForErrors` DefaultTargets=`Build` ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                        <Target Name=`Build`>
                            <Message Text=`BuildTargetExecuted`/>
                        </Target>
                        <Target Name=`CheckForErrors`>
                            <Message Text=`CheckForErrorsTargetExecuted`/>
                        </Target>
                    </Project>

                ", myLogger);

            // Build the target.
            p.Build(null, null);

            Assertion.Assert("Build target should have been run.", myLogger.FullLog.Contains("BuildTargetExecuted"));
            Assertion.Assert("CheckForErrors target should have been run.", myLogger.FullLog.Contains("CheckForErrorsTargetExecuted"));
        }

        /// <summary>
        /// Simple case.  Just make sure that the target specified in InitialTargets gets run.
        /// </summary>
        [Test]
        public void RunInitialTargetsInMainProjectWithMissingTargets()
        {
            MockLogger myLogger = new MockLogger();
            Project p = ObjectModelHelpers.CreateInMemoryProject(new Engine(),
                  @"<Project InitialTargets=`CheckForErrors` DefaultTargets=`Build` ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                        <Target Name=`Build`>
                            <Message Text=`BuildTargetExecuted`/>
                        </Target>
                        <Target Name=`CheckForErrors`>
                            <Message Text=`CheckForErrorsTargetExecuted`/>
                        </Target>
                        <Import Project=`baaaa`/>
                    </Project>

                ", myLogger, null, ProjectLoadSettings.IgnoreMissingImports);

            // Build the target.
            p.Build(null, null);

            Assertion.Assert("Build target should have been run.", myLogger.FullLog.Contains("BuildTargetExecuted"));
            Assertion.Assert("CheckForErrors target should have been run.", myLogger.FullLog.Contains("CheckForErrorsTargetExecuted"));
        }

        /// <summary>
        /// We have an "InitialTargets" attribute in the main project as well as two imported projects.  Make sure we
        /// run those initial targets in the correct expected order.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void RunInitialTargetsInMainAndImportedProjects()
        {
            Environment.SetEnvironmentVariable("MyNewChecks", "NewChecks");

            string importedProject1 = ObjectModelHelpers.CreateTempFileOnDisk(@"

                    <Project InitialTargets=`CheckForBadProperties` ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                        <Target Name=`CheckForBadItems`>
                            <CreateItem Include=`CheckForBadItems_Executed`><Output ItemName=`TargetOrder` TaskParameter=`Include`/></CreateItem>
                        </Target>

                        <Target Name=`CheckForBadProperties` DependsOnTargets=`CheckForBadPlatforms; CheckForBadItems`>
                            <CreateItem Include=`CheckForBadProperties_Executed`><Output ItemName=`TargetOrder` TaskParameter=`Include`/></CreateItem>
                        </Target>

                    </Project>

                ");

            string importedProject2 = ObjectModelHelpers.CreateTempFileOnDisk(@"

                    <Project InitialTargets=`CheckForBadConfigurations` ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                        <Target Name=`CheckForBadPlatforms`>
                            <CreateItem Include=`CheckForBadPlatforms_Executed`><Output ItemName=`TargetOrder` TaskParameter=`Include`/></CreateItem>
                        </Target>

                        <Target Name=`CheckForBadConfigurations` DependsOnTargets=`CheckForBadPlatforms`>
                            <CreateItem Include=`CheckForBadConfigurations_Executed`><Output ItemName=`TargetOrder` TaskParameter=`Include`/></CreateItem>
                        </Target>

                    </Project>

                ");

            try
            {
                Project p = ObjectModelHelpers.CreateInMemoryProject(String.Format(@"

                        <Project InitialTargets=`CheckForBadUser` DefaultTargets=`Build` ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                            <Import Project=`{0}`/>

                            <Target Name=`Build`>
                                <CreateItem Include=`Build_Executed`><Output ItemName=`TargetOrder` TaskParameter=`Include`/></CreateItem>
                            </Target>

                            <Import Project=`{1}`/>

                            <Target Name=`CheckForBadUser`>
                                <CreateItem Include=`CheckForBadUser_Executed`><Output ItemName=`TargetOrder` TaskParameter=`Include`/></CreateItem>
                            </Target>

                            <Target Name=`CheckForBadItems`>
                                <CreateItem Include=`CheckForBadItems_Overridden_Executed`><Output ItemName=`TargetOrder` TaskParameter=`Include`/></CreateItem>
                            </Target>

                            <Target Name=`NewChecks`>
                                <CreateItem Include=`NewChecks_Executed`><Output ItemName=`TargetOrder` TaskParameter=`Include`/></CreateItem>
                            </Target>

                        </Project>

                    ", importedProject1, importedProject2));

                Assertion.AssertEquals("Check all InitialTargets", "CheckForBadUser; CheckForBadProperties; CheckForBadConfigurations",
                    p.InitialTargets);

                // Build the default target.
                p.Build(null, null);

                DumpBuildItemGroup(p.GetEvaluatedItemsByName("TargetOrder"));

                // The following method will ensure that the targets were executed in the correct order.
                EngineHelpers.AssertItemsMatch(@"
                    CheckForBadUser_Executed
                    CheckForBadPlatforms_Executed
                    CheckForBadItems_Overridden_Executed
                    CheckForBadProperties_Executed
                    CheckForBadConfigurations_Executed
                    Build_Executed
                    ",
                    p.GetEvaluatedItemsByName("TargetOrder"));

                // Change the InitialTargets on the main project to be "NewChecks", but do it via an environment variable.
                p.InitialTargets = "$(MyNewChecks)";

                Assertion.AssertEquals("Check all InitialTargets", "NewChecks; CheckForBadProperties; CheckForBadConfigurations",
                    p.InitialTargets);

                // Build the default target.
                p.Build(null, null);

                DumpBuildItemGroup(p.GetEvaluatedItemsByName("TargetOrder"));

                // The following method will ensure that the targets were executed in the correct order.
                EngineHelpers.AssertItemsMatch(@"
                    NewChecks_Executed
                    CheckForBadPlatforms_Executed
                    CheckForBadItems_Overridden_Executed
                    CheckForBadProperties_Executed
                    CheckForBadConfigurations_Executed
                    Build_Executed
                    ",
                    p.GetEvaluatedItemsByName("TargetOrder"));
            }
            finally
            {
                File.Delete(importedProject1);
                File.Delete(importedProject2);
            }
        }

        private static void DumpBuildItemGroup(BuildItemGroup itemGroup)
        {
            Console.WriteLine(itemGroup.Count);
            foreach (BuildItem item in itemGroup)
            {
                Console.WriteLine(item.Name + " : " + item.FinalItemSpec);
            }
        }

        /// <summary>
        /// Makes sure that after somebody requests a build of some targets in a project, the project
        /// should always be in the "not reset" state.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void ModifyInitialTargetsInMainProject()
        {
            MockLogger myLogger = new MockLogger();
            Project p = ObjectModelHelpers.CreateInMemoryProject(@"

                    <Project DefaultTargets=`Build` ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                        <Target Name=`Build`>
                            <Message Text=`BuildTargetExecuted`/>
                        </Target>
                        <Target Name=`CheckForErrors`>
                            <Message Text=`CheckForErrorsTargetExecuted`/>
                        </Target>
                    </Project>

                ", myLogger);

            Assertion.AssertEquals("InitialTargets should be empty to start with.", String.Empty, p.InitialTargets);
            p.InitialTargets = "CheckForErrors";
            Assertion.AssertEquals("InitialTargets should be set.", "CheckForErrors", p.InitialTargets);

            ObjectModelHelpers.CompareProjectContents(p, @"

                    <Project DefaultTargets=`Build` ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace` InitialTargets=`CheckForErrors`>
                        <Target Name=`Build`>
                            <Message Text=`BuildTargetExecuted`/>
                        </Target>
                        <Target Name=`CheckForErrors`>
                            <Message Text=`CheckForErrorsTargetExecuted`/>
                        </Target>
                    </Project>

                ");

            // Build the default target.
            p.Build(null, null);

            Assertion.Assert("Build target should have been run.", myLogger.FullLog.Contains("BuildTargetExecuted"));
            Assertion.Assert("CheckForErrors target should have been run.", myLogger.FullLog.Contains("CheckForErrorsTargetExecuted"));
        }

    }

    [TestFixture]
    public class Miscellaneous
    {
        /// <summary>
        /// Regression test for bug VSWhidbey 403429
        /// </summary>
        /// <owner>danmose</owner>
        [Test]
        public void DocTypeInProject()
        {
            string original = @"
                <!DOCTYPE content PUBLIC `foo` `` []>
                <?xml-stylesheet type=`test/xsl` href=`file:||foo||`?>
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <Target Name=`t`/>
                </Project>
                ";

            Project project = ObjectModelHelpers.CreateInMemoryProject(original);
            // Does not throw
        }

        /// <summary>
        /// Test the various encoding and writing methods.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void LoadAndSaveWithDifferentEncodings()
        {
            string file = Path.GetTempFileName();

            string original = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>

                    <ItemGroup Condition=`'$(x)'=='y'`>
                        <ReferencePath Include=`c:\foobar` />
                    </ItemGroup>

                    <ItemGroup Condition=`'$(x)'=='z'`>
                        <ReferencePath Include=`c:\foobar` />
                    </ItemGroup>

                </Project>
                ";

            Project project = ObjectModelHelpers.CreateInMemoryProject(original);

            Engine engine = new Engine();
            engine.BinPath = @"c:\";

            // Save and load using each encoding scheme. The ultimate result should be identity.
            File.Delete(file);
            project.Save(file, Encoding.Default);
            project = new Project(engine);
            project.Load(file);
            string header = string.Format(@"<?xml version=`1.0` encoding=`{0}`?>", Encoding.Default.WebName);
            ObjectModelHelpers.CompareProjectContents(project, header + original);

            File.Delete(file);
            project.Save(file, Encoding.UTF8);
            project.ParentEngine.UnloadProject(project);
            project = new Project(engine);
            project.Load(file);
            ObjectModelHelpers.CompareProjectContents(project, @"<?xml version=`1.0` encoding=`utf-8`?>" + original);

            File.Delete(file);
            project.Save(file, Encoding.Unicode);
            project.ParentEngine.UnloadProject(project);
            project = new Project(engine);
            project.Load(file);
            ObjectModelHelpers.CompareProjectContents(project, @"<?xml version=`1.0` encoding=`utf-16`?>" + original);

            // Save with current encoding.
            File.Delete(file);
            project.Save(file);
            project.ParentEngine.UnloadProject(project);
            project = new Project(engine);
            project.Load(file);
            ObjectModelHelpers.CompareProjectContents(project, @"<?xml version=`1.0` encoding=`utf-16`?>" + original);
            File.Delete(file);

            // Save to writer.
            File.Delete(file);
            using (StreamWriter writer = new StreamWriter(file))
            {
                project.Save(writer);
            }
            project.ParentEngine.UnloadProject(project);
            project = new Project(engine);
            project.Load(file);
            ObjectModelHelpers.CompareProjectContents(project, @"<?xml version=`1.0` encoding=`utf-8`?>" + original);
            File.Delete(file);

            // Verify the final encoding state is Utf-8.
            Assertion.AssertEquals(Encoding.UTF8, project.Encoding);
        }

        /// <summary>
        /// Set and Get ProjectExtensions
        /// </summary>
        /// <owner>danmose</owner>
        [Test]
        public void SetGetProjectExtensions()
        {
            string original = @"
                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <Target Name=`t`/>
                </Project>
                ";

            Project project = ObjectModelHelpers.CreateInMemoryProject(original);
            project.SetProjectExtensions("myID", "<foo />");
            project.SetProjectExtensions("myOtherID", "<bar />");
            Assertion.AssertEquals("<foo />", project.GetProjectExtensions("myID"));
            Assertion.AssertEquals("", project.GetProjectExtensions("myNonexistent"));
        }

        /// <summary>
        /// There is a certain error that the MSBuild engine fires when you try to do a build on
        /// a project that has had its targets disabled because of security.  However, the project
        /// system doesn't want to show this error to the user because it's not actionable for
        /// the user.  So it looks for code MSB4112 to throw away this error.  Here we're just
        /// to catch the case where somebody accidentally changes the error code for this error,
        /// without realizing that somebody else has a dependency on it.
        /// </summary>
        /// <owner>RGoel</owner>
        [Test]
        public void VerifySecurityErrorHasCodeMSB4112()
        {
            ResourceManager resourceManager = new ResourceManager("Microsoft.Build.Engine.Resources.Strings", typeof(Project).Assembly);
            string securityMessage = resourceManager.GetString("SecurityProjectBuildDisabled", CultureInfo.CurrentUICulture);

            Assertion.Assert(
                "Security message about disabled targets need to have code MSB4112, because code in the VS Core project system depends on this.  See DesignTimeBuildFeedback.cpp.",
                securityMessage.Contains("MSB4112")
            );
        }

        /// <summary>
        /// Verify that warning & error tags at target level work correctly
        /// </summary>
        /// <owner>LukaszG</owner>
        [Test]
        public void WarningErrorTagsTargetLevel()
        {
            MockLogger logger = new MockLogger();
            Project project = ObjectModelHelpers.CreateInMemoryProject(@"
                <Project DefaultTargets=`Build` ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <Target Name=`Build`>
                        <Warning Text=`This is a scary warning message.` Code=`MSB9999` HelpKeyword=`MSBuild.keyword`/>
                        <Error Text=`A horrible error has occurred. Be very afraid.` Code=`MSB1111` HelpKeyword=`MSBuild.otherkeyword`/>
                    </Target>
                </Project>
                ", logger);

            project.Build(null, null);

            Assertion.AssertEquals(1, logger.Warnings.Count);
            BuildWarningEventArgs warning = logger.Warnings[0];

            Assertion.AssertEquals("This is a scary warning message.", warning.Message);
            Assertion.AssertEquals("MSB9999", warning.Code);
            Assertion.AssertEquals("MSBuild.keyword", warning.HelpKeyword);

            Assertion.AssertEquals(1, logger.Errors.Count);
            BuildErrorEventArgs error = logger.Errors[0];

            Assertion.AssertEquals("A horrible error has occurred. Be very afraid.", error.Message);
            Assertion.AssertEquals("MSB1111", error.Code);
            Assertion.AssertEquals("MSBuild.otherkeyword", error.HelpKeyword);
        }

        [Test]
        public void TestLoadProjectDifferentGP()
        {
            MockLogger logger = new MockLogger();
            string path = ObjectModelHelpers.CreateTempFileOnDisk(@"
                <Project DefaultTargets=`Build` ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <Target Name=`Build`>
                        <Warning Text=`This is a scary warning message.` Code=`MSB9999` HelpKeyword=`MSBuild.keyword`/>
                        <Error Text=`A horrible error has occurred. Be very afraid.` Code=`MSB1111` HelpKeyword=`MSBuild.otherkeyword`/>
                    </Target>
                </Project>
                ");

            Engine engine = new Engine();

            Project project = engine.CreateNewProject();

            project.Load(path);

            project.GlobalProperties.SetProperty("a", "b");
            engine.BuildProjectFile(path);
        }

        [Test]
        public void ICollectionMethodsOnItemPropertyGroupCollection()
        {
            Engine engine = new Engine(@"C:\");
            Project project = new Project(engine);
            BuildPropertyGroup pg1 = project.AddNewPropertyGroup(true);
            BuildPropertyGroup pg2 = project.AddNewPropertyGroup(true);
            BuildItemGroup ig1 = project.AddNewItemGroup();
            BuildItemGroup ig2 = project.AddNewItemGroup();

            BuildPropertyGroup[] pgarray = new BuildPropertyGroup[2];
            BuildItemGroup[] igarray = new BuildItemGroup[2];

            project.PropertyGroups.CopyTo(pgarray, 0);
            project.ItemGroups.CopyTo(igarray, 0);

            Assertion.Assert(pgarray[0] == pg1 || pgarray[1] == pg1);
            Assertion.Assert(pgarray[0] == pg2 || pgarray[1] == pg2);

            Assertion.Assert(igarray[0] == ig1 || igarray[1] == ig1);
            Assertion.Assert(igarray[0] == ig2 || igarray[1] == ig2);
        }

        [Test]
        public void RegressVsWhidbey579075()
        {
            MockProjectStartedLogger logger = new MockProjectStartedLogger();
            Project project = ObjectModelHelpers.CreateInMemoryProject(@"
                <Project DefaultTargets=`Build` ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                    <Target Name=`Build`>
                    </Target>
                </Project>
                ", logger);

            // Set a property and force project evaluation
            project.SetProperty("Configuration", "Release");
            BuildPropertyGroup evaluatedProperties = project.EvaluatedProperties;

            // Set a different value of the property and build without forced reevaluation,
            // check if the new value is passed to the logger
            project.SetProperty("Configuration", "Debug");
            project.Build();
            Assertion.AssertEquals("Debug", logger.ProjectStartedProperties["Configuration"]);
        }
    }

    [TestFixture]
    public class ToolsVersion
    {
        [Test]
        public void VersionBasedMSBuildBinPathDefault()
        {
            Engine e = new Engine("www.msbuild.org");
            Project project = ObjectModelHelpers.CreateInMemoryProject(e, @"
                <Project DefaultTargets=`Build` xmlns=`msbuildnamespace`>
                    <Target Name=`Build`/>
                </Project>", null);

            Assertion.AssertEquals("Nonexistent ToolsVersion should evaluate to the default version",
                Constants.defaultToolsVersion, project.ToolsVersion);

            Assertion.AssertEquals("Nonexistent ToolsVersion should mean ToolsVersionAttribute is the default version",
                Constants.defaultToolsVersion, project.DefaultToolsVersion);

            Assertion.AssertEquals("BinPath is the MSBuildBinPath for the default version",
                "www.msbuild.org", project.EvaluatedProperties[ReservedPropertyNames.binPath].FinalValue);

            Assertion.AssertEquals("BinPath is the MSBuildToolsPath for the default version",
                "www.msbuild.org", project.EvaluatedProperties[ReservedPropertyNames.toolsPath].FinalValue);
        }

        [Test]
        public void VersionBasedMSBuildBinPathExplicit()
        {
            Engine e = new Engine("www.msbuild.org");
            e.AddToolset(new Toolset("myValidToolsVersion", "myValidToolsVersion's path"));

            Project project = ObjectModelHelpers.CreateInMemoryProject(e, @"
                <Project ToolsVersion=`myValidToolsVersion` DefaultTargets=`Build` xmlns=`msbuildnamespace`>
                    <Target Name=`Build`/>
                </Project>", null);

            Assertion.AssertEquals("ToolsVersion should have been picked up from the project attribute",
                "myValidToolsVersion", project.ToolsVersion);

            Assertion.AssertEquals("ToolsVersionAttribute should have been picked up from the project attribute",
                "myValidToolsVersion", project.DefaultToolsVersion);

            Assertion.AssertEquals("BinPath is the MSBuildBinPath for the default version",
                "myValidToolsVersion's path", project.EvaluatedProperties[ReservedPropertyNames.binPath].FinalValue);

            Assertion.AssertEquals("BinPath is the MSBuildToolsPath for the default version",
                "myValidToolsVersion's path", project.EvaluatedProperties[ReservedPropertyNames.toolsPath].FinalValue);
        }

        [Test]
        public void ChangingToolsVersion()
        {
            Engine e = new Engine("www.msbuild.org");
            e.AddToolset(new Toolset("myValidToolsVersion", "myValidToolsVersion's path"));

            Project project = ObjectModelHelpers.CreateInMemoryProject(e, @"
                <Project DefaultTargets=`Build` xmlns=`msbuildnamespace`>
                    <Target Name=`Build`/>
                </Project>", null);

            Assertion.AssertEquals("Nonexistent ToolsVersion should evaluate to the default version",
                Constants.defaultToolsVersion, project.ToolsVersion);

            Assertion.AssertEquals("BinPath is the MSBuildBinPath for the default version",
                "www.msbuild.org", project.EvaluatedProperties[ReservedPropertyNames.binPath].FinalValue);

            Assertion.AssertEquals("BinPath is the MSBuildToolsPath for the default version",
                "www.msbuild.org", project.EvaluatedProperties[ReservedPropertyNames.toolsPath].FinalValue);

            project.DefaultToolsVersion = "myValidToolsVersion";

            Assertion.AssertEquals("ToolsVersion should have been changed by the project attribute (because it wasn't overridden)",
                "myValidToolsVersion", project.ToolsVersion);

            Assertion.AssertEquals("ToolsVersionAttribute should have been picked up from the project attribute",
                "myValidToolsVersion", project.DefaultToolsVersion);

            Assertion.AssertEquals("OverridingToolsVersion should be false",
                false, project.OverridingToolsVersion);

            Assertion.AssertEquals("BinPath is the MSBuildBinPath for the default version",
                "myValidToolsVersion's path", project.EvaluatedProperties[ReservedPropertyNames.binPath].FinalValue);

            Assertion.AssertEquals("BinPath is the MSBuildToolsPath for the default version",
                "myValidToolsVersion's path", project.EvaluatedProperties[ReservedPropertyNames.toolsPath].FinalValue);
        }

        /// <summary>
        /// It's okay to change DefaultToolsVersion to some apparently bogus value -- the project can be persisted
        /// that way, and maybe later it'll correspond to some known toolset. If the effective ToolsVersion was being
        /// gotten from the attribute, that'll be affected too; and thus might be bogus.
        /// </summary>
        [Test]
        public void ChangingToolsVersionAttributeToUnrecognizedValue()
        {
            Engine e = new Engine("www.msbuild.org");
            e.AddToolset(new Toolset("myValidToolsVersion", "myValidToolsVersion's path"));

            Project project = ObjectModelHelpers.CreateInMemoryProject(e, @"
                <Project ToolsVersion=`myValidToolsVersion` DefaultTargets=`Build` xmlns=`msbuildnamespace`>
                    <Target Name=`Build`/>
                </Project>", null);

            Assertion.AssertEquals("We should have toolsVersion equal to myValidToolsVersion",
                "myValidToolsVersion", project.DefaultToolsVersion);

            // Build should succeed at this point
            Assertion.Assert(project.Build());

            project.DefaultToolsVersion = "UnknownToolsVersion";

            // When an unknown toolsversion is used, MSBuild treats it as TV4.0.
            Assertion.AssertEquals("Because a bogus ToolsVersion was set, the ToolsVersion gets treated as v4.0", "4.0", project.ToolsVersion);

            Assertion.AssertEquals("ToolsVersionAttribute has the new value", "4.0", project.DefaultToolsVersion);

            // It's a valid ToolsVersion, so the build should succeed
            Assertion.Assert(project.Build());
        }

        /// <summary>
        /// If project has not loaded from XML, it should have the default tools version
        /// </summary>
        [Test]
        public void EmptyProjectCreatedViaOMHasDefaultToolsVersion()
        {
            Engine e = new Engine("www.msbuild.org");
            Project p = new Project(e);
            // Don't load any project here
            Assertion.AssertEquals(Constants.defaultToolsVersion, p.ToolsVersion);
        }

        [Test]
        public void ToolsVersionAttributeConstructor()
        {
            Engine e = new Engine("www.msbuild.org");
            e.AddToolset(new Toolset("myValidToolsVersion", "myValidToolsVersion's path"));
            e.AddToolset(new Toolset("myOtherToolsVersion", "myOtherToolsVersion's path"));

            Project project = ObjectModelHelpers.CreateInMemoryProject(e, @"
                <Project ToolsVersion=`myOtherToolsVersion` DefaultTargets=`Build` xmlns=`msbuildnamespace`>
                    <Target Name=`Build`/>
                </Project>", null, "myValidToolsVersion");

            Assertion.AssertEquals("We should have Override equal to true",
                true, project.OverridingToolsVersion);
            Assertion.AssertEquals("We should have ToolsVersion equal to myValidToolsVersion",
                "myValidToolsVersion", project.ToolsVersion);
            Assertion.AssertEquals("We should have ToolsVersionAttribute equal to myOtherToolsVersion",
                "myOtherToolsVersion", project.DefaultToolsVersion);
        }

        // Regular case of setting DefaultToolsVersion
        [Test]
        public void SettingToolsVersionAttribute()
        {
            Engine e = new Engine("www.msbuild.org");
            e.AddToolset(new Toolset("myValidToolsVersion", "myValidToolsVersion's path"));

            Project project = ObjectModelHelpers.CreateInMemoryProject(e, @"
                <Project DefaultTargets=`Build` xmlns=`msbuildnamespace`>
                    <Target Name=`Build`/>
                </Project>", null);

            project.DefaultToolsVersion = "myValidToolsVersion";

            Assertion.AssertEquals("We should have Override equal to false",
                false, project.OverridingToolsVersion);
            Assertion.AssertEquals("We should have ToolsVersion equal to myValidToolsVersion",
                "myValidToolsVersion", project.ToolsVersion);
            Assertion.AssertEquals("We should have ToolsVersionAttribute equal to myValidToolsVersion",
                "myValidToolsVersion", project.DefaultToolsVersion);
        }

        // Setting DefaultToolsVersion should not modify ToolsVersion if it was an override value
        [Test]
        public void SettingToolsVersionAttributeAfterToolsVersionSetInProjectConstructor()
        {
            Engine e = new Engine("www.msbuild.org");
            e.AddToolset(new Toolset("myValidToolsVersion", "myValidToolsVersion's path"));
            e.AddToolset(new Toolset("myOtherToolsVersion", "myOtherToolsVersion's path"));

            Project project = ObjectModelHelpers.CreateInMemoryProject(e, @"
                <Project DefaultTargets=`Build` xmlns=`msbuildnamespace`>
                    <Target Name=`Build`/>
                </Project>", null, "myValidToolsVersion");

            project.DefaultToolsVersion = "myOtherToolsVersion";

            Assertion.AssertEquals("We should have Override equal to true",
                true, project.OverridingToolsVersion);
            Assertion.AssertEquals("We should have ToolsVersion equal to myOtherToolsVersion",
                "myValidToolsVersion", project.ToolsVersion);
            Assertion.AssertEquals("We should have ToolsVersionAttribute equal to myOtherToolsVersion",
                "myOtherToolsVersion", project.DefaultToolsVersion);
        }

        [Test]
        public void MSBuildToolsVersionProperty()
        {
            Engine e = new Engine("www.msbuild.org");
            e.AddToolset(new Toolset("myValidToolsVersion", "myValidToolsVersion's path"));

            MockLogger logger = new MockLogger();

            Project project = ObjectModelHelpers.CreateInMemoryProject(e, ObjectModelHelpers.CleanupFileContents(@"
                <Project DefaultTargets=`Build` xmlns=`msbuildnamespace`>
                    <UsingTask TaskName='Message' AssemblyName='Microsoft.Build.Tasks.Core, Version=msbuildassemblyversion, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a'/>
                    <Target Name=`Build`>
                        <Message Text=`##$(MSBuildToolsVersion)##`/>
                    </Target>
                </Project>"), logger);

            project.Build();

            logger.AssertLogContains("##2.0##");
        }

        [Test]
        public void MSBuildToolsVersionProperty2()
        {
            Engine e = new Engine("www.msbuild.org");
            e.AddToolset(new Toolset("myValidToolsVersion", "myValidToolsVersion's path"));

            MockLogger logger = new MockLogger();

            Project project = ObjectModelHelpers.CreateInMemoryProject(e, ObjectModelHelpers.CleanupFileContents(@"
                <Project DefaultTargets=`Build` xmlns=`msbuildnamespace`>
                    <UsingTask TaskName='Message' AssemblyName='Microsoft.Build.Tasks.Core, Version=msbuildassemblyversion, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a'/>
                    <Target Name=`Build`>
                        <Message Text=`##$(MSBuildToolsVersion)##`/>
                    </Target>
                </Project>"), logger);
            project.DefaultToolsVersion = "myValidToolsVersion";

            project.Build();

            logger.AssertLogContains("##myValidToolsVersion##");
        }

        [Test]
        public void SetEffectiveToolsVersionAttribute()
        {
            Engine e = new Engine("www.msbuild.org");
            e.AddToolset(new Toolset("myValidToolsVersion", "myValidToolsVersion's path"));
            e.AddToolset(new Toolset("myOtherToolsVersion", "myOtherToolsVersion's path"));

            MockLogger logger = new MockLogger();

            Project project = ObjectModelHelpers.CreateInMemoryProject(e, ObjectModelHelpers.CleanupFileContents(@"
                <Project ToolsVersion=`myValidToolsVersion` DefaultTargets=`Build` xmlns=`msbuildnamespace`>
                    <UsingTask TaskName='Message' AssemblyName='Microsoft.Build.Tasks.Core, Version=msbuildassemblyversion, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a'/>
                    <PropertyGroup>
                       <TheToolsVersion>$(MSBuildToolsVersion)</TheToolsVersion>
                    </PropertyGroup>
                    <Target Name=`Build`>
                       <Message Text=`### $(TheToolsVersion) ###` />
                    </Target>
                </Project>"), logger, "myValidToolsVersion");

            project.ToolsVersion = "myOtherToolsVersion";

            project.Build();

            Assertion.Assert("We should be using a ToolsVersion override", project.OverridingToolsVersion);
            Assertion.AssertEquals("We should have ToolsVersion equal to myOtherToolsVersion",
                "myOtherToolsVersion", project.ToolsVersion);
            Assertion.AssertEquals("We should have ToolsVersionAttribute equal to myValidToolsVersion",
                "myValidToolsVersion", project.DefaultToolsVersion);
            logger.AssertLogContains("### myOtherToolsVersion ###");
        }


        [Test]
        public void PropertiesFromToolsetAppliedToProjectWhenToolsVersionSet()
        {
            Engine e = new Engine("www.msbuild.org");
            BuildPropertyGroup properties1 = new BuildPropertyGroup();
            properties1.SetProperty("foo1", "bar1");
            properties1.SetProperty("foo2", "bar2");
            BuildPropertyGroup properties2 = new BuildPropertyGroup();
            properties2.SetProperty("foo3", "bar3");
            properties2.SetProperty("foo1", "bar4");
            e.AddToolset(new Toolset("myValidToolsVersion", "myValidToolsVersion's path", properties1));
            e.AddToolset(new Toolset("myOtherToolsVersion", "myOtherToolsVersion's path", properties2));

            MockLogger logger = new MockLogger();

            Project project = ObjectModelHelpers.CreateInMemoryProject(e, @"
                <Project ToolsVersion=`myValidToolsVersion` xmlns=`msbuildnamespace`/>", logger);

            Assertion.AssertEquals("bar1", project.EvaluatedProperties["foo1"].Value);
            Assertion.AssertEquals("bar2", project.EvaluatedProperties["foo2"].Value);
            Assertion.AssertEquals(null, project.EvaluatedProperties["foo3"]);

            // Now update tools version: should grab properties from the new toolset
            project.ToolsVersion = "myOtherToolsVersion";

            Assertion.AssertEquals("bar4", project.EvaluatedProperties["foo1"].Value);  // Updated
            Assertion.AssertEquals(null, project.EvaluatedProperties["foo2"]);      // Reset
            Assertion.AssertEquals("bar3", project.EvaluatedProperties["foo3"].Value);  // New
        }

        [Test]
        public void PropertiesFromToolsetAppliedToProjectWhenToolsVersionOverridden()
        {
            Engine e = new Engine("www.msbuild.org");
            BuildPropertyGroup properties1 = new BuildPropertyGroup();
            properties1.SetProperty("foo1", "bar1");
            properties1.SetProperty("foo2", "bar2");
            BuildPropertyGroup properties2 = new BuildPropertyGroup();
            properties2.SetProperty("foo3", "bar3");
            properties2.SetProperty("foo1", "bar4");
            e.AddToolset(new Toolset("myValidToolsVersion", "myValidToolsVersion's path", properties1));
            e.AddToolset(new Toolset("myOtherToolsVersion", "myOtherToolsVersion's path", properties2));

            MockLogger logger = new MockLogger();

            Project project = ObjectModelHelpers.CreateInMemoryProject(e, @"
                <Project ToolsVersion=`myValidToolsVersion` xmlns=`msbuildnamespace`/>", logger, "myOtherToolsVersion");

            Assertion.AssertEquals("bar4", project.EvaluatedProperties["foo1"].Value);  // Updated
            Assertion.AssertEquals(null, project.EvaluatedProperties["foo2"]);      // Reset
            Assertion.AssertEquals("bar3", project.EvaluatedProperties["foo3"].Value);  // New
        }
    }
}
