using System;
using System.Collections.Generic;
using RimMind.Infrastructure.UI.DebugActions;
using RimMind.Testing;
using Xunit;

namespace RimMind.Tests.Contracts
{
    /// <summary>
    /// Harness settings isolation (B6): an in-game runner snapshots the player's settings, mutates
    /// them for the run, and must hand back exactly what the player had — and nothing it overrode
    /// (API key, endpoint, fake showcase credentials) may ever be persisted.
    /// </summary>
    public class SettingsOverrideScopeContracts
    {
        private class FrameworkSettingsBase
        {
            public string frameworkOwned = "framework";
        }

        private sealed class BackupEntry
        {
            public string apiKey = "";
            public bool enabled = true;
        }

        private sealed class NestedContext
        {
            public int budget = 100;
            public Dictionary<string, int> weights = new Dictionary<string, int> { ["pawn"] = 1 };
        }

        private sealed class PlayerSettings : FrameworkSettingsBase
        {
            public string apiKey = "player-key";
            public string apiEndpoint = "https://player.example/v1";
            public int maxTokens = 800;
            public float requestOverlayX = 155f;
            public DayOfWeek mode = DayOfWeek.Monday;
            public List<BackupEntry> backupEndpoints = new List<BackupEntry> { new BackupEntry { apiKey = "player-backup" } };
            public NestedContext Context = new NestedContext();
        }

        private static void MutateLikeHarness(PlayerSettings s)
        {
            s.apiKey = "sk-env-harness-key";
            s.apiEndpoint = "https://harness.example/v1";
            s.maxTokens = 600;
            s.requestOverlayX = 20f;
            s.mode = DayOfWeek.Friday;
            s.backupEndpoints[0].apiKey = "sk-env-backup";
            s.backupEndpoints[0].enabled = false;
            s.backupEndpoints.Insert(0, new BackupEntry { apiKey = "sk-injected" });
            s.Context.budget = 1;
            s.Context.weights["pawn"] = 9;
            s.Context.weights["harness"] = 5;
        }

        private static void AssertPlayerValues(PlayerSettings s, List<BackupEntry> originalList, BackupEntry originalEntry, NestedContext originalContext)
        {
            Assert.Equal("player-key", s.apiKey);
            Assert.Equal("https://player.example/v1", s.apiEndpoint);
            Assert.Equal(800, s.maxTokens);
            Assert.Equal(155f, s.requestOverlayX);
            Assert.Equal(DayOfWeek.Monday, s.mode);
            Assert.Same(originalList, s.backupEndpoints);
            Assert.Single(s.backupEndpoints);
            Assert.Same(originalEntry, s.backupEndpoints[0]);
            Assert.Equal("player-backup", originalEntry.apiKey);
            Assert.True(originalEntry.enabled);
            Assert.Same(originalContext, s.Context);
            Assert.Equal(100, s.Context.budget);
            Assert.Equal(new Dictionary<string, int> { ["pawn"] = 1 }, s.Context.weights);
        }

        [Fact]
        public void Scope_restores_player_settings_in_place_and_never_persists_harness_values()
        {
            ContractCaseRunner.Run(
                ("dispose restores scalars, lists, list items, nested objects and dictionaries without replacing references", () =>
                {
                    var s = new PlayerSettings();
                    var list = s.backupEndpoints;
                    var entry = list[0];
                    var context = s.Context;

                    var scope = SettingsOverrideScope.Begin(s, "test", typeof(FrameworkSettingsBase));
                    MutateLikeHarness(s);
                    scope.Dispose();

                    AssertPlayerValues(s, list, entry, context);
                    Assert.Empty(scope.RestoreErrors);
                    Assert.Null(SettingsOverrideScope.Active);
                }),
                ("fields declared on the stop type (framework base) are outside the snapshot", () =>
                {
                    var s = new PlayerSettings();
                    using (SettingsOverrideScope.Begin(s, "test", typeof(FrameworkSettingsBase)))
                    {
                        s.frameworkOwned = "changed-by-framework";
                    }
                    Assert.Equal("changed-by-framework", s.frameworkOwned);
                }),
                ("abort path: an exception inside the run still restores, and dispose is idempotent", () =>
                {
                    var s = new PlayerSettings();
                    SettingsOverrideScope? captured = null;
                    Action crash = () =>
                    {
                        using var scope = SettingsOverrideScope.Begin(s, "test");
                        captured = scope;
                        s.apiKey = "sk-env-harness-key";
                        throw new InvalidOperationException("harness crashed");
                    };
                    Assert.Throws<InvalidOperationException>(crash);
                    Assert.Equal("player-key", s.apiKey);

                    s.apiKey = "player-edited-after-run";
                    captured!.Dispose();
                    Assert.Equal("player-edited-after-run", s.apiKey);
                }),
                ("persist during an override writes the player's original values, then re-applies the override", () =>
                {
                    var s = new PlayerSettings();
                    var list = s.backupEndpoints;
                    var entry = list[0];
                    var context = s.Context;
                    string? persistedKey = null;
                    string? persistedBackupKey = null;
                    int persistedBackupCount = -1;
                    bool nestedRedirected = true;

                    using (SettingsOverrideScope.Begin(s, "test"))
                    {
                        MutateLikeHarness(s);
                        bool redirected = SettingsOverrideScope.TryRedirectActivePersist(s, () =>
                        {
                            persistedKey = s.apiKey;
                            persistedBackupKey = s.backupEndpoints[0].apiKey;
                            persistedBackupCount = s.backupEndpoints.Count;
                            // The real write re-enters the guard; it must fall through to the actual persist.
                            nestedRedirected = SettingsOverrideScope.TryRedirectActivePersist(s, () => { });
                        });

                        Assert.True(redirected);
                        Assert.False(nestedRedirected);
                        Assert.Equal("player-key", persistedKey);
                        Assert.Equal("player-backup", persistedBackupKey);
                        Assert.Equal(1, persistedBackupCount);

                        Assert.Equal("sk-env-harness-key", s.apiKey);
                        Assert.Equal(2, s.backupEndpoints.Count);
                        Assert.Equal("sk-injected", s.backupEndpoints[0].apiKey);
                        Assert.Equal(9, s.Context.weights["pawn"]);
                    }

                    AssertPlayerValues(s, list, entry, context);
                }),
                ("persist of unrelated objects, or with no active scope, is not redirected", () =>
                {
                    var s = new PlayerSettings();
                    var other = new PlayerSettings();
                    bool ran = false;
                    Assert.False(SettingsOverrideScope.TryRedirectActivePersist(s, () => ran = true));

                    using (SettingsOverrideScope.Begin(s, "test"))
                    {
                        Assert.False(SettingsOverrideScope.TryRedirectActivePersist(other, () => ran = true));
                    }
                    Assert.False(ran);
                }),
                ("nested scopes restore to their own snapshot while persistence always uses the outermost", () =>
                {
                    var s = new PlayerSettings();
                    string? persistedKey = null;

                    using (var outer = SettingsOverrideScope.Begin(s, "outer"))
                    {
                        s.apiKey = "sk-outer";
                        using (var inner = SettingsOverrideScope.Begin(s, "inner"))
                        {
                            Assert.Same(inner, SettingsOverrideScope.Active);
                            s.apiKey = "sk-inner";
                            SettingsOverrideScope.TryRedirectActivePersist(s, () => persistedKey = s.apiKey);
                            Assert.Equal("sk-inner", s.apiKey);
                        }
                        Assert.Equal("sk-outer", s.apiKey);
                        Assert.Same(outer, SettingsOverrideScope.Active);
                    }

                    Assert.Equal("player-key", persistedKey);
                    Assert.Equal("player-key", s.apiKey);
                    Assert.Null(SettingsOverrideScope.Active);
                }),
                ("external values (global prefs) are captured at registration and restored on dispose", () =>
                {
                    bool devMode = false;
                    var s = new PlayerSettings();
                    using (var scope = SettingsOverrideScope.Begin(s, "test"))
                    {
                        scope.TrackExternal(() => devMode, v => devMode = v);
                        devMode = true;
                    }
                    Assert.False(devMode);
                }));
        }
    }
}
