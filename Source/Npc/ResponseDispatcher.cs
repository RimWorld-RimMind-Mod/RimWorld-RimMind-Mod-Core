using System;
using RimMind.Core.Extensions;
using Verse;
using RimWorld;

namespace RimMind.Core.Npc
{
    public static class ResponseDispatcher
    {
        public static void Dispatch(NpcChatResult result, Pawn? pawn = null)
        {
            if (result == null) return;

            if (!string.IsNullOrEmpty(result.Error))
            {
                Log.Warning($"[RimMind] ResponseDispatcher: chat error - {result.Error}");
                return;
            }

            if (!string.IsNullOrEmpty(result.Message))
                DispatchMessage(result.Message, pawn);

            if (result.Commands != null && result.Commands.Count > 0)
                DispatchCommands(result.Commands.ToArray(), pawn);

            if (!string.IsNullOrEmpty(result.AudioUrl))
                DispatchAudio(result.AudioUrl!);
        }

        public static void DispatchMessage(string message, Pawn? pawn)
        {
            if (string.IsNullOrEmpty(message)) return;

            if (pawn != null && !pawn.Dead && pawn.Map != null)
            {
                try
                {
                    MoteMaker.ThrowText(pawn.PositionHeld.ToVector3Shifted(), pawn.Map, message);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimMind] ResponseDispatcher: failed to show mote for pawn {pawn.LabelShort} - {ex.Message}");
                }
            }

            Log.Message($"[RimMind] NPC: {message}");
        }

        public static void DispatchCommands(NpcCommandResult[] commands, Pawn? pawn)
        {
            if (commands == null || commands.Length == 0) return;

            foreach (var cmd in commands)
            {
                if (string.IsNullOrEmpty(cmd.Name)) continue;

                try
                {
                    var actor = pawn;
                    if (actor == null || actor.Dead)
                    {
                        Log.Warning($"[RimMind] ResponseDispatcher: cannot execute command '{cmd.Name}' - no valid actor pawn");
                        continue;
                    }

                    var bridge = RimMindAPI.GetAgentActionBridge();
                    if (bridge == null)
                    {
                        Log.Warning($"[RimMind] ResponseDispatcher: cannot execute command '{cmd.Name}' - no AgentActionBridge registered");
                        continue;
                    }

                    bool ok = bridge.Execute(cmd.Name, actor, null, cmd.Arguments);

                    if (!ok)
                        Log.Warning($"[RimMind] ResponseDispatcher: command '{cmd.Name}' execution failed");
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimMind] ResponseDispatcher: command '{cmd.Name}' threw exception - {ex.Message}");
                }
            }
        }

        private static void DispatchAudio(string audioUrl)
        {
            try
            {
                var player = RimMindAPI.AudioPlayer;
                player.PlayAudio(audioUrl);
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimMind] ResponseDispatcher: audio playback failed - {ex.Message}");
            }
        }
    }
}
