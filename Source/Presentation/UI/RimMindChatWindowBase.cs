using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using RimMind.Infrastructure.UI;
using RimMind.Presentation.UI.Layout;
using UnityEngine;
using Verse;

namespace RimMind.Presentation.UI
{
    /// <summary>
    /// Represents one chat message entry rendered inside <see cref="RimMindChatWindowBase"/>.
    /// </summary>
    public readonly struct ChatWindowMessage
    {
        public ChatWindowMessage(string role, string content, string? senderPrefix = null)
        {
            Role = role ?? string.Empty;
            Content = content ?? string.Empty;
            SenderPrefix = senderPrefix;
        }

        public string Role { get; }
        public string Content { get; }
        public string? SenderPrefix { get; }
    }

    /// <summary>
    /// Reusable interactive chat window base class built on <see cref="RimMindWindowBase"/>.
    /// Handles layout-scoped rendering, zebra-striped auto-scrolling message history,
    /// thread-safe background response marshaling, status bar, Enter-to-send input row,
    /// and automatic <see cref="CancellationTokenSource"/> teardown on <see cref="PostClose"/>.
    /// </summary>
    public abstract class RimMindChatWindowBase : RimMindWindowBase
    {
        protected const float ChatPadding = 8f;
        protected const float ChatInputHeight = 36f;
        protected const float ChatSendButtonWidth = 80f;
        protected const float ChatHeaderHeight = 28f;
        protected const float ChatStatusHeight = 22f;
        protected const float ChatScrollbarWidth = 16f;

        private readonly List<ChatWindowMessage> _messages = new List<ChatWindowMessage>();
        private readonly ConcurrentQueue<Action> _mainThreadUpdates = new ConcurrentQueue<Action>();
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private Vector2 _scrollPosition = Vector2.zero;
        private string _inputText = string.Empty;
        private bool _isWaiting;
        private bool _autoScroll = true;
        private bool _closed;

        protected RimMindChatWindowBase()
        {
            doCloseButton = false;
            doCloseX = true;
            closeOnAccept = false;
            forcePause = false;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
            draggable = true;
        }

        public override Vector2 InitialSize => new Vector2(480f, 540f);

        protected CancellationToken LifetimeToken => _lifetime.Token;
        protected bool IsClosed => _closed;
        protected bool IsWaiting => _isWaiting;
        protected IReadOnlyList<ChatWindowMessage> Messages => _messages;

        protected virtual Color HeaderColor => new Color(0.8f, 0.95f, 1f);
        protected virtual Color UserColor => new Color(0.7f, 0.85f, 1f);
        protected virtual Color AssistantColor => new Color(1f, 0.95f, 0.8f);
        protected virtual Color SystemColor => new Color(1f, 0.55f, 0.55f);
        protected virtual Color ChatBackgroundColor => new Color(0.08f, 0.08f, 0.12f, 0.5f);
        protected virtual Color StatusColor => new Color(1f, 1f, 0.5f);

        protected abstract string GetTitle();
        protected abstract string GetWaitingText();
        protected abstract string GetSendButtonLabel();
        protected abstract string GetPrefixForRole(string role);
        protected abstract void OnSubmitUserMessage(string trimmedInput);

        /// <summary>
        /// Optional guard called at the start of every frame. Return false to auto-close the window.
        /// </summary>
        protected virtual bool ValidateCanStayOpen() => true;

        /// <summary>
        /// Optional hook invoked on the main thread whenever a new message is appended via <see cref="AppendMessage"/>
        /// or <see cref="PostMessage"/>.
        /// </summary>
        protected virtual void OnMessageAppended(ChatWindowMessage message)
        {
        }

        protected virtual Color GetColorForRole(string role)
        {
            if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
                return UserColor;
            if (string.Equals(role, "system", StringComparison.OrdinalIgnoreCase))
                return SystemColor;
            return AssistantColor;
        }

        /// <summary>
        /// Appends a message immediately on the main thread.
        /// </summary>
        protected void AppendMessage(string role, string content, string? senderPrefix = null, bool notifyHook = true)
        {
            if (string.IsNullOrEmpty(content))
                return;
            var msg = new ChatWindowMessage(role, content, senderPrefix);
            _messages.Add(msg);
            _autoScroll = true;
            if (notifyHook)
                OnMessageAppended(msg);
        }

        /// <summary>
        /// Thread-safe helper for background LLM callbacks to deliver a reply and clear waiting state on the next UI frame.
        /// </summary>
        protected void PostReply(string role, string content, bool clearWaiting = true, string? senderPrefix = null)
        {
            if (_closed) return;
            _mainThreadUpdates.Enqueue(() =>
            {
                if (_closed) return;
                if (clearWaiting)
                    _isWaiting = false;
                if (!string.IsNullOrEmpty(content))
                    AppendMessage(role, content, senderPrefix);
            });
        }

        /// <summary>
        /// Thread-safe helper to toggle the waiting indicator.
        /// </summary>
        protected void SetWaiting(bool waiting)
        {
            _isWaiting = waiting;
        }

        public override void PostClose()
        {
            if (!_closed)
            {
                _closed = true;
                _isWaiting = false;
                try { _lifetime.Cancel(); } catch (ObjectDisposedException) { }
                try { _lifetime.Dispose(); } catch (ObjectDisposedException) { }
            }
            base.PostClose();
        }

        protected override void DrawContents(Rect inRect, RimMindLayoutScope scope)
        {
            if (!ValidateCanStayOpen())
            {
                Close();
                return;
            }

            while (_mainThreadUpdates.TryDequeue(out var update))
            {
                update?.Invoke();
            }

            Text.Font = GameFont.Small;

            float statusH = _isWaiting ? ChatStatusHeight + ChatPadding : 0f;
            float chatHeight = Mathf.Max(40f, inRect.height - ChatHeaderHeight - statusH - ChatInputHeight - ChatPadding * 3f);
            float inputY = inRect.yMax - ChatInputHeight;

            Rect headerRect = new Rect(inRect.x, inRect.y, inRect.width, ChatHeaderHeight);
            Rect chatRect = new Rect(inRect.x, inRect.y + ChatHeaderHeight + ChatPadding, inRect.width, chatHeight);
            scope.Record(headerRect, "ChatWindow:Header");
            scope.Record(chatRect, "ChatWindow:History");

            GUI.color = HeaderColor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(headerRect, GetTitle());
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            Widgets.DrawBoxSolid(chatRect, ChatBackgroundColor);
            DrawChatHistory(chatRect);

            if (_isWaiting)
            {
                Rect statusRect = new Rect(inRect.x, inputY - ChatStatusHeight - ChatPadding, inRect.width, ChatStatusHeight);
                scope.Record(statusRect, "ChatWindow:Status");
                GUI.color = StatusColor;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(statusRect, GetWaitingText());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }

            Rect inputRect = new Rect(inRect.x, inputY, Mathf.Max(40f, inRect.width - ChatSendButtonWidth - ChatPadding), ChatInputHeight);
            Rect sendRect = new Rect(inRect.xMax - ChatSendButtonWidth, inputY, ChatSendButtonWidth, ChatInputHeight);
            scope.Record(inputRect, "ChatWindow:Input");
            scope.Record(sendRect, "ChatWindow:Send");

            _inputText = Widgets.TextField(inputRect, _inputText ?? string.Empty);

            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && !_isWaiting;
            bool sendClicked = Widgets.ButtonText(sendRect, GetSendButtonLabel());
            GUI.enabled = wasEnabled;

            if (sendClicked && !_isWaiting)
            {
                SubmitCurrentInput();
            }

            if (!_isWaiting
                && Event.current != null
                && Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                && !string.IsNullOrWhiteSpace(_inputText))
            {
                SubmitCurrentInput();
                Event.current.Use();
            }
        }

        private void SubmitCurrentInput()
        {
            string trimmed = (_inputText ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(trimmed))
                return;

            _inputText = string.Empty;
            OnSubmitUserMessage(trimmed);
        }

        private void DrawChatHistory(Rect rect)
        {
            if (_messages.Count == 0)
                return;

            float contentWidth = Mathf.Max(20f, rect.width - ChatScrollbarWidth);
            float textWidth = Mathf.Max(20f, contentWidth - ChatPadding * 2f);
            float contentHeight = CalcMessagesHeight(textWidth);
            Rect viewRect = new Rect(0f, 0f, contentWidth, Mathf.Max(rect.height, contentHeight));

            float prevScrollY = _scrollPosition.y;
            Widgets.BeginScrollView(rect, ref _scrollPosition, viewRect);

            float y = 0f;
            for (int i = 0; i < _messages.Count; i++)
            {
                var msg = _messages[i];
                string prefix = msg.SenderPrefix ?? GetPrefixForRole(msg.Role);
                string line = prefix + msg.Content;
                float lineH = Text.CalcHeight(line, textWidth) + ChatPadding;

                if (i % 2 == 0)
                {
                    Widgets.DrawBoxSolid(new Rect(0f, y, contentWidth, lineH), new Color(1f, 1f, 1f, 0.03f));
                }

                GUI.color = GetColorForRole(msg.Role);
                Widgets.Label(new Rect(ChatPadding, y, textWidth, lineH), line);
                GUI.color = Color.white;

                y += lineH;
            }

            Widgets.EndScrollView();

            if (_autoScroll)
            {
                _scrollPosition.y = Mathf.Max(0f, contentHeight - rect.height);
                _autoScroll = false;
            }
            else if (Mathf.Abs(_scrollPosition.y - prevScrollY) > 0.5f)
            {
                // User scrolled manually; keep their position.
            }
        }

        private float CalcMessagesHeight(float textWidth)
        {
            float total = 0f;
            for (int i = 0; i < _messages.Count; i++)
            {
                var msg = _messages[i];
                string prefix = msg.SenderPrefix ?? GetPrefixForRole(msg.Role);
                total += Text.CalcHeight(prefix + msg.Content, textWidth) + ChatPadding;
            }
            return total;
        }
    }
}
