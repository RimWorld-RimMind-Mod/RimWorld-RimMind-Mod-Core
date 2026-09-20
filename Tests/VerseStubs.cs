using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    /// <summary>Stub for Unity Texture2D used in test compilation.</summary>
    public class Texture2D { }

    public enum EventType { Repaint, Layout, MouseDown, MouseUp, KeyDown, ScrollWheel }
    public class Event
    {
        public static Event? current;
        public EventType type;
    }
    public static class Time
    {
        public static int frameCount;
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height)
        {
            this.x = x;
            this.y = y;
            this.width = width;
            this.height = height;
        }

        public float xMin => x;
        public float yMin => y;
        public float xMax => x + width;
        public float yMax => y + height;

        public bool Contains(Vector2 point)
            => point.x >= xMin && point.x < xMax && point.y >= yMin && point.y < yMax;
    }

    public static class Mathf
    {
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Clamp(float value, float min, float max) => Math.Min(Math.Max(value, min), max);
    }

    /// <summary>Stub for Unity Color struct used in UI theme tests.</summary>
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new(1f, 1f, 1f, 1f);
        public static Color gray => new(0.5f, 0.5f, 0.5f, 1f);
        public static Color red => new(1f, 0f, 0f, 1f);
        public static bool operator ==(Color lhs, Color rhs) =>
            System.Math.Abs(lhs.r - rhs.r) < 1e-6f && System.Math.Abs(lhs.g - rhs.g) < 1e-6f &&
            System.Math.Abs(lhs.b - rhs.b) < 1e-6f && System.Math.Abs(lhs.a - rhs.a) < 1e-6f;
        public static bool operator !=(Color lhs, Color rhs) => !(lhs == rhs);
        public override bool Equals(object? obj) => obj is Color c && this == c;
        public override int GetHashCode() => HashCode.Combine(r, g, b, a);
    }

    /// <summary>Stub for UnityEngine.GUI used in UI overlay tests.</summary>
    public static class GUI
    {
        public static Color color { get; set; } = Color.white;
        public static bool enabled { get; set; } = true;
        public static void BeginGroup(Rect rect) { }
        public static void EndGroup() { }
    }
}

namespace RimWorld
{
    public static class Messages
    {
        public static void Message(string text, MessageTypeDef def, bool historical = false) { }
    }

    public class MessageTypeDef { }
    public static class MessageTypeDefOf
    {
        public static MessageTypeDef RejectInput = new();
        public static MessageTypeDef PositiveEvent = new();
    }

    public class LetterDef { }
    public static class LetterDefOf
    {
        public static LetterDef NeutralEvent = new();
    }

    /// <summary>Stub for RimWorld.ITab used in UI base class tests.</summary>
    public abstract class ITab
    {
        public UnityEngine.Vector2 size = new UnityEngine.Vector2(100f, 100f);
        protected abstract void FillTab();
    }

    /// <summary>Stub for RimWorld.MainTabWindow used in UI base class tests.</summary>
    public class MainTabWindow : Verse.Window { }
}

namespace Verse
{
    public class Game
    {
        public T? GetComponent<T>() where T : class
            => Activator.CreateInstance(typeof(T)) as T;
    }

    public static class Current
    {
        public static Game? Game { get; set; } = new Game();
    }

    public class GameComponent
    {
        public GameComponent() { }
        public GameComponent(Game game) { }
        public virtual void GameComponentTick() { }
        public virtual void StartedNewGame() { }
        public virtual void LoadedGame() { }
        public virtual void ExposeData() { }
    }

    /// <summary>Stub for RimWorld Verse.Pawn used in test compilation.</summary>
    public class Pawn : ThingWithComps
    {
        public int thingIDNumber;
        public bool Dead;
        public Map? Map { get; set; }
        public Pawn_Name Name => new Pawn_Name();
        public string LabelShort => "TestPawn";
        public string LabelShortCap => LabelShort;
        public string ThingID => "Pawn_" + thingIDNumber;
        public object jobs = new();

        public T? GetComp<T>() where T : ThingComp
        {
            return _comps.OfType<T>().FirstOrDefault();
        }

        public void AddComp(ThingComp comp)
        {
            comp.parent = this;
            _comps.Add(comp);
        }

        private readonly List<ThingComp> _comps = new();
    }

    public class Pawn_Name
    {
        public string ToStringShort => "TestPawn";
    }

    /// <summary>Stub for Verse.Map used by context orchestration tests.</summary>
    public class Map
    {
        public int uniqueID;
        public MapPawns mapPawns = new();
    }

    public class MapPawns
    {
        public List<Pawn> AllPawnsSpawned = new();
    }

    /// <summary>Stub for Verse.ThingWithComps base class.</summary>
    public class ThingWithComps { }

    public enum DestroyMode
    {
        Vanish,
    }

    /// <summary>Stub for Verse.ThingComp base class.</summary>
    public class ThingComp
    {
        public ThingWithComps parent = null!;

        public virtual void PostSpawnSetup(bool respawningAfterLoad) { }
        public virtual void PostDestroy(DestroyMode mode, Map previousMap) { }
        public virtual void CompTick() { }
        public virtual void PostExposeData() { }
        public virtual IEnumerable<Gizmo> CompGetGizmosExtra() { yield break; }
    }

    /// <summary>Stub for Verse.CompProperties base class.</summary>
    public class CompProperties
    {
        public Type compClass = null!;
    }

    /// <summary>Stub for Verse.Gizmo base class.</summary>
    public class Gizmo { }

    /// <summary>Stub for Verse.Command_Action used in Gizmo tests.</summary>
    public class Command_Action : Gizmo
    {
        public string defaultLabel = "";
        public string defaultDesc = "";
        public UnityEngine.Texture2D? icon;
        public Action? action;
    }

    /// <summary>Stub for Verse.ContentFinder used in Gizmo tests.</summary>
    public static class ContentFinder<T>
    {
        public static T? Get(string path, bool reportFailure = true) => default;
    }

    /// <summary>Stub for Verse.Prefs used in Gizmo tests.</summary>
    public static class Prefs
    {
        public static bool DevMode = false;
    }

    /// <summary>Stub for Verse.Log used in Gizmo tests.</summary>
    public static class Log
    {
        public static void Message(string msg) { }
        public static void Warning(string msg) { }
        public static void Error(string msg) { }
    }

    /// <summary>Stub for Verse.BaseContent used in CompPawnAgent compilation.</summary>
    public static class BaseContent
    {
        public static UnityEngine.Texture2D BadTex = new();
    }

    /// <summary>Stub for Verse.FloatMenuOption used in Gizmo tests.</summary>
    public class FloatMenuOption
    {
        public string Label;
        public Action Action;
        public bool Disabled;

        public FloatMenuOption(string label, Action action) { Label = label; Action = action; }
    }

    /// <summary>Stub for Verse.FloatMenu used in Gizmo tests.</summary>
    public class FloatMenu : Window
    {
        public List<FloatMenuOption> Options;
        public FloatMenu(List<FloatMenuOption> options, string title = "") { Options = options; }
    }

    public static class LongEventHandler
    {
        public static void ExecuteWhenFinished(Action action) => action?.Invoke();
    }

    public class LetterStack
    {
        public void ReceiveLetter(string label, string text, RimWorld.LetterDef textLetterDef) { }
    }

    /// <summary>Stub for Verse.Find used in Gizmo tests.</summary>
    public static class Find
    {
        public static TickManager TickManager = new();
        public static WindowStack WindowStack = new();
        public static Map? CurrentMap;
        public static Selector Selector = new();
        public static LetterStack? LetterStack = new();
    }

    public class Selector
    {
        public void Select(Pawn pawn, bool playSound, bool forceDesignatorDeselect) { }
    }

    /// <summary>Stub for Verse.TickManager used in Gizmo tests.</summary>
    public class TickManager
    {
        public int TicksGame { get; set; }
    }

    /// <summary>Stub for Verse.WindowStack used in Gizmo tests.</summary>
    public class WindowStack
    {
        public void Add(Window window) { }
    }

    /// <summary>Stub for Verse.Window used in Gizmo/UI tests.</summary>
    public class Window
    {
        public bool IsOpen { get; set; } = true;
        public virtual UnityEngine.Vector2 InitialSize => new UnityEngine.Vector2(400f, 300f);
        public virtual void DoWindowContents(UnityEngine.Rect inRect) { }
    }

    /// <summary>Stub for Verse.GameFont used in UI base class tests.</summary>
    public enum GameFont { Tiny, Small, Medium, MediumBig }

    /// <summary>Stub for Verse.TextAnchor used in UI base class tests.</summary>
    public enum TextAnchor
    {
        UpperLeft, UpperCenter, UpperRight,
        MiddleLeft, MiddleCenter, MiddleRight,
        LowerLeft, LowerCenter, LowerRight
    }

    /// <summary>Stub for Verse.Text used in UI base class tests.</summary>
    public static class Text
    {
        public static GameFont Font { get; set; } = GameFont.Small;
        public static TextAnchor Anchor { get; set; } = TextAnchor.UpperLeft;
        public static UnityEngine.Vector2 CalcSize(string text) => new(text.Length * 6f, 22f);
        public static float CalcHeight(string text, float width)
            => Math.Max(1f, (float)Math.Ceiling(text.Length * 6f / Math.Max(1f, width))) * 22f;
    }

    public sealed record WidgetDraw(string Kind, UnityEngine.Rect Rect, string Label, UnityEngine.Color Color, bool Enabled);

    /// <summary>Stub for Verse.Widgets used in UI overlay tests.</summary>
    public static class Widgets
    {
        public static readonly List<WidgetDraw> Draws = new();
        public static string? ClickLabel;
        private static readonly Stack<UnityEngine.Vector2> ScrollOffsets = new();

        public static void ResetDrawing()
        {
            Draws.Clear();
            ScrollOffsets.Clear();
            ClickLabel = null;
            TooltipHandler.Tips.Clear();
        }

        private static void Record(string kind, UnityEngine.Rect rect, string label = "")
        {
            foreach (var offset in ScrollOffsets)
            {
                rect.x += offset.x;
                rect.y += offset.y;
            }
            Draws.Add(new WidgetDraw(kind, rect, label, UnityEngine.GUI.color, UnityEngine.GUI.enabled));
        }

        public static void DrawBox(UnityEngine.Rect rect, int borderSize = 1) { }
        public static void DrawBoxSolid(UnityEngine.Rect rect, UnityEngine.Color color) { }
        public static void DrawLine(UnityEngine.Vector2 start, UnityEngine.Vector2 end, UnityEngine.Color color, float width) { }
        public static void Label(UnityEngine.Rect rect, string label) => Record("Label", rect, label);
        public static void LabelEllipses(UnityEngine.Rect rect, string label) => Record("LabelEllipses", rect, label);
        public static void DrawHighlight(UnityEngine.Rect rect) { }
        public static bool ButtonText(UnityEngine.Rect rect, string label)
        {
            Record("ButtonText", rect, label);
            return UnityEngine.GUI.enabled && ClickLabel == label;
        }
        public static bool ButtonInvisible(UnityEngine.Rect rect) => false;
        public static void BeginScrollView(UnityEngine.Rect viewport, ref UnityEngine.Vector2 scroll, UnityEngine.Rect content)
            => ScrollOffsets.Push(new UnityEngine.Vector2(viewport.x - content.x - scroll.x, viewport.y - content.y - scroll.y));
        public static void EndScrollView() => ScrollOffsets.Pop();
    }

    public static class Mouse
    {
        public static bool IsOver(UnityEngine.Rect rect) => false;
    }

    public static class TooltipHandler
    {
        public static readonly List<(UnityEngine.Rect Rect, string Text)> Tips = new();
        public static void TipRegion(UnityEngine.Rect rect, string tip) => Tips.Add((rect, tip));
    }

    public static class UiExtensions
    {
        public static UnityEngine.Rect ContractedBy(this UnityEngine.Rect rect, float amount)
            => new(rect.x + amount, rect.y + amount, rect.width - amount * 2f, rect.height - amount * 2f);
        public static bool NullOrEmpty([System.Diagnostics.CodeAnalysis.NotNullWhen(false)] this string? text)
            => string.IsNullOrEmpty(text);
    }

    /// <summary>Stub for Verse.Translate extension method.</summary>
    public static class TranslateStub
    {
        public static string Translate(this string key) => key;
        public static string Translate(this string key, string arg1) => $"{key}:{arg1}";
        public static string Translate(this string key, object arg1) => $"{key}:{arg1}";
    }

    /// <summary>Stub for Verse.IExposable used in test compilation.</summary>
    public interface IExposable { }

    /// <summary>Stub for Verse.LoadSaveMode used in test compilation.</summary>
    public enum LoadSaveMode
    {
        Inactive,
        Saving,
        LoadingVars,
        ResolvingCrossRefs,
        PostLoadInit
    }

    /// <summary>Stub for Verse.LookMode used in test compilation.</summary>
    public enum LookMode
    {
        Reference,
        Value,
        Deep,
        Undef
    }

    /// <summary>Stub for Verse.Scribe used in test compilation.</summary>
    public static class Scribe
    {
        public static LoadSaveMode mode = LoadSaveMode.Inactive;
    }

    /// <summary>Stub for Verse.Scribe_Values used in test compilation.</summary>
    public static class Scribe_Values
    {
        public static string? NextString { get; set; }
        public static string? LastString { get; private set; }

        public static void Look(ref string value, string label, string defaultValue = "")
        {
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                value = NextString ?? defaultValue;
            else if (Scribe.mode == LoadSaveMode.Saving)
                LastString = value;
        }

        public static void Reset()
        {
            NextString = null;
            LastString = null;
        }

        public static void Look<T>(ref T value, string label, T defaultValue = default!) { }
        public static void Look<T>(ref T value, string label, bool saveDestroyedThings) { }
    }

    /// <summary>Stub for Verse.Scribe_Collections used in test compilation.</summary>
    public static class Scribe_Collections
    {
        public static void Look<T>(ref System.Collections.Generic.List<T>? list, string label, LookMode lookMode = LookMode.Undef) where T : new() { }
        public static void Look<T>(ref System.Collections.Generic.List<T>? list, string label, bool saveDestroyedThings) where T : new() { }
        public static void Look<TKey, TValue>(
            ref System.Collections.Generic.Dictionary<TKey, TValue> dictionary,
            string label,
            LookMode keyLookMode,
            LookMode valueLookMode)
            where TKey : notnull
        {
        }
    }
}

namespace RimMind.Infrastructure.Verse
{
    using RimMind.Application.Common.Interfaces.Internal;
    using RimMind.Application.Common.Interfaces.Npc;
    using RimMind.Application.Common.Models.Npc;
    using RimMind.Domain.Llm;

    public sealed class NpcManager : INpcManager
    {
        public void SpawnNpc(NpcProfile profile) { }
        public void KillNpc(string npcId) { }
        public bool IsNpcAlive(string npcId) => false;
        public NpcProfile? GetNpc(string npcId) => null;
        public IReadOnlyList<NpcProfile> GetAllNpcs() => Array.Empty<NpcProfile>();
        public string GetNpcForMap(object map) => string.Empty;
        public object? FindPawnByNpcId(string npcId) => null;
        public object? FindProxyPawnForMap(object map) => null;
        public void RegisterActiveAgent(int thingId) { }
        public void UnregisterActiveAgent(int thingId) { }
        public HashSet<int> GetActiveAgentPawnIds() => new HashSet<int>();
        public void IndexPawn(object pawn) { }
        public void UnindexPawn(int thingId) { }
        public string GetMapNpcId(object map) => string.Empty;
    }

    public sealed class AIDebugLog : IAIDebugLog
    {
        public IReadOnlyList<AIDebugEntry> Entries => Array.Empty<AIDebugEntry>();
        public void Clear() { }
        public void Record(LlmRequestEnvelope envelope, LlmResponse response, int elapsedMs) { }
    }
}

namespace Verse.AI
{
    /// <summary>Stub for Verse.AI.Job used in test compilation.</summary>
    public class Job { }
    public class JobQueue { }
    public class Pawn_JobTracker { public JobQueue jobQueue = new JobQueue(); }
}

namespace RimMind.Presentation.Agent
{
    using RimMind.Application.Common.Interfaces;
    using RimMind.Application.Common.Interfaces.Agent;
    using Verse;

    /// <summary>Stub for IPawnAgentVerse used in test compilation.</summary>
    public interface IPawnAgentVerse : IPawnAgent, IExposable
    {
        Pawn Pawn { get; }
        new Verse.AI.Job? ConsumePendingJob();
        void SetPendingJob(Verse.AI.Job job);
    }

    /// <summary>Stub for IPawnAgentFactoryVerse used in test compilation.</summary>
    public interface IPawnAgentFactoryVerse : IPawnAgentFactory
    {
        IPawnAgent Create(Pawn pawn, IAgentBus agentBus);
    }

    /// <summary>Stub for IPawnActorVerse used in test compilation.</summary>
    public interface IPawnActorVerse : IPawnActor
    {
        Verse.AI.Job? ConsumePendingJob();
        void SetPendingJob(Verse.AI.Job job);
    }
}

namespace RimMind.Presentation.Api
{
    using RimMind.Application.Common.Interfaces.Agent.Modes;
    using RimMind.Application.Common.Interfaces.Extension;

    /// <summary>Stub for RimMindAPI used in test compilation.</summary>
    public static partial class RimMindAPI
    {
        private static IExtensionRegistry<IAgentMode>? _modes;

        public static IExtensionRegistry<IAgentMode>? Modes
        {
            get => _modes;
            set => _modes = value;
        }

        public static class Request
        {
            public static void Send(RimMind.Domain.Llm.LlmRequestEnvelope envelope, System.Action<RimMind.Domain.ValueObjects.Result<RimMind.Domain.Llm.LlmResponse, RimMind.Domain.ValueObjects.RimMindError>> onComplete) { }
            public static System.Threading.Tasks.Task<RimMind.Domain.ValueObjects.Result<RimMind.Domain.Llm.LlmResponse, RimMind.Domain.ValueObjects.RimMindError>> SendAsync(RimMind.Domain.Llm.LlmRequestEnvelope envelope) => null!;
        }
    }
}

namespace RimMind.Infrastructure.UI
{
    public class Window_AgentStateDebug : global::Verse.Window
    {
        public Window_AgentStateDebug() { }
        public Window_AgentStateDebug(global::Verse.Pawn? pawn) { }
    }

    public class Window_RimMindHub : global::Verse.Window
    {
        public Window_RimMindHub() { }
        public static Window_RimMindHub OpenAgentsForPawn(global::Verse.Pawn selectedPawn) => new();
        public static Window_RimMindHub OpenAIRequests() => new();
    }
}

namespace RimMind.Tests.Stubs
{
    internal static class TestTickProvider
    {
        public static int TicksGame => 0;
    }
}
