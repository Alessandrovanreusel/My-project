// THROWAWAY SPIKE CODE (Story 1.14). Never referenced by game code. See README.md in this folder.
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using CameraGame.Core;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace CameraGame.Spikes.RecordingSpike
{
    /// <summary>
    /// Candidate (A)'s encoder: an ffmpeg child process fed raw RGBA frames over stdin from a worker
    /// thread draining a BOUNDED queue. The main thread never blocks on the pipe: when no slot is free the
    /// frame is counted as dropped and the next frame repeats to keep the clip's true duration.
    /// Owns its NativeArray slots (single-owner rule, GalleryService.cs) and disposes all of them.
    /// </summary>
    public sealed class FfmpegSink : IDisposable
    {
        public readonly int FrameBytes;
        public bool Failed { get; private set; }
        public string FailReason { get; private set; } = "";
        public long FramesWritten, BytesPiped, WriteFailures;
        public int Dropped;
        public int QueueDepth => _queue?.Count ?? 0;
        public string EncoderLine = "";   // the MFT / encoder ffmpeg actually opened (from its log)
        public int ExitCode = int.MinValue;

        private readonly NativeArray<byte>[] _slots;
        private readonly int[] _state;    // 0 free, 1 in flight (readback), 2 queued/writing
        private BlockingCollection<(int slot, int repeats)> _queue;
        private Process _proc;
        private Stream _stdin;
        private Thread _worker;
        private readonly byte[] _managed;
        private StreamWriter _errLog;
        private readonly object _errLock = new object();

        public FfmpegSink(int frameBytes, int slotCount)
        {
            FrameBytes = frameBytes;
            _slots = new NativeArray<byte>[slotCount];
            _state = new int[slotCount];
            for (int i = 0; i < slotCount; i++)
                _slots[i] = new NativeArray<byte>(frameBytes, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            _managed = new byte[frameBytes];
        }

        public long BufferedBytes => (long)_slots.Length * FrameBytes;

        /// <summary>True once the transport is connected and frames can flow.</summary>
        public volatile bool Ready;
        public string Transport = "pipe";
        public double ConnectMs = -1;
        private int _port;
        private System.Net.Sockets.TcpClient _tcp;

        /// <summary>Start ffmpeg. <paramref name="args"/> contains "{IN}" where the input goes. Transport "pipe"
        /// is Process stdin; "tcp" is ffmpeg listening on a loopback port (measured 2026-11-10: Mono's
        /// Process stdin starved ffmpeg at ~6 fps of 1080p RGBA, while a Python pipe managed 111 fps).</summary>
        public bool Start(string exe, string args, string stderrPath, string transport = "tcp")
        {
            Transport = transport;
            try
            {
                if (transport == "tcp")
                {
                    var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
                    l.Start(); _port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port; l.Stop();
                    args = args.Replace("{IN}", "tcp://127.0.0.1:" + _port + "?listen=1");
                }
                else args = args.Replace("{IN}", "-");
                _errLog = new StreamWriter(stderrPath, false) { AutoFlush = true };
                _errLog.WriteLine("# " + exe + " " + args);
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardInput = transport != "tcp", RedirectStandardError = true, RedirectStandardOutput = false,
                };
                _proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                _proc.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data == null) return;
                    lock (_errLock)
                    {
                        _errLog?.WriteLine(e.Data);
                        if (EncoderLine.Length == 0 && (e.Data.Contains("MFT name") || e.Data.Contains("MFT:")))
                            EncoderLine = e.Data.Trim();
                    }
                };
                _proc.Start();
                _proc.BeginErrorReadLine();
                if (transport != "tcp") { _stdin = _proc.StandardInput.BaseStream; Ready = true; }
                _queue = new BlockingCollection<(int, int)>(_slots.Length);
                _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "RecordingSpike.ffmpeg" };
                _worker.Start();
                return true;
            }
            catch (Exception e)
            {
                Fail("ffmpeg failed to start: " + e.GetType().Name + ": " + e.Message);
                return false;
            }
        }

        private void Fail(string why)
        {
            if (Failed) return;
            Failed = true; FailReason = why;
            // NFR8: log ONCE, keep the game running. GameLog is a thin Debug.Log wrapper, safe off-thread.
            GameLog.Warn("RecordingSpike", "encoder unavailable, recording stops (game continues): " + why);
        }

        /// <summary>Main thread: claim a free slot for the next readback.</summary>
        public bool TryAcquire(out int slot)
        {
            for (int i = 0; i < _state.Length; i++)
                if (Interlocked.CompareExchange(ref _state[i], 1, 0) == 0) { slot = i; return true; }
            slot = -1; return false;
        }

        public ref NativeArray<byte> Slot(int i) => ref _slots[i];

        public void Submit(int slot, int repeats)
        {
            if (Failed) { Volatile.Write(ref _state[slot], 0); return; }
            _state[slot] = 2;
            if (!_queue.TryAdd((slot, repeats))) { Volatile.Write(ref _state[slot], 0); Dropped += repeats; }
        }

        public void Release(int slot) => Volatile.Write(ref _state[slot], 0);

        private unsafe void WorkerLoop()
        {
            if (Transport == "tcp")
            {
                var sw = Stopwatch.StartNew();
                while (!Ready && sw.Elapsed.TotalSeconds < 10 && !Failed)
                {
                    try
                    {
                        _tcp = new System.Net.Sockets.TcpClient { SendBufferSize = 16 * 1024 * 1024 };
                        _tcp.Connect(System.Net.IPAddress.Loopback, _port);
                        _stdin = _tcp.GetStream(); ConnectMs = sw.Elapsed.TotalMilliseconds; Ready = true;
                    }
                    catch (Exception) { _tcp?.Close(); Thread.Sleep(20); }
                }
                if (!Ready) Fail("could not connect to ffmpeg on loopback port " + _port);
            }
            foreach (var (slot, repeats) in _queue.GetConsumingEnumerable())
            {
                try
                {
                    if (!Failed)
                    {
                        void* src = NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(_slots[slot]);
                        fixed (byte* dst = _managed) UnsafeUtility.MemCpy(dst, src, FrameBytes);
                        for (int r = 0; r < repeats; r++)
                        {
                            _stdin.Write(_managed, 0, FrameBytes);
                            Interlocked.Increment(ref FramesWritten);
                            Interlocked.Add(ref BytesPiped, FrameBytes);
                        }
                    }
                }
                catch (Exception e)
                {
                    Interlocked.Increment(ref WriteFailures);
                    Fail("pipe write failed: " + e.GetType().Name + ": " + e.Message);
                }
                finally { Volatile.Write(ref _state[slot], 0); }
            }
        }

        /// <summary>Close stdin and wait for ffmpeg to finish the file. Returns elapsed ms.</summary>
        public double Finish(int timeoutMs = 30000)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                _queue?.CompleteAdding();
                _worker?.Join(timeoutMs);
                try { _stdin?.Flush(); _stdin?.Close(); _tcp?.Close(); } catch (Exception) { }
                if (_proc != null)
                {
                    if (!_proc.WaitForExit(timeoutMs)) { _proc.Kill(); Fail("ffmpeg did not exit in time"); }
                    _proc.WaitForExit();
                    ExitCode = _proc.ExitCode;
                }
            }
            catch (Exception e) { Fail("finish failed: " + e.Message); }
            lock (_errLock) { _errLog?.Flush(); }
            return sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>Fail-soft scenario: kill ffmpeg mid-clip.</summary>
        public void KillForTest() { try { _proc?.Kill(); } catch (Exception) { } }

        public void Dispose()
        {
            if (_queue != null && !_queue.IsAddingCompleted) Finish(5000);
            // Every in-flight readback must complete before its NativeArray is disposed (Unity docs).
            UnityEngine.Rendering.AsyncGPUReadback.WaitAllRequests();
            for (int i = 0; i < _slots.Length; i++) if (_slots[i].IsCreated) _slots[i].Dispose();
            lock (_errLock) { _errLog?.Dispose(); _errLog = null; }
            _proc?.Dispose();
        }
    }

    /// <summary>
    /// Candidate (C): raw frames in memory. A pre-allocated ring of NativeArrays sized for the clip; never
    /// allocates per frame. One owner disposes all of them.
    /// </summary>
    public sealed class RawRing : IDisposable
    {
        public readonly int FrameBytes;
        public readonly NativeArray<byte>[] Slots;
        public readonly int[] Repeats;
        public readonly int[] VideoFrame;   // output frame index of each slot's first repeat
        public readonly bool[] Ready;
        public int Count;                   // slots used in the current clip
        public int Dropped, ReadbackErrors, Overflow;
        public bool FullLogged;

        public RawRing(int frameBytes, int capacity)
        {
            FrameBytes = frameBytes;
            Slots = new NativeArray<byte>[capacity];
            Repeats = new int[capacity]; VideoFrame = new int[capacity]; Ready = new bool[capacity];
            for (int i = 0; i < capacity; i++)
                Slots[i] = new NativeArray<byte>(frameBytes, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        public long BufferedBytes => (long)Slots.Length * FrameBytes;
        public long UsedBytes => (long)Count * FrameBytes;

        public void Reset()
        {
            UnityEngine.Rendering.AsyncGPUReadback.WaitAllRequests();
            Count = 0; Dropped = 0; ReadbackErrors = 0; Overflow = 0; FullLogged = false;
            Array.Clear(Ready, 0, Ready.Length);
            Array.Clear(Repeats, 0, Repeats.Length);
        }

        public void Dispose()
        {
            UnityEngine.Rendering.AsyncGPUReadback.WaitAllRequests();
            for (int i = 0; i < Slots.Length; i++) if (Slots[i].IsCreated) Slots[i].Dispose();
        }
    }
}
