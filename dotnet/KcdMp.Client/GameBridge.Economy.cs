// SPDX-License-Identifier: GPL-3.0-only
namespace KcdMp.Client;

/// <summary>
/// Session 10: the economy audit ledger (EconomyLedger.cs) fed by the mod's pack watcher (kdcmp_containers.lua, event w134_econ).
/// Observation and bookkeeping only: it never changes the game or another player's state.
/// </summary>
public partial class GameBridge
{
    private readonly object _econGate = new();
    private EconomyLedger? _econ;
    private int _econBad, _econWriteFailures;

    /// <summary>Tests point the ledger at their own file; empty keeps it in memory.</summary>
    public string? EconomyPathOverride { get; init; }

    internal EconomyLedger Economy()
    {
        lock (_econGate)
        {
            if (_econ != null) return _econ;
            try { _econ = new EconomyLedger(EconomyPathOverride ?? Path.Combine(Path.GetDirectoryName(HenryStore.DefaultRoot())!, "economy-ledger.jsonl")); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Console.WriteLine($"MP-ECON economy ledger unavailable ({ex.Message}); this session's pack changes are not audited (preserve the file)");
                _econ = new EconomyLedger();
            }
            return _econ;
        }
    }

    private void EconOnEvent(string? arg)
    {
        if (!EconomyLedger.TryParseEvent(arg ?? "", out var kind, out string cls, out long delta, out double wt, out string reference)) { Interlocked.Increment(ref _econBad); return; }
        try { Economy().Record(kind, cls, delta, wt, reference); }
        catch (Exception ex)
        {
            if (Interlocked.Increment(ref _econWriteFailures) == 1) Console.WriteLine($"MP-ECON an entry could not be journalled ({ex.Message})");
        }
    }

    private string EconStatsLine()
    {
        var e = Economy();
        return FormattableString.Invariant($"MP-ECON audit ledger: {e.Count} entries, refused={_econBad}, write failures={_econWriteFailures}");
    }
}
