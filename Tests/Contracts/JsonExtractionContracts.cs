using System.Collections.Generic;
using RimMind.Application.Features.Json;
using RimMind.Testing;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class JsonExtractionContracts
    {
        private sealed class SampleActionPayload
        {
            public string? Action { get; set; }
            public string? Target { get; set; }
        }

        [Fact]
        public void JsonTagExtractor_handles_markdown_and_malformed_json_and_fallback()
        {
            ContractCaseRunner.Run(
                ("extracts json wrapped in markdown code blocks inside tag", () =>
                {
                    string input = @"Here is my decision:
<Action>
```json
{
  ""Action"": ""Harvest"",
  ""Target"": ""RiceCrop""
}
```
</Action>";
                    var result = JsonTagExtractor.Extract<SampleActionPayload>(input, "Action");
                    Assert.NotNull(result);
                    Assert.Equal("Harvest", result!.Action);
                    Assert.Equal("RiceCrop", result.Target);
                }),
                ("repairs trailing commas in extracted json", () =>
                {
                    string input = @"<Action>
{
  ""Action"": ""Construct"",
  ""Target"": ""Wall"",
}
</Action>";
                    var result = JsonTagExtractor.Extract<SampleActionPayload>(input, "Action");
                    Assert.NotNull(result);
                    Assert.Equal("Construct", result!.Action);
                    Assert.Equal("Wall", result.Target);
                }),
                ("falls back to bare json when tags are missing", () =>
                {
                    string input = @"```json
{
  ""Action"": ""Haul"",
  ""Target"": ""Wood""
}
```";
                    var result = JsonTagExtractor.Extract<SampleActionPayload>(input, "Action");
                    Assert.NotNull(result);
                    Assert.Equal("Haul", result!.Action);
                    Assert.Equal("Wood", result.Target);
                }));
        }
    }
}
