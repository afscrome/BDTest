﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using BDTest.Output;
using BDTest.Paths;
using BDTest.ReportGenerator.Models;
using BDTest.Test;
using HtmlTags;
using Newtonsoft.Json;

namespace BDTest.ReportGenerator.Builders
{

    public class HtmlReportBuilder
    {
        private readonly TestTimer _testTimer;
        private readonly List<Scenario> _scenarios;
        private readonly List<string> _stories;

        private int ScenariosCount => _scenarios.Count;
        private int StoriesCount => _stories.Count;
        private int PassedCount => _scenarios.Count(it => it.Status == Status.Passed);
        private int FailedCount => _scenarios.Count(it => it.Status == Status.Failed);
        private int InconclusiveCount => _scenarios.Count(it => it.Status == Status.Inconclusive);
        private int NotImplementedCount => _scenarios.Count(it => it.Status == Status.NotImplemented);

        private const string StatusTag = "status";
        private const string TimeTag = "time";

        private int _storiesBuiltCounter;
        private readonly WarningsChecker _warnings;

        internal static HtmlReportBuilder CreateReport(DataOutputModel dataOutputModel)
        {
            return new HtmlReportBuilder(dataOutputModel);
        }

        internal HtmlReportBuilder(DataOutputModel dataOutputModel)
        {
            _scenarios = dataOutputModel.Scenarios;
            _stories = _scenarios.Select(scenario => scenario.GetStoryText()).Distinct().ToList();
            _testTimer = dataOutputModel.TestTimer;
            _warnings = dataOutputModel.Warnings;
            CreateFlakinessReport();
            CreateTestTimesComparisonReport();
            CreateReportWithoutStories();
            CreateReportWithStories();
        }

        private static void CreateFlakinessReport()
        {
            if (string.IsNullOrWhiteSpace(BDTestSettings.PersistentResultsDirectory))
            {
                return;
            }

            using (var stringWriter = new StringWriter())
            {
                new HtmlTag("html")
                    .Append(BuildHead())
                    .Append(BuildFlakinessBody())
                    .Style("padding", "25px")
                    .WriteTo(stringWriter, HtmlEncoder.Default);

                File.WriteAllText(Path.Combine(BDTestReportGenerator.ResultDirectory, BDTestSettings.FlakinessReportHtmlFilename ?? FileNames.ReportFlakiness),
                    stringWriter.ToString());
            }
        }

        private static void CreateTestTimesComparisonReport()
        {
            if (string.IsNullOrWhiteSpace(BDTestSettings.PersistentResultsDirectory))
            {
                return;
            }

            using (var stringWriter = new StringWriter())
            {
                new HtmlTag("html")
                    .Append(BuildHead())
                    .Append(BuildTestTimeComparisonBody())
                    .Style("padding", "25px")
                    .WriteTo(stringWriter, HtmlEncoder.Default);

                File.WriteAllText(Path.Combine(BDTestReportGenerator.ResultDirectory, BDTestSettings.TestTimesReportHtmlFilename ?? FileNames.ReportTestTimesComparison),
                    stringWriter.ToString());
            }
        }

        private void CreateReportWithStories()
        {
            using (var stringWriter = new StringWriter())
            {
                new HtmlTag("html")
                    .Append(BuildHead())
                    .Append(BuildBodyWithStories())
                    .Style("padding", "25px")
                    .WriteTo(stringWriter, HtmlEncoder.Default);

                File.WriteAllText(Path.Combine(BDTestReportGenerator.ResultDirectory, BDTestSettings.ScenariosByStoryReportHtmlFilename ?? FileNames.ReportByStory),
                    stringWriter.ToString());
            }
        }

        private void CreateReportWithoutStories()
        {
            using (var stringWriter = new StringWriter())
            {
                new HtmlTag("html")
                    .Append(BuildHead())
                    .Append(BuildBodyWithoutStories())
                    .Style("padding", "25px")
                    .WriteTo(stringWriter, HtmlEncoder.Default);

                File.WriteAllText(Path.Combine(BDTestReportGenerator.ResultDirectory, BDTestSettings.AllScenariosReportHtmlFilename ?? FileNames.ReportAllScenarios),
                    stringWriter.ToString());
            }
        }

        private static HtmlTag BuildFlakinessBody()
        {
            var flakyScenarioBatched = GetScenarioBatched();
            var flakyScenarios = FlattenBatchScenarios(flakyScenarioBatched);
            var flakyScenariosGroupedByStory =
                flakyScenarios.GroupBy(scenario => new {Story = scenario.GetStoryText(), scenario.FileName});

            return new HtmlTag("body").Append(
                new HtmlTag("div").Append(
                    new HtmlTag("h3").AppendText("Flakiness"),
                    new HtmlTag("div").Append(
                        flakyScenariosGroupedByStory.SelectMany(flakyScenariosWithSameStory =>
                            new[]
                            {
                                new HtmlTag("div").AddClass("box").Append(
                                    new HtmlTag("h4")
                                        .AppendText(
                                            $"Story: {flakyScenariosWithSameStory.FirstOrDefault()?.GetStoryText()}"),
                                    new HtmlTag("table").Append(
                                        new HtmlTag("thead").Append(
                                            new HtmlTag("tr").Append(
                                                HtmlReportPrebuilt.ScenariosHeader,
                                                new HtmlTag("th").AppendText("Flakiness")
                                            ).Append(HtmlReportPrebuilt.StatusIconHeaders)
                                        ),
                                        new HtmlTag("tbody").Append(
                                            flakyScenarios.Where(scenario =>
                                                    scenario.GetStoryText() == flakyScenariosWithSameStory.Key.Story &&
                                                    scenario.FileName == flakyScenariosWithSameStory.Key.FileName)
                                                .GroupBy(scenario => scenario.GetScenarioText())
                                                .Select(flakySameScenarios =>
                                                {
                                                    var flakyGroupedByDistinctScenarioText =
                                                        flakySameScenarios.ToList();
                                                    return new HtmlTag("tr").Append(
                                                        new HtmlTag("td").AppendText(flakyGroupedByDistinctScenarioText
                                                            .FirstOrDefault()
                                                            ?.GetScenarioText()),
                                                        new HtmlTag("td").Append(
                                                            new HtmlTag("div").AppendText(
                                                                $"{flakyGroupedByDistinctScenarioText.GetFlakinessPercentage()}%")
                                                        ),
                                                        new HtmlTag("td").AppendText(
                                                            $"{flakyGroupedByDistinctScenarioText.GetCount(Status.Passed)} / {flakyGroupedByDistinctScenarioText.Count}"
                                                        ),
                                                        new HtmlTag("td").AppendText(
                                                            $"{flakyGroupedByDistinctScenarioText.GetCount(Status.Failed)} / {flakyGroupedByDistinctScenarioText.Count}"
                                                        ),
                                                        new HtmlTag("td").AppendText(
                                                            $"{flakyGroupedByDistinctScenarioText.GetCount(Status.Inconclusive)} / {flakyGroupedByDistinctScenarioText.Count}"
                                                        ),
                                                        new HtmlTag("td").AppendText(
                                                            $"{flakyGroupedByDistinctScenarioText.GetCount(Status.NotImplemented)} / {flakyGroupedByDistinctScenarioText.Count}"
                                                        )
                                                    );
                                                })
                                        )
                                    )
                                ),
                                new BrTag()
                            }
                        )
                    )
                )
            );
        }

        private static IEnumerable<List<Scenario>> GetScenarioBatched()
        {
            var scenarioBatched = Directory.GetFiles(BDTestSettings.PersistentResultsDirectory).Where(it =>
                    it.EndsWith(".json") && File.GetCreationTime(it) > BDTestSettings.PersistentResultsCompareStartTime)
                .Select(filePath =>
                    JsonConvert.DeserializeObject<DataOutputModel>(File.ReadAllText(filePath)).Scenarios)
                .ToList();
            return scenarioBatched;
        }

        private static HtmlTag BuildTestTimeComparisonBody()
        {
            var testTimesScenarioBatched = GetScenarioBatched();
            var testTimesScenarios = FlattenBatchScenarios(testTimesScenarioBatched).Where(scenario => scenario.Status == Status.Passed).ToList();
            var testTimesScenariosGroupedByStory = testTimesScenarios.GroupBy(scenario => new { Story = scenario.GetStoryText(), scenario.FileName });

            return new HtmlTag("body").Append(
                new HtmlTag("div").Append(
                    new HtmlTag("h3").AppendText("Test Times (Passed Only)"),
                    new HtmlTag("div").Append(
                        testTimesScenariosGroupedByStory.Select(testTimesScenariosWithSameStory =>
                            new HtmlTag("div").AddClass("box").Append(
                                new HtmlTag("h4")
                                    .AppendText($"Story: {testTimesScenariosWithSameStory.FirstOrDefault()?.GetStoryText()}"),
                                new HtmlTag("table").Append(
                                    new HtmlTag("thead").Append(
                                        new HtmlTag("tr").Append(
                                            HtmlReportPrebuilt.ScenariosHeader,
                                            new HtmlTag("th").AppendText("Min"),
                                            new HtmlTag("th").AppendText("Avg"),
                                            new HtmlTag("th").AppendText("Max")
                                        )
                                    ),
                                    new HtmlTag("tbody").Append(
                                        testTimesScenarios.Where(scenario =>
                                                scenario.GetStoryText() == testTimesScenariosWithSameStory.Key.Story && scenario.FileName == testTimesScenariosWithSameStory.Key.FileName)
                                            .GroupBy(scenario => scenario.GetScenarioText())
                                            .Select(testTimesSameScenarios =>
                                            {
                                                var testTimesGroupedByDistinctScenarioText =
                                                    testTimesSameScenarios.ToList();
                                                return new HtmlTag("tr").Append(
                                                    new HtmlTag("td").AppendText(testTimesGroupedByDistinctScenarioText
                                                        .FirstOrDefault()
                                                        ?.GetScenarioText()),
                                                    new HtmlTag("td").AppendText(testTimesGroupedByDistinctScenarioText
                                                        .Min(scenario => scenario.TimeTaken).ToPrettyFormat()),
                                                    new HtmlTag("td").AppendText(
                                                        new TimeSpan(Convert.ToInt64(
                                                            testTimesGroupedByDistinctScenarioText.Average(scenario =>
                                                                scenario.TimeTaken.Ticks))).ToPrettyFormat()),
                                                    new HtmlTag("td").AppendText(testTimesGroupedByDistinctScenarioText
                                                        .Max(scenario => scenario.TimeTaken).ToPrettyFormat())
                                                );
                                            })
                                    )
                                )
                            )
                        )
                    )
                )
            );
        }

        private static List<Scenario> FlattenBatchScenarios(IEnumerable<List<Scenario>> scenarioBatched)
        {
            return scenarioBatched.SelectMany(it => it)
                .WithCurrentVersion()
                .ToList();
        }

        private HtmlTag BuildBodyWithStories()
        {
            return new HtmlTag("body").Append(
                BuildHeaderBoxes(),
                BuildWarnings(),
                new HtmlTag("p").Append(
                    BuildStorySection()
                ),
                new HtmlTag("p").Append(
                    BuildFooter()
                ),
                new HtmlTag("p").Append(
                    BuildJavascript(StoriesCount)
                )
            );
        }

        private HtmlTag BuildHeaderBoxes()
        {
            return new HtmlTag("div").AddClass("box").Append(
                new HtmlTag("h3").AppendText("Summary"),
                BuildSummaryBox(),
                BuildTimerBox(),
                BuildChart()
            );
        }

        private HtmlTag BuildBodyWithoutStories()
        {
            return new HtmlTag("body").Append(
                BuildHeaderBoxes(),
                BuildWarnings(),
                new HtmlTag("p").Append(
                    BuildScenariosSection(_scenarios)
                ),
                new HtmlTag("p").Append(
                    BuildFooter()
                ),
                new HtmlTag("p").Append(
                    BuildJavascript(0)
                )
            );
        }

        private HtmlTag BuildWarnings()
        {
            var warningsNonExecutedTests = _warnings.NonExecutedTests.ToList();
            if (!warningsNonExecutedTests.Any())
            {
                return HtmlTag.Empty();
            }

            return new HtmlTag("details").Append(
                new HtmlTag("summary").AddClass("canToggle").AppendText("Tests Not Executed"),
                new HtmlTag("p").Append(
                    new HtmlTag("table").Append(
                        new HtmlTag("thead").Append(
                            new HtmlTag("tr").Append(
                                HtmlReportPrebuilt.StoryHeader,
                                HtmlReportPrebuilt.ScenarioHeader,
                                new HtmlTag("th").AppendText("Parameters")
                            )
                        ),
                        new HtmlTag("tbody").Append(
                            warningsNonExecutedTests.Select(it =>
                                new HtmlTag("tr").Append(
                                    new HtmlTag("td").AppendText(it.GetStoryText()),
                                    new HtmlTag("td").AppendText(it.GetScenarioText()),
                                    new HtmlTag("td").Append(it.TestDetails.Parameters?.Select(parameterName => new HtmlTag("div").AppendText(parameterName)) ?? new List<HtmlTag> { HtmlTag.Empty() })
                                )
                            )
                        )
                    )
                )
            );
        }

        private HtmlTag BuildTimerBox()
        {
            if (_testTimer == null)
            {
                return new HtmlTag("br");
            }

            return new HtmlTag("table").Append(
                new HtmlTag("thead").Append(
                    new HtmlTag("tr").Append(
                        new HtmlTag("th").AppendText("Started At"),
                        new HtmlTag("th").AppendText("Finished At"),
                        new HtmlTag("th").AppendText("Time Taken")
                    )
                ),
                new HtmlTag("tbody").Append(
                    new HtmlTag("tr").Append(
                        new HtmlTag("td").AppendText(_testTimer.TestsStartedAt.ToStringForReport()),
                        new HtmlTag("td").AppendText(_testTimer.TestsFinishedAt.ToStringForReport()),
                        new HtmlTag("td").AppendText(_testTimer.ElapsedTime.ToPrettyFormat())
                    )
                )
            ).Attr("width", "1000");
        }

        private HtmlTag BuildSummaryBox()
        {
            return new HtmlTag("table").Append(
                new HtmlTag("thead").Append(
                    new HtmlTag("tr").Append(
                        HtmlReportPrebuilt.StoriesHeader,
                        HtmlReportPrebuilt.ScenariosHeader,
                        new HtmlTag("th").Append(
                            new HtmlTag("div").Append(
                                HtmlReportPrebuilt.PassedIcon
                            ),
                            new HtmlTag("input").Attr("type", "checkbox").Attr("checked", "checked").Id("Passed")
                        ),
                        new HtmlTag("th").Append(
                            new HtmlTag("div").Append(
                                HtmlReportPrebuilt.FailedIcon
                            ),
                            new HtmlTag("input").Attr("type", "checkbox").Attr("checked", "checked").Id("Failed")
                        ),
                        new HtmlTag("th").Append(
                            new HtmlTag("div").Append(
                                HtmlReportPrebuilt.InconclusiveIcon
                            ),
                            new HtmlTag("input").Attr("type", "checkbox").Attr("checked", "checked").Id("Inconclusive")
                        ),
                        new HtmlTag("th").Append(
                            new HtmlTag("div").Append(
                                HtmlReportPrebuilt.NotImplementedIcon
                            ),
                            new HtmlTag("input").Attr("type", "checkbox").Attr("checked", "checked").Id("NotImplemented")
                        )
                    )
                ),
                new HtmlTag("tbody").Append(
                    new HtmlTag("tr").Append(
                        new HtmlTag("td").AppendText(StoriesCount.ToString()),
                        new HtmlTag("td").AppendText(ScenariosCount.ToString()),
                        new HtmlTag("td").AppendText(PassedCount.ToString()),
                        new HtmlTag("td").AppendText(FailedCount.ToString()),
                        new HtmlTag("td").AppendText(InconclusiveCount.ToString()),
                        new HtmlTag("td").AppendText(NotImplementedCount.ToString())
                    )
                )
            ).Attr("width", "1000");
        }

        private HtmlTag BuildStorySection()
        {
            return new HtmlTag("ul").Append(
                    _stories.Select(story => _scenarios.Where(scenario => scenario.GetStoryText() == story)).Select(BuildStory)
                );
        }

        private HtmlTag BuildStory(IEnumerable<Scenario> enumerableScenarios)
        {
            _storiesBuiltCounter++;
            var scenarios = enumerableScenarios.ToList();
            var storyText = scenarios.FirstOrDefault()?.GetStoryText();

            return
                new HtmlTag("div").Append(
                    new HtmlTag("div").AddClass("Story").AddClass(HtmlReportPrebuilt.GetStatus(scenarios)).Append(
                        new HtmlTag("h4").AddClass("StoryText").AppendHtml(
                            $"Story: {storyText}".UsingHtmlNewLines()
                        ),
                        new HtmlTag("table").Append(
                            new HtmlTag("thead").Append(
                                new HtmlTag("tr").Append(
                                    HtmlReportPrebuilt.StatusHeader,
                                    HtmlReportPrebuilt.ScenariosHeader,
                                    new HtmlTag("th").Append(
                                        new HtmlTag("div").Append(
                                            HtmlReportPrebuilt.PassedIcon
                                        ),

                                        new HtmlTag("input").Attr("type", "checkbox").Attr("checked", "checked").Id($"Passed{_storiesBuiltCounter}")
                                    ),
                                    new HtmlTag("th").Append(
                                        new HtmlTag("div").Append(
                                            HtmlReportPrebuilt.FailedIcon
                                        ),
                                        new HtmlTag("input").Attr("type", "checkbox").Attr("checked", "checked").Id($"Failed{_storiesBuiltCounter}")
                                    ),
                                    new HtmlTag("th").Append(
                                        new HtmlTag("div").Append(
                                            HtmlReportPrebuilt.InconclusiveIcon
                                        ),
                                        new HtmlTag("input").Attr("type", "checkbox").Attr("checked", "checked").Id($"Inconclusive{_storiesBuiltCounter}")
                                    ),
                                    new HtmlTag("th").Append(
                                        new HtmlTag("div").Append(
                                            HtmlReportPrebuilt.NotImplementedIcon
                                        ),
                                        new HtmlTag("input").Attr("type", "checkbox").Attr("checked", "checked").Id($"NotImplemented{_storiesBuiltCounter}")
                                    ),
                                    HtmlReportPrebuilt.DurationHeader,
                                    HtmlReportPrebuilt.StartHeader,
                                    HtmlReportPrebuilt.EndHeader
                                )
                            ),
                            new HtmlTag("tbody").Append(
                                new HtmlTag("tr").Append(
                                    new HtmlTag("td").Append(
                                        HtmlReportPrebuilt.GetStatusIcon(scenarios)
                                    ),
                                    new HtmlTag("td").AppendText(
                                        scenarios.Count().ToString()
                                    ),
                                    new HtmlTag("td").AppendText(
                                        scenarios.Count(scenario => scenario.Status == Status.Passed).ToString()
                                    ),
                                    new HtmlTag("td").AppendText(
                                        scenarios.Count(scenario => scenario.Status == Status.Failed).ToString()
                                    ),
                                    new HtmlTag("td").AppendText(
                                        scenarios.Count(scenario => scenario.Status == Status.Inconclusive).ToString()
                                    ),
                                    new HtmlTag("td").AppendText(
                                        scenarios.Count(scenario => scenario.Status == Status.NotImplemented).ToString()
                                    ),
                                    new HtmlTag("td").AppendText(
                                        (scenarios.GetEndDateTime() - scenarios.GetStartDateTime())
                                            .ToPrettyFormat()
                                    ),
                                    new HtmlTag("td").AppendText(
                                        scenarios.GetStartDateTime()
                                            .ToStringForReport()
                                    ),
                                    new HtmlTag("td").AppendText(
                                        scenarios.GetEndDateTime()
                                            .ToStringForReport()
                                    )
                                )
                            )
                        ),
                        new HtmlTag("details").Append(
                            new HtmlTag("summary").AddClass("canToggle").AppendText("Scenarios"),
                            new HtmlTag("p").Append(
                                new HtmlTag("h4").Append(
                                    BuildScenariosSection(scenarios)
                                )
                            )
                        ),
                        BuildChart()
                    )
                );
        }

        private HtmlTag BuildChart()
        {
            return new HtmlTag("details").Append(
                new HtmlTag("summary").AddClass("canToggle").AppendText("Charts"),
                new HtmlTag("div").Id($"piechart{StatusTag + _storiesBuiltCounter}").Style("display", "inline-block"),
                new HtmlTag("div").Id($"piechart{TimeTag + _storiesBuiltCounter}").Style("display", "inline-block")
            );
        }

        private HtmlTag BuildScenariosSection(IEnumerable<Scenario> scenarios)
        {
            return new HtmlTag("li").Append(
                new HtmlTag("table").Append(
                    new HtmlTag("thead").Append(
                        new HtmlTag("tr").Append(
                            HtmlReportPrebuilt.ScenarioHeader,
                            HtmlReportPrebuilt.StatusHeader,
                            HtmlReportPrebuilt.DurationHeader,
                            HtmlReportPrebuilt.StartHeader,
                            HtmlReportPrebuilt.EndHeader
                        )
                    ),
                    new HtmlTag("tbody").Append(
                        scenarios.Select(BuildScenario)
                    )
                )
            );
        }

        private HtmlTag BuildScenario(Scenario scenario)
        {
            var scenarioText = scenario.GetScenarioText();

            return new HtmlTag("tr").AddClass(HtmlReportPrebuilt.GetStatus(scenario) + _storiesBuiltCounter).AddClass(HtmlReportPrebuilt.GetStatus(scenario)).Append(
                new HtmlTag("td").Append(
                    new HtmlTag("details").Append(
                        new HtmlTag("summary").Append(
                            new HtmlTag("span").AddClass("ScenarioText").AppendText(
                                scenarioText
                                )
                        ),
                        new HtmlTag("p").Style("margin-left", "25px").Append(
                            BuildSteps(scenario.Steps),
                            BuildScenarioTeardownOutput(scenario)
                        )
                    )
                ),
                new HtmlTag("td").Append(
                    HtmlReportPrebuilt.GetStatusIcon(scenario)
                ),
                new HtmlTag("td").AppendText(
                    scenario.TimeTaken.ToPrettyFormat()
                ),
                new HtmlTag("td").AppendText(
                    scenario.StartTime.ToStringForReport()
                ),
                new HtmlTag("td").AppendText(
                        scenario.EndTime.ToStringForReport()
                )
            );
        }

        private HtmlTag BuildScenarioTeardownOutput(Scenario scenario)
        {
            if (string.IsNullOrEmpty(scenario.TearDownOutput))
            {
                return HtmlTag.Empty();
            }

            var scenarioTearDownOutputLines = scenario.TearDownOutput.SplitOnNewLines();
            
            return new HtmlTag("details").Style("margin-left", "25px").Append(
                new HtmlTag("summary").Append(
                    new HtmlTag("span").AppendText("Test Tear Down Output").AddClass("step")
                ),
                new HtmlTag("pre").Append(scenarioTearDownOutputLines.SelectMany(
                    line => new[] {new HtmlTag("span").AppendText(line), new BrTag()}))
            );
        }

        private static IEnumerable<HtmlTag> AddStylesheets()
        {
            return new[]
            {
                new HtmlTag("link").Attr("rel", "stylesheet").Attr("href", "https://fonts.googleapis.com/css?family=Roboto:300,300italic,700,700italic").Attr("media", "print").Attr("onload", "this.media='all'"),
                new HtmlTag("link").Attr("rel", "stylesheet").Attr("href", "http://cdn.rawgit.com/necolas/normalize.css/master/normalize.css").Attr("media", "print").Attr("onload", "this.media='all'"),
                new HtmlTag("link").Attr("rel", "stylesheet").Attr("href", "./css/milligram/dist/milligram.min.css").Attr("media", "print").Attr("onload", "this.media='all'"),
                new HtmlTag("link").Attr("rel", "stylesheet").Attr("href", "./css/milligram/dist/milligram.css").Attr("media", "print").Attr("onload", "this.media='all'"),
                new HtmlTag("link").Attr("rel", "stylesheet").Attr("href", "./css/testy.css").Attr("media", "print").Attr("onload", "this.media='all'")
            };
        }

        private static HtmlTag BuildSteps(List<Step> steps)
        {
            return new HtmlTag("table").Append(
                new HtmlTag("thead").Append(
                    new HtmlTag("tr").Append(
                        HtmlReportPrebuilt.StepHeader.AddClass("step"),
                        HtmlReportPrebuilt.StatusHeader.AddClass("step"),
                        HtmlReportPrebuilt.DurationHeader.AddClass("step"),
                        HtmlReportPrebuilt.StartHeader.AddClass("step"),
                        HtmlReportPrebuilt.EndHeader.AddClass("step")
                    )
                ),
                new HtmlTag("tbody").Append(
                    steps.Select(BuildStep)
                )
            );
        }

        private static HtmlTag BuildStep(Step step)
        {
            var expandedStepInfo = BuildStepExpandedInfo(step);
            HtmlTag stepEntry;
            if (expandedStepInfo == null)
            {
                stepEntry = new HtmlTag("span").AddClass("step").AppendText(step.StepText);
            }
            else
            {
                stepEntry = new HtmlTag("details").Append(
                            new HtmlTag("summary").AddClass("step").AppendText(
                                step.StepText
                            ),
                            expandedStepInfo
                        );
            }

            return new HtmlTag("tr").Append(
                new HtmlTag("td").Append(
                    stepEntry
                ),
                new HtmlTag("td").Append(
                    HtmlReportPrebuilt.GetStatusIcon(step)
                ),
                new HtmlTag("td").AddClass("step").AppendText(
                    step.TimeTaken.ToPrettyFormat()
                ),
                new HtmlTag("td").AddClass("step").AppendText(
                    step.StartTime.ToStringForReport()
                ),
                new HtmlTag("td").AddClass("step").AppendText(
                    step.EndTime.ToStringForReport()
                )
            ).Style("margin-left", "25px");
        }

        private static HtmlTag BuildStepExpandedInfo(Step step)
        {
            var exceptionTag = BuildExceptionTag(step);
            var outputTag = BuildOutputTag(step);

            if (exceptionTag == null && outputTag == null)
            {
                return null;
            }

            var returnTag = new HtmlTag("p").Append(
                exceptionTag ?? HtmlTag.Empty(),
                outputTag ?? HtmlTag.Empty()
            );

            return returnTag;
        }

        private static HtmlTag BuildOutputTag(Step step)
        {
            if (string.IsNullOrWhiteSpace(step.Output))
            {
                return null;
            }

            var outputLines = step.Output.SplitOnNewLines();

            return new HtmlTag("details").Append(
                new HtmlTag("summary").Style("margin-left", "25px").AddClass("step").AppendText("Output"),
                new HtmlTag("pre").AddClass("output")
                    .Append(outputLines.SelectMany(
                        line => new[] {new HtmlTag("span").AppendText(line), new BrTag()}))
            );
        }

        private static HtmlTag BuildExceptionTag(Step step)
        {
            if (step.Exception == null)
            {
                return null;
            }

            return new HtmlTag("details").Append(
                new HtmlTag("summary").Style("margin-left", "25px").AddClass("step").AppendText("Exception"),
                new HtmlTag("pre").AddClass("exception").AppendText(step.Exception?.ToString() ?? "")
            );
        }

        private static HtmlTag BuildHead()
        {
            return new HtmlTag("head")
                .Append(
                    new HtmlTag("h1").AppendText("BDTest"),
                    new HtmlTag("h2").AppendText("A Testing Framework")
                )
                .Append(AddStylesheets());
        }

        private static HtmlTag BuildFooter()
        {
            return new HtmlTag("div").AddClass("footer").AppendText("Powered by ")
                    .AppendHtml("<a href=\"https://github.com/thomhurst/BDTest\">BDTest</a>");
        }

        private IEnumerable<HtmlTag> BuildJavascript(int storiesCount)
        {
            var list = new List<HtmlTag>
            {
                new HtmlTag("script").Attr("type","text/javascript").Attr("src", "./css/jquery-3.3.1.min.js"),
                new HtmlTag("script").Attr("type","text/javascript").Attr("src", "./css/checkbox_toggle_js.js"),
                new HtmlTag("script").Attr("type", "text/javascript").AppendHtml(JavascriptStringHelpers.LoadJavascriptChartsAsync(BuildChartJavascript(storiesCount)))
            };

            return list.ToArray();
        }

        private string BuildChartJavascript(int storiesCount)
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.Append(
                "google.charts.load('current', {'packages':['corechart']});\r\n      google.charts.setOnLoadCallback(drawChart);\r\n\r\n      function drawChart() {\r\n\r\n        "
                );

             for (var i = 0; i <= storiesCount; i++)
            {
                stringBuilder.Append("var data" + StatusTag + i + " = google.visualization.arrayToDataTable([\r\n          ['Scenarios', 'Amount'],\r\n          " + BuildChartScenarioStatusData(i) + "\r\n        ]);\r\n\r\n        var options" + StatusTag + i + " = {\r\n          title: 'Test Status', width: 700, height: 400, pieSliceText: 'none', slices: {\r\n            0: { color: '#34A853' },\r\n            1: { color: '#EA4335' },\r\n 2: { color: '#FBBc05' },\r\n 3: { color: '#4285F4' }          }\r\n        };\r\n\r\n        var chart" + StatusTag + i + " = new google.visualization.PieChart(document.getElementById('piechart" + StatusTag + i + "'));\r\n\r\n        chart" + StatusTag + i + ".draw(data" + StatusTag + i + ", options" + StatusTag + i + ");\r\n      ");
            }

            for (var i = 0; i <= storiesCount; i++)
            {
                stringBuilder.Append("var data" + TimeTag + i + " = google.visualization.arrayToDataTable([\r\n          ['Scenarios', 'Amount'],\r\n          " + BuildChartScenarioTimesData(i) + "\r\n        ]);\r\n\r\n        var options" + TimeTag + i + " = {\r\n          title: 'Test Times', width: 700, height: 400, pieSliceText: 'none'};\r\n\r\n        var chart" + TimeTag + i + " = new google.visualization.PieChart(document.getElementById('piechart" + TimeTag + i + "'));\r\n\r\n        chart" + TimeTag + i + ".draw(data" + TimeTag + i + ", options" + TimeTag + i + ");\r\n      ");
            }
            
            stringBuilder.Append("}");

            return stringBuilder.ToString();
        }



        private string BuildChartScenarioStatusData(int i)
        {
            var scenarios = i == 0 ? _scenarios : _scenarios.Where(scenario => scenario.GetStoryText() == _stories[i - 1]).ToList();

            var passed = scenarios.Count(scenario => scenario.Status == Status.Passed);
            var failed = scenarios.Count(scenario => scenario.Status == Status.Failed);
            var inconclusive = scenarios.Count(scenario => scenario.Status == Status.Inconclusive);
            var notImplemented = scenarios.Count(scenario => scenario.Status == Status.NotImplemented);

            return $"['Passed', {passed}], ['Failed', {failed}], ['Inconclusive', {inconclusive}], ['Not Implemented', {notImplemented}]";
        }

        private string BuildChartScenarioTimesData(int i)
        {
            var scenarios = i == 0 ? _scenarios : _scenarios.Where(scenario => scenario.GetStoryText() == _stories[i - 1]).ToList();

            var stringBuilder = new List<string>();

            foreach (var scenario in scenarios)
            {
                stringBuilder.Add($"['{scenario.GetScenarioText().EscapeQuotes()}', {scenario.TimeTaken.Ticks}]");
            }

            return string.Join(",", stringBuilder);
        }
    }
}