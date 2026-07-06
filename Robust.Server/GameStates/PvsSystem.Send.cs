using System;
using System.Threading;
using System.Threading.Tasks;
using Prometheus;
using Robust.Shared.Log;
using Robust.Shared.Network.Messages;
using Robust.Shared.Player;
using Robust.Shared.Threading;
using Robust.Shared.Utility;

namespace Robust.Server.GameStates;

internal sealed partial class PvsSystem
{
    private WaitHandle? _sendTask;

    /// <summary>
    /// Compress and send game states to connected clients.
    /// </summary>
    private void SendStates()
    {
        // When async is enabled, compress+send runs after the critical tick via ProcessSendStates.
        if (_async)
            return;

        SendStatesNow();
    }

    private void SendStatesNow()
    {
        using var _ = Histogram.WithLabels("Send States").NewTimer();
        var opts = new ParallelOptions {MaxDegreeOfParallelism = _parallelMgr.ParallelProcessCount};
        Parallel.ForEach(_sessions, opts, _threadResourcesPool.Get, SendSessionState, _threadResourcesPool.Return);
    }

    private void ProcessSendStates()
    {
        if (_sessions.Length == 0)
            return;

        DebugTools.AssertNull(_sendTask);

        if (!_async)
            return;

        _sendTask = _parallelMgr.Process(_sendJob, _sendJob.Count);
    }

    private PvsThreadResources SendSessionState(PvsSession data, ParallelLoopState state, PvsThreadResources resource)
    {
        try
        {
            SendSessionState(data, resource.CompressionContext);
        }
        catch (Exception e)
        {
            Log.Log(LogLevel.Error, e, $"Caught exception while sending mail for {data.Session}.");
        }

        return resource;
    }

    private void SendSessionState(PvsSession data, ZStdCompressionContext ctx)
    {
        DebugTools.AssertEqual(data.State, null);

        // PVS benchmarks use dummy sessions.
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (data.Session.Channel is not DummyChannel)
        {
            DebugTools.AssertNotEqual(data.StateStream, null);
            var msg = new MsgState
            {
                StateStream = data.StateStream,
                ForceSendReliably = data.ForceSendReliably,
                CompressionContext = ctx
            };

            _netMan.ServerSendMessage(msg, data.Session.Channel);
            if (msg.ShouldSendReliably())
            {
                data.RequestedFull = false;
                data.LastReceivedAck = _gameTiming.CurTick;
                lock (PendingAcks)
                {
                    PendingAcks.Add(data.Session);
                }
            }
        }
        else
        {
            // Always "ack" dummy sessions.
            data.LastReceivedAck = _gameTiming.CurTick;
            data.RequestedFull = false;
            lock (PendingAcks)
            {
                PendingAcks.Add(data.Session);
            }
        }

        data.StateStream?.Dispose();
        data.StateStream = null;
    }

    private record struct PvsSendJob(PvsSystem Pvs) : IParallelRobustJob
    {
        public int BatchSize => 1;
        public int Count => Pvs._sessions.Length;

        public void Execute(int index)
        {
            try
            {
                var resource = Pvs._threadResourcesPool.Get();
                try
                {
                    Pvs.SendSessionState(Pvs._sessions[index], resource.CompressionContext);
                }
                finally
                {
                    Pvs._threadResourcesPool.Return(resource);
                }
            }
            catch (Exception e)
            {
                Pvs.Log.Log(LogLevel.Error, e, $"Caught exception while sending mail for session index {index}.");
#if !EXCEPTION_TOLERANCE
                throw;
#endif
            }
        }
    }
}
