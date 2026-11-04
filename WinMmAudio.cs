using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace CodexNativeLite
{
    internal sealed class WinMmMicrophone : IDisposable
    {
        private const int WaveMapper = -1;
        private const int CallbackFunction = 0x00030000;
        private const int WimData = 0x3C0;
        private const int BufferBytes = 4800;
        private readonly object _gate = new object();
        private readonly List<WaveBuffer> _buffers = new List<WaveBuffer>();
        private WaveInProc _callback;
        private IntPtr _handle;
        private bool _running;

        public event EventHandler<AudioDataEventArgs> DataAvailable;

        public void Start()
        {
            lock (_gate)
            {
                if (_running) return;
                _callback = OnWaveIn;
                var format = WaveFormat.Pcm16Mono24000();
                int result = waveInOpen(out _handle, WaveMapper, ref format, _callback, IntPtr.Zero, CallbackFunction);
                Check(result, "Could not open the microphone");
                try
                {
                    for (int i = 0; i < 8; i++)
                    {
                        var buffer = WaveBuffer.Create(BufferBytes);
                        Check(waveInPrepareHeader(_handle, buffer.Header, Marshal.SizeOf(typeof(WaveHeader))), "Could not prepare microphone audio");
                        buffer.Prepared = true;
                        _buffers.Add(buffer);
                        Check(waveInAddBuffer(_handle, buffer.Header, Marshal.SizeOf(typeof(WaveHeader))), "Could not queue microphone audio");
                    }
                    _running = true;
                    Check(waveInStart(_handle), "Could not start the microphone");
                }
                catch
                {
                    ReleaseBuffers();
                    waveInClose(_handle);
                    _handle = IntPtr.Zero;
                    throw;
                }
            }
        }

        public void Stop()
        {
            IntPtr handle;
            lock (_gate)
            {
                if (_handle == IntPtr.Zero) return;
                _running = false;
                handle = _handle;
            }
            waveInStop(handle);
            waveInReset(handle);
            lock (_gate)
            {
                if (_handle == IntPtr.Zero) return;
                ReleaseBuffers();
                waveInClose(_handle);
                _handle = IntPtr.Zero;
                _callback = null;
            }
        }

        private void OnWaveIn(IntPtr handle, int message, IntPtr instance, IntPtr headerPointer, IntPtr reserved)
        {
            if (message != WimData || headerPointer == IntPtr.Zero) return;
            byte[] copy = null;
            lock (_gate)
            {
                if (!_running || _handle == IntPtr.Zero) return;
                WaveHeader header = (WaveHeader)Marshal.PtrToStructure(headerPointer, typeof(WaveHeader));
                int count = (int)header.BytesRecorded;
                if (count > 0)
                {
                    copy = new byte[count];
                    Marshal.Copy(header.Data, copy, 0, count);
                }
                waveInAddBuffer(_handle, headerPointer, Marshal.SizeOf(typeof(WaveHeader)));
            }
            if (copy != null)
            {
                var handler = DataAvailable;
                if (handler != null) handler(this, new AudioDataEventArgs(copy));
            }
        }

        private void ReleaseBuffers()
        {
            foreach (WaveBuffer buffer in _buffers)
            {
                if (buffer.Prepared && _handle != IntPtr.Zero)
                    waveInUnprepareHeader(_handle, buffer.Header, Marshal.SizeOf(typeof(WaveHeader)));
                buffer.Dispose();
            }
            _buffers.Clear();
        }

        public void Dispose() { Stop(); }

        private static void Check(int code, string message)
        {
            if (code != 0) throw new InvalidOperationException(message + " (Windows audio error " + code + ").");
        }

        private delegate void WaveInProc(IntPtr waveIn, int message, IntPtr instance, IntPtr parameter1, IntPtr parameter2);

        [DllImport("winmm.dll")]
        private static extern int waveInOpen(out IntPtr waveIn, int deviceId, ref WaveFormat format, WaveInProc callback, IntPtr instance, int flags);
        [DllImport("winmm.dll")]
        private static extern int waveInPrepareHeader(IntPtr waveIn, IntPtr header, int size);
        [DllImport("winmm.dll")]
        private static extern int waveInUnprepareHeader(IntPtr waveIn, IntPtr header, int size);
        [DllImport("winmm.dll")]
        private static extern int waveInAddBuffer(IntPtr waveIn, IntPtr header, int size);
        [DllImport("winmm.dll")]
        private static extern int waveInStart(IntPtr waveIn);
        [DllImport("winmm.dll")]
        private static extern int waveInStop(IntPtr waveIn);
        [DllImport("winmm.dll")]
        private static extern int waveInReset(IntPtr waveIn);
        [DllImport("winmm.dll")]
        private static extern int waveInClose(IntPtr waveIn);
    }

    internal sealed class WinMmSpeaker : IDisposable
    {
        private const int WaveMapper = -1;
        private const int CallbackFunction = 0x00030000;
        private const int WomDone = 0x3BD;
        private readonly object _gate = new object();
        private readonly Dictionary<IntPtr, WaveBuffer> _buffers = new Dictionary<IntPtr, WaveBuffer>();
        private WaveOutProc _callback;
        private IntPtr _handle;

        public WinMmSpeaker()
        {
            _callback = OnWaveOut;
            var format = WaveFormat.Pcm16Mono24000();
            int result = waveOutOpen(out _handle, WaveMapper, ref format, _callback, IntPtr.Zero, CallbackFunction);
            if (result != 0) throw new InvalidOperationException("Could not open the speaker (Windows audio error " + result + ").");
        }

        public void Play(byte[] data)
        {
            if (data == null || data.Length == 0) return;
            lock (_gate)
            {
                if (_handle == IntPtr.Zero) return;
                var buffer = WaveBuffer.Create(data.Length);
                Marshal.Copy(data, 0, buffer.Data, data.Length);
                int size = Marshal.SizeOf(typeof(WaveHeader));
                int result = waveOutPrepareHeader(_handle, buffer.Header, size);
                if (result != 0) { buffer.Dispose(); return; }
                buffer.Prepared = true;
                _buffers[buffer.Header] = buffer;
                result = waveOutWrite(_handle, buffer.Header, size);
                if (result != 0) ReleaseBuffer(buffer.Header);
            }
        }

        public void Clear()
        {
            IntPtr handle;
            lock (_gate) handle = _handle;
            if (handle != IntPtr.Zero) waveOutReset(handle);
        }

        private void OnWaveOut(IntPtr handle, int message, IntPtr instance, IntPtr headerPointer, IntPtr reserved)
        {
            if (message != WomDone || headerPointer == IntPtr.Zero) return;
            lock (_gate) ReleaseBuffer(headerPointer);
        }

        private void ReleaseBuffer(IntPtr headerPointer)
        {
            WaveBuffer buffer;
            if (!_buffers.TryGetValue(headerPointer, out buffer)) return;
            _buffers.Remove(headerPointer);
            if (buffer.Prepared && _handle != IntPtr.Zero)
                waveOutUnprepareHeader(_handle, buffer.Header, Marshal.SizeOf(typeof(WaveHeader)));
            buffer.Dispose();
        }

        public void Dispose()
        {
            IntPtr handle;
            lock (_gate)
            {
                if (_handle == IntPtr.Zero) return;
                handle = _handle;
            }
            waveOutReset(handle);
            lock (_gate)
            {
                if (_handle == IntPtr.Zero) return;
                var headers = new List<IntPtr>(_buffers.Keys);
                foreach (IntPtr header in headers) ReleaseBuffer(header);
                waveOutClose(_handle);
                _handle = IntPtr.Zero;
                _callback = null;
            }
        }

        private delegate void WaveOutProc(IntPtr waveOut, int message, IntPtr instance, IntPtr parameter1, IntPtr parameter2);

        [DllImport("winmm.dll")]
        private static extern int waveOutOpen(out IntPtr waveOut, int deviceId, ref WaveFormat format, WaveOutProc callback, IntPtr instance, int flags);
        [DllImport("winmm.dll")]
        private static extern int waveOutPrepareHeader(IntPtr waveOut, IntPtr header, int size);
        [DllImport("winmm.dll")]
        private static extern int waveOutUnprepareHeader(IntPtr waveOut, IntPtr header, int size);
        [DllImport("winmm.dll")]
        private static extern int waveOutWrite(IntPtr waveOut, IntPtr header, int size);
        [DllImport("winmm.dll")]
        private static extern int waveOutReset(IntPtr waveOut);
        [DllImport("winmm.dll")]
        private static extern int waveOutClose(IntPtr waveOut);
    }

    internal sealed class AudioDataEventArgs : EventArgs
    {
        public byte[] Data { get; private set; }
        public AudioDataEventArgs(byte[] data) { Data = data; }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WaveFormat
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSecond;
        public uint AverageBytesPerSecond;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort ExtraSize;

        public static WaveFormat Pcm16Mono24000()
        {
            return new WaveFormat
            {
                FormatTag = 1,
                Channels = 1,
                SamplesPerSecond = 24000,
                AverageBytesPerSecond = 48000,
                BlockAlign = 2,
                BitsPerSample = 16,
                ExtraSize = 0
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WaveHeader
    {
        public IntPtr Data;
        public uint BufferLength;
        public uint BytesRecorded;
        public IntPtr User;
        public uint Flags;
        public uint Loops;
        public IntPtr Next;
        public IntPtr Reserved;
    }

    internal sealed class WaveBuffer : IDisposable
    {
        public IntPtr Data { get; private set; }
        public IntPtr Header { get; private set; }
        public bool Prepared { get; set; }

        public static WaveBuffer Create(int size)
        {
            var buffer = new WaveBuffer();
            buffer.Data = Marshal.AllocHGlobal(size);
            buffer.Header = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WaveHeader)));
            var header = new WaveHeader { Data = buffer.Data, BufferLength = (uint)size };
            Marshal.StructureToPtr(header, buffer.Header, false);
            return buffer;
        }

        public void Dispose()
        {
            if (Header != IntPtr.Zero) { Marshal.FreeHGlobal(Header); Header = IntPtr.Zero; }
            if (Data != IntPtr.Zero) { Marshal.FreeHGlobal(Data); Data = IntPtr.Zero; }
        }
    }
}
