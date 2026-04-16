using System;
using RimMind.Core.Internal;
using Xunit;

// 测试纯逻辑层，不依赖 RimWorld
namespace RimMind.Core.Tests
{
    public class JsonTagExtractorTests
    {
        // ──────────────────────────────────────────────
        // 1. 基本提取
        // ──────────────────────────────────────────────

        [Fact]
        public void Extract_ValidTag_ReturnsDeserializedObject()
        {
            string input = "AI思考...<Incident>{\"defName\":\"RaidEnemy\",\"reason\":\"局势紧张\"}</Incident>";
            var result = JsonTagExtractor.Extract<IncidentStub>(input, "Incident");

            Assert.NotNull(result);
            Assert.Equal("RaidEnemy", result!.defName);
            Assert.Equal("局势紧张", result.reason);
        }

        [Fact]
        public void Extract_TagWithWhitespace_TrimsAndReturnsObject()
        {
            string input = "<Incident>\n  {\"defName\":\"ToxicFallout\",\"reason\":\"test\"}\n</Incident>";
            var result = JsonTagExtractor.Extract<IncidentStub>(input, "Incident");

            Assert.NotNull(result);
            Assert.Equal("ToxicFallout", result!.defName);
        }

        [Fact]
        public void Extract_TagAtEndOfLongText_ReturnsObject()
        {
            string input = "这是一段很长的叙述文本，AI 在里面描述了很多事情。\n" +
                           "殖民者们经历了很多困难……\n" +
                           "<Incident>{\"defName\":\"Eclipse\",\"reason\":\"dramatic\"}</Incident>";
            var result = JsonTagExtractor.Extract<IncidentStub>(input, "Incident");

            Assert.NotNull(result);
            Assert.Equal("Eclipse", result!.defName);
        }

        // ──────────────────────────────────────────────
        // 2. 缺失 / 格式错误 → null
        // ──────────────────────────────────────────────

        [Fact]
        public void Extract_MissingTag_ReturnsNull()
        {
            var result = JsonTagExtractor.Extract<IncidentStub>("无标签内容", "Incident");
            Assert.Null(result);
        }

        [Fact]
        public void Extract_MalformedJson_ReturnsNull()
        {
            string input = "<Incident>{not valid json}</Incident>";
            var result = JsonTagExtractor.Extract<IncidentStub>(input, "Incident");
            Assert.Null(result);
        }

        [Fact]
        public void Extract_EmptyTagContent_ReturnsNull()
        {
            string input = "<Incident></Incident>";
            var result = JsonTagExtractor.Extract<IncidentStub>(input, "Incident");
            Assert.Null(result);
        }

        [Fact]
        public void Extract_WrongTagName_ReturnsNull()
        {
            string input = "<Personality>{\"defName\":\"X\"}</Personality>";
            var result = JsonTagExtractor.Extract<IncidentStub>(input, "Incident");
            Assert.Null(result);
        }

        // ──────────────────────────────────────────────
        // 3. 多 Tag 时取第一个
        // ──────────────────────────────────────────────

        [Fact]
        public void Extract_MultipleMatchingTags_ReturnsFirst()
        {
            string input = "<Incident>{\"defName\":\"First\",\"reason\":\"a\"}</Incident>" +
                           "<Incident>{\"defName\":\"Second\",\"reason\":\"b\"}</Incident>";
            var result = JsonTagExtractor.Extract<IncidentStub>(input, "Incident");

            Assert.NotNull(result);
            Assert.Equal("First", result!.defName);
        }

        // ──────────────────────────────────────────────
        // 4. ExtractRaw — 只返回 JSON 字符串
        // ──────────────────────────────────────────────

        [Fact]
        public void ExtractRaw_ValidTag_ReturnsJsonString()
        {
            string input = "<Incident>{\"defName\":\"RaidEnemy\"}</Incident>";
            string? raw = JsonTagExtractor.ExtractRaw(input, "Incident");

            Assert.NotNull(raw);
            Assert.Contains("RaidEnemy", raw);
        }

        [Fact]
        public void ExtractRaw_MissingTag_ReturnsNull()
        {
            string? raw = JsonTagExtractor.ExtractRaw("nothing here", "Incident");
            Assert.Null(raw);
        }

        // ──────────────────────────────────────────────
        // 5. Personality 结构（嵌套数组）
        // ──────────────────────────────────────────────

        [Fact]
        public void Extract_NestedArray_DeserializesCorrectly()
        {
            string input = "<Personality>{\"thoughts\":[{\"type\":\"state\",\"label\":\"疲惫\",\"intensity\":-1}],\"narrative\":\"Alice很累\"}</Personality>";
            var result = JsonTagExtractor.Extract<PersonalityStub>(input, "Personality");

            Assert.NotNull(result);
            Assert.Equal("Alice很累", result!.narrative);
            Assert.Single(result.thoughts);
            Assert.Equal("疲惫", result.thoughts[0].label);
            Assert.Equal(-1, result.thoughts[0].intensity);
        }

        // ──────────────────────────────────────────────
        // 帮助类型（仅供测试）
        // ──────────────────────────────────────────────

        private class IncidentStub
        {
            public string defName { get; set; } = "";
            public string reason { get; set; } = "";
        }

        private class PersonalityStub
        {
            public ThoughtStub[] thoughts { get; set; } = Array.Empty<ThoughtStub>();
            public string narrative { get; set; } = "";
        }

        private class ThoughtStub
        {
            public string type { get; set; } = "";
            public string label { get; set; } = "";
            public int intensity { get; set; }
        }
    }
}
