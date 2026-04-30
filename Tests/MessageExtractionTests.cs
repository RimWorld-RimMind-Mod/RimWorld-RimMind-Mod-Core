using System.Collections.Generic;
using Newtonsoft.Json;
using Xunit;

namespace RimMind.Core.Tests
{
    public class MessageExtractionTests
    {
        private static string ExtractMessageFromJson(string content)
        {
            if (string.IsNullOrEmpty(content) || !content.TrimStart().StartsWith("{")) return content;
            try
            {
                var obj = JsonConvert.DeserializeObject<Dictionary<string, object>>(content);
                if (obj != null && obj.TryGetValue("reply", out var msg) && msg != null)
                {
                    string? extracted = msg.ToString();
                    if (!string.IsNullOrEmpty(extracted)) return extracted;
                }
            }
            catch { }
            return content;
        }

        [Fact]
        public void DialogueResponseJson_ExtractsReply()
        {
            var json = "{\"reply\":\"早啊，有什么事吗？\",\"thought\":{\"tag\":\"NONE\",\"description\":\"平淡问候\"}}";
            var result = ExtractMessageFromJson(json);
            Assert.Equal("早啊，有什么事吗？", result);
        }

        [Fact]
        public void MonologueResponseJson_ExtractsReply()
        {
            var json = "{\"reply\":\"这矿洞真冷……不知道还要挖多久。\",\"thought\":{\"tag\":\"STRESSED\",\"description\":\"感到疲惫\"}}";
            var result = ExtractMessageFromJson(json);
            Assert.Equal("这矿洞真冷……不知道还要挖多久。", result);
        }

        [Fact]
        public void PlainText_PassedThrough()
        {
            var text = "你好，有什么需要帮忙的吗？";
            var result = ExtractMessageFromJson(text);
            Assert.Equal(text, result);
        }

        [Fact]
        public void NullOrEmpty_PassedThrough()
        {
            Assert.Null(ExtractMessageFromJson(null!));
            Assert.Equal("", ExtractMessageFromJson(""));
        }

        [Fact]
        public void InvalidJson_PassedThrough()
        {
            var text = "{not valid json";
            Assert.Equal(text, ExtractMessageFromJson(text));
        }

        [Fact]
        public void JsonWithoutReplyKey_PassedThrough()
        {
            var json = "{\"error\":\"something went wrong\"}";
            var result = ExtractMessageFromJson(json);
            Assert.Equal(json, result);
        }

        [Fact]
        public void JsonWithEmptyReply_ReturnsOriginal()
        {
            var json = "{\"reply\":\"\"}";
            var result = ExtractMessageFromJson(json);
            Assert.Equal(json, result);
        }
    }
}
