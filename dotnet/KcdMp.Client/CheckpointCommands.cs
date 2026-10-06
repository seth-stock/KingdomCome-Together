// SPDX-License-Identifier: GPL-3.0-only
using Coop.Persistence;
using System.Text.Json;

namespace KcdMp.Client;
public static class CheckpointCommands
{
    public sealed record CharacterInput(string ParticipantId, string CharacterKind, string CharacterBlock, string ChestLedger);
    public sealed record CaptureInput(string WorldId, string BranchId, string? ParentId, string ContentFingerprint, string WorldFile, CharacterInput[] Characters);
    public static int Run(string[] args, TextWriter output)
    {
        try
        {
            string Value(string name)
            { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : throw new ArgumentException("Missing " + name); }
            string storeRoot = Value("--store"); var store = new CheckpointStore(storeRoot); var service = new ReconcileService(storeRoot);
            if (args.Contains("--list"))
            { foreach (var m in store.Checkpoints()) output.WriteLine(JsonSerializer.Serialize(m)); return 0; }
            if (args.Contains("--capture"))
            {
                var input = JsonSerializer.Deserialize<CaptureInput>(File.ReadAllBytes(Value("--capture"))) ?? throw new InvalidDataException("Missing capture request.");
                var m = service.Capture(input.WorldId, input.BranchId, input.ParentId, input.ContentFingerprint, File.ReadAllBytes(input.WorldFile),
                    input.Characters.Select(c => new ReconcileService.CaptureCharacter(c.ParticipantId, c.CharacterKind,
                        File.ReadAllBytes(c.CharacterBlock), File.ReadAllBytes(c.ChestLedger))).ToArray());
                output.WriteLine(JsonSerializer.Serialize(m)); return 0;
            }
            string? choice = args.Contains("--choose") ? Value("--choose") : null;
            var selected = service.Select(Value("--local"), Value("--remote"), choice);
            if (args.Contains("--out")) service.Prepare(selected, Value("--out"), WhsSave.QuestClasses(Value("--tables")));
            output.WriteLine(JsonSerializer.Serialize(selected)); return 0;
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ArgumentException or JsonException or UnauthorizedAccessException)
        { output.WriteLine("Checkpoint operation stopped: " + e.Message); return 2; }
    }
}
