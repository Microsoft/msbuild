// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Build.BackEnd;
using Microsoft.Build.Construction;
using Microsoft.Build.Engine.UnitTests;
using Microsoft.Build.Exceptions;
using Microsoft.Build.Execution;
using Microsoft.Build.Shared;
using Microsoft.Build.UnitTests;
using Shouldly;
using Xunit;
using Xunit.Abstractions;
using static Microsoft.Build.Graph.UnitTests.GraphTestingUtilities;
using static Microsoft.Build.UnitTests.Helpers;

namespace Microsoft.Build.Graph.UnitTests
{
    public class GraphLoadedFromSolutionTests : IDisposable
    {
        private TestEnvironment _env;

        public GraphLoadedFromSolutionTests(ITestOutputHelper output)
        {
            _env = TestEnvironment.Create(output);
        }

        [Theory]
        [InlineData("1.sln", "2.sln")]
        [InlineData("1.sln", "2.proj")]
        public void ASolutionShouldBeTheSingleEntryPoint(params string[] files)
        {
            for (var i = 0; i < files.Length; i++)
            {
                files[i] = _env.CreateFile(files[i], string.Empty).Path;
            }

            var exception = Should.Throw<ArgumentException>(
                () =>
                {
                    new ProjectGraph(files);
                });

            exception.Message.ShouldContain("MSB4261");
        }

        [Fact]
        public void GraphConstructionFailsOnNonExistentSolution()
        {
            var exception = Should.Throw<InvalidProjectFileException>(
                () =>
                {
                    new ProjectGraph("nonExistent.sln");
                });

            exception.Message.ShouldContain("The project file could not be loaded. Could not find file");
        }

        [Fact]
        public void StaticGraphShouldNotSupportNestedSolutions()
        {
            var solutionFile = _env.CreateFile("solutionReference.sln", string.Empty).Path;

            var referenceToSolution = $@"<ItemGroup>
                                           <ProjectReference Include='{solutionFile}' />
                                       </ItemGroup>".Cleanup();

            var root = GraphTestingUtilities.CreateProjectFile(
                env: _env,
                projectNumber: 1,
                projectReferences: null,
                projectReferenceTargets: null,
                defaultTargets: null,
                extraContent: referenceToSolution);

            var exception = Should.Throw<InvalidOperationException>(
                () =>
                {
                    new ProjectGraph(root.Path);
                });

            exception.Message.ShouldContain("MSB4263:");
        }

        public static IEnumerable<object[]> Graphs
        {
            get
            {
                yield return new object[]
                {
                    new Dictionary<int, int[]>()
                };

                yield return new object[]
                {
                    new Dictionary<int, int[]>
                    {
                        {1, null}
                    }
                };

                yield return new object[]
                {
                    new Dictionary<int, int[]>
                    {
                        {1, null},
                        {2, null}
                    }
                };

                yield return new object[]
                {
                    new Dictionary<int, int[]>
                    {
                        {1, new []{2}}
                    }
                };

                yield return new object[]
                {
                    new Dictionary<int, int[]>
                    {
                        {1, new []{2}},
                        {2, new []{3}}
                    }
                };

                yield return new object[]
                {
                    new Dictionary<int, int[]>
                    {
                        {1, new []{2, 3}}
                    }
                };

                yield return new object[]
                {
                    new Dictionary<int, int[]>
                    {
                        {1, new []{3, 2}},
                        {2, new []{3}}
                    }
                };

                yield return new object[]
                {
                    new Dictionary<int, int[]>
                    {
                        {1, new []{2, 3}},
                        {2, new []{4}},
                        {3, new []{4}}
                    }
                };

                yield return new object[]
                {
                    new Dictionary<int, int[]>
                    {
                        {1, new []{2, 3}},
                        {2, new []{4}},
                        {3, new []{4}},
                        {5, new []{3, 2}}
                    }
                };
            }
        }

        public static IEnumerable<object[]> GraphsWithUniformSolutionConfigurations
        {
            get
            {
                foreach (var graph in Graphs)
                {
                    foreach (
                        var currentSolutionConfigurationPlatform in SolutionFileBuilder.SolutionConfigurationPlatformsDefaults.Concat(new SolutionConfigurationInSolution[] {null}))
                    {
                        yield return new[]
                        {
                            graph.First(),
                            currentSolutionConfigurationPlatform,
                            SolutionFileBuilder.SolutionConfigurationPlatformsDefaults
                        };
                    }
                }
            }
        }

        [Theory(Skip = "hangs in CI, can't repro locally: https://github.com/dotnet/msbuild/issues/5453")]
        [MemberData(nameof(GraphsWithUniformSolutionConfigurations))]
        public void GraphConstructionCanLoadEntryPointsFromSolution(
            Dictionary<int, int[]> edges,
            SolutionConfigurationInSolution currentSolutionConfiguration,
            IReadOnlyCollection<SolutionConfigurationInSolution> solutionConfigurations)
        {
            AssertSolutionBasedGraph(edges, currentSolutionConfiguration, solutionConfigurations);
        }

        [Theory(Skip = "hangs in CI, can't repro locally: https://github.com/dotnet/msbuild/issues/5453")]
        [MemberData(nameof(GraphsWithUniformSolutionConfigurations))]
        public void SolutionBasedGraphCanMatchProjectSpecificConfigurations(
            Dictionary<int, int[]> edges,
            SolutionConfigurationInSolution currentSolutionConfiguration,
            IReadOnlyCollection<SolutionConfigurationInSolution> solutionConfigurations)
        {
            var graph = CreateProjectGraph(_env, edges);

            var projectSpecificConfigurations = graph.ProjectNodes.ToDictionary(
                node => GetProjectNumber(node).ToString(),
                n => solutionConfigurations.ToDictionary(
                    sc => sc,
                    sc => new ProjectConfigurationInSolution(
                        configurationName: $"{sc.ConfigurationName}_{GetProjectNumber(n)}",
                        platformName: $"{sc.PlatformName}_{GetProjectNumber(n)}",
                        includeInBuild: true)));

            AssertSolutionBasedGraph(edges, currentSolutionConfiguration, solutionConfigurations, projectSpecificConfigurations);
        }

        [Fact]
        public void SolutionParserIgnoresProjectConfigurationsThatDoNotFullyMatchAnySolutionConfiguration()
        {
            var solutionContents = new SolutionFileBuilder
            {
                Projects = new Dictionary<string, string>
                {
                    {"1", _env.CreateFile("1.csproj", string.Empty).Path}
                },
                SolutionConfigurationPlatforms = new[] {new SolutionConfigurationInSolution("Foo", "Bar")},
                ProjectConfigurations = new Dictionary<string, Dictionary<SolutionConfigurationInSolution, ProjectConfigurationInSolution>>
                {
                    {
                        "1", new Dictionary<SolutionConfigurationInSolution, ProjectConfigurationInSolution>
                        {
                            {
                                new SolutionConfigurationInSolution("NonMatchingConfiguration", "NonMatchingPlatform"),
                                new ProjectConfigurationInSolution("1a", "1b", true)
                            },
                            {
                                new SolutionConfigurationInSolution("Foo", "NonMatchingPlatform"),
                                new ProjectConfigurationInSolution("1c", "1d", true)
                            }
                        }
                    }
                }
            }.BuildSolution();

            var solutionFile = _env.CreateFile("solution.sln", solutionContents).Path;

            var graph = new ProjectGraph(solutionFile);

            graph.ProjectNodes.ShouldBeEmpty();
        }

        public static IEnumerable<object[]> SolutionOnlyDependenciesData
        {
            get
            {
                yield return new object[]
                {
                    new Dictionary<int, int[]> //graph nodes and ProjectReference edges
                    {
                        {1, null},
                        {2, null}
                    },
                    new[] {(1, 2)}, // solution only edges
                    false, // is there a cycle
                    false // solution edges overlap with graph edges
                };

                yield return new object[]
                {
                    new Dictionary<int, int[]>
                    {
                        {1, null},
                        {2, null}
                    },
                    new[] {(1, 2), (2, 1)},
                    true,
                    false
                };

                yield return new object[]
                {
                    new Dictionary<int, int[]>
                    {
                        {1, null},
                        {2, null},
                        {3, null},
                        {4, null},
                        {5, null}
                    },
                    new[] {(1, 2), (1, 3), (2, 4), (3,4), (4, 5)},
                    false,
                    false
                };

                yield return new object[]
                {
                    new Dictionary<int, int[]>
                    {
                        {1, null},
                        {2, null},
                        {3, null},
                        {4, null},
                        {5, null}
                    },
                    new[] {(1, 2), (1, 3), (2, 4), (3, 4), (4, 5), (2, 3)},
                    false,
                    false
                };

                yield return new object[]
                {
                    new Dictionary<int, int[]>
                    {
                        {1, null},
                        {2, null},
                        {3, null},
                        {4, null},
                        {5, null}
                    },
                    new[] {(1, 3), (2, 3), (3, 4), (3, 5), (5, 4), (2, 1)},
                    false,
                    false
                };

                yield return new object[]
                {
                    new Dictionary<int, int[]>
                    {
                        {1, new []{2}},
                    },
                    new[] {(1, 2)},
                    false,
                    true
                };

                yield return new object[]
                {
                    new Dictionary<int, int[]>
                    {
                        {1, new []{2}},
                    },
                    new[] {(1, 2), (1, 2)},
                    false,
                    true
                };

                yield return new object[]
                {
                    new Dictionary<int, int[]>
                    {
                        {1, new []{2}},
                    },
                    new[] {(2, 1)},
                    true,
                    false
                };

                yield return new object[]
                {
                    new Dictionary<int, int[]>
                    {
                        {1, new []{2, 3}},
                        {2, new []{4}},
                        {3, new []{4}},
                        {4, new []{5}},
                        {5, null}
                    },
                    new[] {(3, 2)},
                    false,
                    false
                };

                yield return new object[]
                {
                    new Dictionary<int, int[]>
                    {
                        {1, new []{2, 3}},
                        {2, new []{4}},
                        {3, new []{4}},
                        {4, new []{5}},
                        {5, null}
                    },
                    new[] {(1, 2), (1, 3), (3, 2), (1, 5)},
                    false,
                    true
                };

                yield return new object[]
                {
                    new Dictionary<int, int[]>
                    {
                        {1, new []{2, 3}},
                        {2, new []{4}},
                        {3, new []{4}},
                        {4, new []{5}},
                        {5, null}
                    },
                    new[] {(3, 2), (5, 3)},
                    true,
                    false
                };

                yield return new object[]
                {
                    new Dictionary<int, int[]>
                    {
                        {1, new []{2, 3}},
                        {2, new []{4}},
                        {3, new []{4}},
                        {4, new []{5}},
                        {5, null}
                    },
                    new[] {(5, 3)},
                    true,
                    false
                };

                yield return new object[]
                {
                    new Dictionary<int, int[]>
                    {
                        {1, new[] {2}},
                        {2, new[] {3}},
                        {3, new[] {4}},
                    },
                    new[] {(1,3), (2, 4)},
                    false,
                    false
                };

                yield return new object[]
                {
                    new Dictionary<int, int[]>
                    {
                        {1, new[] {2}},
                        {2, new[] {3}},
                        {3, new[] {4}},
                    },
                    new[] {(1,3), (2, 4), (1, 2), (2, 3), (3, 4)},
                    false,
                    true
                };

                yield return new object[]
                {
                    new Dictionary<int, int[]>
                    {
                        {1, new[] {2}},
                        {3, null},
                        {4, null}
                    },
                    new[] {(3, 2), (2, 4)},
                    false,
                    false
                };

                yield return new object[]
                {
                    new Dictionary<int, int[]>
                    {
                        {1, new[] {2}},
                        {3, null},
                        {4, null}
                    },
                    new[] {(3, 2), (2, 4), (4, 1)},
                    true,
                    false
                };
            }
        }

        [Theory]
        [MemberData(nameof(SolutionOnlyDependenciesData))]
        public void SolutionsCanInjectEdgesIntoTheProjectGraph(Dictionary<int, int[]> edges, (int, int)[] solutionDependencies, bool hasCycle, bool solutionEdgesOverlapGraphEdges)
        {
            // Use the same global properties as the solution would use so all ConfigurationMetadata objects would match on global properties.
            var graph = CreateProjectGraph(
                _env,
                edges,
                new Dictionary<string, string>()
                {
                    {"Configuration", "Debug"},
                    {"Platform", "AnyCPU"}
                });

            // Use ConfigurationMetadata because it is IEquatable, whereas ProjectGraphNode is not.
            var graphEdges = graph.TestOnly_Edges.TestOnly_AsConfigurationMetadata();

            var solutionContents =
                SolutionFileBuilder.FromGraph(
                    graph,
                    solutionDependencies: solutionDependencies.Select(dependency => (dependency.Item1.ToString(), dependency.Item2.ToString())).ToArray())
                    .BuildSolution();
            var solutionFile = _env.CreateFile("solution.sln", solutionContents).Path;

            Exception exception = null;

            ProjectGraph graphFromSolution = null;

            try
            {
                graphFromSolution = new ProjectGraph(solutionFile);
            }
            catch (Exception e)
            {
                exception = e;
            }

            if (hasCycle)
            {
                exception.ShouldNotBeNull();
                exception.Message.ShouldContain("MSB4251");

                return;
            }

            exception.ShouldBeNull();

            var graphFromSolutionEdges = graphFromSolution.TestOnly_Edges.TestOnly_AsConfigurationMetadata();

            // Original edges get preserved.
            foreach (var graphEdge in graphEdges)
            {
                graphFromSolutionEdges.Keys.ShouldContain(graphEdge.Key);
            }

            // Solution edges get added. Assert each solution dependency is found within the graph edges
            var solutionOnlyEdges = graphFromSolutionEdges.Keys.Except(graphEdges.Keys).ToList();

            foreach (var solutionDependency in solutionDependencies)
            {
                if (!solutionEdgesOverlapGraphEdges)
                {
                    solutionOnlyEdges.ShouldContain(edge => EdgeCompliesWithSolutionDependency(edge, solutionDependency));
                }

                graphFromSolutionEdges.Keys.ShouldContain(edge => EdgeCompliesWithSolutionDependency(edge, solutionDependency));

                solutionOnlyEdges.RemoveAll(edge => EdgeCompliesWithSolutionDependency(edge, solutionDependency));
            }

            // no extra edges get added
            solutionOnlyEdges.ShouldBeEmpty();
        }

        [Fact]
        public void SolutionEdgesShouldNotOverwriteProjectReferenceEdges()
        {
            var solutionContents = SolutionFileBuilder.FromGraphEdges(
                _env,
                new Dictionary<int, int[]>()
                {
                    {1, new[] {2}}
                }).BuildSolution();

            var graph = new ProjectGraph(_env.CreateFile("solution.sln", solutionContents).Path);

            var edges = graph.TestOnly_Edges.TestOnly_AsConfigurationMetadata();

            edges.Count.ShouldBe(1);

            edges.First().Value.ItemType.ShouldBe(ItemTypeNames.ProjectReference);
        }

        [Fact]
        public void SolutionEdgesShouldNotOverwriteMultitargetingEdges()
        {
            var solutionContents = new SolutionFileBuilder
            {
                Projects = new Dictionary<string, string>
                {
                    {"1", GraphTestingUtilities.CreateProjectFile(_env, 1, new[] {2}).Path},
                    {"2", GraphTestingUtilities.CreateProjectFile(_env, 2, extraContent: MultitargetingSpecificationPropertyGroup).Path},
                    {"3", GraphTestingUtilities.CreateProjectFile(_env, 3, new[] {4}, extraContent: MultitargetingSpecificationPropertyGroup).Path},
                    {"4", GraphTestingUtilities.CreateProjectFile(_env, 4).Path}
                },
                SolutionDependencies = new[] {("1", "2"), ("3", "4")}
            }.BuildSolution();

            var graph = new ProjectGraph(_env.CreateFile("solution.sln", solutionContents).Path);

            var edges = graph.TestOnly_Edges.TestOnly_AsConfigurationMetadata();
            edges.Count.ShouldBe(8);

            var node1 = GetFirstNodeWithProjectNumber(graph, 1);
            node1.ProjectReferences.Count.ShouldBe(3);
            node1.ProjectReferences.Count(r => GetProjectNumber(r) == 2).ShouldBe(3);
            GetOutgoingEdgeItemsFromNode(node1, edges).ShouldAllBe(edgeItem => !IsSolutionItemReference(edgeItem));

            var outerBuild3 = GetOuterBuild(graph, 3);
            outerBuild3.ProjectReferences.Count.ShouldBe(3);
            outerBuild3.ProjectReferences.Count(r => GetProjectNumber(r) == 3).ShouldBe(2);
            outerBuild3.ProjectReferences.Count(r => GetProjectNumber(r) == 4).ShouldBe(1);

            GetInnerBuilds(graph, 3).SelectMany(n => n.ProjectReferences).Count(r => GetProjectNumber(r) == 4).ShouldBe(2);
            GetInnerBuilds(graph, 3).SelectMany(n => GetIncomingEdgeItemsToNode(n, edges)).ShouldAllBe(edgeItem => !IsSolutionItemReference(edgeItem));
            GetInnerBuilds(graph, 3).SelectMany(n => GetOutgoingEdgeItemsFromNode(n, edges)).ShouldAllBe(edgeItem => !IsSolutionItemReference(edgeItem));

            IEnumerable<ProjectItemInstance> GetOutgoingEdgeItemsFromNode(ProjectGraphNode node, IReadOnlyDictionary<(ConfigurationMetadata, ConfigurationMetadata), ProjectItemInstance> edgeInfos)
            {
                return edgeInfos.Where(e => e.Key.Item1.Equals(node.ToConfigurationMetadata())).Select(e => e.Value);
            }

            IEnumerable<ProjectItemInstance> GetIncomingEdgeItemsToNode(ProjectGraphNode node, IReadOnlyDictionary<(ConfigurationMetadata, ConfigurationMetadata), ProjectItemInstance> edgeInfos)
            {
                return edgeInfos.Where(e => e.Key.Item2.Equals(node.ToConfigurationMetadata())).Select(e => e.Value);
            }
        }

        [Fact]
        public void GraphConstructionShouldThrowOnMissingSolutionDependencies()
        {
            var solutionContents = SolutionFileBuilder.FromGraphEdges(
                _env,
                new Dictionary<int, int[]> {{1, null}, {2, null}},
                new[] {("1", new[] {Guid.NewGuid().ToString("B")})}).BuildSolution();

            var solutionFile = _env.CreateFile(
                "solution.sln",
                solutionContents)
                .Path;

            var exception = Should.Throw<InvalidProjectFileException>(
                () =>
                {
                    new ProjectGraph(solutionFile);
                });

            exception.Message.ShouldContain("but a project with this GUID was not found in the .SLN file");
        }

        private static bool IsSolutionItemReference(ProjectItemInstance edgeItem)
        {
            return edgeItem.ItemType == GraphBuilder.SolutionItemReference;
        }

        private static bool EdgeCompliesWithSolutionDependency((ConfigurationMetadata, ConfigurationMetadata) edge, (int, int) solutionDependency)
        {
            return GetProjectNumber(edge.Item1) == solutionDependency.Item1 && GetProjectNumber(edge.Item2) == solutionDependency.Item2;
        }

        private void AssertSolutionBasedGraph(
            Dictionary<int, int[]> edges,
            SolutionConfigurationInSolution currentSolutionConfiguration,
            IReadOnlyCollection<SolutionConfigurationInSolution> solutionConfigurations,
            Dictionary<string, Dictionary<SolutionConfigurationInSolution, ProjectConfigurationInSolution>> projectConfigurations = null)
        {
            var graph = CreateProjectGraph(_env, edges);
            var graphEdges = graph.TestOnly_Edges.TestOnly_AsConfigurationMetadata();

            var solutionFileBuilder = SolutionFileBuilder.FromGraph(graph, projectConfigurations);

            var solutionContents = solutionFileBuilder.BuildSolution();

            var solutionPath = _env.CreateFile("TheSolution.sln", solutionContents).Path;
            var globalProperties = currentSolutionConfiguration != null
                ? new Dictionary<string, string>
                {
                    ["Configuration"] = currentSolutionConfiguration.ConfigurationName,
                    ["Platform"] = currentSolutionConfiguration.PlatformName
                }
                : new Dictionary<string, string>();

            var graphFromSolution = new ProjectGraph(
                new ProjectGraphEntryPoint(
                    solutionPath,
                    globalProperties),
                _env.CreateProjectCollection().Collection);

            // in the solution, all nodes are entry points
            graphFromSolution.EntryPointNodes.Select(GetProjectPath)
                .ShouldBeSetEquivalentTo(graph.ProjectNodes.Select(GetProjectPath));

            if (projectConfigurations == null || graphFromSolution.ProjectNodes.All(n => n.ProjectReferences.Count == 0))
            {
                graphFromSolution.GraphRoots.Select(GetProjectPath)
                    .ShouldBeSameIgnoringOrder(graph.GraphRoots.Select(GetProjectPath));

                graphFromSolution.ProjectNodes.Select(GetProjectPath)
                    .ShouldBeSameIgnoringOrder(graph.ProjectNodes.Select(GetProjectPath));
            }

            var expectedCurrentConfiguration = currentSolutionConfiguration ?? solutionConfigurations.First();
            var actualProjectConfigurations = projectConfigurations ?? solutionFileBuilder.ProjectConfigurations;

            foreach (var node in graphFromSolution.ProjectNodes)
            {
                // Project references get duplicated, once as entry points from the solution (handled in the if block) and once as nodes
                // produced by ProjectReference items (handled in the else block).
                if (node.ReferencingProjects.Count == 0)
                {
                    var expectedProjectConfiguration = actualProjectConfigurations[GetProjectNumber(node).ToString()][expectedCurrentConfiguration];
                    GetConfiguration(node).ShouldBe(expectedProjectConfiguration.ConfigurationName);
                    GetPlatform(node).ShouldBe(expectedProjectConfiguration.PlatformName);
                }
                else
                {
                    GetConfiguration(node).ShouldBe(GetConfiguration(node.ReferencingProjects.First()));
                    GetPlatform(node).ShouldBe(GetPlatform(node.ReferencingProjects.First()));
                }
            }
        }

        private static string GetConfiguration(ProjectGraphNode node)
        {
            return node.ProjectInstance.GlobalProperties["Configuration"];
        }

        private static string GetPlatform(ProjectGraphNode node)
        {
            return node.ProjectInstance.GlobalProperties["Platform"];
        }

        public void Dispose()
        {
            _env.Dispose();
        }
    }
}
