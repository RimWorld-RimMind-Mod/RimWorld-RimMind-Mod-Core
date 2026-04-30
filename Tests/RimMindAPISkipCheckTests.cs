using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace RimMind.Core.Tests
{
    public class RimMindAPISkipCheckTests
    {
        private static readonly ConcurrentDictionary<string, Func<object, string, bool>> _skipChecks
            = new ConcurrentDictionary<string, Func<object, string, bool>>();

        private static void Register(string sourceId, Func<object, string, bool> check)
            => _skipChecks[sourceId] = check;

        private static void Unregister(string sourceId)
            => _skipChecks.TryRemove(sourceId, out _);

        private static bool ShouldSkip(object target, string triggerType)
        {
            foreach (var check in _skipChecks.Values.ToList())
            {
                try
                {
                    if (check(target, triggerType)) return true;
                }
                catch (Exception)
                {
                }
            }
            return false;
        }

        public RimMindAPISkipCheckTests()
        {
            _skipChecks.Clear();
        }

        [Fact]
        public void Register_ShouldSkipReturnsTrue()
        {
            Register("test_mod", (target, type) => true);
            Assert.True(ShouldSkip(new object(), "Chitchat"));
        }

        [Fact]
        public void Register_AllReturnFalse_ShouldSkipReturnsFalse()
        {
            Register("test_mod", (target, type) => false);
            Assert.False(ShouldSkip(new object(), "Chitchat"));
        }

        [Fact]
        public void NoChecksRegistered_ShouldSkipReturnsFalse()
        {
            Assert.False(ShouldSkip(new object(), "Chitchat"));
        }

        [Fact]
        public void MultipleChecks_FirstReturnsFalseSecondReturnsTrue_ReturnsTrue()
        {
            Register("mod_a", (target, type) => false);
            Register("mod_b", (target, type) => true);
            Assert.True(ShouldSkip(new object(), "Thought"));
        }

        [Fact]
        public void Unregister_RemovesCheck()
        {
            Register("mod_a", (target, type) => true);
            Assert.True(ShouldSkip(new object(), "Chitchat"));
            Unregister("mod_a");
            Assert.False(ShouldSkip(new object(), "Chitchat"));
        }

        [Fact]
        public void Overwrite_SameSourceId_ReplacesPreviousCheck()
        {
            Register("mod_a", (target, type) => false);
            Register("mod_a", (target, type) => true);
            Assert.True(ShouldSkip(new object(), "Chitchat"));
        }

        [Fact]
        public void CheckThrowsException_IsSwallowed_ContinuesToNextCheck()
        {
            Register("mod_a", (target, type) => throw new InvalidOperationException("boom"));
            Register("mod_b", (target, type) => true);
            Assert.True(ShouldSkip(new object(), "Chitchat"));
        }

        [Fact]
        public void AllChecksThrow_ReturnsFalse()
        {
            Register("mod_a", (target, type) => throw new Exception("a"));
            Register("mod_b", (target, type) => throw new Exception("b"));
            Assert.False(ShouldSkip(new object(), "Chitchat"));
        }

        [Fact]
        public void TargetAndTriggerType_PassedCorrectly()
        {
            object? capturedTarget = null;
            string? capturedTrigger = null;

            Register("mod_a", (target, type) =>
            {
                capturedTarget = target;
                capturedTrigger = type;
                return false;
            });

            var obj = new object();
            ShouldSkip(obj, "PlayerInput");

            Assert.Same(obj, capturedTarget);
            Assert.Equal("PlayerInput", capturedTrigger);
        }

        [Fact]
        public void ToListSnapshot_PreventsCollectionModified()
        {
            var results = new List<bool>();
            Register("mod_a", (target, type) => { results.Add(true); return false; });

            ShouldSkip(new object(), "Auto");

            Assert.Single(results);
            Assert.True(results[0]);
        }
    }
}
