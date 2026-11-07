using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace RimMind.Infrastructure.UI.DebugActions
{
    /// <summary>
    /// Run-scoped override of a settings object for in-game harnesses (behavior autotest, playthrough,
    /// showcase capture). <see cref="Begin"/> snapshots every instance field of the target (deeply, in
    /// place: lists, dictionaries and nested settings objects keep their identity), the harness then
    /// mutates the live object freely, and <see cref="Dispose"/> restores the snapshot. While a scope is
    /// active, persistence of the target is redirected through <see cref="TryRedirectActivePersist"/>
    /// so that only the player's pre-run values are ever written to disk — never harness API keys,
    /// endpoints or fake showcase credentials.
    /// Verse-free so the snapshot/restore contract is unit-tested; the RimWorld binding (ModSettings
    /// write guard) lives in <see cref="CoreSettingsOverrideScope"/>.
    /// TODO(P5): lift behind an ISettingsOverrideScope port.
    /// </summary>
    internal sealed class SettingsOverrideScope : IDisposable
    {
        private static readonly object Gate = new object();
        private static SettingsOverrideScope? _active;
        private static bool _redirectingPersist;

        private readonly SettingsOverrideScope? _parent;
        private readonly Type? _stopAt;
        private readonly SettingsFieldSnapshot _original;
        private readonly List<Action> _externalRestores = new List<Action>();
        private bool _disposed;

        private SettingsOverrideScope(object target, string owner, Type? stopAt, SettingsOverrideScope? parent)
        {
            Target = target;
            Owner = owner;
            _stopAt = stopAt;
            _parent = parent;
            _original = SettingsFieldSnapshot.Capture(target, stopAt);
        }

        /// <summary>Innermost live scope, or null when no harness is overriding settings.</summary>
        public static SettingsOverrideScope? Active
        {
            get { lock (Gate) return _active; }
        }

        public object Target { get; }

        public string Owner { get; }

        public bool IsDisposed => _disposed;

        /// <summary>Errors raised while restoring (one entry per field that could not be restored).</summary>
        public IReadOnlyList<string> RestoreErrors { get; private set; } = Array.Empty<string>();

        /// <summary>
        /// Snapshots <paramref name="target"/> and makes the new scope the active one. Fields declared on
        /// <paramref name="stopAt"/> and its base types (e.g. the framework's ModSettings) are left alone.
        /// Nested scopes on the same target are allowed; persistence always uses the outermost snapshot.
        /// </summary>
        public static SettingsOverrideScope Begin(object target, string owner, Type? stopAt = null)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            lock (Gate)
            {
                var parent = _active != null && !_active._disposed ? _active : null;
                var scope = new SettingsOverrideScope(target, owner ?? string.Empty, stopAt, parent);
                _active = scope;
                return scope;
            }
        }

        /// <summary>
        /// Captures a value that lives outside the settings object (e.g. a global preference) and
        /// restores it when the scope ends. Restores run in reverse registration order.
        /// </summary>
        public void TrackExternal<T>(Func<T> getter, Action<T> setter)
        {
            if (getter == null) throw new ArgumentNullException(nameof(getter));
            if (setter == null) throw new ArgumentNullException(nameof(setter));
            if (_disposed) throw new ObjectDisposedException(nameof(SettingsOverrideScope));
            T original = getter();
            _externalRestores.Add(() => setter(original));
        }

        /// <summary>
        /// If a scope is overriding <paramref name="instance"/>, temporarily restores the player's
        /// pre-run values (outermost snapshot), runs <paramref name="persist"/>, then re-applies the
        /// current overridden state, and returns true. Returns false (caller should persist normally)
        /// when no scope targets the instance or when called re-entrantly from inside a redirect.
        /// </summary>
        public static bool TryRedirectActivePersist(object instance, Action persist)
        {
            if (instance == null || persist == null) return false;
            SettingsOverrideScope? root;
            lock (Gate)
            {
                if (_redirectingPersist || _active == null) return false;
                root = _active.FindOutermostFor(instance);
                if (root == null) return false;
                _redirectingPersist = true;
            }

            try
            {
                var current = SettingsFieldSnapshot.Capture(instance, root._stopAt);
                try
                {
                    root._original.RestoreInPlace();
                    persist();
                }
                finally
                {
                    current.RestoreInPlace();
                }
                return true;
            }
            finally
            {
                lock (Gate) _redirectingPersist = false;
            }
        }

        /// <summary>Restores the snapshot and external values. Idempotent; safe on abort paths.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            var errors = new List<string>();
            try
            {
                _original.RestoreInPlace(errors);
            }
            catch (Exception ex)
            {
                errors.Add(ex.GetType().Name + ": " + ex.Message);
            }

            for (int i = _externalRestores.Count - 1; i >= 0; i--)
            {
                try
                {
                    _externalRestores[i]();
                }
                catch (Exception ex)
                {
                    errors.Add("external: " + ex.GetType().Name + ": " + ex.Message);
                }
            }

            RestoreErrors = errors;

            lock (Gate)
            {
                if (ReferenceEquals(_active, this))
                {
                    var next = _parent;
                    while (next != null && next._disposed) next = next._parent;
                    _active = next;
                }
            }
        }

        private SettingsOverrideScope? FindOutermostFor(object instance)
        {
            SettingsOverrideScope? found = null;
            for (var s = this; s != null; s = s._parent)
            {
                if (!s._disposed && ReferenceEquals(s.Target, instance)) found = s;
            }
            return found;
        }
    }

    /// <summary>
    /// Deep, identity-preserving snapshot of an object's instance fields. Restoring writes the captured
    /// values back into the same object graph: lists/dictionaries are cleared and refilled, nested
    /// objects are restored field by field, scalars are reassigned.
    /// </summary>
    internal sealed class SettingsFieldSnapshot
    {
        private const int MaxDepth = 8;
        private const BindingFlags InstanceFields =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private readonly ObjectNode _root;

        private SettingsFieldSnapshot(ObjectNode root)
        {
            _root = root;
        }

        public static SettingsFieldSnapshot Capture(object target, Type? stopAt = null)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            var visited = new Dictionary<object, Node>(ReferenceComparer.Instance);
            return new SettingsFieldSnapshot(CaptureObject(target, stopAt, 0, visited));
        }

        /// <summary>Restores the captured state in place. Returns the number of fields that failed.</summary>
        public int RestoreInPlace(List<string>? errors = null)
        {
            var sink = errors ?? new List<string>();
            int before = sink.Count;
            _root.Restore(new HashSet<Node>(), sink);
            return sink.Count - before;
        }

        private static ObjectNode CaptureObject(object instance, Type? stopAt, int depth, Dictionary<object, Node> visited)
        {
            var node = new ObjectNode(instance);
            visited[instance] = node;
            for (Type? t = instance.GetType(); t != null && t != typeof(object) && t != stopAt; t = t.BaseType)
            {
                foreach (var field in t.GetFields(InstanceFields))
                {
                    if (field.IsLiteral) continue;
                    object? value;
                    try
                    {
                        value = field.GetValue(instance);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    node.Fields.Add(new KeyValuePair<FieldInfo, Node>(field, CaptureValue(value, depth + 1, visited)));
                }
            }
            return node;
        }

        private static Node CaptureValue(object? value, int depth, Dictionary<object, Node> visited)
        {
            if (value == null) return new ValueNode(null);
            var type = value.GetType();
            if (type.IsValueType || value is string || value is Delegate || value is Type || value is MemberInfo || depth >= MaxDepth)
                return new ValueNode(value);
            if (visited.TryGetValue(value, out var seen)) return seen;

            if (value is Array array)
            {
                var arrayNode = new ArrayNode(array);
                visited[value] = arrayNode;
                foreach (var item in array) arrayNode.Items.Add(CaptureValue(item, depth + 1, visited));
                return arrayNode;
            }

            if (value is IDictionary dictionary)
            {
                var dictNode = new DictionaryNode(dictionary);
                visited[value] = dictNode;
                foreach (DictionaryEntry entry in dictionary)
                    dictNode.Entries.Add(new KeyValuePair<object, Node>(entry.Key, CaptureValue(entry.Value, depth + 1, visited)));
                return dictNode;
            }

            if (value is IList list)
            {
                var listNode = new ListNode(list);
                visited[value] = listNode;
                foreach (var item in list) listNode.Items.Add(CaptureValue(item, depth + 1, visited));
                return listNode;
            }

            return CaptureObject(value, null, depth, visited);
        }

        private abstract class Node
        {
            public abstract object? Restore(HashSet<Node> restored, List<string> errors);
        }

        private sealed class ValueNode : Node
        {
            private readonly object? _value;

            public ValueNode(object? value) { _value = value; }

            public override object? Restore(HashSet<Node> restored, List<string> errors) => _value;
        }

        private sealed class ObjectNode : Node
        {
            private readonly object _instance;

            public ObjectNode(object instance) { _instance = instance; }

            public List<KeyValuePair<FieldInfo, Node>> Fields { get; } = new List<KeyValuePair<FieldInfo, Node>>();

            public override object? Restore(HashSet<Node> restored, List<string> errors)
            {
                if (!restored.Add(this)) return _instance;
                foreach (var pair in Fields)
                {
                    var field = pair.Key;
                    try
                    {
                        object? value = pair.Value.Restore(restored, errors);
                        if (!field.FieldType.IsValueType && ReferenceEquals(field.GetValue(_instance), value)) continue;
                        field.SetValue(_instance, value);
                    }
                    catch (Exception ex)
                    {
                        errors.Add(field.DeclaringType?.Name + "." + field.Name + ": " + ex.GetType().Name + ": " + ex.Message);
                    }
                }
                return _instance;
            }
        }

        private sealed class ListNode : Node
        {
            private readonly IList _list;

            public ListNode(IList list) { _list = list; }

            public List<Node> Items { get; } = new List<Node>();

            public override object? Restore(HashSet<Node> restored, List<string> errors)
            {
                if (!restored.Add(this)) return _list;
                var values = new List<object?>(Items.Count);
                foreach (var item in Items) values.Add(item.Restore(restored, errors));
                _list.Clear();
                foreach (var v in values) _list.Add(v);
                return _list;
            }
        }

        private sealed class ArrayNode : Node
        {
            private readonly Array _array;

            public ArrayNode(Array array) { _array = array; }

            public List<Node> Items { get; } = new List<Node>();

            public override object? Restore(HashSet<Node> restored, List<string> errors)
            {
                if (!restored.Add(this)) return _array;
                if (_array.Rank != 1) return _array;
                for (int i = 0; i < Items.Count && i < _array.Length; i++)
                    _array.SetValue(Items[i].Restore(restored, errors), i);
                return _array;
            }
        }

        private sealed class DictionaryNode : Node
        {
            private readonly IDictionary _dictionary;

            public DictionaryNode(IDictionary dictionary) { _dictionary = dictionary; }

            public List<KeyValuePair<object, Node>> Entries { get; } = new List<KeyValuePair<object, Node>>();

            public override object? Restore(HashSet<Node> restored, List<string> errors)
            {
                if (!restored.Add(this)) return _dictionary;
                var values = new List<KeyValuePair<object, object?>>(Entries.Count);
                foreach (var entry in Entries)
                    values.Add(new KeyValuePair<object, object?>(entry.Key, entry.Value.Restore(restored, errors)));
                _dictionary.Clear();
                foreach (var entry in values) _dictionary[entry.Key] = entry.Value;
                return _dictionary;
            }
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();

            public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
